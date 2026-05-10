using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using NINA.Plugin.SeeDrift.Models;
using NINA.Plugin.SeeDrift.Utility;

namespace NINA.Plugin.SeeDrift.Services {

    internal static class SessionAnalysisService {

        public static TargetAnalysis AnalyzeTarget(string targetName, IReadOnlyList<DriftSample> samples) {
            var ordered = samples.OrderBy(s => s.FrameIndex).ToList();
            var analysis = new TargetAnalysis {
                TargetName = string.IsNullOrWhiteSpace(targetName) ? "Unknown" : targetName.Trim(),
                FrameCount = ordered.Count,
                DriftRate = BuildDriftRate(ordered)
            };

            analysis.Dithers.AddRange(BuildDitherAnalyses(ordered));
            analysis.SuspectDitherCount = analysis.Dithers.Count(d => d.IsSuspect);
            analysis.SuspectDitherDiscountedAbsRaArcSec = analysis.Dithers.Where(d => d.IsSuspect).Sum(d => Math.Abs(d.DeltaRaArcSec));
            analysis.SuspectDitherDiscountedAbsDecArcSec = analysis.Dithers.Where(d => d.IsSuspect).Sum(d => Math.Abs(d.DeltaDecArcSec));
            analysis.Centers.AddRange(BuildCenterAnalyses(ordered));
            analysis.Timeline.AddRange(BuildTimeline(ordered, analysis));
            analysis.Recommendations.AddRange(BuildRecommendations(analysis));
            return analysis;
        }

        public static SeeDriftReportPayload BuildReportPayload(
                IReadOnlyList<DriftTrackingService.CompletedTarget> batches,
                int minExposuresPerTarget,
                string sessionDateLabel) {
            var payload = new SeeDriftReportPayload {
                PluginVersion = typeof(SessionAnalysisService).Assembly.GetName().Version?.ToString() ?? "",
                SessionDate = sessionDateLabel,
                GeneratedLocal = DateTime.Now
            };

            var min = Math.Max(1, minExposuresPerTarget);
            foreach (var batch in batches) {
                foreach (var group in SplitByTarget(batch.Samples).Where(g => g.Samples.Count >= min)) {
                    var analysis = AnalyzeTarget(group.Name, group.Samples);
                    payload.Targets.Add(new SeeDriftReportTargetPayload {
                        TargetName = group.Name,
                        FrameCount = group.Samples.Count,
                        StartUtc = group.Samples.Min(s => s.ExposureStartUtc),
                        EndUtc = group.Samples.Max(s => s.ExposureStartUtc),
                        Samples = group.Samples
                            .OrderBy(s => s.FrameIndex)
                            .Select(s => {
                                GetAnchoredPoint(group.Samples, s, out var x, out var y);
                                return new SeeDriftReportSamplePayload {
                                    FrameIndex = s.FrameIndex,
                                    ExposureStartUtc = s.ExposureStartUtc,
                                    FileName = s.FileName,
                                    DeltaRaArcSec = Math.Round(x, 4),
                                    DeltaDecArcSec = Math.Round(y, 4)
                                };
                            })
                            .ToList(),
                        Analysis = analysis
                    });
                }
            }

            return payload;
        }

        private static List<(string Name, List<DriftSample> Samples)> SplitByTarget(IReadOnlyList<DriftSample> samples) {
            var groups = new Dictionary<string, List<DriftSample>>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            foreach (var s in samples.OrderBy(s => s.FrameIndex)) {
                var key = string.IsNullOrWhiteSpace(s.TargetName) ? "Unknown" : s.TargetName.Trim();
                if (!groups.TryGetValue(key, out var list)) {
                    list = new List<DriftSample>();
                    groups[key] = list;
                    order.Add(key);
                }
                list.Add(s);
            }
            return order.Select(k => (k, groups[k])).ToList();
        }

