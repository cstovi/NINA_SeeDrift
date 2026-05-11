using System;
using System.Collections.Generic;

namespace NINA.Plugin.SeeDrift.Models {

    public enum SequencerEventKind {
        Dither,
        CenterAfterDrift
    }

    /// <summary>
    /// Per-batch observations read from NINA logs by <c>NinaLogCorrelator.AnnotateWithLogEvents</c>.
    /// Used by <c>SessionAnalysisService</c> to build the run-wide "Session settings used" panel.
    /// All lists may be empty when nothing was captured for that signal.
    /// </summary>
    public sealed class SessionLogObservations {
        /// <summary>Configured CenterAfterDrift threshold (arc-minutes) — Y in <c>Drift: X / Y arc minutes</c>.</summary>
        public List<double> CenterAfterDriftThresholdArcMin { get; init; } = new();

        /// <summary>Frame-index gaps between consecutive CenterAfterDriftTrigger evaluation lines (inferred cadence).</summary>
        public List<int> CenterAfterDriftEvaluateGapsFrames { get; init; } = new();

        /// <summary>DitherAfterExposures N values parsed directly from the trigger line (when the NINA build prints it).</summary>
        public List<int> DitherAfterExposuresFromLog { get; init; } = new();

        /// <summary>Frame-index gaps between consecutive DitherAfterExposures triggers (fallback cadence).</summary>
        public List<int> DitherAfterExposuresInferredGapsFrames { get; init; } = new();

        /// <summary>Per-pulse commanded dither magnitude in guider pixels (max(|Δx|,|Δy|) from <c>SelectDitherPulse</c>).</summary>
        public List<double> DitherPulseMagnitudePixels { get; init; } = new();

        /// <summary>Per-pulse guide duration (seconds), first axis (RA), from <c>using guide durations of X and Y</c>.</summary>
        public List<double> DitherGuideDurationsFirstSec { get; init; } = new();

        /// <summary>Per-pulse guide duration (seconds), second axis (Dec).</summary>
        public List<double> DitherGuideDurationsSecondSec { get; init; } = new();

        public int ObservedCenterEvaluationCount { get; init; }
        public int ObservedDitherTriggerCount { get; init; }
        public int ObservedDitherPulseCount { get; init; }
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
        public double? EstimatedDriftPerExposureArcSec { get; init; }
        public double? EstimatedDriftPerExposurePixels { get; init; }
        public double? WorstWindowDriftArcSec { get; init; }
        public double? WorstWindowDriftPixels { get; init; }
        public int WorstWindowFrameCount { get; init; }
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

    /// <summary>
    /// Run-wide "Session settings used" panel data — aggregates one or more <see cref="SessionLogObservations"/>
    /// into mode/min/max/median values. <c>null</c> when no triggers or pulses were observed at all.
    /// </summary>
    public sealed class SessionSequencerSettings {
        /// <summary>Median commanded dither magnitude in guider pixels (proxy for the Mount Dither Device "Dither Pixels" setting).</summary>
        public double? DitherPixelsMedian { get; init; }
        public int DitherPulseCount { get; init; }

        public double? CenterMaxArcMin { get; init; }
        public double? CenterMaxArcMinMin { get; init; }
        public double? CenterMaxArcMinMax { get; init; }

        public int? CenterEvaluateAfterExposures { get; init; }
        public int? CenterEvaluateAfterExposuresMin { get; init; }
        public int? CenterEvaluateAfterExposuresMax { get; init; }
        public bool CenterEvaluateInferred { get; init; } = true;

        public int? DitherAfterExposuresN { get; init; }
        public int? DitherAfterExposuresNMin { get; init; }
        public int? DitherAfterExposuresNMax { get; init; }
        public bool DitherCadenceInferred { get; init; }

        public double? DitherGuideDurationFirstSecMedian { get; init; }
        public double? DitherGuideDurationSecondSecMedian { get; init; }
        public int GuideDurationSampleCount { get; init; }

        public int ObservedCenterEvaluationCount { get; init; }
        public int ObservedDitherTriggerCount { get; init; }
    }

    public sealed class SeeDriftReportPayload {
        public int SchemaVersion { get; init; } = 1;
        public string PluginVersion { get; init; } = "";
        public string SessionDate { get; init; } = "";
        public DateTime GeneratedLocal { get; init; }
        public SeestarDeviceInfo SeestarDevice { get; init; } = SeestarDeviceInfo.Unknown;

        /// <summary>NINA log file path(s) that contributed to this export (previous session = one log; Stop may list several).</summary>
        public List<string> SourceLogPaths { get; init; } = new();

        /// <summary>Sum of batch processing wall times (plate solve, correlation, HTML write) included in this report.</summary>
        public double RunProcessingSeconds { get; init; }

        /// <summary>Configured-as-observed settings used during the run (Mount Dither Pixels proxy, CenterAfterDrift, DitherAfterExposures, dither pulse durations).</summary>
        public SessionSequencerSettings? SequencerSettings { get; init; }

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
        public SeestarDeviceInfo BeforeSeestarDevice { get; init; } = SeestarDeviceInfo.Unknown;
        public SeestarDeviceInfo AfterSeestarDevice { get; init; } = SeestarDeviceInfo.Unknown;
        public string DeviceComparisonAdvisory { get; set; } = "";
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
