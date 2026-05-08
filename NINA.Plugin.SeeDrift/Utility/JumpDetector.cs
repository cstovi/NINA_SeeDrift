using System;
using System.Collections.Generic;
using System.Linq;
using NINA.Plugin.SeeDrift.Models;

namespace NINA.Plugin.SeeDrift.Utility {

    /// <summary>
    /// Detects large frame-to-frame position jumps (dithers, slews, re-centres, etc.) in a
    /// sorted sample list and annotates each jump sample with <see cref="DriftSample.IsJump"/>
    /// and a human-readable <see cref="DriftSample.JumpReason"/>.
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
            if (samples.Count < 3) return;
            var isPixel = samples[0].IsPixelPath;

            var steps = new double[samples.Count - 1];
            for (var i = 1; i < samples.Count; i++) {
                if (isPixel) {
                    var dx = samples[i].CumulativePixelX!.Value - samples[i - 1].CumulativePixelX!.Value;
                    var dy = samples[i].CumulativePixelY!.Value - samples[i - 1].CumulativePixelY!.Value;
                    steps[i - 1] = Math.Sqrt(dx * dx + dy * dy);
                } else {
                    var dra  = samples[i].DeltaRaArcSec  - samples[i - 1].DeltaRaArcSec;
                    var ddec = samples[i].DeltaDecArcSec - samples[i - 1].DeltaDecArcSec;
                    steps[i - 1] = Math.Sqrt(dra * dra + ddec * ddec);
                }
            }

            var sorted = (double[])steps.Clone();
            Array.Sort(sorted);
            var median    = sorted[sorted.Length / 2];
            var threshold = Math.Max(median * JumpMultiplier, MinThreshold);

            for (var i = 1; i < samples.Count; i++) {
                if (steps[i - 1] > threshold) {
                    samples[i].IsJump = true;
                    samples[i].JumpReason = isPixel
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
