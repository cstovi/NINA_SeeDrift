using System;

namespace NINA.Plugin.SeeDrift.Models {

    public sealed class DriftSample {
        public int FrameIndex { get; init; }
        public DateTime ExposureStartUtc { get; init; }
        public string FileName { get; init; } = "";
        public string TargetName { get; init; } = "";
        public double DeltaRaArcSec { get; init; }
        public double DeltaDecArcSec { get; init; }
        public double RawRaHours { get; init; }
        public double RawDecDeg { get; init; }
    }
}
