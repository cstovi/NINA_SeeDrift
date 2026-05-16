using System;
using System.Collections.Generic;
using System.Linq;
using NINA.Plugin.SeeDrift.Models;

namespace NINA.Plugin.SeeDrift.Utility {

    /// <summary>
    /// Flags logged dither intervals that look like tracking slews or plate-solve glitches rather than honest dither steps.
    /// </summary>
    internal static class DitherSuspectRules {

        /// <summary>Measured move must stay below this multiple of the commanded guider pulse magnitude.</summary>
        public const double CommandedPulseOversizeRatio = 2.5;

        private const double AbsoluteSuspectFloorArcSec = 300.0;
        private const double SingleDitherSuspectFloorArcSec = 600.0;
        private const double MedianMultiplier = 2.75;
        private const double SinglePeerMoveMultiplier = 2.25;

        private const double AbsoluteAxisSuspectFloorArcSec = 120.0;
        private const double AxisMedianMultiplier = 4.0;
        private const double AxisRelativeMultiplier = 3.0;
        private const double StepMatchToleranceArcSec = 0.05;

        public static bool IsSuspectDitherInterval(
                IReadOnlyList<DriftSample> group,
                double dRaArcSec,
                double dDecArcSec,
                SequencerEdgeMarker ditherMarker,
                out string reason) {
            reason = "";
            var move = Math.Sqrt(dRaArcSec * dRaArcSec + dDecArcSec * dDecArcSec);
            var steps = CollectAnchoredDitherSteps(group);
            var peers = ExcludeMatchingStep(steps, dRaArcSec, dDecArcSec);

            if (peers.Count == 0) {
                if (move >= SingleDitherSuspectFloorArcSec) {
                    reason = FormattableString.Invariant(
                        $"Likely tracking issue: measured {move:0.##}″ exceeds the single-dither suspect floor ({SingleDitherSuspectFloorArcSec:0.##}″).");
                    return true;
                }
            } else {
                var peerMoves = peers.Select(p => p.Move).OrderBy(v => v).ToList();
                var vectorFloor = peers.Count >= 2
                    ? Math.Max(AbsoluteSuspectFloorArcSec, Median(peerMoves) * MedianMultiplier)
                    : Math.Max(AbsoluteSuspectFloorArcSec, peerMoves[0] * SinglePeerMoveMultiplier);
                if (move >= vectorFloor) {
                    reason = FormattableString.Invariant(
                        $"Likely tracking issue: measured {move:0.##}″ is far outside other logged dithers on this target (floor {vectorFloor:0.##}″).");
                    return true;
                }

                if (peers.Count >= 2) {
                    var peerAbsRa = peers.Select(p => Math.Abs(p.Ra)).OrderBy(v => v).ToList();
                    var peerAbsDec = peers.Select(p => Math.Abs(p.Dec)).OrderBy(v => v).ToList();
                    if (IsAxisOutlier(Math.Abs(dRaArcSec), peerAbsRa, "RA", out var axisReason)
                        || IsAxisOutlier(Math.Abs(dDecArcSec), peerAbsDec, "Dec", out axisReason)) {
                        reason = axisReason;
                        return true;
                    }
                }
            }

            if (TryGetCommandedPulsePixels(ditherMarker, out var commandedPx)
                && TryMedianNominalPlateScale(group, out var arcSecPerPx)
                && arcSecPerPx > 0) {
                var measuredPx = move / arcSecPerPx;
                var limitPx = CommandedPulseOversizeRatio * commandedPx;
                if (measuredPx >= limitPx) {
                    reason = FormattableString.Invariant(
                        $"Measured {measuredPx:0.##} px vs {commandedPx:0.##} px commanded pulse ({measuredPx / commandedPx:0.1}×, limit {CommandedPulseOversizeRatio}×) — likely tracking/slew, not dither.");
                    return true;
                }
            }

            return false;
        }

