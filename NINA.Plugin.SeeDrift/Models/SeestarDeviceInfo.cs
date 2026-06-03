using System;

namespace NINA.Plugin.SeeDrift.Models {

    public sealed class SeestarDeviceInfo {
        public string Model { get; init; } = "";
        public string Serial { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public string FileNameToken { get; init; } = "";
        public bool IsKnown { get; init; }
        public bool IsMixed { get; init; }

        /// <summary>
        /// Default pixel scale in arcsec/pixel for this model's native (unbinned) sensor.
        /// Used as fallback when FITS header does not contain PIXSCALE / CDELT1.
        /// </summary>
        public double DefaultPixelScaleArcSec => Model.ToUpperInvariant() switch {
            "S50" => 2.39,
            "S30" => 3.99,
            "S30P" or "S30_PRO" => 3.99,
            _ => 2.39, // fallback to S50
        };

        public static SeestarDeviceInfo Unknown { get; } = new() {
            DisplayName = "Unknown scope"
        };

        public static SeestarDeviceInfo Mixed { get; } = new() {
            DisplayName = "Mixed Seestars",
            FileNameToken = "mixed_scopes",
            IsKnown = true,
            IsMixed = true
        };

        public static SeestarDeviceInfo FromId(string id) {
            id = (id ?? "").Trim();
            if (string.IsNullOrWhiteSpace(id))
                return Unknown;

            var parts = id.Split(new[] { '_' }, 2, StringSplitOptions.RemoveEmptyEntries);
            return new SeestarDeviceInfo {
                Model = parts.Length > 0 ? parts[0].Trim() : "",
                Serial = parts.Length > 1 ? parts[1].Trim() : "",
                DisplayName = id,
                FileNameToken = id,
                IsKnown = true
            };
        }
    }
}
