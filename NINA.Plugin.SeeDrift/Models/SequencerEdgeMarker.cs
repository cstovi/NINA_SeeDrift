using System;

namespace NINA.Plugin.SeeDrift.Models {

    /// <summary>
    /// One NINA sequencer event (dither or center-after-drift) in the strict interval before this frame,
    /// shown as a scatter marker along the segment from the previous frame to this one.
    /// </summary>
    public sealed class SequencerEdgeMarker {
        /// <summary>True = dither-after-exposures (triangle); false = center-after-drift (square).</summary>
        public bool IsDither { get; init; }

        /// <summary>True when the dither originated from SeeDither plugin; false for built-in DitherAfterExposures.</summary>
        public bool IsSeeDither { get; init; }

        public SequencerEventKind Kind => IsDither ? SequencerEventKind.Dither : SequencerEventKind.CenterAfterDrift;

        public DateTime EventUtc { get; init; }

        public int FromFrameIndex { get; init; }

        public int ToFrameIndex { get; init; }

        public double DeltaRaArcSec { get; init; }

        public double DeltaDecArcSec { get; init; }

        public double? DeltaPixelX { get; init; }

        public double? DeltaPixelY { get; init; }

        public double? LoggedGuiderDx { get; init; }

        public double? LoggedGuiderDy { get; init; }

        public double? LoggedCenterDriftArcMin { get; init; }

        public double? LoggedCenterThresholdArcMin { get; init; }

        /// <summary>Full multi-line tracker tooltip for this marker.</summary>
        public string Tooltip { get; init; } = "";
    }
}
