using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NINA.Plugin.SeeDrift.Models;

namespace NINA.Plugin.SeeDrift.Utility {

  internal static class TargetVisitSegmentation {

    private static readonly TimeSpan InterFrameLogUpperSlop = TimeSpan.FromSeconds(90);

    public static TargetVisitPlan BuildPlan(
        string targetName,
        IReadOnlyList<DriftSample> targetOrdered,
        IReadOnlyList<DriftSample>? batchOrdered,
        IReadOnlyList<LightSaveCatalogEntry>? lightCatalog,
        IReadOnlyList<TargetSchedulerStartEvent>? schedulerStarts) {
      if (targetOrdered.Count == 0) {
        return new TargetVisitPlan {
          Visits = Array.Empty<IReadOnlyList<DriftSample>>(),
          ReturnVisitBoundaryEdges = Array.Empty<int>(),
          GapAssessments = Array.Empty<ExposureGapAssessment>()
        };
      }

      var gaps = new List<ExposureGapAssessment>();
      var boundaryEdges = new List<int>();
      var targetKey = Normalize(targetName);
      var solvedBySeq = BuildSolvedSeqSet(targetOrdered);

      for (var i = 1; i < targetOrdered.Count; i++) {
        var prev = targetOrdered[i - 1];
        var cur = targetOrdered[i];
        var assessment = ClassifyEdge(
            targetKey, prev, cur, batchOrdered, lightCatalog, schedulerStarts, solvedBySeq);
        gaps.Add(assessment);
        if (assessment.Kind == ExposureGapKind.ReturnVisit)
          boundaryEdges.Add(i);
      }

      var visits = SplitAtBoundaries(targetOrdered, boundaryEdges);
      return new TargetVisitPlan {
        Visits = visits,
        ReturnVisitBoundaryEdges = boundaryEdges,
        GapAssessments = gaps
      };
    }

    public static bool IsReturnVisitBoundary(TargetVisitPlan plan, int edgeIndex) =>
        plan.ReturnVisitBoundaryEdges.Contains(edgeIndex);

    private static ExposureGapAssessment ClassifyEdge(
        string targetKey,
        DriftSample prev,
        DriftSample cur,
        IReadOnlyList<DriftSample>? batchOrdered,
        IReadOnlyList<LightSaveCatalogEntry>? lightCatalog,
        IReadOnlyList<TargetSchedulerStartEvent>? schedulerStarts,
        HashSet<int> solvedBySeq) {
      if (!FitsFolderImport.TryExposureSequenceFromFileName(prev.FileName, out var seqA)
          || !FitsFolderImport.TryExposureSequenceFromFileName(cur.FileName, out var seqB)
          || seqB - seqA <= 1) {
        return new ExposureGapAssessment {
          Kind = ExposureGapKind.None,
          SequenceFrom = 0,
          SequenceTo = 0,
          MissingSequenceCount = 0
        };
      }

      var missing = seqB - seqA - 1;
      var t0 = prev.ExposureStartUtc;
      var t1 = cur.ExposureStartUtc;
      if (t1 <= t0)
        t1 = t0.AddSeconds(1);

      if (HasSchedulerNewTargetStart(schedulerStarts, t0, t1, targetKey)) {
        var detail = BuildReturnVisitDetail(missing, seqA, seqB, lightCatalog, batchOrdered, targetKey, prev, cur);
        return new ExposureGapAssessment {
          Kind = ExposureGapKind.ReturnVisit,
          SequenceFrom = seqA,
          SequenceTo = seqB,
          MissingSequenceCount = missing,
          Detail = detail
        };
      }

      var sameTargetLogged = CountCatalogInRange(lightCatalog, seqA, seqB, targetKey, sameTarget: true);
      if (sameTargetLogged > 0) {
        return new ExposureGapAssessment {
          Kind = ExposureGapKind.MissingOrUnsolved,
          SequenceFrom = seqA,
          SequenceTo = seqB,
          MissingSequenceCount = missing,
          Detail = FormattableString.Invariant(
            $"{sameTargetLogged} logged LIGHT save(s) for this target between exposure {seqA} and {seqB} were not included in the solved trace.")
        };
      }

      var otherDetail = BuildOtherTargetDetail(lightCatalog, batchOrdered, seqA, seqB, targetKey, prev, cur);
      return new ExposureGapAssessment {
        Kind = ExposureGapKind.MissingOrUnsolved,
        SequenceFrom = seqA,
        SequenceTo = seqB,
        MissingSequenceCount = missing,
        Detail = string.IsNullOrEmpty(otherDetail)
          ? FormattableString.Invariant(
            $"{missing} exposure number(s) between {seqA} and {seqB} are not in the solved trace (no Target Scheduler NewTargetStart in logs for this interval).")
          : otherDetail
      };
    }

    private static bool HasSchedulerNewTargetStart(
        IReadOnlyList<TargetSchedulerStartEvent>? events,
        DateTime t0,
        DateTime t1,
        string targetKey) {
      if (events == null || events.Count == 0)
        return false;
      var t1Log = t1 + InterFrameLogUpperSlop;
      foreach (var ev in events) {
        if (ev.UtcTime <= t0 || ev.UtcTime >= t1Log)
          continue;
        if (string.IsNullOrWhiteSpace(ev.TargetLabel))
          return true;
        if (string.Equals(Normalize(ev.TargetLabel), targetKey, StringComparison.OrdinalIgnoreCase))
          return true;
      }
      return false;
    }

