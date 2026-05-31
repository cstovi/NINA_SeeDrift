using System;
using System.Collections.Generic;
using System.IO;
using NINA.Plugin.SeeDrift.Models;
using NINA.Plugin.SeeDrift.Utility;
using Xunit;

namespace NINA.Plugin.SeeDrift.Tests.Utility {

    public sealed class NinaLogCorrelatorTests {

        [Fact]
        public void AnnotateWithLogEvents_detects_SeeDither_trigger_and_log_lines() {
            var logPath = Path.Combine(Path.GetTempPath(), "SeeDriftTest_" + Guid.NewGuid().ToString("N") + ".log");
            var logLines = new[] {
                "2026-05-30T23:02:23.5194Z|INFO|SequenceTrigger.cs|Run|114|Starting Category: SeeDither, Item: SeeDitherAfterExposuresTrigger, Every: 15",
                "2026-05-30T23:02:23.8775Z|INFO|SeeDitherLog.cs|Info|9|[SeeDither] Dithering by RA=101.7\" Dec=-167.6\" → RA=12:18:20 Dec=69° 16' 24\""
            };
            File.WriteAllLines(logPath, logLines);

            try {
                var samples = new List<DriftSample> {
                    new() {
                        FrameIndex = 1,
                        ExposureStartUtc = DateTime.Parse("2000-01-01T00:00:00Z"),
                        FileName = "caldwell3_0001.fits",
                        DeltaRaArcSec = 0,
                        DeltaDecArcSec = 0,
                        RawRaHours = 0,
                        RawDecDeg = 0
                    },
                    new() {
                        FrameIndex = 2,
                        ExposureStartUtc = DateTime.Parse("2100-01-01T00:00:00Z"),
                        FileName = "caldwell3_0002.fits",
                        DeltaRaArcSec = 0,
                        DeltaDecArcSec = 0,
                        RawRaHours = 0,
                        RawDecDeg = 0,
                        IsJump = true
                    }
                };

                var result = NinaLogCorrelator.AnnotateWithLogEvents(samples, out var observations, new[] { logPath });

                Assert.Equal(1, observations.ObservedDitherTriggerCount);
                Assert.Equal(1, result.TraceDitherTriggers);
                Assert.Contains(15, observations.DitherAfterExposuresFromLog);

                var markers = samples[1].EdgeSequencerMarkers;
                Assert.NotNull(markers);
                var marker = Assert.Single(markers!);
                Assert.True(marker.IsDither);
                Assert.Contains("SeeDither", marker.Tooltip);
                Assert.Contains("ΔRA 101.7", marker.Tooltip);
            } finally {
                if (File.Exists(logPath)) {
                    File.Delete(logPath);
                }
            }
        }
    }
}
