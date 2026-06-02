# SeeDrift Report Simplification — Implementation Plan

## Overview

This plan removes report bloat while preserving core drift visualization value. Changes are organized into **3 phases** for incremental delivery and testing.

**Goal**: Transform SeeDrift from a mount diagnostic tool into a focused drift visualization tool.

---

## Phase 1: Quick Wins (Remove Obvious Bloat)

**Goal**: Remove redundant/confusing elements with minimal code changes  
**Estimated effort**: 2-3 hours  
**Risk**: Low (pure deletions)

### 1.1 Remove Movement Totals Line

**File**: `NINA.Plugin.SeeDrift/Services/HtmlReportExporter.cs`

**Location**: Line ~221 (in per-target section)

**Current code**:
```csharp
sb.AppendLine($"    <p class=\"mt-2 text-xs text-slate-400\">{Escape(FormatMovementTotalsLine(visitPlan, grp))}</p>");
```

**Action**: Delete this line entirely

**Also remove**: The `FormatMovementTotalsLine()` method (lines ~971-1007) and supporting methods:
- `SumAbsoluteSegmentMovementAlongTrace()` (lines ~499-513)
- `SumAbsoluteSegmentMovementInPixels()` (lines ~516-535)
- `TryMedianNominalPlateScale()` (lines ~538-551)

---

### 1.2 Remove "Settings Hints from this Session"

**File**: `NINA.Plugin.SeeDrift/Services/HtmlReportExporter.cs`

**Location**: Lines ~826-834 (in `FormatAnalysisSummaryHtml()`)

**Current code**:
```csharp
if (analysis.Recommendations.Count > 0) {
    sb.AppendLine("    <div class=\"mt-3 rounded-lg border border-slate-700 bg-slate-900/30 p-3\">");
    sb.AppendLine("      <p class=\"text-[10px] font-semibold uppercase tracking-wide text-amber-300\">Settings hints from this session</p>");
    sb.AppendLine("      <ul class=\"mt-2 list-disc space-y-1 pl-5 text-xs text-slate-300\">");
    foreach (var rec in analysis.Recommendations.Take(4))
        sb.AppendLine($"        <li>{Escape(rec.Text)}</li>");
    sb.AppendLine("      </ul>");
    sb.AppendLine("    </div>");
}
```

**Action**: Delete this entire block

**Also remove**: The `BuildRecommendations()` method in `SessionAnalysisService.cs` (lines ~942-1050) and the `SessionRecommendation` model in `SessionAnalysisModels.cs` (lines ~130-133)

---

### 1.3 Remove Dither Effectiveness Summary Box

**File**: `NINA.Plugin.SeeDrift/Services/HtmlReportExporter.cs`

**Location**: Lines ~789-808 (in `FormatAnalysisSummaryHtml()`)

**Current code**:
```csharp
sb.AppendLine("      <div class=\"rounded-lg border border-slate-700 bg-slate-900/40 p-3\">");
sb.AppendLine("        <p class=\"text-[10px] font-semibold uppercase tracking-wide text-purple-400\">Dither effectiveness</p>");
if (analysis.Dithers.Count == 0) {
    sb.AppendLine("        <p class=\"mt-1 text-sm text-slate-300\">No logged dithers on this target.</p>");
} else {
    var assessed = analysis.Dithers.Where(d => !d.IsSuspect).ToList();
    var weak = assessed.Count(d => d.Assessment == "Weak");
    var repeated = assessed.Count(d => d.Assessment == "Repeated direction");
    var med = assessed.Count == 0 ? 0 : assessed.Select(d => d.MoveArcSec).OrderBy(v => v).ElementAt(assessed.Count / 2);
    sb.AppendLine($"        <p class=\"mt-1 text-sm text-slate-200\">{assessed.Count} assessed dither{(assessed.Count == 1 ? "" : "s")} · median {med:0.##}″</p>");
    sb.AppendLine($"        <p class=\"mt-1 text-xs text-slate-500\">Weak {weak} · repeated direction {repeated}</p>");
    if (analysis.SuspectDitherCount > 0) {
        sb.AppendLine($"        <p class=\"mt-1 text-xs text-amber-300\">Excluded suspect tracking intervals: {analysis.SuspectDitherCount}; discounted RA {analysis.SuspectDitherDiscountedAbsRaArcSec:0.#}″ · Dec {analysis.SuspectDitherDiscountedAbsDecArcSec:0.#}″.</p>");
    }
}
sb.AppendLine("      </div>");
```

**Action**: Delete this entire `<div>` block (keep the surrounding grid structure)

**Result**: The 3-column grid becomes a 2-column grid (Drift rate + Center-after-drift)

