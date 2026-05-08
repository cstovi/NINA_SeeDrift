namespace NINA.Plugin.SeeDrift.Models {

    /// <summary>How folder import builds the pointing trace (live capture always uses FITS header RA/Dec).</summary>
    public enum FolderPlotMode {
        /// <summary>RA° vs Dec° from primary FITS keywords per file (fast).</summary>
        FitsHeaderCoordinates = 0,
        /// <summary>Cumulative detector shifts from phase cross-correlation on central crops (matches typical dither/drift in image data).</summary>
        PixelRegistration = 1
    }
}
