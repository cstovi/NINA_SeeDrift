using System;
using System.Collections.Generic;
using NINA.Plugin.SeeDrift.Models;
using NINA.Plugin.SeeDrift.Utility;
using Xunit;

namespace NINA.Plugin.SeeDrift.Tests.Utility {

    public sealed class JumpDetectorTests {

        private static DriftSample Sample(int frame, double? px = null, double? py = null,
            double? pdr = null, double? pdDec = null) => new() {
            FrameIndex = frame,
            CumulativePixelX = px,
            CumulativePixelY = py,
            PixelDerivedRaArcSec = pdr,
            PixelDerivedDecArcSec = pdDec,
        };

        [Fact]
        public void AnnotateJumps_clears_previous_annotations() {
            var samples = new List<DriftSample> { Sample(0), Sample(1) };
            samples[0].IsJump = true;
            samples[0].JumpReason = "old";
            JumpDetector.AnnotateJumps(samples);
            Assert.False(samples[0].IsJump);
            Assert.Null(samples[0].JumpReason);
        }

        [Fact]
        public void AnnotateJumps_empty_list_does_not_throw() {
            var samples = new List<DriftSample>();
            JumpDetector.AnnotateJumps(samples);
        }

        [Fact]
        public void AnnotateJumps_two_samples_no_jumps() {
            var samples = new List<DriftSample> { Sample(0, 0, 0), Sample(1, 1, 1) };
            JumpDetector.AnnotateJumps(samples);
            Assert.False(samples[1].IsJump);
        }

        [Fact]
        public void AnnotateJumps_small_steps_no_jumps() {
            var samples = new List<DriftSample>();
            for (int i = 0; i < 5; i++)
                samples.Add(Sample(i, i * 0.1, i * 0.1));
            JumpDetector.AnnotateJumps(samples);
            Assert.False(JumpDetector.CountJumps(samples) > 0);
        }

        [Fact]
        public void AnnotateJumps_large_jump_detected() {
            var samples = new List<DriftSample> { Sample(0, 0, 0), Sample(1, 1, 1) };
            // Frame 2: huge jump from frame 1 (50px vs typical 0.1px steps)
            samples.Add(Sample(2, 50, 50));
            for (int i = 3; i < 10; i++) {
                var prev = samples[samples.Count - 1];
                samples.Add(Sample(i, prev.CumulativePixelX!.Value + 0.1, prev.CumulativePixelY!.Value + 0.1));
            }
            JumpDetector.AnnotateJumps(samples);
            Assert.True(samples[2].IsJump);
            Assert.NotNull(samples[2].JumpReason);
        }

        [Fact]
        public void AnnotateJumps_pixel_derived_mode_detects_jumps() {
            var samples = new List<DriftSample> { Sample(0, 0, 0, 0, 0), Sample(1, 1, 1, 0.1, 0.1) };
            // Big jump in derived coordinates
            samples.Add(Sample(2, 50, 50, 500, 500));
            for (int i = 3; i < 8; i++) {
                var prev = samples[samples.Count - 1];
                samples.Add(Sample(i, prev.CumulativePixelX!.Value + 0.1, prev.CumulativePixelY!.Value + 0.1,
                    prev.PixelDerivedRaArcSec!.Value + 0.1, prev.PixelDerivedDecArcSec!.Value + 0.1));
            }
            JumpDetector.AnnotateJumps(samples);
            Assert.True(samples[2].IsJump);
        }

        [Fact]
        public void CountJumps_returns_total() {
            var samples = new List<DriftSample> { Sample(0, 0, 0) };
            // Normal step
            samples.Add(Sample(1, 1, 1));
            // Jump
            samples.Add(Sample(2, 50, 50));
            // Another jump
            samples.Add(Sample(3, 100, 100));
            for (int i = 4; i < 8; i++) {
                var prev = samples[samples.Count - 1];
                samples.Add(Sample(i, prev.CumulativePixelX!.Value + 0.1, prev.CumulativePixelY!.Value + 0.1));
            }
            JumpDetector.AnnotateJumps(samples);
            Assert.Equal(2, JumpDetector.CountJumps(samples));
        }

        [Fact]
        public void AnnotateJumps_jump_reason_contains_median_pixel() {
            var samples = new List<DriftSample> { Sample(0, 0, 0), Sample(1, 1, 1) };
            samples.Add(Sample(2, 50, 50));
            for (int i = 3; i < 6; i++) {
                var prev = samples[samples.Count - 1];
                samples.Add(Sample(i, prev.CumulativePixelX!.Value + 0.1, prev.CumulativePixelY!.Value + 0.1));
            }
            JumpDetector.AnnotateJumps(samples);
            Assert.Contains("px", samples[2].JumpReason ?? string.Empty);
        }

        [Fact]
        public void AnnotateJumps_jump_reason_contains_arcsec_pixel_derived() {
            var samples = new List<DriftSample> { Sample(0, 0, 0, 0, 0), Sample(1, 1, 1, 0.1, 0.1) };
            samples.Add(Sample(2, 50, 50, 500, 500));
            for (int i = 3; i < 6; i++) {
                var prev = samples[samples.Count - 1];
                samples.Add(Sample(i, prev.CumulativePixelX!.Value + 0.1, prev.CumulativePixelY!.Value + 0.1,
                    prev.PixelDerivedRaArcSec!.Value + 0.1, prev.PixelDerivedDecArcSec!.Value + 0.1));
            }
            JumpDetector.AnnotateJumps(samples);
            Assert.Contains("derived from pixels", samples[2].JumpReason ?? string.Empty);
        }

        [Fact]
        public void AnnotateJumps_multiple_jumps() {
            var samples = new List<DriftSample> { Sample(0, 0, 0) };
            for (int i = 1; i < 5; i++) {
                var prev = samples[samples.Count - 1];
                // Normal step
                samples.Add(Sample(i, prev.CumulativePixelX!.Value + 0.1, prev.CumulativePixelY!.Value + 0.1));
            }
            // Jump at frame 5
            samples.Add(Sample(5, 60, 60));
            for (int i = 6; i < 8; i++) {
                var prev = samples[samples.Count - 1];
                samples.Add(Sample(i, prev.CumulativePixelX!.Value + 0.1, prev.CumulativePixelY!.Value + 0.1));
            }
            // Jump at frame 8
            samples.Add(Sample(8, 120, 120));

            JumpDetector.AnnotateJumps(samples);
            Assert.Equal(2, JumpDetector.CountJumps(samples));
            Assert.True(samples[5].IsJump);
            Assert.True(samples[8].IsJump);
        }
    }
}