---

### 1.4 Remove Drift Risk Advisory Paragraph

**File**: `NINA.Plugin.SeeDrift/Services/HtmlReportExporter.cs`

**Location**: Lines ~894 (in `FormatDriftRiskHtml()`)

**Current code**:
```csharp
sb.AppendLine($"      <p class=\"mt-1 text-xs text-slate-500\">{Escape(detail)} Risk is advisory: star-shape is about single-exposure round stars (&lt; 1–2 px is typically acceptable), walking-noise is about cumulative drift between dithers vs the dither magnitude.</p>");
```

**Action**: Delete this line entirely

---

### 1.5 Remove "92% directional" from Drift Risk Display

**File**: `NINA.Plugin.SeeDrift/Services/HtmlReportExporter.cs`

**Location**: Lines ~869-872 (in `FormatDriftRiskHtml()`)

**Current code**:
```csharp
sb.AppendLine($"      <p class=\"mt-2 text-sm {tone.Item3}\">{risk.NaturalDriftArcSecPerMinute:0.##}″/min natural drift{Escape(px)} · {risk.DirectionConsistency:P0} directional</p>");
```

**Action**: Change to:
```csharp
sb.AppendLine($"      <p class=\"mt-2 text-sm {tone.Item3}\">{risk.NaturalDriftArcSecPerMinute:0.##}″/min natural drift{Escape(px)}</p>");
```

---

### 1.6 Remove "Net drift without logged..." Line

**File**: `NINA.Plugin.SeeDrift/Services/HtmlReportExporter.cs`

**Location**: Lines ~871-872 (in `FormatDriftRiskHtml()`)

**Current code**:
```csharp
sb.AppendLine($"      <p class=\"mt-1 text-xs text-slate-400\">Net drift without logged dither/center intervals: {risk.NetNaturalDriftArcSec:0.#}″ across {risk.IntervalCount} interval{(risk.IntervalCount == 1 ? "" : "s")}.</p>");
```

**Action**: Delete this line entirely

---

### 1.7 Remove Sequencer Events Section

**File**: `NINA.Plugin.SeeDrift/Services/HtmlReportExporter.cs`

**Location**: Lines ~1109-1170 (the `AppendSequencerSection()` method call and implementation)

**Current code** (call site, line ~414):
```csharp
AppendSequencerSection(sb, orderedFull, targetName);
```

**Action**: Delete this line

**Also remove**: The entire `AppendSequencerSection()` method (lines ~1109-1170)

**Rationale**: Users who want log details can check NINA logs directly. The report should focus on visual drift representation, not log archaeology.

---

## Phase 2: Simplify Remaining Text (Improve Clarity)

**Goal**: Make remaining advisory text clearer and more concise  
**Estimated effort**: 3-4 hours  
**Risk**: Low (text changes only)

### 2.1 Simplify Star-Shape Hint Text

**File**: `NINA.Plugin.SeeDrift/Services/HtmlReportExporter.cs`

**Location**: Lines ~873-878 (in `FormatDriftRiskHtml()`)

**Current code**:
```csharp
if (risk.EstimatedDriftPerExposureArcSec.HasValue) {
    var perSubPx = risk.EstimatedDriftPerExposurePixels.HasValue
        ? FormattableString.Invariant($" · ≈ {risk.EstimatedDriftPerExposurePixels.Value:0.##} px")
        : "";
    sb.AppendLine($"      <p class=\"mt-1 text-xs text-slate-400\">Star-shape hint: estimated {risk.EstimatedDriftPerExposureArcSec.Value:0.#}″ drift during a typical exposure{Escape(perSubPx)}. Round stars typically tolerate &lt; 1–2 px of motion per exposure.</p>");
}
```

**Action**: Replace with:
```csharp
if (risk.EstimatedDriftPerExposurePixels.HasValue) {
    var assessment = risk.EstimatedDriftPerExposurePixels.Value < 1.0 ? "excellent"
        : risk.EstimatedDriftPerExposurePixels.Value < 2.0 ? "acceptable"
        : "may show elongation";
    sb.AppendLine($"      <p class=\"mt-1 text-xs text-slate-400\">Per-exposure drift: ~{risk.EstimatedDriftPerExposurePixels.Value:0.#} px ({assessment} for round stars)</p>");
} else if (risk.EstimatedDriftPerExposureArcSec.HasValue) {
    sb.AppendLine($"      <p class=\"mt-1 text-xs text-slate-400\">Per-exposure drift: ~{risk.EstimatedDriftPerExposureArcSec.Value:0.#}″</p>");
}
```

---

### 2.2 Simplify Walking-Noise Hint Text

