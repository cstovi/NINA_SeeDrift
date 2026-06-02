using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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

        [Fact]
        public void SeeDither_attaches_to_gap_after_current_frame_when_log_matches_save_time() {
            var logPath = Path.Combine(Path.GetTempPath(), "SeeDriftTest_" + Guid.NewGuid().ToString("N") + ".log");
            var logLines = new[] {
                "2026-05-30T23:26:25.0000Z|INFO|BaseImageData.cs|SaveToDisk|346|Saved image to C:\\temp\\Target_LIGHT_0001.fits",
                "2026-05-30T23:26:50.0000Z|INFO|BaseImageData.cs|SaveToDisk|346|Saved image to C:\\temp\\Target_LIGHT_0002.fits",
                "2026-05-30T23:26:50.0000Z|INFO|SequenceTrigger.cs|Run|114|Starting Category: SeeDither, Item: SeeDitherAfterExposuresTrigger, Every: 15",
                "2026-05-30T23:26:50.0000Z|INFO|SeeDitherLog.cs|Info|9|[SeeDither] Dithering by RA=-10\" Dec=20\" → RA=...",
                "2026-05-30T23:27:20.0000Z|INFO|BaseImageData.cs|SaveToDisk|346|Saved image to C:\\temp\\Target_LIGHT_0003.fits"
            };
            File.WriteAllLines(logPath, logLines);

            try {
                var samples = new List<DriftSample> {
                    new() {
                        FrameIndex = 1,
                        ExposureStartUtc = DateTime.Parse("2026-05-30T23:26:00Z"),
                        FileName = "Target_LIGHT_0001.fits",
                        DeltaRaArcSec = 0,
                        DeltaDecArcSec = 0,
                        RawRaHours = 0,
                        RawDecDeg = 0
                    },
                    new() {
                        FrameIndex = 2,
                        ExposureStartUtc = DateTime.Parse("2026-05-30T23:26:30Z"),
                        FileName = "Target_LIGHT_0002.fits",
                        DeltaRaArcSec = 0,
                        DeltaDecArcSec = 0,
                        RawRaHours = 0,
                        RawDecDeg = 0
                    },
                    new() {
                        FrameIndex = 3,
                        ExposureStartUtc = DateTime.Parse("2026-05-30T23:27:30Z"),
                        FileName = "Target_LIGHT_0003.fits",
                        DeltaRaArcSec = 0,
                        DeltaDecArcSec = 0,
                        RawRaHours = 0,
                        RawDecDeg = 0
                    }
                };

                NinaLogCorrelator.AnnotateWithLogEvents(samples, out _, new[] { logPath });

                var secondFrameMarkers = samples.Single(s => s.FrameIndex == 2).EdgeSequencerMarkers;
                Assert.True(secondFrameMarkers == null || secondFrameMarkers.Count == 0);

                var thirdFrameMarkers = samples.Single(s => s.FrameIndex == 3).EdgeSequencerMarkers;
                var marker = Assert.Single(thirdFrameMarkers!);
                Assert.True(marker.IsDither);
                Assert.Contains("SeeDither", marker.Tooltip);
            } finally {
                if (File.Exists(logPath)) {
                    File.Delete(logPath);
                }
            }
        }

        [Fact]
        public void TryCollectSavedImagePaths_respects_local_clock_window_when_log_has_no_offset() {
            var armLocal = new DateTime(2026, 6, 1, 22, 0, 0, DateTimeKind.Local);
            var utcOffset = TimeZoneInfo.Local.GetUtcOffset(armLocal);
            if (utcOffset == TimeSpan.Zero && !TimeZoneInfo.Local.SupportsDaylightSavingTime)
                return; // Environment already UTC — no difference to assert.

            var disarmLocal = armLocal.AddHours(6);
            var logPath = Path.Combine(Path.GetTempPath(), "SeeDriftTest_" + Guid.NewGuid().ToString("N") + ".log");

            static string FormatLocal(DateTime dt) => dt.ToString("yyyy-MM-ddTHH:mm:ss.ffff", CultureInfo.InvariantCulture);

            var logLines = new[] {
                $"{FormatLocal(armLocal.AddMinutes(-5))}|INFO|BaseImageData.cs|SaveToDisk|0|Saved image to C\\temp\\before_arm.fits",
                $"{FormatLocal(armLocal.AddMinutes(5))}|INFO|BaseImageData.cs|SaveToDisk|0|Saved image to C\\temp\\within_window_1.fits",
                $"{FormatLocal(disarmLocal.AddMinutes(-5))}|INFO|BaseImageData.cs|SaveToDisk|0|Saved image to C\\temp\\within_window_2.fits"
            };
            File.WriteAllLines(logPath, logLines);

            try {
                var success = NinaLogCorrelator.TryCollectSavedImagePathsFromLogs(
                    new[] { logPath },
                    armLocal.ToUniversalTime(),
                    disarmLocal.ToUniversalTime(),
                    out var ordered,
                    out _,
                    out _);

                Assert.True(success);
                Assert.Equal(new[] {
                        "C\\temp\\within_window_1.fits",
                        "C\\temp\\within_window_2.fits"
                    },
                    ordered.Select(o => o.path).ToArray());
            } finally {
                if (File.Exists(logPath))
                    File.Delete(logPath);
            }
        }
    }
}
