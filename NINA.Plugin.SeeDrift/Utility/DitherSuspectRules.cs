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
        private const double MedianMultiplier = 5.0;

        public static bool IsSuspectDitherInterval(
                IReadOnlyList<DriftSample> group,
                double dRaArcSec,
                double dDecArcSec,
                SequencerEdgeMarker ditherMarker,
                out string reason) {
            reason = "";
            var move = Math.Sqrt(dRaArcSec * dRaArcSec + dDecArcSec * dDecArcSec);

            var floor = CalculateSessionSuspectFloorArcSec(group);
            if (move >= floor) {
                reason =
                    $"Likely tracking issue: measured {move:0.##}″ is far outside normal logged dither scale (floor {floor:0.##}″).";
                return true;
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

        public static double CalculateSessionSuspectFloorArcSec(IReadOnlyList<DriftSample> group) {
            var moves = CollectAnchoredDitherMoveArcSec(group);
            if (moves.Count == 0)
                return double.PositiveInfinity;
            moves.Sort();
            var median = moves[(moves.Count - 1) / 2];
            return moves.Count > 1
                ? Math.Max(AbsoluteSuspectFloorArcSec, median * MedianMultiplier)
                : SingleDitherSuspectFloorArcSec;
        }

        private static List<double> CollectAnchoredDitherMoveArcSec(IReadOnlyList<DriftSample> group) {
            var moves = new List<double>();
            if (group.Count < 2)
                return moves;

            for (var i = 1; i < group.Count; i++) {
                if (group[i].EdgeSequencerMarkers?.Any(m => m.IsDither) != true)
                    continue;
                StepAlongTrace(group, i, out var dRa, out var dDec);
                moves.Add(Math.Sqrt(dRa * dRa + dDec * dDec));
            }

            return moves;
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
