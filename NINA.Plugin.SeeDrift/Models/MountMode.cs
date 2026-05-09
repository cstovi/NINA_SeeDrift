namespace NINA.Plugin.SeeDrift.Models {

    /// <summary>Mount mode — determines how pixel shifts are converted to RA/Dec.</summary>
    public enum MountMode {
        /// <summary>Equatorial platform: field rotation is cancelled, sensor ≈ North up. Pure scale conversion.</summary>
        EQ,
        /// <summary>Alt/Az tripod: camera is level, field rotates. Per-frame parallactic angle applied before scaling.</summary>
        AltAz
    }
}
