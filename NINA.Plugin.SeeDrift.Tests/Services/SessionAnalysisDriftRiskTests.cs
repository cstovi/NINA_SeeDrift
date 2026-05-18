using System;
using System.Collections.Generic;
using System.Linq;
using NINA.Plugin.SeeDrift.Models;
using NINA.Plugin.SeeDrift.Services;
using Xunit;

namespace NINA.Plugin.SeeDrift.Tests.Services {

    public sealed class SessionAnalysisDriftRiskTests {

        private const double Scale = 3.988;
        private static readonly DateTime BaseUtc = new(2026, 5, 18, 4, 0, 0, DateTimeKind.Utc);

        private static DriftSample Frame(
                int index,
                double raArcSec,
                double decArcSec,
                List<SequencerEdgeMarker>? markers = null) {
            const double ra0Hours = 12.0;
            const double dec0Deg = 30.0;
            var raHours = ra0Hours + raArcSec / (15.0 * 3600.0 * Math.Cos(dec0Deg * Math.PI / 180.0));
            var decDeg = dec0Deg + decArcSec / 3600.0;
            return new DriftSample {
                FrameIndex = index,
                ExposureStartUtc = BaseUtc.AddMinutes(index * 0.5),
                FileName = $"frame_{index}.fits",
                NominalPlateScaleArcSecPerPx = Scale,
                ExposureDurationSeconds = 30,
                RawRaHours = raHours,
                RawDecDeg = decDeg,
                EdgeSequencerMarkers = markers
            };
        }

        private static SequencerEdgeMarker DitherMarker(int from, int to, double markerRa = 0, double markerDec = 0) =>
            new() {
                IsDither = true,
                FromFrameIndex = from,
                ToFrameIndex = to,
                EventUtc = BaseUtc.AddMinutes(to * 0.5),
                DeltaRaArcSec = markerRa,
                DeltaDecArcSec = markerDec
            };

        [Fact]
        public void LargeDithers_ZigzagDrift_WalkingNoiseLow() {
            var samples = new List<DriftSample> { Frame(0, 0, 0) };
            var ra = 0.0;
            var dec = 0.0;
            for (var i = 1; i <= 24; i++) {
                if (i % 5 == 0) {
                    dec += 28.0;
                    samples.Add(Frame(i, ra, dec, new List<SequencerEdgeMarker> {
                        DitherMarker(i - 1, i)
                    }));
                } else {
                    ra += (i % 2 == 0) ? 3.0 : -3.0;
                    samples.Add(Frame(i, ra, dec));
                }
            }

            var analysis = SessionAnalysisService.AnalyzeTarget("M 84-like", samples);

            Assert.Equal("Low", analysis.DriftRisk.WalkingNoiseStatus);
            Assert.Equal("good", analysis.DriftRisk.WalkingNoiseTone);
            Assert.True(analysis.DriftRisk.DitherHeadroomRatio is > 1.5,
                $"expected headroom from measured dithers, got {analysis.DriftRisk.DitherHeadroomRatio}");
            Assert.True(analysis.DriftRisk.DirectionConsistency < 0.25);
        }

        [Fact]
        public void CoherentDrift_SmallDithers_WalkingNoiseElevated() {
            var samples = new List<DriftSample> { Frame(0, 0, 0) };
            var ra = 0.0;
            for (var i = 1; i <= 19; i++) {
                ra += 2.5;
                if (i % 5 == 0) {
                    ra += 5.0;
                    samples.Add(Frame(i, ra, 0, new List<SequencerEdgeMarker> {
                        DitherMarker(i - 1, i)
                    }));
                } else {
                    samples.Add(Frame(i, ra, 0));
                }
            }

            var analysis = SessionAnalysisService.AnalyzeTarget("coherent-small-dither", samples);

            Assert.True(analysis.DriftRisk.DirectionConsistency >= 0.5);
            Assert.Contains(analysis.DriftRisk.WalkingNoiseStatus, new[] { "Moderate", "Caution" });
        }

        [Fact]
        public void ShortWindowBurst_BelowDitherRatio_WalkingNoiseLow() {
            var samples = new List<DriftSample> { Frame(0, 0, 0) };
            var ra = 0.0;
            var dec = 0.0;
            for (var i = 1; i <= 19; i++) {
                if (i is 5 or 10 or 15) {
                    dec += 30.0;
                    samples.Add(Frame(i, ra, dec, new List<SequencerEdgeMarker> {
                        DitherMarker(i - 1, i)
                    }));
                } else if (i is >= 6 and <= 9) {
                    ra += 7.0;
                    samples.Add(Frame(i, ra, dec));
                } else {
                    ra += (i % 2 == 0) ? 2.5 : -2.5;
                    samples.Add(Frame(i, ra, dec));
                }
            }

            var analysis = SessionAnalysisService.AnalyzeTarget("burst-vs-dither", samples);

            Assert.Equal("Low", analysis.DriftRisk.WalkingNoiseStatus);
            Assert.True(analysis.DriftRisk.WorstWindowDriftPixels is >= 5.0);
            Assert.True(analysis.Dithers.Count(d => !d.IsSuspect) >= 2);
        }
    }
}
