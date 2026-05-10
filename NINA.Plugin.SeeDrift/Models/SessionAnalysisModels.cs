using System;
using System.Collections.Generic;

namespace NINA.Plugin.SeeDrift.Models {

    public enum SequencerEventKind {
        Dither,
        CenterAfterDrift
    }

    public sealed class DitherEventAnalysis {
        public int FromFrameIndex { get; init; }
        public int ToFrameIndex { get; init; }
        public DateTime EventUtc { get; init; }
        public double DeltaRaArcSec { get; init; }
        public double DeltaDecArcSec { get; init; }
        public double MoveArcSec { get; init; }
        public double? EquivalentPixels { get; init; }
        public double? LoggedGuiderDx { get; init; }
        public double? LoggedGuiderDy { get; init; }
        public bool IsSuspect { get; init; }
        public string SuspectReason { get; init; } = "";
        public string Assessment { get; init; } = "";
        public string Detail { get; init; } = "";
    }

    public sealed class CenterEventAnalysis {
        public int FromFrameIndex { get; init; }
        public int ToFrameIndex { get; init; }
        public DateTime EventUtc { get; init; }
        public double PreDriftArcSec { get; init; }
        public double ImmediatePostDriftArcSec { get; init; }
        public double ResidualAfterFramesArcSec { get; init; }
        public double ImprovementArcSec { get; init; }
        public double ImprovementPercent { get; init; }
        public double? LoggedDriftArcMin { get; init; }
        public double? LoggedThresholdArcMin { get; init; }
        public string Assessment { get; init; } = "";
        public string Detail { get; init; } = "";
    }

    public sealed class DriftRateSummary {
        public int FrameCount { get; init; }
        public double DurationMinutes { get; init; }
        public double DeltaRaArcSecPerMinute { get; init; }
        public double DeltaDecArcSecPerMinute { get; init; }
        public double TotalArcSecPerMinute { get; init; }
        public double? TotalPixelsPerMinute { get; init; }
        public string Detail { get; init; } = "";
    }

    public sealed class DriftRiskSummary {
        public string Status { get; init; } = "";
        public string Tone { get; init; } = "";
        public int IntervalCount { get; init; }
        public double DurationMinutes { get; init; }
        public double NaturalDriftArcSecPerMinute { get; init; }
        public double? NaturalDriftPixelsPerMinute { get; init; }
        public double NetNaturalDriftArcSec { get; init; }
        public double DirectionConsistency { get; init; }
        public string Detail { get; init; } = "";
    }

    public sealed class QualityTimelineSegment {
        public DateTime StartUtc { get; init; }
        public DateTime EndUtc { get; init; }
        public string Label { get; init; } = "";
        public string Detail { get; init; } = "";
        public string Tone { get; init; } = "";
    }

    public sealed class SessionRecommendation {
        public string Level { get; init; } = "";
        public string Text { get; init; } = "";
    }

    public sealed class TargetAnalysis {
        public string TargetName { get; init; } = "";
        public int FrameCount { get; init; }
        public DriftRateSummary DriftRate { get; init; } = new();
        public DriftRiskSummary DriftRisk { get; init; } = new();
        public List<DitherEventAnalysis> Dithers { get; init; } = new();
        public List<CenterEventAnalysis> Centers { get; init; } = new();
        public List<QualityTimelineSegment> Timeline { get; init; } = new();
        public List<SessionRecommendation> Recommendations { get; init; } = new();
        public int SuspectDitherCount { get; set; }
        public double SuspectDitherDiscountedAbsRaArcSec { get; set; }
        public double SuspectDitherDiscountedAbsDecArcSec { get; set; }
    }

    public sealed class SeeDriftReportPayload {
        public int SchemaVersion { get; init; } = 1;
        public string PluginVersion { get; init; } = "";
        public string SessionDate { get; init; } = "";
        public DateTime GeneratedLocal { get; init; }
        public List<SeeDriftReportTargetPayload> Targets { get; init; } = new();
    }

    public sealed class SeeDriftReportTargetPayload {
        public string TargetName { get; init; } = "";
        public int FrameCount { get; init; }
        public DateTime StartUtc { get; init; }
        public DateTime EndUtc { get; init; }
        public List<SeeDriftReportSamplePayload> Samples { get; init; } = new();
        public TargetAnalysis Analysis { get; init; } = new();
    }

    public sealed class SeeDriftReportSamplePayload {
        public int FrameIndex { get; init; }
        public DateTime ExposureStartUtc { get; init; }
        public string FileName { get; init; } = "";
        public double DeltaRaArcSec { get; init; }
        public double DeltaDecArcSec { get; init; }
    }

    public sealed class ReportComparisonResult {
        public string BeforePath { get; init; } = "";
        public string AfterPath { get; init; } = "";
        public string OutputPath { get; set; } = "";
        public string OverallSummary { get; set; } = "";
        public string ScopeSummary { get; set; } = "";
        public string BeforePluginVersion { get; init; } = "";
        public string AfterPluginVersion { get; init; } = "";
        public int BeforeSchemaVersion { get; init; }
        public int AfterSchemaVersion { get; init; }
        public List<ReportComparisonMetricResult> Metrics { get; init; } = new();
    }

    public sealed class ReportComparisonMetricResult {
        public string Metric { get; init; } = "";
        public string BeforeValue { get; init; } = "";
        public string AfterValue { get; init; } = "";
        public string Direction { get; init; } = "";
        public string Read { get; init; } = "";
    }
}
