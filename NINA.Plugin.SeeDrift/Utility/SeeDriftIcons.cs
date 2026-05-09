using System.Windows.Media;

namespace NINA.Plugin.SeeDrift.Utility {

    /// <summary>
    /// Single source for the small SeeDrift vector (sequencer palette, sequence steps, plugin chrome).
    /// Assign <see cref="InstructionIcon"/> to <c>SequenceItem.Icon</c> so it always appears without relying on host resource timing.
    /// </summary>
    internal static class SeeDriftIcons {

        /// <summary>Frozen 24×24-style drift motif: corner axes + rising drift trace (distinct silhouette at small size).</summary>
        public static GeometryGroup InstructionIcon { get; }

        static SeeDriftIcons() {
            var gg = new GeometryGroup { FillRule = FillRule.Nonzero };
            gg.Children.Add(PathGeometry.Parse("M6,18 L6,8 L18,8"));
            gg.Children.Add(PathGeometry.Parse("M8,15 L11,12 L14,13 L17,9"));
            gg.Freeze();
            InstructionIcon = gg;
        }
    }
}