**File**: `NINA.Plugin.SeeDrift/Services/HtmlReportExporter.cs`

**Location**: Lines ~880-892 (in `FormatDriftRiskHtml()`)

**Current code**:
```csharp
if (risk.DitherHeadroomRatio.HasValue && risk.BetweenDitherDriftArcSec.HasValue) {
    var ditherPxBit = risk.BetweenDitherDriftPixels.HasValue
        ? FormattableString.Invariant($" · ≈ {risk.BetweenDitherDriftPixels.Value:0.##} px between dithers")
        : "";
    sb.AppendLine($"      <p class=\"mt-1 text-xs text-slate-400\">Walking-noise hint: dither headroom <span class=\"font-semibold text-slate-200\">{risk.DitherHeadroomRatio.Value:0.0}×</span> (median dither magnitude vs cumulative drift between dithers — {risk.BetweenDitherDriftArcSec.Value:0.##}″ between dithers{Escape(ditherPxBit)}). Higher is safer.</p>");
} else if (risk.WorstWindowDriftArcSec.HasValue && risk.WorstWindowFrameCount > 0) {
    var windowPx = risk.WorstWindowDriftPixels.HasValue
        ? FormattableString.Invariant($" · ≈ {risk.WorstWindowDriftPixels.Value:0.##} px")
        : "";
    sb.AppendLine($"      <p class=\"mt-1 text-xs text-slate-400\">Walking-noise hint (no dither headroom data): worst short-window drift was {risk.WorstWindowDriftArcSec.Value:0.#}″ over {risk.WorstWindowFrameCount} frame{(risk.WorstWindowFrameCount == 1 ? "" : "s")}{Escape(windowPx)}.</p>");
}
```

**Action**: Replace with:
```csharp
if (risk.DitherHeadroomRatio.HasValue && risk.BetweenDitherDriftPixels.HasValue) {
    var assessment = risk.DitherHeadroomRatio.Value >= 3.0 ? "good"
        : risk.DitherHeadroomRatio.Value >= 2.0 ? "acceptable"
        : "tight";
    sb.AppendLine($"      <p class=\"mt-1 text-xs text-slate-400\">Dither headroom: <span class=\"font-semibold text-slate-200\">{risk.DitherHeadroomRatio.Value:0.1}×</span> ({assessment}) — Drift between dithers: ~{risk.BetweenDitherDriftPixels.Value:0.#} px</p>");
} else if (risk.BetweenDitherDriftArcSec.HasValue) {
    sb.AppendLine($"      <p class=\"mt-1 text-xs text-slate-400\">Drift between dithers: ~{risk.BetweenDitherDriftArcSec.Value:0.#}″</p>");
}
```

---

### 2.3 Simplify Center-After-Drift Box

**File**: `NINA.Plugin.SeeDrift/Services/HtmlReportExporter.cs`

**Location**: Lines ~810-823 (in `FormatAnalysisSummaryHtml()`)

**Current code**:
```csharp
sb.AppendLine("      <div class=\"rounded-lg border border-slate-700 bg-slate-900/40 p-3\">");
sb.AppendLine("        <p class=\"text-[10px] font-semibold uppercase tracking-wide text-pink-400\">Center-after-drift</p>");
if (analysis.Centers.Count == 0) {
    sb.AppendLine("        <p class=\"mt-1 text-sm text-slate-300\">No correlated center events.</p>");
} else {
    var helpful = analysis.Centers.Count(c => c.Assessment == "Helpful");
    var partial = analysis.Centers.Count(c => c.Assessment == "Partial");
    var ineffective = analysis.Centers.Count(c => c.Assessment == "Ineffective");
    sb.AppendLine($"        <p class=\"mt-1 text-sm text-slate-200\">{analysis.Centers.Count} center event{(analysis.Centers.Count == 1 ? "" : "s")}</p>");
    sb.AppendLine($"        <p class=\"mt-1 text-xs text-slate-500\">Helpful {helpful} · partial {partial} · ineffective {ineffective}</p>");
}
sb.AppendLine("      </div>");
```

**Action**: Replace with:
```csharp
sb.AppendLine("      <div class=\"rounded-lg border border-slate-700 bg-slate-900/40 p-3\">");
sb.AppendLine("        <p class=\"text-[10px] font-semibold uppercase tracking-wide text-pink-400\">Center-after-drift</p>");
if (analysis.Centers.Count == 0) {
    sb.AppendLine("        <p class=\"mt-1 text-sm text-slate-300\">No correlated center events</p>");
} else {
    sb.AppendLine($"        <p class=\"mt-1 text-sm text-slate-200\">{analysis.Centers.Count} center event{(analysis.Centers.Count == 1 ? "" : "s")}</p>");
}
sb.AppendLine("      </div>");
```

