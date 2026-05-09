namespace NINA.Plugin.SeeDrift.Models {

    /// <summary>
    /// One NINA sequencer event (dither or center-after-drift) in the strict interval before this frame,
    /// shown as a scatter marker along the segment from the previous frame to this one.
    /// </summary>
    public sealed class SequencerEdgeMarker {
        /// <summary>True = dither-after-exposures (triangle); false = center-after-drift (square).</summary>
        public bool IsDither { get; init; }

        /// <summary>Full multi-line tracker tooltip for this marker.</summary>
        public string Tooltip { get; init; } = "";
    }
}
