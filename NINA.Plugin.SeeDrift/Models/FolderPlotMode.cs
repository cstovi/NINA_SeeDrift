namespace NINA.Plugin.SeeDrift.Models {

    /// <summary>How the plotted trace is built for folder import and for live capture while armed (mode snapshotted at <c>Arm()</c>).</summary>
    public enum FolderPlotMode {
        /// <summary>ΔRA/ΔDec from primary FITS keywords per file (fast).</summary>
        FitsHeaderCoordinates = 0,
        /// <summary>Cumulative detector shifts from central-crop SSD template registration (sub-pixel refined).</summary>
        PixelRegistration = 1
    }
}