---

### 2.4 Clarify Drift Rate Labels

**File**: `NINA.Plugin.SeeDrift/Services/HtmlReportExporter.cs`

**Location**: Lines ~778-788 (in `FormatAnalysisSummaryHtml()`)

**Current code**:
```csharp
sb.AppendLine("      <div class=\"rounded-lg border border-slate-700 bg-slate-900/40 p-3\">");
sb.AppendLine("        <p class=\"text-[10px] font-semibold uppercase tracking-wide text-sky-400\">Drift rate</p>");
var rate = analysis.DriftRate;
var px = rate.TotalPixelsPerMinute.HasValue
    ? FormattableString.Invariant($" · ≈ {rate.TotalPixelsPerMinute.Value:0.##} px/min")
    : "";
sb.AppendLine($"        <p class=\"mt-1 text-sm text-slate-200\">{rate.TotalArcSecPerMinute:0.###}″/min{Escape(px)}</p>");
sb.AppendLine($"        <p class=\"mt-1 text-xs text-slate-500\">ΔRA {FmtSignedArcSec(rate.DeltaRaArcSecPerMinute)}″/min · ΔDec {FmtSignedArcSec(rate.DeltaDecArcSecPerMinute)}″/min</p>");
sb.AppendLine("      </div>");
```

**Action**: Replace with:
```csharp
sb.AppendLine("      <div class=\"rounded-lg border border-slate-700 bg-slate-900/40 p-3\">");
sb.AppendLine("        <p class=\"text-[10px] font-semibold uppercase tracking-wide text-sky-400\">Net drift rate</p>");
var rate = analysis.DriftRate;
var px = rate.TotalPixelsPerMinute.HasValue
    ? FormattableString.Invariant($" · ≈ {rate.TotalPixelsPerMinute.Value:0.##} px/min")
    : "";
sb.AppendLine($"        <p class=\"mt-1 text-sm text-slate-200\">{rate.TotalArcSecPerMinute:0.###}″/min{Escape(px)}</p>");
sb.AppendLine($"        <p class=\"mt-1 text-xs text-slate-500\">First → last frame · ΔRA {FmtSignedArcSec(rate.DeltaRaArcSecPerMinute)}″/min · ΔDec {FmtSignedArcSec(rate.DeltaDecArcSecPerMinute)}″/min</p>");
sb.AppendLine("      </div>");
```

**Also update** the drift risk box label (line ~854):
```csharp
sb.AppendLine("        <p class=\"text-[10px] font-semibold uppercase tracking-wide text-slate-300\">Drift / walking-noise risk</p>");
```

Change to:
```csharp
sb.AppendLine("        <p class=\"text-[10px] font-semibold uppercase tracking-wide text-slate-300\">Natural drift risk</p>");
```

And update line ~870:
```csharp
sb.AppendLine($"      <p class=\"mt-2 text-sm {tone.Item3}\">{risk.NaturalDriftArcSecPerMinute:0.##}″/min natural drift{Escape(px)}</p>");
```

Change to:
```csharp
sb.AppendLine($"      <p class=\"mt-2 text-sm {tone.Item3}\">{risk.NaturalDriftArcSecPerMinute:0.##}″/min{Escape(px)} (between corrections)</p>");
```

---

### 2.5 Decouple Star Shape and Walking Noise from Overall Box Tone

**Problem**: Currently the drift risk box gets colored based on the worst of the two sub-tiers (star shape vs walking noise). This is confusing when one is "Low" and the other is "Caution" — the whole box turns amber.

**Goal**: Remove the overall box tone/status. Show only the two independent sub-tier ratings.

---

#### 2.5.1 Remove Overall Status Badge

**File**: `NINA.Plugin.SeeDrift/Services/HtmlReportExporter.cs`

**Location**: Lines ~852-856 (in `FormatDriftRiskHtml()`)

**Current code**:
```csharp
sb.AppendLine("      <div class=\"flex flex-wrap items-center justify-between gap-2\">");
sb.AppendLine("        <p class=\"text-[10px] font-semibold uppercase tracking-wide text-slate-300\">Drift / walking-noise risk</p>");
sb.AppendLine($"        <span class=\"rounded-full border {tone.Item1} px-2 py-0.5 text-xs font-semibold {tone.Item3}\">{Escape(tone.Item4)}</span>");
sb.AppendLine("      </div>");
```

**Action**: Replace with:
```csharp
sb.AppendLine("      <div>");
sb.AppendLine("        <p class=\"text-[10px] font-semibold uppercase tracking-wide text-slate-300\">Drift advisory</p>");
sb.AppendLine("      </div>");
```

