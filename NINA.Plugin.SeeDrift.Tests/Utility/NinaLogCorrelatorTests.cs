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

        [Fact]
        public void Real_night_log_DAE0_filtered_SeeDither_preserved() {
            var logPath = @"C:\Users\carls\Desktop\20260603-210301-3.2.0.9001.15380-202606.log";
            if (!File.Exists(logPath))
                return; // Only runs on machines with this log file

            // Minimal sample to avoid early return in AnnotateWithLogEvents
            var samples = new List<DriftSample> {
                new() {
                    FrameIndex = 1,
                    ExposureStartUtc = new DateTime(2026, 6, 3, 21, 0, 0, DateTimeKind.Utc),
                    FileName = "dummy.fits"
                }
            };

            NinaLogCorrelator.AnnotateWithLogEvents(samples, out var observations, new[] { logPath });

            // Log contains 361 DAE:0 lines (all filtered) + 71 SeeDither lines (all kept)
            // = 71 dither triggers expected
            Assert.Equal(71, observations.ObservedDitherTriggerCount);

            // All 71 SeeDither triggers carry Every: 5 → all cadence values are 5
            Assert.Equal(71, observations.DitherAfterExposuresFromLog.Count);
            Assert.All(observations.DitherAfterExposuresFromLog, v => Assert.Equal(5, v));
        }

        [Fact]
        public void Real_log_with_Dither_instruction_is_detected() {
            var logPath = @"C:\Users\carls\Desktop\20260601-103759-3.2.0.9001.15744-202606.log";
            if (!File.Exists(logPath))
                return; // Only runs on machines with this log file

            var samples = new List<DriftSample> {
                new() {
                    FrameIndex = 1,
                    ExposureStartUtc = new DateTime(2026, 6, 1, 22, 0, 0, DateTimeKind.Utc),
                    FileName = "dummy.fits"
                }
            };

            NinaLogCorrelator.AnnotateWithLogEvents(samples, out var observations, new[] { logPath });

            // Log contains 141 Dither instruction lines → all should be detected
            Assert.Equal(141, observations.ObservedDitherTriggerCount);

            // No AfterExposures cadence from instruction lines (not a trigger)
            Assert.Empty(observations.DitherAfterExposuresFromLog);

            // DitherPulse lines are also parsed (141 DirectGuider lines)
            Assert.Equal(141, observations.ObservedDitherPulseCount);
        }

        [Fact]
        public void DitherAfterExposures_zero_is_filtered_out() {
            var logPath = Path.Combine(Path.GetTempPath(), "SeeDriftTest_" + Guid.NewGuid().ToString("N") + ".log");
            var logLines = new[] {
                "2026-06-03T21:46:30.0000Z|INFO|SequenceTrigger.cs|Run|114|Starting Trigger: DitherAfterExposures, After Exposures: 0",
                "2026-06-03T21:47:00.0000Z|INFO|SequenceTrigger.cs|Run|114|Starting Trigger: DitherAfterExposures, After Exposures: 5",
                "2026-06-03T21:50:00.0000Z|INFO|SequenceTrigger.cs|Run|114|Starting Category: SeeDither, Item: SeeDitherAfterExposuresTrigger, Every: 5"
            };
            File.WriteAllLines(logPath, logLines);

            try {
                var samples = new List<DriftSample> {
                    new() { FrameIndex = 1, ExposureStartUtc = DateTime.Parse("2026-06-03T21:46:00Z"), FileName = "t_0001.fits" },
                    new() { FrameIndex = 2, ExposureStartUtc = DateTime.Parse("2026-06-03T21:48:00Z"), FileName = "t_0002.fits" },
                    new() { FrameIndex = 3, ExposureStartUtc = DateTime.Parse("2026-06-03T21:55:00Z"), FileName = "t_0003.fits" }
                };

                NinaLogCorrelator.AnnotateWithLogEvents(samples, out var observations, new[] { logPath });

                // DAE:0 is filtered → only DAE:5 + SeeDither remain
                Assert.Equal(2, observations.ObservedDitherTriggerCount);
            } finally {
                if (File.Exists(logPath))
                    File.Delete(logPath);
            }
        }
    }
}