        /// <summary>Session-wide vector floor using all logged dither moves (upper bound for reporting).</summary>
        public static double CalculateSessionSuspectFloorArcSec(IReadOnlyList<DriftSample> group) {
            var moves = CollectAnchoredDitherSteps(group).Select(s => s.Move).ToList();
            if (moves.Count == 0)
                return double.PositiveInfinity;
            moves.Sort();
            return moves.Count > 1
                ? Math.Max(AbsoluteSuspectFloorArcSec, Median(moves) * MedianMultiplier)
                : SingleDitherSuspectFloorArcSec;
        }

        private static bool IsAxisOutlier(double absComponent, IReadOnlyList<double> peerAbsSorted, string axisLabel, out string reason) {
            reason = "";
            var median = Median(peerAbsSorted);
            var floor = Math.Max(AbsoluteAxisSuspectFloorArcSec, median * AxisMedianMultiplier);
            if (absComponent >= floor && absComponent >= median * AxisRelativeMultiplier) {
                reason = FormattableString.Invariant(
                    $"Likely tracking issue: |Δ{axisLabel}| {absComponent:0.##}″ is far outside other logged dithers (median |Δ{axisLabel}| {median:0.##}″ on this target).");
                return true;
            }
            return false;
        }

        private static double Median(IReadOnlyList<double> sortedValues) {
            return sortedValues[(sortedValues.Count - 1) / 2];
        }

        private static List<DitherStep> ExcludeMatchingStep(IReadOnlyList<DitherStep> steps, double dRaArcSec, double dDecArcSec) {
            var peers = new List<DitherStep>(steps.Count);
            foreach (var step in steps) {
                if (Math.Abs(step.Ra - dRaArcSec) <= StepMatchToleranceArcSec
                    && Math.Abs(step.Dec - dDecArcSec) <= StepMatchToleranceArcSec)
                    continue;
                peers.Add(step);
            }
            return peers;
        }

        private readonly struct DitherStep {
            public double Ra { get; init; }
            public double Dec { get; init; }
            public double Move { get; init; }
        }

        private static List<DitherStep> CollectAnchoredDitherSteps(IReadOnlyList<DriftSample> group) {
            var steps = new List<DitherStep>();
            if (group.Count < 2)
                return steps;

            for (var i = 1; i < group.Count; i++) {
                if (group[i].EdgeSequencerMarkers?.Any(m => m.IsDither) != true)
                    continue;
                StepAlongTrace(group, i, out var dRa, out var dDec);
                steps.Add(new DitherStep {
                    Ra = dRa,
                    Dec = dDec,
                    Move = Math.Sqrt(dRa * dRa + dDec * dDec)
                });
            }

            return steps;
        }

        internal static void StepAlongTrace(IReadOnlyList<DriftSample> group, int toIndex, out double dRa, out double dDec) {
            var r0 = group[0].RawRaHours;
            var d0 = group[0].RawDecDeg;
            AstrometryMath.DeltaArcSec(r0, d0, group[toIndex - 1].RawRaHours, group[toIndex - 1].RawDecDeg,
                out var ra0, out var dec0);
            AstrometryMath.DeltaArcSec(r0, d0, group[toIndex].RawRaHours, group[toIndex].RawDecDeg,
                out var ra1, out var dec1);
            dRa = ra1 - ra0;
            dDec = dec1 - dec0;
        }

        private static bool TryGetCommandedPulsePixels(SequencerEdgeMarker marker, out double pixels) {
            pixels = 0;
            if (!marker.LoggedGuiderDx.HasValue || !marker.LoggedGuiderDy.HasValue)
                return false;
            pixels = Math.Sqrt(marker.LoggedGuiderDx.Value * marker.LoggedGuiderDx.Value
                               + marker.LoggedGuiderDy.Value * marker.LoggedGuiderDy.Value);
            return pixels >= 0.01;
        }

        private static bool TryMedianNominalPlateScale(IReadOnlyList<DriftSample> group, out double medianArcSecPerPx) {
            medianArcSecPerPx = 0;
            var list = group
                .Select(s => s.NominalPlateScaleArcSecPerPx)
                .Where(v => v.HasValue && v.Value > 0)
                .Select(v => v!.Value)
                .OrderBy(v => v)
                .ToList();
            if (list.Count == 0)
                return false;
            medianArcSecPerPx = list[list.Count / 2];
            return true;
        }
    }
}