---

#### 2.5.2 Remove Overall Box Border/Background Coloring

**File**: `NINA.Plugin.SeeDrift/Services/HtmlReportExporter.cs`

**Location**: Line ~852 (in `FormatDriftRiskHtml()`)

**Current code**:
```csharp
sb.AppendLine($"    <div class=\"mt-4 rounded-lg border {tone.Item1} {tone.Item2} p-3\">");
```

**Action**: Replace with neutral styling:
```csharp
sb.AppendLine("    <div class=\"mt-4 rounded-lg border border-slate-700 bg-slate-900/40 p-3\">");
```

---

#### 2.5.3 Make Sub-Tier Chips More Prominent

**File**: `NINA.Plugin.SeeDrift/Services/HtmlReportExporter.cs`

**Location**: Lines ~859-866 (in `FormatDriftRiskHtml()`)

**Current code**:
```csharp
if (!string.IsNullOrWhiteSpace(risk.StarShapeStatus) || !string.IsNullOrWhiteSpace(risk.WalkingNoiseStatus)) {
    sb.AppendLine("      <div class=\"mt-2 flex flex-wrap gap-2\">");
    if (!string.IsNullOrWhiteSpace(risk.StarShapeStatus))
        sb.AppendLine($"        {FormatSubTierChip("Star shape", risk.StarShapeStatus, risk.StarShapeTone)}");
    if (!string.IsNullOrWhiteSpace(risk.WalkingNoiseStatus))
        sb.AppendLine($"        {FormatSubTierChip("Walking noise", risk.WalkingNoiseStatus, risk.WalkingNoiseTone)}");
    sb.AppendLine("      </div>");
}
```

