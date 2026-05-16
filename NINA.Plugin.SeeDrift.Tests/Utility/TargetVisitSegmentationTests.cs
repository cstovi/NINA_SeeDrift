using System;
using System.Collections.Generic;
using NINA.Plugin.SeeDrift.Models;
using NINA.Plugin.SeeDrift.Utility;
using Xunit;

namespace NINA.Plugin.SeeDrift.Tests.Utility {

    public sealed class TargetVisitSegmentationTests {

        private static DriftSample Sample(
                int frameIndex,
                int exposureSeq,
                DateTime utc,
                string target = "Eagle") {
            var fn = FormattableString.Invariant($"{target}_LIGHT_20.00s_{exposureSeq:D4}.fits");
            return new DriftSample {
                FrameIndex = frameIndex,
                ExposureStartUtc = utc,
                FileName = fn,
                TargetName = target,
                DeltaRaArcSec = 0,
                DeltaDecArcSec = 0
            };
        }

        [Fact]
        public void BuildPlan_scheduler_start_between_frames_marks_return_visit() {
            var t0 = new DateTime(2026, 5, 16, 1, 0, 0, DateTimeKind.Utc);
            var samples = new List<DriftSample> {
                Sample(0, 10, t0),
                Sample(1, 11, t0.AddMinutes(20)),
                Sample(2, 50, t0.AddHours(3)),
                Sample(3, 51, t0.AddHours(3).AddMinutes(20))
            };
            var scheduler = new List<TargetSchedulerStartEvent> {
                new() {
                    UtcTime = t0.AddHours(2).AddMinutes(30),
                    TargetLabel = "Eagle Nebula"
                }
            };

            var plan = TargetVisitSegmentation.BuildPlan(
                "Eagle Nebula", samples, samples, null, scheduler);

            Assert.Equal(2, plan.Visits.Count);
            Assert.Single(plan.ReturnVisitBoundaryEdges);
            Assert.Equal(2, plan.ReturnVisitBoundaryEdges[0]);
            Assert.Equal(ExposureGapKind.ReturnVisit, plan.GapAssessments[1].Kind);
            Assert.Contains("NewTargetStart", plan.GapAssessments[1].Detail, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BuildPlan_sequence_gap_without_scheduler_stays_missing() {
            var t0 = new DateTime(2026, 5, 16, 1, 0, 0, DateTimeKind.Utc);
            var samples = new List<DriftSample> {
                Sample(0, 10, t0),
                Sample(1, 15, t0.AddMinutes(40))
            };

            var plan = TargetVisitSegmentation.BuildPlan(
                "Eagle", samples, samples, null, Array.Empty<TargetSchedulerStartEvent>());

            Assert.Single(plan.Visits);
            Assert.Empty(plan.ReturnVisitBoundaryEdges);
            Assert.Equal(ExposureGapKind.MissingOrUnsolved, plan.GapAssessments[0].Kind);
        }

        [Fact]
        public void BuildPlan_splits_visits_at_return_boundary() {
            var t0 = new DateTime(2026, 5, 16, 1, 0, 0, DateTimeKind.Utc);
            var samples = new List<DriftSample> {
                Sample(0, 1, t0),
                Sample(1, 2, t0.AddMinutes(20)),
                Sample(2, 20, t0.AddHours(2)),
                Sample(3, 21, t0.AddHours(2).AddMinutes(20))
            };
            var scheduler = new List<TargetSchedulerStartEvent> {
                new() { UtcTime = t0.AddHours(1), TargetLabel = "Eagle" }
            };

            var plan = TargetVisitSegmentation.BuildPlan(
                "Eagle", samples, samples, null, scheduler);

            Assert.Equal(2, plan.Visits.Count);
            Assert.Equal(2, plan.Visits[0].Count);
            Assert.Equal(2, plan.Visits[1].Count);
        }
    }
}
