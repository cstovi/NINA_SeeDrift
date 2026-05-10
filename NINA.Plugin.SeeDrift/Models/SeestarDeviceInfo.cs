using System;

namespace NINA.Plugin.SeeDrift.Models {

    public sealed class SeestarDeviceInfo {
        public string Model { get; init; } = "";
        public string Serial { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public string FileNameToken { get; init; } = "";
        public bool IsKnown { get; init; }
        public bool IsMixed { get; init; }

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