    private static string BuildReturnVisitDetail(
        int missing,
        int seqA,
        int seqB,
        IReadOnlyList<LightSaveCatalogEntry>? catalog,
        IReadOnlyList<DriftSample>? batchOrdered,
        string targetKey,
        DriftSample prev,
        DriftSample cur) {
      var parts = new List<string> {
        FormattableString.Invariant(
          $"Target Scheduler started this target again (NewTargetStart) between {FitsFolderImport.FormatBetweenFramesLabel(prev.FileName, prev.FrameIndex, cur.FileName, cur.FrameIndex)}.")
      };
      parts.Add(FormattableString.Invariant(
        $"Exposure numbering continued ({missing} counter step(s) between {seqA} and {seqB}); not missing frames for this target."));

      var otherTargets = SummarizeOtherTargetsInGap(catalog, batchOrdered, seqA, seqB, targetKey, prev, cur);
      if (!string.IsNullOrEmpty(otherTargets))
        parts.Add(otherTargets);
      return string.Join(" ", parts);
    }

    private static string BuildOtherTargetDetail(
        IReadOnlyList<LightSaveCatalogEntry>? catalog,
        IReadOnlyList<DriftSample>? batchOrdered,
        int seqA,
        int seqB,
        string targetKey,
        DriftSample prev,
        DriftSample cur) {
      var other = SummarizeOtherTargetsInGap(catalog, batchOrdered, seqA, seqB, targetKey, prev, cur);
      if (string.IsNullOrEmpty(other))
        return "";
      return FormattableString.Invariant(
        $"Possibly missing/unsolved frames: {seqB - seqA - 1} exposure number(s) between {seqA} and {seqB}. {other}");
    }

    private static string SummarizeOtherTargetsInGap(
        IReadOnlyList<LightSaveCatalogEntry>? catalog,
        IReadOnlyList<DriftSample>? batchOrdered,
        int seqA,
        int seqB,
        string targetKey,
        DriftSample prev,
        DriftSample cur) {
      var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
      if (catalog != null) {
        foreach (var row in catalog) {
          if (!row.ExposureSequence.HasValue)
            continue;
          var seq = row.ExposureSequence.Value;
          if (seq <= seqA || seq >= seqB)
            continue;
          var name = Normalize(row.TargetName);
          if (string.Equals(name, targetKey, StringComparison.OrdinalIgnoreCase))
            continue;
          counts.TryGetValue(name, out var c);
          counts[name] = c + 1;
        }
      }

      if (batchOrdered != null) {
        foreach (var s in batchOrdered) {
          if (s.FrameIndex <= prev.FrameIndex || s.FrameIndex >= cur.FrameIndex)
            continue;
          var name = Normalize(s.TargetName);
          if (string.Equals(name, targetKey, StringComparison.OrdinalIgnoreCase))
            continue;
          counts.TryGetValue(name, out var c);
          counts[name] = c + 1;
        }
      }

      if (counts.Count == 0)
        return "";
      var bits = counts
        .OrderByDescending(kv => kv.Value)
        .Take(3)
        .Select(kv => $"{kv.Value} on {kv.Key}");
      return "Other target(s) in the same interval: " + string.Join(", ", bits) + ".";
    }

    private static int CountCatalogInRange(
        IReadOnlyList<LightSaveCatalogEntry>? catalog,
        int seqA,
        int seqB,
        string targetKey,
        bool sameTarget) {
      if (catalog == null)
        return 0;
      var n = 0;
      foreach (var row in catalog) {
        if (!row.ExposureSequence.HasValue)
          continue;
        var seq = row.ExposureSequence.Value;
        if (seq <= seqA || seq >= seqB)
          continue;
        var match = string.Equals(Normalize(row.TargetName), targetKey, StringComparison.OrdinalIgnoreCase);
        if (sameTarget == match)
          n++;
      }
      return n;
    }

    private static HashSet<int> BuildSolvedSeqSet(IReadOnlyList<DriftSample> targetOrdered) {
      var set = new HashSet<int>();
      foreach (var s in targetOrdered) {
        if (FitsFolderImport.TryExposureSequenceFromFileName(s.FileName, out var n))
          set.Add(n);
      }
      return set;
    }

    private static List<IReadOnlyList<DriftSample>> SplitAtBoundaries(
        IReadOnlyList<DriftSample> ordered,
        IReadOnlyList<int> boundaryEdges) {
      if (ordered.Count == 0)
        return new List<IReadOnlyList<DriftSample>>();
      if (boundaryEdges.Count == 0) {
        return new List<IReadOnlyList<DriftSample>> { ordered.ToList() };
      }

      var visits = new List<IReadOnlyList<DriftSample>>();
      var start = 0;
      foreach (var edge in boundaryEdges.OrderBy(e => e)) {
        if (edge <= start)
          continue;
        visits.Add(ordered.Skip(start).Take(edge - start).ToList());
        start = edge;
      }
      if (start < ordered.Count)
        visits.Add(ordered.Skip(start).ToList());
      return visits;
    }

    private static string Normalize(string? label) =>
        string.IsNullOrWhiteSpace(label) ? "Unknown" : label.Trim();
  }
}
