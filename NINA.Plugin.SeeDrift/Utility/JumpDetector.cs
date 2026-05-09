using System;
using System.Collections.Generic;
using System.Linq;
using NINA.Plugin.SeeDrift.Models;

namespace NINA.Plugin.SeeDrift.Utility {

    /// <summary>
    /// Detects large frame-to-frame position jumps (dithers, slews, re-centres, etc.) in a
    /// sorted sample list and annotates each jump sample with <see cref="DriftSample.IsJump"/>
    /// and a human-readable <see cref="DriftSample.JumpReason"/>. Uses the same coordinates as
    /// the drift plot when possible (derived ΔRA/ΔDec vs pixels vs FITS headers).
    /// </summary>
    internal static class JumpDetector {

        /// <summary>
        /// Step must be &gt; this multiple of the median step to be called a jump.
        /// </summary>
        private const double JumpMultiplier = 4.0;

        /// <summary>Absolute minimum threshold in pixels (or arcsec for header mode).</summary>
        private const double MinThreshold = 2.0;

        /// <summary>
        /// Annotates <paramref name="samples"/> (already in frame order) in-place.
        /// Works for both pixel-registration and header-coordinate samples.
        /// </summary>
        public static void AnnotateJumps(List<DriftSample> samples) {
            foreach (var s in samples) {
                s.IsJump = false;
                s.JumpReason = null;
            }
            if (samples.Count < 3) return;
            var isPixel = samples[0].IsPixelPath;
            // When every frame has plate-scale-derived ΔRA/ΔDec, the plot uses those values; jump
            // detection must use the same (per-frame dec / parallactic angle break tie with pixels).
            var allPixelDerived = isPixel && samples.All(s => s.HasPixelDerivedRaDec);

            var steps = new double[samples.Count - 1];
            for (var i = 1; i < samples.Count; i++) {
                var prev = samples[i - 1];
                var cur  = samples[i];
                if (allPixelDerived) {
                    var dra  = cur.PixelDerivedRaArcSec!.Value  - prev.PixelDerivedRaArcSec!.Value;
                    var ddec = cur.PixelDerivedDecArcSec!.Value - prev.PixelDerivedDecArcSec!.Value;
                    steps[i - 1] = Math.Sqrt(dra * dra + ddec * ddec);
                } else if (isPixel) {
                    var dx = cur.CumulativePixelX!.Value - prev.CumulativePixelX!.Value;
                    var dy = cur.CumulativePixelY!.Value - prev.CumulativePixelY!.Value;
                    steps[i - 1] = Math.Sqrt(dx * dx + dy * dy);
                } else {
                    var dra  = cur.DeltaRaArcSec  - prev.DeltaRaArcSec;
                    var ddec = cur.DeltaDecArcSec - prev.DeltaDecArcSec;
                    steps[i - 1] = Math.Sqrt(dra * dra + ddec * ddec);
                }
            }

            var sorted = (double[])steps.Clone();
            Array.Sort(sorted);
            var median    = sorted[sorted.Length / 2];
            double threshold;
            if (isPixel && !allPixelDerived) {
                var dev = new double[steps.Length];
                for (var k = 0; k < steps.Length; k++)
                    dev[k] = Math.Abs(steps[k] - median);
                Array.Sort(dev);
                var mad = dev[dev.Length / 2];
                threshold = Math.Max(Math.Max(median * JumpMultiplier, median + 3.5 * mad), MinThreshold);
            } else {
                threshold = Math.Max(median * JumpMultiplier, MinThreshold);
            }

            for (var i = 1; i < samples.Count; i++) {
                if (steps[i - 1] > threshold) {
                    samples[i].IsJump = true;
                    samples[i].JumpReason = allPixelDerived
                        ? $"Large shift {steps[i - 1]:F1}″ (median {median:F1}″, derived from pixels)"
                        : isPixel
                            ? $"Large shift {steps[i - 1]:F1}px (median {median:F1}px)"
                            : $"Large shift {steps[i - 1]:F1}\" (median {median:F1}\")";
                }
            }
        }

        /// <summary>Returns the number of jump frames in <paramref name="samples"/>.</summary>
        public static int CountJumps(IEnumerable<DriftSample> samples) =>
            samples.Count(s => s.IsJump);
    }
}