**Action**: Make these chips larger and more prominent (they're now the primary signal):
```csharp
if (!string.IsNullOrWhiteSpace(risk.StarShapeStatus) || !string.IsNullOrWhiteSpace(risk.WalkingNoiseStatus)) {
    sb.AppendLine("      <div class=\"mt-2 flex flex-wrap gap-3\">");
    if (!string.IsNullOrWhiteSpace(risk.StarShapeStatus))
        sb.AppendLine($"        {FormatSubTierChip("Star shape", risk.StarShapeStatus, risk.StarShapeTone)}");
    if (!string.IsNullOrWhiteSpace(risk.WalkingNoiseStatus))
        sb.AppendLine($"        {FormatSubTierChip("Walking noise", risk.WalkingNoiseStatus, risk.WalkingNoiseTone)}");
    sb.AppendLine("      </div>");
}
```

**Also update** the `FormatSubTierChip()` method (lines ~908-912) to make chips larger:

**Current code**:
```csharp
private static string FormatSubTierChip(string label, string status, string tone) {
    var t = ToneTriple(tone, status);
    return FormattableString.Invariant(
        $"<span class=\"rounded-full border {t.Item1} {t.Item2} px-2 py-0.5 text-[11px] {t.Item3}\"><span class=\"font-semibold uppercase tracking-wide\">{Escape(label)}</span>: {Escape(status)}</span>");
}
```

**Action**: Replace with larger styling:
```csharp
private static string FormatSubTierChip(string label, string status, string tone) {
    var t = ToneTriple(tone, status);
    return FormattableString.Invariant(
        $"<span class=\"rounded-full border {t.Item1} {t.Item2} px-3 py-1 text-xs {t.Item3}\"><span class=\"font-semibold uppercase tracking-wide\">{Escape(label)}</span>: {Escape(status)}</span>");
}
```

---

#### 2.5.4 Update Drift Rate Display Text

**File**: `NINA.Plugin.SeeDrift/Services/HtmlReportExporter.cs`

**Location**: Line ~870 (in `FormatDriftRiskHtml()`)

**Current code** (after Phase 2.4 changes):
```csharp
sb.AppendLine($"      <p class=\"mt-2 text-sm {tone.Item3}\">{risk.NaturalDriftArcSecPerMinute:0.##}″/min{Escape(px)} (between corrections)</p>");
```

**Action**: Remove tone coloring (since box is now neutral):
```csharp
sb.AppendLine($"      <p class=\"mt-3 text-sm text-slate-200\">{risk.NaturalDriftArcSecPerMinute:0.##}″/min{Escape(px)} (between corrections)</p>");
```

---

#### 2.5.5 Remove Unused Headline Logic

**File**: `NINA.Plugin.SeeDrift/Services/SessionAnalysisService.cs`

**Location**: Lines ~391-397 (in `BuildDriftRisk()`)

**Current code**:
```csharp
var (headlineStatus, headlineTone) = HeadlineOf(starStatus, starTone, walkStatus, walkTone);

var detail = BuildDriftRiskDetail(
    intervals.Count, rate, consistency,
    starStatus, walkStatus, walkReason,
    driftPerExposurePx, driftPerExposure, headroom,
    headlineStatus);

return new DriftRiskSummary {
    Status = headlineStatus,
    Tone = headlineTone,
    // ...
```

**Action**: Remove headline calculation (no longer needed):
```csharp
var detail = BuildDriftRiskDetail(
    intervals.Count, rate, consistency,
    starStatus, walkStatus, walkReason,
    driftPerExposurePx, driftPerExposure, headroom);

return new DriftRiskSummary {
    Status = "", // No longer used
    Tone = "", // No longer used
    // ...
```

**Also remove**: The `HeadlineOf()` method (find and delete it) and update the `BuildDriftRiskDetail()` method signature to remove the `headlineStatus` parameter

---

## Phase 3: Improve Session Quality Timeline

**Goal**: Make timeline more useful with distinct colors and clearer labels  
**Estimated effort**: 2-3 hours  
**Risk**: Low (visual changes only)

### 3.1 Add Distinct Colors for Dither vs Center

**File**: `NINA.Plugin.SeeDrift/Services/SessionAnalysisService.cs`

**Location**: Lines ~914-940 (in `BuildTimeline()`)

**Current code**:
```csharp
var label = markers.Any(m => !m.IsDither)
    ? "center recovery"
    : markers.Any(m => m.IsDither)
        ? "dither recovery"
        : step > Math.Max(5.0, analysis.DriftRate.TotalArcSecPerMinute)
            ? "drifting"
            : "stable";
var tone = label == "stable" ? "good" : label == "drifting" ? "warn" : "event";
```

**Action**: Replace with:
```csharp
string label;
string tone;
if (markers.Any(m => !m.IsDither)) {
    label = "center";
    tone = "center";
} else if (markers.Any(m => m.IsDither)) {
    label = "dither";
    tone = "dither";
} else if (step > Math.Max(5.0, analysis.DriftRate.TotalArcSecPerMinute)) {
    label = "drifting";
    tone = "warn";
} else {
    label = "tracking";
    tone = "good";
}
```

---

### 3.2 Update Timeline HTML Rendering

**File**: `NINA.Plugin.SeeDrift/Services/HtmlReportExporter.cs`

**Location**: Lines ~914-946 (in `FormatTimelineHtml()`)

**Current code**:
```csharp
var cls = seg.Tone == "good"
    ? "bg-emerald-500/80"
    : seg.Tone == "warn"
        ? "bg-amber-500/80"
        : "bg-sky-500/80";
```

**Action**: Replace with:
```csharp
var cls = seg.Tone == "good"
    ? "bg-emerald-500/80"
    : seg.Tone == "warn"
        ? "bg-amber-500/80"
        : seg.Tone == "dither"
            ? "bg-purple-500/80"
            : seg.Tone == "center"
                ? "bg-pink-500/80"
                : "bg-sky-500/80";
```

**Also update** the legend section (lines ~933-943):
```csharp
var cls = tone == "good"
    ? "border-emerald-500/40 bg-emerald-500/10 text-emerald-200"
    : tone == "warn"
        ? "border-amber-500/40 bg-amber-500/10 text-amber-200"
        : "border-sky-500/40 bg-sky-500/10 text-sky-200";
```

**Action**: Replace with:
```csharp
var cls = tone == "good"
    ? "border-emerald-500/40 bg-emerald-500/10 text-emerald-200"
    : tone == "warn"
        ? "border-amber-500/40 bg-amber-500/10 text-amber-200"
        : tone == "dither"
            ? "border-purple-500/40 bg-purple-500/10 text-purple-200"
            : tone == "center"
                ? "border-pink-500/40 bg-pink-500/10 text-pink-200"
                : "border-sky-500/40 bg-sky-500/10 text-sky-200";
```

---

### 3.3 Add Timeline Legend

**File**: `NINA.Plugin.SeeDrift/Services/HtmlReportExporter.cs`

**Location**: After line ~919 (in `FormatTimelineHtml()`)

**Action**: Add after the timeline title:
```csharp
sb.AppendLine("      <p class=\"text-[10px] font-semibold uppercase tracking-wide text-sky-400\">Session quality timeline</p>");
sb.AppendLine("      <p class=\"mt-1 text-xs text-slate-500\"><span class=\"text-emerald-400\">■</span> tracking · <span class=\"text-amber-400\">■</span> drifting · <span class=\"text-purple-400\">■</span> dither · <span class=\"text-pink-400\">■</span> center</p>");
```

---

## Phase 4: Add Session Settings Indicators (Optional Enhancement)

**Goal**: Show which settings are direct vs. inferred  
**Estimated effort**: 2-3 hours  
**Risk**: Low (visual enhancement)

### 4.1 Add Indicator Helper Method

**File**: `NINA.Plugin.SeeDrift/Services/HtmlReportExporter.cs`

**Location**: Add after `FormatSettingsRow()` method

**Action**: Add new helper:
```csharp
private static string SettingSourceIndicator(bool isDirect) {
    return isDirect
        ? "<span class=\"ml-1 text-emerald-400\" title=\"Direct from log\">✓</span>"
        : "<span class=\"ml-1 text-amber-400\" title=\"Inferred from frame gaps\">~</span>";
}
```

---

### 4.2 Update DitherAfterExposures Row

**File**: `NINA.Plugin.SeeDrift/Services/HtmlReportExporter.cs`

**Location**: Lines ~1233-1250 (in `FormatSessionSettingsHtml()`)

**Current code**:
```csharp
if (s.DitherAfterExposuresN.HasValue && s.ObservedDitherTriggerCount > 0) {
    var n = s.DitherAfterExposuresN.Value;
    var triggers = s.ObservedDitherTriggerCount;
    var note = $"every {n} exposure{(n == 1 ? "" : "s")} · {triggers} trigger{(triggers == 1 ? "" : "s")} observed";
    var variance = (s.DitherAfterExposuresNMin.HasValue && s.DitherAfterExposuresNMax.HasValue
                    && s.DitherAfterExposuresNMin.Value != s.DitherAfterExposuresNMax.Value)
        ? $" · varied: every {s.DitherAfterExposuresNMin}–{s.DitherAfterExposuresNMax} exposures"
        : "";
    rows.Add(FormatSettingsRow(
        "DitherAfterExposures",
        $"Every {n} exposure{(n == 1 ? "" : "s")}",
        note + variance,
        s.DitherCadenceInferred,
        ""));
}
```

**Action**: Update the value string to include indicator:
```csharp
rows.Add(FormatSettingsRow(
    "DitherAfterExposures",
    $"Every {n} exposure{(n == 1 ? "" : "s")}{SettingSourceIndicator(!s.DitherCadenceInferred)}",
    note + variance,
    s.DitherCadenceInferred,
    ""));
```

---

### 4.3 Update CenterAfterDrift Evaluate Cadence Row

**File**: `NINA.Plugin.SeeDrift/Services/HtmlReportExporter.cs`

**Location**: Lines ~1220-1231 (in `FormatSessionSettingsHtml()`)

**Action**: Similar update to add `{SettingSourceIndicator(!s.CenterEvaluateInferred)}` to the value string

---

## Visual Mockups

### Before (Current)
```
┌─────────────────────────────────────────────────┐
│ DRIFT / WALKING-NOISE RISK          [CAUTION]   │ ← Whole box is amber
│ ─────────────────────────────────────────────── │
│ [Star shape: Low] [Walking noise: Caution]      │ ← Sub-tiers buried
│                                                  │
│ 16.52″/min natural drift · 4.14 px/min · 92%    │
│ directional                                      │
│                                                  │
│ Net drift without logged dither/center           │
│ intervals: 1475.7″ across 227 intervals.         │
│                                                  │
│ Star-shape hint: estimated 5.5″ drift during...  │
│ Walking-noise hint: dither headroom 2.1×...      │
│                                                  │
│ Advisory: 227 natural-drift intervals; 16.52"/min│ ← Redundant paragraph
└─────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────┐
│ SEQUENCER EVENTS (NINA LOGS) — 45 rows          │ ← Log archaeology
│ ─────────────────────────────────────────────── │
│ [Detailed table of every dither/center event]   │
└─────────────────────────────────────────────────┘
```

### After (Simplified)
```
┌─────────────────────────────────────────────────┐
│ DRIFT ADVISORY                                   │ ← Neutral box
│ ─────────────────────────────────────────────── │
│ [Star shape: Low] [Walking noise: Caution]      │ ← Prominent, independent
│                                                  │
│ 16.52″/min (between corrections)                 │ ← Simplified
│                                                  │
│ Per-exposure drift: ~1.4 px (acceptable)         │ ← Clear, concise
│ Dither headroom: 2.1× (acceptable)               │ ← Clear, concise
│   Drift between dithers: ~23 px                  │
└─────────────────────────────────────────────────┘

[Sequencer events section removed entirely]
```

---

## Testing Checklist

After each phase:

- [ ] Generate report with typical Seestar session (50+ frames, multiple dithers/centers)
- [ ] Verify removed elements are gone
- [ ] Verify remaining elements render correctly
- [ ] Check HTML validates (no broken tags)
- [ ] Test with edge cases:
  - [ ] No dithers
  - [ ] No centers
  - [ ] Single target
  - [ ] Multiple targets
  - [ ] Very short session (<10 frames)
  - [ ] Very long session (500+ frames)
- [ ] Verify comparison reports still work (if kept)

### Phase 1 Specific
- [ ] Verify sequencer events section is completely removed
- [ ] Verify no broken references to `AppendSequencerSection()`
- [ ] Verify movement totals line is gone
- [ ] Verify settings hints section is gone
- [ ] Verify dither effectiveness box is gone

### Phase 2 Specific
- [ ] Verify drift advisory box has neutral styling (no amber/green border)
- [ ] Verify star shape and walking noise chips are independent
- [ ] Verify one can be "Low" while the other is "Caution" without confusion
- [ ] Verify drift rate text is not colored by tone
- [ ] Verify simplified hint text is clearer

### Phase 3 Specific
- [ ] Verify timeline uses distinct colors for dither (purple) vs center (pink)
- [ ] Verify "stable" renamed to "tracking"
- [ ] Verify timeline legend is present and accurate

### Phase 4 Specific
- [ ] Verify ✓ indicator appears for direct settings
- [ ] Verify ~ indicator appears for inferred settings
- [ ] Verify tooltips work on hover

---

## Rollback Plan

Each phase is independent. If issues arise:

1. **Phase 1**: Revert file to previous commit (pure deletions)
2. **Phase 2**: Revert text changes (no logic changes)
3. **Phase 3**: Revert timeline color changes
4. **Phase 4**: Revert indicator additions

---

## Expected Outcome

**Before**: Report tries to be a mount diagnostic tool with effectiveness scoring, risk advisories, recommendations, and log archaeology

**After**: Report is a clean drift visualization tool showing:
- What the mount did (drift path chart)
- When corrections happened (dither/center markers)
- Basic stats (drift rate, event counts)
- Minimal advisory context (per-exposure motion, dither headroom)
- Independent star shape and walking noise ratings

**Lines of code removed**: ~400-500 lines  
**Complexity reduction**: ~30-40%  
**User clarity improvement**: Significant (less cognitive load, clearer focus)

---

## Rationale Summary

### Why Remove Movement Totals?
- **No practical value**: Sum of absolute steps is a geometric curiosity
- **Confusing**: Users don't understand what it means or why it matters
- **Redundant**: Drift rate already tells the story

### Why Remove Settings Hints?
- **Speculative**: Recommendations are often wrong (e.g., "increase dither" when real issue is polar alignment)
- **Unwanted**: Users want to see what happened, not be told what to do
- **Bloat**: Adds complexity without clear user benefit

### Why Remove Dither Effectiveness Box?
- **Redundant**: Same info already in expandable dither intervals table
- **Unnecessary**: Users can see dither markers on chart

### Why Remove Sequencer Events?
- **Bloat**: Detailed log correlation is archaeological work
- **Redundancy**: Dither/center markers already shown on chart
- **Accessibility**: Users who need log details can check NINA logs directly
- **Focus**: Report should visualize drift, not replicate logs

### Why Decouple Star Shape and Walking Noise?
- **Clarity**: These are independent concerns (single-exposure vs. stacking)
- **Confusion**: Current design makes whole box amber when only one sub-tier is "Caution"
- **User value**: Users need to know which specific aspect needs attention
- **Simplicity**: No need for complex "headline" logic that combines two unrelated metrics

### Why Improve Timeline?
- **Clarity**: Distinct colors make it easier to see when dithers vs centers occurred
- **Terminology**: "Tracking" is clearer than "stable"
- **Usability**: Legend helps users understand the visualization

### Why Add Setting Indicators?
- **Transparency**: Users should know which settings are direct vs. inferred
- **Trust**: Clear indicators build confidence in the data
- **Optional**: This is an enhancement, not a core simplification

---

## Next Steps

1. Create feature branch: `feature/simplify-report`
2. Implement Phase 1 (quick wins)
3. Test thoroughly
4. Commit with message: "Phase 1: Remove report bloat (movement totals, settings hints, redundant boxes, sequencer events)"
5. Repeat for Phases 2-4
6. Merge to main after all phases complete

---

## Notes

- All line numbers are approximate and may shift as code is modified
- Test after each phase before proceeding to the next
- Keep commits atomic (one phase per commit) for easier rollback
- Update CHANGELOG.md after all phases complete
- Consider bumping version number (e.g., 1.x.0 → 1.y.0) to signal significant UX change