        private static DriftRateSummary BuildDriftRate(IReadOnlyList<DriftSample> ordered) {
            if (ordered.Count < 2)
                return new DriftRateSummary { FrameCount = ordered.Count, Detail = "Need at least two solved frames." };

            var first = ordered[0];
            var last = ordered[^1];
            GetAnchoredPoint(ordered, first, out var x0, out var y0);
            GetAnchoredPoint(ordered, last, out var x1, out var y1);
            var minutes = Math.Max(0.001, (last.ExposureStartUtc - first.ExposureStartUtc).TotalMinutes);
            var dRaMin = (x1 - x0) / minutes;
            var dDecMin = (y1 - y0) / minutes;
            var totalMin = Math.Sqrt(dRaMin * dRaMin + dDecMin * dDecMin);
            double? pxMin = null;
            if (TryMedianPlateScale(ordered, out var scale) && scale > 0)
                pxMin = totalMin / scale;

            return new DriftRateSummary {
                FrameCount = ordered.Count,
                DurationMinutes = minutes,
                DeltaRaArcSecPerMinute = dRaMin,
                DeltaDecArcSecPerMinute = dDecMin,
                TotalArcSecPerMinute = totalMin,
                TotalPixelsPerMinute = pxMin,
                Detail = FormattableString.Invariant(
                    $"{ordered.Count} frames over {minutes:0.#} min; net drift rate {totalMin:0.###}\"/min.")
            };
        }

        private static IEnumerable<DitherEventAnalysis> BuildDitherAnalyses(IReadOnlyList<DriftSample> ordered) {
            var allDitherSteps = ordered
                .Skip(1)
                .SelectMany(s => s.EdgeSequencerMarkers ?? new List<SequencerEdgeMarker>())
                .Where(m => m.IsDither)
                .Select(m => Math.Sqrt(m.DeltaRaArcSec * m.DeltaRaArcSec + m.DeltaDecArcSec * m.DeltaDecArcSec))
                .OrderBy(v => v)
                .ToList();
            var medianStep = MedianNonEventStep(ordered);
            var medianDitherStep = allDitherSteps.Count == 0 ? medianStep : allDitherSteps[(allDitherSteps.Count - 1) / 2];
            var weakFloor = Math.Max(2.0, medianStep * 1.25);
            var suspectFloor = allDitherSteps.Count > 1
                ? Math.Max(300.0, medianDitherStep * 5.0)
                : 600.0;
            double? prevRa = null;
            double? prevDec = null;

            foreach (var cur in ordered.Skip(1)) {
                var markers = cur.EdgeSequencerMarkers;
                if (markers == null) continue;
                foreach (var marker in markers.Where(m => m.IsDither)) {
                    var move = Math.Sqrt(marker.DeltaRaArcSec * marker.DeltaRaArcSec + marker.DeltaDecArcSec * marker.DeltaDecArcSec);
                    var suspect = move >= suspectFloor;
                    var suspectReason = suspect
                        ? FormattableString.Invariant($"Likely tracking issue: measured movement {move:0.##}\" is far outside normal logged dither scale ({medianDitherStep:0.##}\" median).")
                        : "";
                    var repeated = false;
                    if (!suspect && prevRa.HasValue && prevDec.HasValue) {
                        var prevLen = Math.Sqrt(prevRa.Value * prevRa.Value + prevDec.Value * prevDec.Value);
                        if (prevLen > 0.001 && move > 0.001) {
                            var dot = (prevRa.Value * marker.DeltaRaArcSec + prevDec.Value * marker.DeltaDecArcSec) / (prevLen * move);
                            repeated = dot > 0.85;
                        }
                    }

                    var assessment = suspect
                        ? "Suspect tracking"
                        : move < weakFloor
                            ? "Weak"
                            : repeated
                                ? "Repeated direction"
                                : "Good";
                    var detail = suspect
                        ? suspectReason + " Excluded from dither assessment."
                        : assessment == "Weak"
                            ? FormattableString.Invariant($"Measured dither movement {move:0.##}\" is near/below the session floor of {weakFloor:0.##}\".")
                            : repeated
                                ? "This dither moved mostly in the same direction as the previous logged dither."
                                : "Measured movement is clearly separated from nearby drift/noise.";

                    if (!suspect) {
                        prevRa = marker.DeltaRaArcSec;
                        prevDec = marker.DeltaDecArcSec;
                    }

                    yield return new DitherEventAnalysis {
                        FromFrameIndex = marker.FromFrameIndex,
                        ToFrameIndex = marker.ToFrameIndex,
                        EventUtc = marker.EventUtc,
                        DeltaRaArcSec = marker.DeltaRaArcSec,
                        DeltaDecArcSec = marker.DeltaDecArcSec,
                        MoveArcSec = move,
                        EquivalentPixels = TryMedianPlateScale(ordered, out var scale) && scale > 0 ? move / scale : null,
                        LoggedGuiderDx = marker.LoggedGuiderDx,
                        LoggedGuiderDy = marker.LoggedGuiderDy,
                        IsSuspect = suspect,
                        SuspectReason = suspectReason,
                        Assessment = assessment,
                        Detail = detail
                    };
                }
            }
        }

