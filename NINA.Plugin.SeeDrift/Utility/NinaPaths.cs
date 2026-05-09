using System.IO;
using NINA.Profile.Interfaces;

namespace NINA.Plugin.SeeDrift.Utility {

    public static class NinaPaths {

        /// <summary>NINA “default image path” — profile image file root (<see cref="IImageFileSettings.FilePath"/>).</summary>
        public static string? TryGetDefaultImageDirectory(IProfileService profileService) {
            try {
                var p = profileService?.ActiveProfile?.ImageFileSettings?.FilePath;
                if (string.IsNullOrWhiteSpace(p))
                    return null;
                p = p.Trim();
                return Directory.Exists(p) ? p : null;
            } catch {
                return null;
            }
        }
    }
}
