using System;
using System.Collections.Generic;
using NINA.Plugin.SeeDrift.Models;
using NINA.Plugin.SeeDrift.Utility;
using Xunit;

namespace NINA.Plugin.SeeDrift.Tests.Utility {

    public sealed class DitherSuspectRulesTests {

        [Fact]
        public void Flags_ra_outlier_when_other_logged_dithers_are_small_on_that_axis() {
            var group = BuildUserSessionGroup();
            DitherSuspectRules.StepAlongTrace(group, 2, out var dRa, out var dDec);
            var marker = new SequencerEdgeMarker { IsDither = true };

            var suspect = DitherSuspectRules.IsSuspectDitherInterval(group, dRa, dDec, marker, out var reason);

            Assert.True(suspect);
            Assert.Contains("ΔRA", reason);
        }

        [Fact]
        public void Does_not_flag_typical_dec_heavy_dither_when_peers_match_scale() {
            var group = BuildUserSessionGroup();
            DitherSuspectRules.StepAlongTrace(group, 4, out var dRa, out var dDec);
            var marker = new SequencerEdgeMarker { IsDither = true };

            var suspect = DitherSuspectRules.IsSuspectDitherInterval(group, dRa, dDec, marker, out _);

            Assert.False(suspect);
        }

        [Fact]
        public void Single_logged_dither_uses_high_absolute_floor() {
            var group = new List<DriftSample> {
                SampleAt(0, 0, 0),
                SampleAt(1, 700, 0, dither: true)
            };
            DitherSuspectRules.StepAlongTrace(group, 1, out var dRa, out var dDec);
            var marker = new SequencerEdgeMarker { IsDither = true };

            Assert.True(DitherSuspectRules.IsSuspectDitherInterval(group, dRa, dDec, marker, out _));
        }

        private static List<DriftSample> BuildUserSessionGroup() {
            var steps = new (int frameIndex, double dRa, double dDec)[] {
                (12, 419.902, -148.894),
                (24, 4.055, 269.426),
                (36, -3.96, -191.052),
                (48, 1.447, 64.826)
            };

            var group = new List<DriftSample> { SampleAt(0, 0, 0) };
            var cumRa = 0.0;
            var cumDec = 0.0;
            var prevIndex = 0;
            foreach (var (frameIndex, dRa, dDec) in steps) {
                if (frameIndex - 1 != prevIndex)
                    group.Add(SampleAt(frameIndex - 1, cumRa, cumDec));
                cumRa += dRa;
                cumDec += dDec;
                group.Add(SampleAt(frameIndex, cumRa, cumDec, dither: true));
                prevIndex = frameIndex;
            }

            return group;
        }

        private static DriftSample SampleAt(int frameIndex, double cumRaArcSec, double cumDecArcSec, bool dither = false) {
            const double refRaHours = 10.0;
            const double refDecDeg = 45.0;
            var decMidRad = refDecDeg * (Math.PI / 180.0);
            var raHours = refRaHours + cumRaArcSec / (15.0 * 3600.0 * Math.Cos(decMidRad));
            var decDeg = refDecDeg + cumDecArcSec / 3600.0;
            return new DriftSample {
                FrameIndex = frameIndex,
                RawRaHours = raHours,
                RawDecDeg = decDeg,
                EdgeSequencerMarkers = dither
                    ? new List<SequencerEdgeMarker> { new() { IsDither = true } }
                    : null
            };
        }
    }
}