        private static IEnumerable<CenterEventAnalysis> BuildCenterAnalyses(IReadOnlyList<DriftSample> ordered) {
            var byIndex = ordered.ToDictionary(s => s.FrameIndex);
            foreach (var cur in ordered.Skip(1)) {
                var markers = cur.EdgeSequencerMarkers;
                if (markers == null) continue;
                foreach (var marker in markers.Where(m => !m.IsDither)) {
                    if (!byIndex.TryGetValue(marker.FromFrameIndex, out var prev))
                        continue;
                    GetAnchoredPoint(ordered, prev, out var px, out var py);
                    GetAnchoredPoint(ordered, cur, out var cx, out var cy);
                    var pre = Math.Sqrt(px * px + py * py);
                    var post = Math.Sqrt(cx * cx + cy * cy);
                    var residual = ordered
                        .Where(s => s.FrameIndex >= cur.FrameIndex && s.FrameIndex <= cur.FrameIndex + 3)
                        .Select(s => {
                            GetAnchoredPoint(ordered, s, out var x, out var y);
                            return Math.Sqrt(x * x + y * y);
                        })
                        .DefaultIfEmpty(post)
                        .Average();
                    var improvement = pre - residual;
                    var pct = pre > 0.001 ? (improvement / pre) * 100.0 : 0.0;
                    var assessment = improvement > Math.Max(2.0, pre * 0.25)
                        ? "Helpful"
                        : improvement > 0.5
                            ? "Partial"
                            : "Ineffective";
                    yield return new CenterEventAnalysis {
                        FromFrameIndex = marker.FromFrameIndex,
                        ToFrameIndex = marker.ToFrameIndex,
                        EventUtc = marker.EventUtc,
                        PreDriftArcSec = pre,
                        ImmediatePostDriftArcSec = post,
                        ResidualAfterFramesArcSec = residual,
                        ImprovementArcSec = improvement,
                        ImprovementPercent = pct,
                        LoggedDriftArcMin = marker.LoggedCenterDriftArcMin,
                        LoggedThresholdArcMin = marker.LoggedCenterThresholdArcMin,
                        Assessment = assessment,
                        Detail = FormattableString.Invariant(
                            $"Residual over next frames changed by {improvement:0.##}\" ({pct:0.#}%).")
                    };
                }
            }
        }

        private static IEnumerable<QualityTimelineSegment> BuildTimeline(IReadOnlyList<DriftSample> ordered, TargetAnalysis analysis) {
            if (ordered.Count < 2)
                yield break;
            for (var i = 1; i < ordered.Count; i++) {
                var prev = ordered[i - 1];
                var cur = ordered[i];
                GetAnchoredPoint(ordered, prev, out var x0, out var y0);
                GetAnchoredPoint(ordered, cur, out var x1, out var y1);
                var step = Math.Sqrt(Math.Pow(x1 - x0, 2) + Math.Pow(y1 - y0, 2));
                var markers = cur.EdgeSequencerMarkers ?? new List<SequencerEdgeMarker>();
                var label = markers.Any(m => !m.IsDither)
                    ? "center recovery"
                    : markers.Any(m => m.IsDither)
                        ? "dither recovery"
                        : step > Math.Max(5.0, analysis.DriftRate.TotalArcSecPerMinute)
                            ? "drifting"
                            : "stable";
                var tone = label == "stable" ? "good" : label == "drifting" ? "warn" : "event";
                yield return new QualityTimelineSegment {
                    StartUtc = prev.ExposureStartUtc,
                    EndUtc = cur.ExposureStartUtc,
                    Label = label,
                    Tone = tone,
                    Detail = FormattableString.Invariant($"Frame {prev.FrameIndex + 1}->{cur.FrameIndex + 1}: {step:0.##}\" step.")
                };
            }
        }

