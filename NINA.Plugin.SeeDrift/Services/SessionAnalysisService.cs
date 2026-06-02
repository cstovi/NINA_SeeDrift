using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using NINA.Plugin.SeeDrift.Models;
using NINA.Plugin.SeeDrift.Utility;

namespace NINA.Plugin.SeeDrift.Services {

    internal static class SessionAnalysisService {

        public static TargetAnalysis AnalyzeTarget(
                string targetName,
                IReadOnlyList<DriftSample> samples,
                TargetVisitPlan? visitPlan = null) {
            var ordered = samples.OrderBy(s => s.FrameIndex).ToList();
            var boundaryEdges = visitPlan?.ReturnVisitBoundaryEdges ?? Array.Empty<int>();
            var boundarySet = boundaryEdges.Count > 0
                ? new HashSet<int>(boundaryEdges)
                : null;
            var dithers = BuildDitherAnalyses(ordered, boundarySet).ToList();
            var analysis = new TargetAnalysis {
                TargetName = string.IsNullOrWhiteSpace(targetName) ? "Unknown" : targetName.Trim(),
                FrameCount = ordered.Count,
                VisitCount = visitPlan?.Visits.Count ?? 1,
                DriftRate = BuildDriftRate(ordered),
                DriftRisk = BuildDriftRisk(ordered, boundarySet, dithers),
            };

            analysis.Dithers.AddRange(dithers);
            analysis.SuspectDitherCount = analysis.Dithers.Count(d => d.IsSuspect);
            analysis.SuspectDitherDiscountedAbsRaArcSec = analysis.Dithers.Where(d => d.IsSuspect).Sum(d => Math.Abs(d.DeltaRaArcSec));
            analysis.SuspectDitherDiscountedAbsDecArcSec = analysis.Dithers.Where(d => d.IsSuspect).Sum(d => Math.Abs(d.DeltaDecArcSec));
            FillAssessedDitherIntervalStats(analysis);
            FillRealizedDitherStats(analysis);
            analysis.Centers.AddRange(BuildCenterAnalyses(ordered));
            analysis.Timeline.AddRange(BuildTimeline(ordered, analysis));
            return analysis;
        }

