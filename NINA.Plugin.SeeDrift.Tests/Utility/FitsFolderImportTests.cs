using NINA.Plugin.SeeDrift.Utility;
using Xunit;

namespace NINA.Plugin.SeeDrift.Tests.Utility {

    public sealed class FitsFolderImportTests {

        [Fact]
        public void FormatBetweenFramesLabel_uses_exposure_number_from_fits_name() {
            var label = FitsFolderImport.FormatBetweenFramesLabel(
                "Target_LIGHT_20.00s_0011.fits", 11,
                "Target_LIGHT_20.00s_0012.fits", 12);

            Assert.Equal("Frames 11→12", label);
        }

        [Fact]
        public void FormatBetweenFramesLabel_falls_back_to_trace_position_when_names_unparsed() {
            var label = FitsFolderImport.FormatBetweenFramesLabel(
                "frame_a.fits", 2,
                "frame_b.fits", 3);

            Assert.Equal("Frames 3→4", label);
        }
    }
}
