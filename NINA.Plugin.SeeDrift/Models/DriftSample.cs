using System;
using System.Collections.Generic;

namespace NINA.Plugin.SeeDrift.Models {

    public sealed class DriftSample {
        public int FrameIndex { get; init; }
        public DateTime ExposureStartUtc { get; init; }
        public string FileName { get; init; } = "";
        public string TargetName { get; init; } = "";
        public double DeltaRaArcSec { get; init; }
        public double DeltaDecArcSec { get; init; }
        public double RawRaHours { get; init; }
        public double RawDecDeg { get; init; }

        /// <summary>Cumulative detector X shift in pixels (folder import + pixel registration mode).</summary>
        public double? CumulativePixelX { get; init; }

        /// <summary>Cumulative detector Y shift in pixels (down positive).</summary>
        public double? CumulativePixelY { get; init; }

        /// <summary>Nominal arcsec/px from FITS (<c>XPIXSZ</c> + <c>FOCALLEN</c>) when readable; used for pixel-equivalent totals in HTML.</summary>
        public double? NominalPlateScaleArcSecPerPx { get; init; }

        /// <summary>Exposure duration in seconds from FITS (<c>EXPTIME</c> / related keywords) when readable.</summary>
        public double? ExposureDurationSeconds { get; init; }

        public bool IsPixelPath => CumulativePixelX.HasValue && CumulativePixelY.HasValue;

        /// <summary>
        /// ΔRA derived from pixel registration + mount-mode conversion (arcsec, relative to frame 1).
        /// Populated only in pixel registration mode when plate scale and orientation are available.
        /// </summary>
        public double? PixelDerivedRaArcSec  { get; set; }

        /// <summary>ΔDec derived from pixel registration + mount-mode conversion (arcsec, relative to frame 1).</summary>
        public double? PixelDerivedDecArcSec { get; set; }

        public bool HasPixelDerivedRaDec => PixelDerivedRaArcSec.HasValue && PixelDerivedDecArcSec.HasValue;

        /// <summary>True when the step from the previous frame exceeds the jump threshold.</summary>
        public bool IsJump { get; set; }

        /// <summary>Human-readable reason for the jump from step detection (not from NINA log trigger names).</summary>
        public string? JumpReason { get; set; }

        /// <summary>Unused; between-frame sequencer tooltips use <see cref="EdgeSequencerMarkers"/>.</summary>
        public string? SequencerLogHint { get; set; }

        // --- Inter-frame edge (FrameIndex-1 → FrameIndex), from NINA log correlation ---

        /// <summary>
        /// NINA log triggers in the strict interval before this frame, in chronological order.
        /// Each entry is drawn along the segment from the previous plotted point to this one (evenly spaced around the midpoint when there are several).
        /// </summary>
        public List<SequencerEdgeMarker>? EdgeSequencerMarkers { get; set; }
    }
}