        private static IEnumerable<SessionRecommendation> BuildRecommendations(TargetAnalysis analysis) {
            if (analysis.FrameCount < 3) {
                yield return new SessionRecommendation { Level = "info", Text = "Need more solved frames before giving reliable setting hints." };
                yield break;
            }

            var emitted = false;
            if (analysis.DriftRate.TotalArcSecPerMinute > 1.0) {
                emitted = true;
                yield return new SessionRecommendation {
                    Level = "warn",
                    Text = FormattableString.Invariant($"Baseline drift is {analysis.DriftRate.TotalArcSecPerMinute:0.##}\"/min; compare altitude, tripod stability, wind, and polar/framing setup between sessions.")
                };
            }

            var assessedDithers = analysis.Dithers.Where(d => !d.IsSuspect).ToList();
            if (analysis.SuspectDitherCount > 0) {
                emitted = true;
                yield return new SessionRecommendation {
                    Level = "warn",
                    Text = FormattableString.Invariant($"Excluded {analysis.SuspectDitherCount} suspect dither interval(s) from dither assessment; discounted {analysis.SuspectDitherDiscountedAbsRaArcSec:0.#}\" RA and {analysis.SuspectDitherDiscountedAbsDecArcSec:0.#}\" Dec as likely tracking issues.")
                };
            }

            var weak = assessedDithers.Count(d => d.Assessment == "Weak");
            if (assessedDithers.Count > 0 && weak * 2 >= assessedDithers.Count) {
                emitted = true;
                yield return new SessionRecommendation {
                    Level = "warn",
                    Text = "At least half of logged dithers measured weak movement; increase dither size or check guider dither response."
                };
            }

            var repeated = assessedDithers.Count(d => d.Assessment == "Repeated direction");
            if (repeated > 0) {
                emitted = true;
                yield return new SessionRecommendation {
                    Level = "info",
                    Text = "Some dithers repeated the same direction; randomization may not be spreading walking noise as much as expected."
                };
            }

            var ineffectiveCenters = analysis.Centers.Count(c => c.Assessment == "Ineffective");
            if (analysis.Centers.Count > 0 && ineffectiveCenters * 2 >= analysis.Centers.Count) {
                emitted = true;
                yield return new SessionRecommendation {
                    Level = "warn",
                    Text = "Most center-after-drift events did not reduce residual drift much; threshold may be too low, or recenters may not settle before the next exposure."
                };
            } else if (analysis.Centers.Count == 0) {
                emitted = true;
                yield return new SessionRecommendation {
                    Level = "info",
                    Text = "No center-after-drift events correlated in this target; if drift is high, consider a lower center threshold."
                };
            }

            if (!emitted && assessedDithers.Count > 0) {
                yield return new SessionRecommendation {
                    Level = "good",
                    Text = "Dither and center behaviour looks broadly consistent in this session."
                };
            }
        }

        private static double MedianNonEventStep(IReadOnlyList<DriftSample> ordered) {
            var steps = new List<double>();
            for (var i = 1; i < ordered.Count; i++) {
                if (ordered[i].EdgeSequencerMarkers is { Count: > 0 })
                    continue;
                GetAnchoredPoint(ordered, ordered[i - 1], out var x0, out var y0);
                GetAnchoredPoint(ordered, ordered[i], out var x1, out var y1);
                steps.Add(Math.Sqrt(Math.Pow(x1 - x0, 2) + Math.Pow(y1 - y0, 2)));
            }
            if (steps.Count == 0)
                return 1.0;
            steps.Sort();
            return steps[steps.Count / 2];
        }

        private static bool TryMedianPlateScale(IReadOnlyList<DriftSample> samples, out double scale) {
            var values = samples
                .Select(s => s.NominalPlateScaleArcSecPerPx)
                .Where(v => v.HasValue && v.Value > 0)
                .Select(v => v!.Value)
                .OrderBy(v => v)
                .ToList();
            if (values.Count == 0) {
                scale = 0;
                return false;
            }
            scale = values[values.Count / 2];
            return true;
        }

        private static void GetAnchoredPoint(IReadOnlyList<DriftSample> group, DriftSample sample, out double x, out double y) {
            if (group.Count == 0) {
                x = y = 0;
                return;
            }
            var first = group.OrderBy(s => s.FrameIndex).First();
            AstrometryMath.DeltaArcSec(first.RawRaHours, first.RawDecDeg, sample.RawRaHours, sample.RawDecDeg,
                out var dRa, out var dDec);
            x = dRa;
            y = dDec;
        }
    }
}