        public static SeeDriftReportPayload BuildReportPayload(
                IReadOnlyList<DriftTrackingService.CompletedTarget> batches,
                int minExposuresPerTarget,
                string sessionDateLabel) {
            var sourceLogs = batches
                .SelectMany(b => b.SourceLogPaths ?? Array.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var runProcessingSeconds = batches.Sum(b => b.RunDuration.TotalSeconds);

            // Build per-target analyses up-front so BuildSequencerSettings can pool the realized-dither medians
            // (run-wide value is the mean of per-target medians, matching how the comparison view averages medians).
            var min = Math.Max(1, minExposuresPerTarget);
            var targetPayloads = new List<SeeDriftReportTargetPayload>();
            foreach (var batch in batches) {
                foreach (var group in SplitByTarget(batch.Samples).Where(g => g.Samples.Count >= min)) {
                    var analysis = AnalyzeTarget(group.Name, group.Samples);
                    targetPayloads.Add(new SeeDriftReportTargetPayload {
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

            var payload = new SeeDriftReportPayload {
                PluginVersion = typeof(SessionAnalysisService).Assembly.GetName().Version?.ToString() ?? "",
                SessionDate = sessionDateLabel,
                GeneratedLocal = DateTime.Now,
                SeestarDevice = ResolveReportDevice(batches),
                SourceLogPaths = sourceLogs,
                RunProcessingSeconds = runProcessingSeconds,
                SequencerSettings = BuildSequencerSettings(batches, targetPayloads.Select(t => t.Analysis).ToList())
            };

            payload.Targets.AddRange(targetPayloads);

            return payload;
        }

        /// <summary>
        /// Aggregates per-batch <see cref="SessionLogObservations"/> into a single run-wide settings block.
        /// Returns null when no batch produced any trigger / pulse observations (older logs or runs with no events).
        /// </summary>
        internal static SessionSequencerSettings? BuildSequencerSettings(
                IReadOnlyList<DriftTrackingService.CompletedTarget> batches,
                IReadOnlyList<TargetAnalysis>? targetAnalyses = null) {
            var observations = batches
                .Select(b => b.LogObservations)
                .Where(o => o != null)
                .Cast<SessionLogObservations>()
                .ToList();
            if (observations.Count == 0)
                return null;

            var thresholds = observations.SelectMany(o => o.CenterAfterDriftThresholdArcMin).ToList();
            var ditherFromLog = observations.SelectMany(o => o.DitherAfterExposuresFromLog).ToList();
            var ditherInferred = observations.SelectMany(o => o.DitherAfterExposuresInferredGapsFrames).ToList();
            var centerEvalGaps = observations.SelectMany(o => o.CenterAfterDriftEvaluateGapsFrames).ToList();
            var pulseMags = observations.SelectMany(o => o.DitherPulseMagnitudePixels).ToList();
            var durFirst = observations.SelectMany(o => o.DitherGuideDurationsFirstSec).ToList();
            var durSecond = observations.SelectMany(o => o.DitherGuideDurationsSecondSec).ToList();

            var centerEvalCount = observations.Sum(o => o.ObservedCenterEvaluationCount);
            var ditherTriggerCount = observations.Sum(o => o.ObservedDitherTriggerCount);
            var ditherPulseCount = observations.Sum(o => o.ObservedDitherPulseCount);

            if (centerEvalCount == 0 && ditherTriggerCount == 0 && ditherPulseCount == 0)
                return null;

            // Threshold: round to 0.1 arcmin before mode/min/max so float jitter from logs collapses to the slider step.
            double? centerMaxArcMin = null;
            double? centerMaxMin = null;
            double? centerMaxMaxV = null;
            if (thresholds.Count > 0) {
                var rounded = thresholds.Select(v => Math.Round(v, 1)).ToList();
                centerMaxArcMin = ModeOrFirst(rounded);
                centerMaxMin = rounded.Min();
                centerMaxMaxV = rounded.Max();
            }

            int? ditherN = null;
            int? ditherMin = null;
            int? ditherMax = null;
            var cadenceInferred = false;
            if (ditherFromLog.Count > 0) {
                ditherN = ModeOrFirst(ditherFromLog);
                ditherMin = ditherFromLog.Min();
                ditherMax = ditherFromLog.Max();
            } else if (ditherInferred.Count > 0) {
                ditherN = ModeOrFirst(ditherInferred);
                ditherMin = ditherInferred.Min();
                ditherMax = ditherInferred.Max();
                cadenceInferred = true;
            }

            int? centerEvalN = null;
            int? centerEvalMin = null;
            int? centerEvalMax = null;
            if (centerEvalGaps.Count > 0) {
                centerEvalN = ModeOrFirst(centerEvalGaps);
                centerEvalMin = centerEvalGaps.Min();
                centerEvalMax = centerEvalGaps.Max();
            }

            // Pool realized-dither stats: run-wide value is the mean of the per-target medians, weighted by sample count
            // for stability across short batches (matches the "average of per-target medians" rule from the plan).
            double? realizedCommanded = null;
            double? realizedMeasured = null;
            double? realizedRatio = null;
            var realizedSampleTotal = 0;
            if (targetAnalyses != null && targetAnalyses.Count > 0) {
                var contributors = targetAnalyses
                    .Where(a => a.RealizedDitherSampleCount > 0
                                && a.MedianCommandedDitherPixels.HasValue
                                && a.MedianRealizedDitherPixels.HasValue
                                && a.MedianRealizedDitherRatio.HasValue)
                    .ToList();
                if (contributors.Count > 0) {
                    realizedSampleTotal = contributors.Sum(a => a.RealizedDitherSampleCount);
                    realizedCommanded = contributors.Average(a => a.MedianCommandedDitherPixels!.Value);
                    realizedMeasured = contributors.Average(a => a.MedianRealizedDitherPixels!.Value);
                    realizedRatio = contributors.Average(a => a.MedianRealizedDitherRatio!.Value);
                }
            }

            return new SessionSequencerSettings {
                DitherPixelsMedian = pulseMags.Count > 0 ? Median(pulseMags) : (double?)null,
                DitherPulseCount = ditherPulseCount,

                CenterMaxArcMin = centerMaxArcMin,
                CenterMaxArcMinMin = centerMaxMin,
                CenterMaxArcMinMax = centerMaxMaxV,

                CenterEvaluateAfterExposures = centerEvalN,
                CenterEvaluateAfterExposuresMin = centerEvalMin,
                CenterEvaluateAfterExposuresMax = centerEvalMax,
                CenterEvaluateInferred = true,

                DitherAfterExposuresN = ditherN,
                DitherAfterExposuresNMin = ditherMin,
                DitherAfterExposuresNMax = ditherMax,
                DitherCadenceInferred = cadenceInferred,

                DitherGuideDurationFirstSecMedian = durFirst.Count > 0 ? Median(durFirst) : (double?)null,
                DitherGuideDurationSecondSecMedian = durSecond.Count > 0 ? Median(durSecond) : (double?)null,
                GuideDurationSampleCount = Math.Min(durFirst.Count, durSecond.Count),

                ObservedCenterEvaluationCount = centerEvalCount,
                ObservedDitherTriggerCount = ditherTriggerCount,

                RealizedCommandedDitherPixels = realizedCommanded,
                RealizedMeasuredDitherPixels = realizedMeasured,
                RealizedDitherRatio = realizedRatio,
                RealizedSampleCount = realizedSampleTotal
            };
        }

        private static double Median(IReadOnlyList<double> values) {
            var sorted = values.OrderBy(v => v).ToList();
            var n = sorted.Count;
            return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
        }

        private static T ModeOrFirst<T>(IReadOnlyList<T> values) where T : notnull {
            var counts = values
                .GroupBy(v => v)
                .Select(g => new { Value = g.Key, Count = g.Count() })
                .ToList();
            var topCount = counts.Max(g => g.Count);
            var top = counts.Where(g => g.Count == topCount).ToList();
            if (top.Count == 1)
                return top[0].Value;
            return values[0];
        }

        private static SeestarDeviceInfo ResolveReportDevice(IReadOnlyList<DriftTrackingService.CompletedTarget> batches) {
            var devices = batches
                .Select(b => b.SeestarDevice)
                .Where(d => d is { IsKnown: true } && !string.IsNullOrWhiteSpace(d.DisplayName))
                .Select(d => d.DisplayName.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (devices.Count == 0)
                return SeestarDeviceInfo.Unknown;
            if (devices.Count > 1 || devices.Any(d => d.Equals(SeestarDeviceInfo.Mixed.DisplayName, StringComparison.OrdinalIgnoreCase)))
                return SeestarDeviceInfo.Mixed;
            return SeestarDeviceInfo.FromId(devices[0]);
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

        private const double WalkingFallbackConsistencyCaution = 0.40;
        private const double WalkingLowConsistencyCeiling = 0.25;
        private const double WalkingBurstRatioModerate = 1.5;
        private const double WalkingBurstRatioCaution = 2.0;

        private static DriftRiskSummary BuildDriftRisk(
                IReadOnlyList<DriftSample> ordered,
                HashSet<int>? returnVisitBoundaryEdges,
                IReadOnlyList<DitherEventAnalysis> dithers) {
            if (ordered.Count < 3) {
                return new DriftRiskSummary {
                    Status = "Not enough data",
                    Tone = "info",
                    StarShapeStatus = "Not enough data",
                    StarShapeTone = "info",
                    WalkingNoiseStatus = "Not enough data",
                    WalkingNoiseTone = "info",
                    Detail = "Need at least three solved frames before judging drift consistency."
                };
            }

            var intervals = new List<(int EdgeIndex, double Dx, double Dy, double Step, double Minutes)>();
            for (var i = 1; i < ordered.Count; i++) {
                if (returnVisitBoundaryEdges != null && returnVisitBoundaryEdges.Contains(i))
                    continue;
                var markers = ordered[i].EdgeSequencerMarkers ?? new List<SequencerEdgeMarker>();
                if (markers.Any())
                    continue;

                GetAnchoredPoint(ordered, ordered[i - 1], out var x0, out var y0);
                GetAnchoredPoint(ordered, ordered[i], out var x1, out var y1);
                var dx = x1 - x0;
                var dy = y1 - y0;
                var step = Math.Sqrt(dx * dx + dy * dy);
                if (step < 0.001)
                    continue;

                var minutes = Math.Max(0.001, (ordered[i].ExposureStartUtc - ordered[i - 1].ExposureStartUtc).TotalMinutes);
                intervals.Add((i, dx, dy, step, minutes));
            }

            if (intervals.Count < 2) {
                return new DriftRiskSummary {
                    Status = "Not enough drift-only intervals",
                    Tone = "info",
                    IntervalCount = intervals.Count,
                    StarShapeStatus = "Not enough data",
                    StarShapeTone = "info",
                    WalkingNoiseStatus = "Not enough data",
                    WalkingNoiseTone = "info",
                    Detail = "Most measured movement is attached to logged dither or center events, so SeeDrift is not separating a natural drift trend here."
                };
            }

            var path = intervals.Sum(i => i.Step);
            var netDx = intervals.Sum(i => i.Dx);
            var netDy = intervals.Sum(i => i.Dy);
            var net = Math.Sqrt(netDx * netDx + netDy * netDy);
            var duration = intervals.Sum(i => i.Minutes);
            var rate = duration > 0 ? path / duration : 0;
            var consistency = path > 0 ? Math.Clamp(net / path, 0.0, 1.0) : 0.0;
            double? pxRate = null;
            var hasScale = TryMedianPlateScale(ordered, out var scale) && scale > 0;
            if (hasScale)
                pxRate = rate / scale;
            var medianExposureSeconds = MedianExposureSeconds(ordered);
            var driftPerExposure = medianExposureSeconds.HasValue
                ? rate * (medianExposureSeconds.Value / 60.0)
                : (double?)null;
            var driftPerExposurePx = driftPerExposure.HasValue && hasScale
                ? driftPerExposure.Value / scale
                : (double?)null;
            var (windowFrames, windowDrift) = WorstShortWindowDrift(intervals);
            var windowPx = windowDrift.HasValue && hasScale
                ? windowDrift.Value / scale
                : (double?)null;

            var (starStatus, starTone) = ClassifyStarShape(driftPerExposure, driftPerExposurePx, consistency);

            var assessedDithers = dithers.Where(d => !d.IsSuspect).ToList();
            var assessedMovesArc = assessedDithers.Select(d => d.MoveArcSec).OrderBy(v => v).ToList();
            double? medianAssessedDitherArc = assessedMovesArc.Count > 0
                ? assessedMovesArc[(assessedMovesArc.Count - 1) / 2]
                : null;
            double? medianAssessedDitherPx = medianAssessedDitherArc.HasValue && hasScale
                ? medianAssessedDitherArc.Value / scale
                : null;

            ComputeBetweenDitherDrift(ordered, hasScale, scale, assessedMovesArc,
                out var medianDitherArc, out var medianDitherPx,
                out var betweenArc, out var betweenPx, out var ditherWindowCount);
            var headroom = ComputeHeadroom(medianDitherPx, betweenPx, medianDitherArc, betweenArc);

            var (walkStatus, walkTone, walkReason) = ClassifyWalkingNoise(
                consistency, headroom, betweenPx, betweenArc, windowDrift, windowPx, ditherWindowCount,
                assessedDithers.Count, medianAssessedDitherPx);

            var detail = BuildDriftRiskDetail(
                intervals.Count, rate, consistency,
                starStatus, walkStatus, walkReason,
                driftPerExposurePx, driftPerExposure, headroom);

            return new DriftRiskSummary {
                Status = "",
                Tone = "",
                IntervalCount = intervals.Count,
                DurationMinutes = duration,
                NaturalDriftArcSecPerMinute = rate,
                NaturalDriftPixelsPerMinute = pxRate,
                NetNaturalDriftArcSec = net,
                DirectionConsistency = consistency,
                EstimatedDriftPerExposureArcSec = driftPerExposure,
                EstimatedDriftPerExposurePixels = driftPerExposurePx,
                WorstWindowDriftArcSec = windowDrift,
                WorstWindowDriftPixels = windowPx,
                WorstWindowFrameCount = windowFrames,
                StarShapeStatus = starStatus,
                StarShapeTone = starTone,
                WalkingNoiseStatus = walkStatus,
                WalkingNoiseTone = walkTone,
                BetweenDitherDriftArcSec = betweenArc,
                BetweenDitherDriftPixels = betweenPx,
                DitherHeadroomRatio = headroom,
                WalkingNoiseReason = walkReason,
                Detail = detail
            };
        }

        /// <summary>
        /// Star-shape sub-tier: how much motion lands within a single exposure (round vs elongated stars).
        /// The cited rule-of-thumb is "<1-2 px per exposure is acceptable" — Caution starts above ~2.5 px.
        /// Consistency can nudge the verdict but only when a small per-exposure floor is also met.
        /// </summary>
        private static (string Status, string Tone) ClassifyStarShape(
                double? perExposureArcSec,
                double? perExposurePixels,
                double consistency) {
            var level = 0;
            if (perExposurePixels.HasValue) {
                if (perExposurePixels.Value >= 2.5)
                    level = Math.Max(level, 2);
                else if (perExposurePixels.Value >= 1.0)
                    level = Math.Max(level, 1);
            } else if (perExposureArcSec.HasValue) {
                if (perExposureArcSec.Value >= 5.0)
                    level = Math.Max(level, 2);
                else if (perExposureArcSec.Value >= 2.0)
                    level = Math.Max(level, 1);
            }

            // Consistency-only escalation is gated by a magnitude floor so a low but directional drift
            // can't push the star-shape verdict to Caution on its own.
            var moderateFloor = perExposurePixels.HasValue
                ? perExposurePixels.Value >= 0.5
                : perExposureArcSec.HasValue && perExposureArcSec.Value >= 1.5;
            var smallFloor = perExposurePixels.HasValue
                ? perExposurePixels.Value >= 0.3
                : perExposureArcSec.HasValue && perExposureArcSec.Value >= 0.8;

            if (level == 1 && consistency >= 0.85 && moderateFloor)
                level = 2;
            else if (level == 0 && consistency >= 0.85 && smallFloor)
                level = 1;

            if (level >= 2)
                return ("Caution", "warn");
            if (level == 1)
                return ("Moderate", "ok");
            return ("Low", "good");
        }

        /// <summary>
        /// Walking-noise sub-tier: cumulative coherent drift between dithers vs the dither magnitude.
        /// References (CN Jon Rista, freestar8n, Wade, Spokeshave) treat this combination — not per-exposure
        /// magnitude — as the root cause of correlated FPN in the stacked image.
        /// Falls back to the worst-short-window check when no logged dithers exist for a headroom comparison.
        /// </summary>
        private static (string Status, string Tone, string Reason) ClassifyWalkingNoise(
                double consistency,
                double? headroom,
                double? betweenDriftPx,
                double? betweenDriftArcSec,
                double? worstWindowArcSec,
                double? worstWindowPx,
                int ditherWindowCount,
                int assessedDitherCount,
                double? medianAssessedDitherPx) {
            if (assessedDitherCount >= 2 && consistency < WalkingLowConsistencyCeiling) {
                return ("Low", "good", FormattableString.Invariant(
                    $"{consistency:P0} directional drift — not coherent enough for walking-noise concern despite short-window movement."));
            }

            if (ditherWindowCount >= 1 && headroom.HasValue) {
                var pxFloorCaution = betweenDriftPx.HasValue
                    ? betweenDriftPx.Value >= 0.5
                    : betweenDriftArcSec.HasValue && betweenDriftArcSec.Value >= 2.0;
                var pxFloorModerate = betweenDriftPx.HasValue
                    ? betweenDriftPx.Value >= 0.25
                    : betweenDriftArcSec.HasValue && betweenDriftArcSec.Value >= 1.0;

                if (consistency >= 0.75 && headroom.Value < 1.5 && pxFloorCaution) {
                    return ("Caution", "warn", FormattableString.Invariant(
                        $"dither headroom {headroom.Value:0.0}× with {consistency:P0} directional drift between dithers."));
                }
                if (consistency >= 0.5 && headroom.Value < 4.0 && pxFloorModerate) {
                    return ("Moderate", "ok", FormattableString.Invariant(
                        $"dither headroom {headroom.Value:0.0}× with {consistency:P0} directional drift."));
                }
                return ("Low", "good", FormattableString.Invariant(
                    $"dither headroom {headroom.Value:0.0}× clears the between-dither drift."));
            }

            if (assessedDitherCount >= 2
                && medianAssessedDitherPx.HasValue
                && worstWindowPx.HasValue) {
                var burstRatio = worstWindowPx.Value / Math.Max(0.05, medianAssessedDitherPx.Value);
                if (burstRatio < WalkingBurstRatioModerate) {
                    return ("Low", "good", FormattableString.Invariant(
                        $"worst 5-frame drift {worstWindowPx.Value:0.##} px is below {WalkingBurstRatioModerate:0.0}× the median assessed dither ({medianAssessedDitherPx.Value:0.##} px)."));
                }
                if (burstRatio >= WalkingBurstRatioCaution
                    && consistency >= WalkingFallbackConsistencyCaution) {
                    return ("Caution", "warn", FormattableString.Invariant(
                        $"short-window drift {worstWindowPx.Value:0.##} px is {burstRatio:0.0}× the median assessed dither with {consistency:P0} directional drift."));
                }
                if (burstRatio >= WalkingBurstRatioModerate
                    && consistency >= WalkingFallbackConsistencyCaution) {
                    return ("Moderate", "ok", FormattableString.Invariant(
                        $"short-window drift {worstWindowPx.Value:0.##} px is {burstRatio:0.0}× the median assessed dither."));
                }
                return ("Low", "good", FormattableString.Invariant(
                    $"short-window drift is elevated but dither size ({medianAssessedDitherPx.Value:0.##} px median) still dominates."));
            }

            // Fallback: no assessed dithers for a ratio comparison — use the short-window check with a consistency gate.
            var level = 0;
            if (worstWindowPx.HasValue) {
                if (worstWindowPx.Value >= 5.0) level = 2;
                else if (worstWindowPx.Value >= 1.0) level = 1;
            } else if (worstWindowArcSec.HasValue) {
                if (worstWindowArcSec.Value >= 20.0) level = 2;
                else if (worstWindowArcSec.Value >= 4.0) level = 1;
            }
            if (level >= 2 && consistency < WalkingFallbackConsistencyCaution)
                level = 1;
            if (level == 1 && consistency >= 0.75) level = 2;
            else if (level == 0 && consistency >= 0.85) level = 1;

            var reason = assessedDitherCount == 0
                ? "no logged dithers in this run — using worst short-window drift instead"
                : ditherWindowCount == 0
                    ? "could not separate drift between logged dithers — using worst short-window drift instead"
                    : "only one logged dither — using worst short-window drift instead";
            if (level >= 2)
                return ("Caution", "warn", reason);
            if (level == 1)
                return ("Moderate", "ok", reason);
            return ("Low", "good", reason);
        }

        private static string BuildDriftRiskDetail(
                int intervalCount, double rate, double consistency,
                string starStatus, string walkStatus, string walkReason,
                double? perExposurePixels, double? perExposureArcSec, double? headroom) {
            var perSub = perExposurePixels.HasValue
                ? FormattableString.Invariant($"per-exposure motion {perExposurePixels.Value:0.##} px")
                : perExposureArcSec.HasValue
                    ? FormattableString.Invariant($"per-exposure motion {perExposureArcSec.Value:0.##}\"")
                    : "per-exposure motion unknown";
            var head = headroom.HasValue
                ? FormattableString.Invariant($" Dither headroom {headroom.Value:0.0}×.")
                : "";
            return FormattableString.Invariant(
                $"Advisory: {intervalCount} natural-drift intervals; {rate:0.##}\"/min, {consistency:P0} directional, {perSub}.{head} {walkReason}.");
        }

        /// <summary>
        /// Collects the per-segment cumulative natural drift (net vector magnitude) between consecutive
        /// logged dither markers, plus the median dither magnitude. Returns 0 windows if there are not
        /// at least two dither markers in this target's samples.
        /// </summary>
        private static void ComputeBetweenDitherDrift(
                IReadOnlyList<DriftSample> ordered,
                bool hasScale, double scale,
                IReadOnlyList<double> assessedDitherMovesArc,
                out double? medianDitherArc, out double? medianDitherPx,
                out double? betweenArc, out double? betweenPx,
                out int windowCount) {
            medianDitherArc = null;
            medianDitherPx = null;
            betweenArc = null;
            betweenPx = null;
            windowCount = 0;

            var ditherEdges = new List<int>();
            for (var i = 1; i < ordered.Count; i++) {
                var markers = ordered[i].EdgeSequencerMarkers ?? new List<SequencerEdgeMarker>();
                if (markers.Any(m => m.IsDither))
                    ditherEdges.Add(i);
            }
            if (ditherEdges.Count == 0)
                return;

            // Median dither magnitude (arcsec) from the markers themselves.
            var ditherMagsArc = new List<double>();
            foreach (var idx in ditherEdges) {
                foreach (var m in ordered[idx].EdgeSequencerMarkers!.Where(mk => mk.IsDither)) {
                    ditherMagsArc.Add(Math.Sqrt(m.DeltaRaArcSec * m.DeltaRaArcSec + m.DeltaDecArcSec * m.DeltaDecArcSec));
                }
            }
            if (ditherMagsArc.Count > 0) {
                ditherMagsArc.Sort();
                medianDitherArc = ditherMagsArc[(ditherMagsArc.Count - 1) / 2];
                if (hasScale)
                    medianDitherPx = medianDitherArc.Value / scale;
            }

            if (assessedDitherMovesArc.Count > 0) {
                medianDitherArc = assessedDitherMovesArc[(assessedDitherMovesArc.Count - 1) / 2];
                if (hasScale)
                    medianDitherPx = medianDitherArc.Value / scale;
            }

            if (ditherEdges.Count < 2)
                return;

            // For each pair of consecutive dither edges, sum the natural-drift-only steps strictly between them.
            var segmentMagsArc = new List<double>();
            for (var w = 0; w + 1 < ditherEdges.Count; w++) {
                var a = ditherEdges[w];
                var b = ditherEdges[w + 1];
                double sumDx = 0, sumDy = 0;
                var any = false;
                for (var k = a + 1; k < b; k++) {
                    var markers = ordered[k].EdgeSequencerMarkers ?? new List<SequencerEdgeMarker>();
                    if (markers.Any()) continue;
                    GetAnchoredPoint(ordered, ordered[k - 1], out var x0, out var y0);
                    GetAnchoredPoint(ordered, ordered[k], out var x1, out var y1);
                    sumDx += (x1 - x0);
                    sumDy += (y1 - y0);
                    any = true;
                }
                if (any)
                    segmentMagsArc.Add(Math.Sqrt(sumDx * sumDx + sumDy * sumDy));
            }
            windowCount = segmentMagsArc.Count;
            if (windowCount == 0)
                return;
            segmentMagsArc.Sort();
            betweenArc = segmentMagsArc[(segmentMagsArc.Count - 1) / 2];
            if (hasScale)
                betweenPx = betweenArc.Value / scale;
        }

        private static double? ComputeHeadroom(double? medianDitherPx, double? betweenPx,
                double? medianDitherArc, double? betweenArc) {
            if (medianDitherPx.HasValue && betweenPx.HasValue)
                return medianDitherPx.Value / Math.Max(0.05, betweenPx.Value);
            if (medianDitherArc.HasValue && betweenArc.HasValue)
                return medianDitherArc.Value / Math.Max(0.2, betweenArc.Value);
            return null;
        }

        /// <summary>
        /// Pairs commanded dither magnitude (from the matched DirectGuider pulse line) with the measured
        /// frame-to-frame magnitude to give a per-target "realized %" view. NINA runs the dither pulse to
        /// completion before settle time starts, so a consistent shortfall most often points at mount
        /// mechanics (Dec backlash on direction-reversing pulses, or a small guide-rate calibration offset);
        /// a too-short settle time is a secondary cause via motion-blur centroid bias during the next
        /// exposure. Suspect dithers are excluded (same rule as totals/effectiveness).
        /// </summary>
        private static void FillRealizedDitherStats(TargetAnalysis analysis) {
            var commanded = new List<double>();
            var measured = new List<double>();
            var ratios = new List<double>();
            foreach (var d in analysis.Dithers.Where(d => !d.IsSuspect)) {
                if (!d.LoggedGuiderDx.HasValue || !d.LoggedGuiderDy.HasValue)
                    continue;
                if (!d.EquivalentPixels.HasValue)
                    continue;
                var cmd = Math.Sqrt(d.LoggedGuiderDx.Value * d.LoggedGuiderDx.Value
                                    + d.LoggedGuiderDy.Value * d.LoggedGuiderDy.Value);
                if (cmd < 0.01)
                    continue;
                var meas = d.EquivalentPixels.Value;
                commanded.Add(cmd);
                measured.Add(meas);
                ratios.Add(meas / cmd);
            }
            analysis.RealizedDitherSampleCount = ratios.Count;
            if (ratios.Count == 0)
                return;
            analysis.MedianCommandedDitherPixels = Median(commanded);
            analysis.MedianRealizedDitherPixels = Median(measured);
            analysis.MedianRealizedDitherRatio = Median(ratios);
        }

        /// <summary>
        /// Fills the run-wide assessed-dither-interval totals (Σ|ΔRA| / Σ|ΔDec| and median |Δ|) on the analysis,
        /// excluding suspect intervals so reported sums match the dither assessment table and effectiveness scoring.
        /// </summary>
        private static void FillAssessedDitherIntervalStats(TargetAnalysis analysis) {
            var assessed = analysis.Dithers.Where(d => !d.IsSuspect).ToList();
            analysis.DitherIntervalAssessedSumAbsRaArcSec = assessed.Sum(d => Math.Abs(d.DeltaRaArcSec));
            analysis.DitherIntervalAssessedSumAbsDecArcSec = assessed.Sum(d => Math.Abs(d.DeltaDecArcSec));
            if (assessed.Count == 0)
                return;
            var moves = assessed.Select(d => d.MoveArcSec).OrderBy(v => v).ToList();
            analysis.DitherIntervalMedianMoveArcSec = moves[(moves.Count - 1) / 2];
            var px = assessed.Where(d => d.EquivalentPixels.HasValue).Select(d => d.EquivalentPixels!.Value).OrderBy(v => v).ToList();
            if (px.Count > 0)
                analysis.DitherIntervalMedianMovePixels = px[(px.Count - 1) / 2];
        }

        private static double? MedianExposureSeconds(IReadOnlyList<DriftSample> ordered) {
            var values = ordered
                .Select(s => s.ExposureDurationSeconds)
                .Where(v => v.HasValue && v.Value > 0)
                .Select(v => v!.Value)
                .OrderBy(v => v)
                .ToList();
            return values.Count == 0 ? null : values[(values.Count - 1) / 2];
        }

        private static (int FrameCount, double? DriftArcSec) WorstShortWindowDrift(
                IReadOnlyList<(int EdgeIndex, double Dx, double Dy, double Step, double Minutes)> intervals) {
            if (intervals.Count == 0)
                return (0, null);
            var preferredFrames = new[] { 5, 10, 15 };
            var bestFrames = 0;
            double? best = null;
            foreach (var frames in preferredFrames) {
                var edgeCount = frames - 1;
                if (intervals.Count < edgeCount)
                    continue;
                for (var i = 0; i <= intervals.Count - edgeCount; i++) {
                    var window = intervals.Skip(i).Take(edgeCount).ToList();
                    if (!IsConsecutive(window))
                        continue;
                    var dx = window.Sum(w => w.Dx);
                    var dy = window.Sum(w => w.Dy);
                    var drift = Math.Sqrt(dx * dx + dy * dy);
                    if (!best.HasValue || drift > best.Value) {
                        best = drift;
                        bestFrames = frames;
                    }
                }
            }
            if (best.HasValue)
                return (bestFrames, best);

            var longest = LongestConsecutiveRun(intervals);
            if (longest.Count == 0)
                return (0, null);
            var ldx = longest.Sum(w => w.Dx);
            var ldy = longest.Sum(w => w.Dy);
            return (longest.Count + 1, Math.Sqrt(ldx * ldx + ldy * ldy));
        }

        private static bool IsConsecutive(IReadOnlyList<(int EdgeIndex, double Dx, double Dy, double Step, double Minutes)> window) {
            for (var i = 1; i < window.Count; i++) {
                if (window[i].EdgeIndex != window[i - 1].EdgeIndex + 1)
                    return false;
            }
            return true;
        }

        private static List<(int EdgeIndex, double Dx, double Dy, double Step, double Minutes)> LongestConsecutiveRun(
                IReadOnlyList<(int EdgeIndex, double Dx, double Dy, double Step, double Minutes)> intervals) {
            var best = new List<(int EdgeIndex, double Dx, double Dy, double Step, double Minutes)>();
            var cur = new List<(int EdgeIndex, double Dx, double Dy, double Step, double Minutes)>();
            foreach (var interval in intervals) {
                if (cur.Count == 0 || interval.EdgeIndex == cur[^1].EdgeIndex + 1) {
                    cur.Add(interval);
                } else {
                    if (cur.Count > best.Count)
                        best = cur;
                    cur = new List<(int EdgeIndex, double Dx, double Dy, double Step, double Minutes)> { interval };
                }
            }
            return cur.Count > best.Count ? cur : best;
        }

        private static IEnumerable<DitherEventAnalysis> BuildDitherAnalyses(
                IReadOnlyList<DriftSample> ordered,
                HashSet<int>? returnVisitBoundaryEdges) {
            var medianStep = MedianNonEventStep(ordered, returnVisitBoundaryEdges);
            var weakFloor = Math.Max(2.0, medianStep * 1.25);
            var byIndex = ordered.ToDictionary(s => s.FrameIndex);
            double? prevRa = null;
            double? prevDec = null;

            for (var i = 1; i < ordered.Count; i++) {
                if (returnVisitBoundaryEdges != null && returnVisitBoundaryEdges.Contains(i))
                    continue;
                var cur = ordered[i];
                var markers = cur.EdgeSequencerMarkers;
                if (markers == null) continue;
                foreach (var marker in markers.Where(m => m.IsDither)) {
                    if (!byIndex.TryGetValue(marker.FromFrameIndex, out var prev))
                        continue;

                    GetAnchoredPoint(ordered, prev, out var x0, out var y0);
                    GetAnchoredPoint(ordered, cur, out var x1, out var y1);
                    var dRa = x1 - x0;
                    var dDec = y1 - y0;
                    var move = Math.Sqrt(dRa * dRa + dDec * dDec);
                    var suspect = DitherSuspectRules.IsSuspectDitherInterval(ordered, dRa, dDec, marker, out var suspectReason);
                    var repeated = false;
                    if (!suspect && prevRa.HasValue && prevDec.HasValue) {
                        var prevLen = Math.Sqrt(prevRa.Value * prevRa.Value + prevDec.Value * prevDec.Value);
                        if (prevLen > 0.001 && move > 0.001) {
                            var dot = (prevRa.Value * dRa + prevDec.Value * dDec) / (prevLen * move);
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
                        prevRa = dRa;
                        prevDec = dDec;
                    }

                    yield return new DitherEventAnalysis {
                        FromFrameIndex = marker.FromFrameIndex,
                        ToFrameIndex = marker.ToFrameIndex,
                        EventUtc = marker.EventUtc,
                        DeltaRaArcSec = dRa,
                        DeltaDecArcSec = dDec,
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
                string label;
                string tone;
                if (markers.Any(m => !m.IsDither)) {
                    label = "center";
                    tone = "center";
                } else if (markers.Any(m => m.IsDither)) {
                    label = "dither";
                    tone = "dither";
                } else if (step > Math.Max(5.0, analysis.DriftRate.TotalArcSecPerMinute)) {
                    label = "drifting";
                    tone = "warn";
                } else {
                    label = "tracking";
                    tone = "good";
                }
                yield return new QualityTimelineSegment {
                    StartUtc = prev.ExposureStartUtc,
                    EndUtc = cur.ExposureStartUtc,
                    Label = label,
                    Tone = tone,
                    Detail = FormattableString.Invariant($"Frame {prev.FrameIndex + 1}->{cur.FrameIndex + 1}: {step:0.##}\" step.")
                };
            }
        }

        private static double MedianNonEventStep(IReadOnlyList<DriftSample> ordered, HashSet<int>? returnVisitBoundaryEdges) {
            var steps = new List<double>();
            for (var i = 1; i < ordered.Count; i++) {
                if (returnVisitBoundaryEdges != null && returnVisitBoundaryEdges.Contains(i))
                    continue;
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
