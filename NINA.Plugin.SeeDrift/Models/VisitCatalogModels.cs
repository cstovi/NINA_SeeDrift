using System;
using System.Collections.Generic;

namespace NINA.Plugin.SeeDrift.Models {

    /// <summary>One LIGHT save from the NINA log (pre-solve), for exposure-sequence gap analysis.</summary>
    public sealed class LightSaveCatalogEntry {
        public int? ExposureSequence { get; init; }
        public string TargetName { get; init; } = "";
        public DateTime ExposureUtc { get; init; }
        public string FileName { get; init; } = "";
    }

    /// <summary>NINA Target Scheduler <c>NewTargetStart</c> message from session logs.</summary>
    public sealed class TargetSchedulerStartEvent {
        public DateTime UtcTime { get; init; }
        public string? TargetLabel { get; init; }
    }

    public enum ExposureGapKind {
        None,
        ReturnVisit,
        MissingOrUnsolved
    }

    public sealed class ExposureGapAssessment {
        public ExposureGapKind Kind { get; init; }
        public int SequenceFrom { get; init; }
        public int SequenceTo { get; init; }
        public int MissingSequenceCount { get; init; }
        public string Detail { get; init; } = "";
    }

    public sealed class TargetVisitPlan {
        public IReadOnlyList<IReadOnlyList<DriftSample>> Visits { get; init; } = Array.Empty<IReadOnlyList<DriftSample>>();

        /// <summary>Edge indices <c>i</c> in the target-ordered list where <c>samples[i-1]→samples[i]</c> is a return-visit boundary.</summary>
        public IReadOnlyList<int> ReturnVisitBoundaryEdges { get; init; } = Array.Empty<int>();

        public IReadOnlyList<ExposureGapAssessment> GapAssessments { get; init; } = Array.Empty<ExposureGapAssessment>();
    }
}
