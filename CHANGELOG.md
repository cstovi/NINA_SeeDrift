# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Changed

- **SeeDrift Stop (nothing to report):** When Stop finds fewer than two usable saved-light lines, LIGHT frames after FITS filter, or successful plate solves, NINA’s status bar now shows **Complete — no drift report…** instead of **Stopped —**. If **Discord webhook** is set, SeeDrift sends a **text-only** webhook (no HTML attachment). **Previous session report** still shows the informational dialog on failure and does not upload to Discord.

## [0.8.26] — 2026-05-14

### Fixed

- **NINA status bar percent:** `ApplicationStatus` defaults to `ProgressType = Percent`, where `Progress` is a **0–1** fraction. SeeDrift was sending **0–100** with `MaxProgress = 100` (intended as percent-complete), which made the main status bar show nonsensical values such as **10000%** at the end of a run. Progress updates now set `ProgressType = ValueOfMaxValue` for that scale.

### Changed

- **Session settings used** card in the night HTML is now **collapsed by default** (click the header to expand). The card behavior in **Compare saved reports** is unchanged — the comparison HTML still renders the settings table inline.
- **Drift recommendation wording:** the high-drift recommendation no longer assumes a tripod-mounted setup. The hint now reads *"…compare altitude, stability, wind, and polar/framing setup between sessions."* (was *"tripod stability"*). Same trigger threshold and tone — text only.

## [0.8.25] — 2026-05-11

### Added

- **Realized dither magnitude** line in the *Session settings used* panel, shown directly under **Mount Dither Device — Dither Pixels** when the night had at least 3 commanded/measured dither pairs: *"Realized X.X px (Y%) median across N pulses"*. The commanded magnitude is the matched `DirectGuider.SelectDitherPulse` Δx/Δy; the measured magnitude is the frame-to-frame Δ converted to detector pixels via the median plate scale. Suspect dithers are excluded (same rule as the assessed-sum totals and effectiveness score).
- **Realized-magnitude recommendation:** when a target's median realized ratio is `< 80%` across `≥ 3` realized samples, a new warn-level recommendation appears: *"Logged dithers measured roughly Y% of the commanded magnitude (median X.X px measured vs Z.Z px commanded across N pulses). On small EQ mounts this is most often Dec backlash on direction-reversing dithers or a small guide-rate calibration offset; a too-short settle time can also bias the measured centroid via residual mount motion during the next exposure. Worth checking the Seestar Alpaca RA/Dec guide rates and, if the shortfall persists, raising settle time."* NINA runs the dither pulse to completion before the settle countdown starts, so a low realized ratio is mostly a mount-mechanics signal (backlash, guide-rate calibration), with settle as a secondary contributor via motion-blur centroid bias — separate from the existing "Weak" dither check (which uses frame-to-frame noise as the floor, not the commanded slew).
- `TargetAnalysis` now carries `MedianCommandedDitherPixels`, `MedianRealizedDitherPixels`, `MedianRealizedDitherRatio`, and `RealizedDitherSampleCount`. `SessionSequencerSettings` exposes pooled run-wide values (`RealizedCommandedDitherPixels`, `RealizedMeasuredDitherPixels`, `RealizedDitherRatio`, `RealizedSampleCount`) — the run-wide value is the mean of the per-target medians, matching how the comparison view averages medians today.

### Notes

- Reports made before 0.8.25 still render — the realized line is hidden when the sample count is below 3 (older payloads, plate-scale-less runs, or runs whose logs did not include `DirectGuider` Δx/Δy).

## [0.8.24] — 2026-05-11

### Changed

- **Drift / walking-noise advisory** is now split into two sub-tiers, each shown as its own chip in the night report strip, with the surrounding box tone driven by the worse of the two:
  - **Star shape** — per-exposure motion concern. Thresholds relaxed to match the community "< 1–2 px per exposure is acceptable" rule of thumb: Moderate at `>= 1.0 px` (`>= 2.0″`), Caution at `>= 2.0 px` (`>= 4.0″`). Consistency-only escalation still requires a small magnitude floor.
  - **Walking noise** — correlated FPN concern. Driven by a new **dither headroom ratio** = `median dither |Δ| (px) / median cumulative drift between dithers (px)`. Caution when `consistency ≥ 0.75 AND headroom < 2× AND between-dither drift ≥ 0.5 px`; Moderate when `consistency ≥ 0.5 AND headroom < 4× AND ≥ 0.25 px`; Low otherwise. Targets with fewer than two logged dithers fall back to the previous worst-short-window check.
- The advisory `Detail` line now says which signal drove the headline (e.g. *"Walking-noise signal drives the headline: dither headroom 1.5× with 87% directional drift between dithers."*) so a Caution headline never surprises when stars in single exposures look fine.

### Added

- `DriftRiskSummary` now carries `StarShapeStatus`/`StarShapeTone`, `WalkingNoiseStatus`/`WalkingNoiseTone`, `BetweenDitherDriftArcSec`, `BetweenDitherDriftPixels`, `DitherHeadroomRatio`, and `WalkingNoiseReason` — surfaced in the embedded JSON so the comparison view can read them.
- **Walking-noise hint** line in the strip shows the new headroom number, e.g. *"dither headroom 3.4× (median dither magnitude vs cumulative drift between dithers — 1.62″ between dithers · ≈ 0.41 px between dithers). Higher is safer."*
- **Recommendations** rewritten around the two sub-tiers:
  - Star-shape Caution → suggests shorter exposures, polar alignment, or guiding (if applicable).
  - Walking-noise Caution → suggests increasing **Mount Dither Pixels** in NINA, dithering more often, and addressing field drift (polar alignment / differential flexure). When stars look fine but walking-noise is Caution, an info line clarifies that single-exposure stars should still look fine.
  - When a logged dither repeated the same direction as the natural drift, an extra recommendation notes that randomization isn't breaking the FPN pattern.
  - Walking-noise Moderate + `headroom < 3×` → info recommendation that the margin is workable but a larger dither widens it.
- **Compare saved reports** gains two new metric rows: **Dither vs drift headroom** (×, higher is better) and **Worst short-window drift** (px, lower is better). Older reports without `DriftRisk` walking-noise fields show `0` and surface no change.

## [0.8.23] — 2026-05-11

### Changed

- **Logged dither intervals total** now reports the **assessed** sums only — suspect tracking jumps (already excluded from the dither effectiveness scoring) are no longer added to `Σ|ΔRA|` / `Σ|ΔDec|` (or the detector `Σ|Δx|` / `Σ|Δy|`) above the per-row table. The same line now appears as `Logged dither intervals total (assessed)`.
- When at least one suspect interval is detected, a new amber subtext lists how many intervals were skipped and the discounted Σ values so the original number can still be reconstructed.
- A new "**Typical dither**" companion line shows the median |Δ| across assessed dithers (with detector px when plate scale is known). This makes a single huge suspect jump hide less of the typical session behaviour.
- **Walking-noise / drift advisory** (`SessionAnalysisService.ClassifyDriftRisk`) no longer escalates the tier on direction consistency alone. The 0→Moderate bump now also requires `≥ 0.15 px` (or `≥ 0.6″`) of estimated per-exposure drift; the Moderate→Caution bump requires `≥ 0.25 px` (or `≥ 1.0″`). Tame traces with consistent but low-magnitude drift will no longer surface as **Caution**.

### Added

- `TargetAnalysis` now carries `DitherIntervalAssessedSumAbsRaArcSec`, `DitherIntervalAssessedSumAbsDecArcSec`, `DitherIntervalMedianMoveArcSec`, and `DitherIntervalMedianMovePixels` so the same numbers reach the embedded JSON payload.
- **Compare saved reports** adds three new metric rows derived from the above: *Typical dither (median |Δ|)*, *Σ|ΔRA| over assessed dither intervals*, *Σ|ΔDec| over assessed dither intervals*. Older reports without the per-target fields fall back to summing the dither events directly, so comparison still works across plugin versions.

## [0.8.22] — 2026-05-11

### Added

- **Compare saved reports — Session settings table:** The comparison HTML now includes a **Session settings used** table directly under the metric table, showing **Before / After** values and a **Changed / Same / —** badge for each row in the night-report settings panel: Mount Dither Pixels, CenterAfterDrift max arc-minutes, CenterAfterDrift evaluate cadence, DitherAfterExposures cadence, and dither pulse durations.
- **Overall read tip:** When any setting differs between the two reports, the comparison's overall summary appends a one-line tip pointing the user at the settings table so metric improvements / regressions can be read alongside the configuration change.
- Reports made before 0.8.21 still compare — settings rows for those show `—` and are tagged `Missing`.

## [0.8.21] — 2026-05-11

### Added

- **Session settings used panel:** New run-wide card near the top of each night HTML reports the **NINA + Seestar** values that drove the session — read from existing log lines (no new file scans):
  - **Mount Dither Device — Dither Pixels** (median commanded `|Δ|` from `DirectGuider.SelectDitherPulse` lines; proxy for the configured Dither Pixels).
  - **Center After Drift — Max arc-minutes** (configured threshold from `CenterAfterDriftTrigger ... Drift: X / Y arc minutes` evaluation lines).
  - **Center After Drift — Evaluate after exposures** (cadence inferred from frame gaps between evaluation lines).
  - **Dither After Exposures — Cadence** (every N exposures; parsed from `AfterExposures =` on the Starting Trigger line when present, otherwise inferred from frame gaps between dither triggers).
  - **Dither pulse guide durations** (median seconds across pulses; Seestar Alpaca RA/Dec guide rate is only visible via these durations and is labelled as a proxy).
- Embedded JSON payload gains an additive **`SequencerSettings`** block carrying the same values for future comparisons; older reports without this block continue to load.

### Changed

- `NinaLogCorrelator.AnnotateWithLogEvents` now exposes a `SessionLogObservations` record (additive overload; the original signature is preserved).

### Changed

- **Drift / walking-noise risk:** The strongest advisory tier label is now **Caution** (was **Investigate** in 0.8.19, **High** before that) — familiar advisory wording that does not assert a certain outcome.

## [0.8.19] — 2026-05-10

### Changed

- **Drift / walking-noise risk:** The strongest advisory tier label is now **Investigate** instead of **High**, to reflect that the metric is heuristic and depends on many imaging variables.

## [0.8.18] — 2026-05-10

### Changed

- **Plate solving:** When **Minimum exposures per target** cannot be reached **even if every not-yet-solved frame succeeded**, remaining plate solves for that run are skipped (typical case: many failures on one target). LIGHT-frame counts were already checked before solving; this addresses wasted time when solves fail.

## [0.8.17] — 2026-05-10

### Fixed

- **Seestar identity:** Scope detection now recognizes ASCOM **`Disconnecting from … Seestar … Telephoto Camera`** lines when connect/discovery was absent (e.g. hardware already connected when the session started).

## [0.8.16] — 2026-05-10

### Fixed

- **Previous session panel:** Completion status, progress, and **Open** now apply only when the log path matches that run — either the report on disk for the selection or the last save whose contributing NINA log(s) include the selected file. Switching the dropdown no longer leaves another session’s “Complete — …” line or **Open** link attached to a different night HTML (e.g. Stop vs previous-session mix-ups).

### Changed

- **Recent logs dropdown:** Tooltip clarifies **Unknown scope** (no matching Seestar discovery / telephoto camera lines in that `.log` scan).

## [0.8.15] — 2026-05-10

### Added

- **Previous session report:** Night HTML embeds **NINA source log path(s)** and total **processing time** in the JSON payload. The options panel resolves the **newest** matching report in **`%LocalAppData%\NINA\SeeDrift\Reports`** for the selected log path and shows **Open** plus a short summary (frames, processing time) when you change the recent-log dropdown or path—without re-running the job. Reports generated before this metadata may still match via header text.

## [0.8.14] — 2026-05-10

### Changed

- **Report library:** The delete control is labeled **Delete selected**, with tooltip and confirmation text stating explicitly that **one** highlighted `.html` file is removed per action (refresh never deletes). Choosing nothing shows a short hint instead of doing nothing silently.

## [0.8.13] — 2026-05-10

### Added

- **Report library:** **Delete** removes the selected saved HTML report from **`%LocalAppData%\NINA\SeeDrift\Reports`** after confirmation (only files in that folder). Compare **Before** / **After** paths and the last **Open** night-report link are cleared if they pointed at the deleted file.

## [0.8.12] — 2026-05-10

### Fixed

- **Seestar identity:** Scope detection now recognizes **`Successfully connected Camera`** log lines that include **`Seestar … Telephoto Camera`** (Alpaca telephoto camera path), so reports are no longer labeled **Unknown scope** when NINA never logged the Alpaca telescope discovery line.

## [0.8.11] — 2026-05-10

### Changed

- **Recent log picker:** Logs with zero detected targets are now hidden from the session dropdown so startup-only or non-imaging logs do not clutter the picker. Browse/paste still accepts any log path.

## [0.8.10] — 2026-05-10

### Added

- **Seestar identity:** Recent-log and saved-report dropdowns now show the connected Seestar model/serial when NINA logged a line such as `Discovered Alpaca Device Seestar S30_0ac17a9b Telescope`.
- **Scope-aware reports:** Night reports embed and display the detected Seestar identity, include it in report filenames when known, and comparison reports warn when before/after reports came from different Seestar devices.

## [0.8.9] — 2026-05-10

### Changed

- **Drift risk thresholds:** The advisory Low / Moderate / High card now separates a per-exposure **star-shape hint** from a short-window **walking-noise hint**. When FITS plate scale and exposure duration are available, thresholds are pixel-aware; directionally consistent drift can raise the warning level.

## [0.8.8] — 2026-05-10

### Changed

- **Dropdown labels:** Recent log and saved report pickers now use compact target/frame/image counts instead of long target-name lists.
- **Saved report picker:** Saved reports now display the imaging session date from the embedded report payload, instead of the date/time the HTML was generated.

## [0.8.7] — 2026-05-10

### Added

- **Drift / walking-noise risk:** Night HTML now shows an advisory color status box per target. It separates natural drift intervals from logged dither/center commands and reports **Low**, **Moderate**, or **High** using drift rate and direction consistency. The wording avoids treating those levels as an image-quality verdict.

## [0.8.6] — 2026-05-10

### Changed

- **Night HTML:** Renamed the run-duration label from **Wall time** to **Processing time** for clearer wording.

## [0.8.5] — 2026-05-10

### Added

- **Report library:** SeeDrift now stores night and comparison reports in `%LocalAppData%\NINA\SeeDrift\Reports`; the previous user-configurable report folder option was removed to keep reports in one predictable place.
- **Compare saved reports:** Added a saved-report dropdown sourced from the report library. Use **Before** / **After** buttons to fill the existing comparison paths, or **Open folder** to view the library in Explorer; Browse/paste still work for reports saved elsewhere.

## [0.8.4] — 2026-05-10

### Added

- **Previous session Log:** Added a recent-log picker that scans the last 14 days of NINA logs and summarizes each log by scope, target count, usable image count, and duration. Selecting a row fills the existing log path; Browse/paste remains available for older or custom logs.

## [0.8.3] — 2026-05-10

### Changed

- **Report compatibility:** Saved reports now expose generator version and report schema in HTML metadata and the visible header. Compare accepts reports from different plugin versions when the embedded schema is supported, and rejects unsupported future schemas with a clear message.
- **Report filenames:** New night and comparison HTML filenames include the plugin version (`SeeDrift_vX_Y_Z_W_...`) so outputs are easier to trace back to the generating build.

## [0.8.2] — 2026-05-10

### Changed

- **Compare saved reports:** Before/after comparison now uses whole-report averages instead of matching target names. It compares assessed dither RA movement, dither Dec movement, RA/Dec balance, weak-dither rate, average center-after-drift improvement, and ineffective-center rate, then gives an overall better/worse/mixed read.

## [0.8.1] — 2026-05-10

### Changed

- **Night HTML:** Drift charts now mark exposure-number gaps with an amber **?** tooltip: **Possibly missing/unsolved frames**. This indicates logged exposures fall between plotted solved points without claiming why they are absent.
- **Night HTML:** Logged-dither interval results now render as a clearer bordered block: the bold **Logged dither intervals total** sits above a collapsed-by-default table of the individual frame-pair dither measurements.
- **Analytics:** Very large outlier dither intervals are now classified as **suspect tracking**, excluded from weak/repeated-direction dither scoring, and reported with discounted absolute RA/Dec totals.

## [0.8.0] — 2026-05-10

### Added

- **Release safety:** Tagged the known-good `0.7.35` build as **`v0.7.35`** and pushed it to origin before starting analytics work.
- **Night HTML analytics:** Each target now shows **drift rate**, **dither effectiveness**, **center-after-drift effectiveness**, a compact **session quality timeline**, and advisory **settings hints from this session**.
- **Structured report data:** Generated HTML embeds a `seedrift-report-data` JSON payload with solved samples, event metrics, drift summaries, and recommendations so future tools can reuse results without re-solving FITS files.
- **Before/after comparison:** Plugins → SeeDrift now has **Compare saved reports**. Pick two analytics HTML reports and SeeDrift writes a comparison HTML from the embedded payloads only—no FITS scan and no plate solving.

### Changed

- **Night HTML:** The **Sequencer events (NINA logs)** table is wrapped in a **collapsed-by-default** `<details>` block (click the summary to expand). The **dither-interval** caption shows the **Total — Σ|ΔRA| / Σ|ΔDec|** line **first**, in **bold**, then **Exposures with logged dithers between them.** and the per-interval lines.

- **Documentation layout:** Shared See\* docs (**`gitship.mdc`**, **`NINA_IMAGE_PATH_ENUMERATION.md`**, **`NINA_plugin_guide.md`** including session-scoped exports) now live only in the workspace sibling folder **`../NINA_shared/`** (same parent as this repo). The nested **`NINA_SeeDrift/NINA_shared/`** copy was removed; `.cursor/rules/gitship.mdc` points agents at **`../NINA_shared/`**.

## [0.7.35] — 2026-05-10

### Changed

- **Stop / night HTML:** **SeeDrift Stop** pre-filters NINA logs by **local calendar dates** derived from the log filename (`yyyyMMdd-HHmmss-…`) around the arm→disarm window (plus one day before the earlier date) so the whole `%LocalAppData%\NINA\Logs` folder is not scanned when unnecessary. If no files match that filter, SeeDrift falls back to scanning **all** logs and logs a warning.
- **Night HTML header:** **NINA log files on this page** lists only logs that contributed at least one in-window **Saved image to …** line for that batch (and **Previous session report** still lists the file you picked). Sequencer correlation parses that same contributing set instead of every log that was opened.

## [0.7.34] — 2026-05-10

### Fixed

- **NINA status progress:** Plate-solve progress used raw frame counts (`Progress = done`, `MaxProgress = total`). NINA’s status bar percent uses a ratio that behaves like **`Progress / MaxProgress × 100`** when **`MaxProgress` is wrong or treated as 1**, which produced nonsensical values (e.g. **12300%** at 123/765). SeeDrift now reports **percent-complete integers** (`Progress` 0–100, **`MaxProgress = 100`**) while solving, and **100/100** for completion-style updates instead of `built.Count/built.Count`.

## [0.7.33] — 2026-05-10

### Changed

- **Night HTML (under each chart):** Trace totals drop the long **Σ|Δstep|** preamble (numbers unchanged). **Dither** block title is **Exposures with logged dithers between them.** and adds a **Total — Σ|ΔRA| / Σ|ΔDec|** line over dither intervals only (plus detector pixel sums when applicable). Removed the redundant **axes/square** explanatory paragraph.

## [0.7.32] — 2026-05-10

### Added

- **Night HTML:** Under each drift chart, when NINA logs correlate a **dither** on an inter-frame interval, an extra line lists **that interval’s** plate-solved **ΔRA / ΔDec** (signed segment delta vs the target’s first frame, same geometry as the chart). **Detector registration** runs also show **|Δx| / |Δy|** on those intervals.

## [0.7.31] — 2026-05-10

### Changed

- **Night HTML movement line:** Clarifies that totals are **Σ|Δstep|** on each axis between **consecutive** plotted frames (not net first→last displacement). Adds **pixel** equivalents: **detector** Σ|Δx|/Σ|Δy| when pixel-registration data exists; otherwise **≈** px from arcseconds ÷ median **″/px** parsed from FITS **XPIXSZ**/**FOCALLEN** per frame.

## [0.7.30] — 2026-05-10

### Added

- **Discord webhook:** Optional **Discord webhook** URL under **Plugins → SeeDrift**. After **SeeDrift Stop** successfully writes the rolling night HTML, the plugin POSTs that **`.html`** file to Discord (**Execute Webhook**, multipart). **Run previous session report** does **not** upload. Invalid URLs, oversize files (~25 MiB), and network failures are recorded only in **`SeeDrift.log`** (webhook URL is never logged).

## [0.7.29] — 2026-05-10

### Added

- **Run duration:** Each successful **Stop** or **Previous session report** records processing time from log read through HTML save. Shown in the **night HTML** batch line and in **completion status** (NINA status bar for Stop; plugin status panel for previous session).

### Changed

- **Plugins → SeeDrift:** Short **sequence tip** at the top (SeeDrift Start / Stop placement; note that many exposures mean heavy plate solving). Removed redundant **Previous session Log file** subheading under **Previous session Log**.

## [0.7.28] — 2026-05-10

### Changed

- **CPU topology:** Physical core count for concurrency limits uses **`kernel32!GetLogicalProcessorInformation`** (no **`System.Management`** / WMI). Fallback remains **`Environment.ProcessorCount`**. Install only **`NINA.Plugin.SeeDrift.dll`** (no extra **`System.Management.dll`**).

## [0.7.27] — 2026-05-09

### Changed

- **HTML report header:** Title and main heading use **Session Log** (replacing “night”) and the **session calendar date** derived from **NINA log file names** when possible (`yyyyMMdd-…` prefix, e.g. `20260421-221631-….log`). If no listed log path matches that pattern, SeeDrift uses the **earliest solved exposure** local day, then log **last-write** time, then today — same rule as the **`sess`** segment in **`SeeDrift_ran*_sess*.html`**. **Generated** still shows when the file was written.

## [0.7.26] — 2026-05-09

### Changed

- **Night HTML filename:** rolling report files are named **`SeeDrift_ranYYYYMMDD_sessYYYYMMDD.html`**: **`ran`** is the local calendar day when the report is written; **`sess`** is the session calendar day (**leading date in NINA log file names** when present; otherwise **earliest solved exposure**, then **earliest log file mtime**, then today).

## [0.7.25] — 2026-05-09

### Changed

- **Night HTML drift charts:** ΔRA and ΔDec use **matching arcsecond span** (max of the two data extents, plus padding, centered on the points—includes sequencer markers in the bounds). The chart area is **square** (`aspect-ratio: 1`) with Chart.js `aspectRatio: 1`, so the drift path is **isotropic** (no independent vertical stretching). Single-point or tiny spreads get a minimum span so the plot stays readable.

## [0.7.24] — 2026-05-09

### Fixed

- **Sequencer:** **SeeDrift Start** / **SeeDrift Stop** now set `Icon` to a **frozen** `GeometryGroup` defined in code (`SeeDriftIcons.InstructionIcon`) so the graphic appears in the instruction list and on dropped blocks **without** depending on `Application` resource timing. The motif is a compact **axes + drift trace** (distinct from the old crosshair-only glyph).

## [0.7.23] — 2026-05-09

### Fixed

- **Sequencer:** If NINA composes the plugin before `Application.Current` exists, icon registration **retries on the UI idle queue** (capped) so **SeeDrift_Icon** can still be published for hosts that resolve icon metadata keys.

## [0.7.22] — 2026-05-09

### Fixed

- **Sequencer:** **SeeDrift Start** / **SeeDrift Stop** now show the **SeeDrift_Icon** geometry in the instruction palette and on sequence blocks (same `GeometryGroup` as in `Resources.xaml`), by registering that resource on `Application.Current` at plugin load—matching how other See\* plugins expose icons to NINA’s app-wide resource lookup.

## [0.7.21] — 2026-05-09

### Added

- **Plugins → SeeDrift:** When a run finishes with a saved night HTML file, the status panel shows **Open** with the **file name** as an underlined click target (default browser via shell execute); tooltip shows the **full path**. The status sentence drops the duplicated trailing path when the link is shown. **NINA’s main status bar** remains plain text (host limitation).

## [0.7.20] — 2026-05-09

### Changed

- **Plugins → SeeDrift:** Renamed **Test report** to **Previous session Log**; primary action **Run previous session report**; status heading **Previous session report status**. Dialogs, log lines, night HTML copy, and docs use the same wording.

- **Plugins → SeeDrift:** **Concurrency** and **Minimum exposures** fields use a **narrow** value column (width tuned to the numeric range) instead of stretching full width.

- **MANUAL:** Explains that **one session is usually one NINA log**, but **log rotation** (e.g. **month** boundary) can split a session across **two** files—matching why **Stop** reads **all** `*.log` files under NINA Logs.

- **Plugins → SeeDrift:** Inline help for **Night report folder**, **Concurrency**, and **Minimum exposures** moved into **hover tooltips** on each row (longer read time on hover).
- **Plugins → SeeDrift:** Removed the static line under the previous-session-report status panel that compared in-plugin status to **Stop** (details remain in the manual).

### Added

- **`NINA_shared`** (workspace sibling folder) documentation: **`NINA_IMAGE_PATH_ENUMERATION.md`** and **`gitship.mdc`** describe how See\* plugins should enumerate files under NINA’s image directory using **`GetFilePattern(imageType)`** and **`$$IMAGETYPE$$`** (LIGHT vs DARK vs FLAT vs BIAS) instead of blind recursion.

## [0.7.19] — 2026-05-09

### Changed

- **Night HTML header:** Featured screenshot is **embedded in the DLL** (`Assets/SeeDrift_featured.png`, embedded resource `SeeDriftFeatured.png`) and written into the report as a **`data:image/png;base64,…`** `<img>` — **no HTTP request** for the artwork (works offline). **`FeaturedImageURL`** remains only as a fallback if the embedded resource is absent.

## [0.7.18] — 2026-05-09

### Changed

- **Night HTML header:** Uses the same **featured image** as the NINA plugin listing (**`FeaturedImageURL`** assembly metadata in `AssemblyInfo.cs`). If that metadata is absent, falls back to the **`SeeDrift_Icon`** SVG in `Resources.xaml`. The hosted PNG loads when online (first open).

## [0.7.17] — 2026-05-09

### Changed

- **Pipeline order:** After FAST reads identify LIGHT frames per FITS target, SeeDrift **skips plate solving** when **no target has enough LIGHT frames** to ever reach **Minimum exposures per target** (same OBJECT/header grouping as the report). After solving, if **no target reached** the threshold in **solved** frames, SeeDrift **skips NINA sequencer log correlation** (still writes the night HTML amber section).
- **Status / Test report panel:** Final **`Complete —`** line includes the same guidance as the HTML amber box (threshold, Plugins → SeeDrift, best-target counts when relevant).

## [0.7.16] — 2026-05-09

### Changed

- **Night HTML:** **R** or **Esc** resets drift chart zoom (same as double-click the canvas); header explains this. User-visible copy uses **run** instead of **batch** where it meant one Stop/Test completion; multi-file log list clarifies that **Stop** reads **all** `*.log` under NINA Logs while **Test report** uses **one** chosen file.

## [0.7.15] — 2026-05-09

### Changed

- **Night HTML:** Header **no longer** shows batch count or chart zoom/pan hints. It lists **full paths** to **NINA log files** used (union across batches on the page; scrollable list when there are many). Each **Target** block shows **Start / End** exposure times (**local**) beside the title, from the solved frames for that target.

## [0.7.14] — 2026-05-09

### Changed

- **Night HTML:** Page header shows the **SeeDrift** icon (**same 24×24 vector** as `SeeDrift_Icon` in `Resources.xaml`) **top-left** beside the title.
- **Plugin listing:** Short description no longer mentions **Chart.js** or **optional** sequencer wording; it now ends with **drift HTML with sequencer markers.**

## [0.7.13] — 2026-05-09

### Changed

- **Plugins → SeeDrift → Concurrency:** The dropdown is no longer fixed **1–16**. The maximum is **80% of reported CPU cores** (physical cores from WMI, or logical count if WMI fails), **rounded down**—e.g. **12 cores → 9**. Defaults and saved values are **clamped** to this ceiling so older settings above the new max drop to the max on load.

## [0.7.11] — 2026-05-09

### Added

- **Night HTML:** Under each target drift chart, show **total ΔRA and ΔDec movement** along the plotted trace as **Σ |Δstep|** between consecutive solved frames (arcseconds, matching chart geometry).

## [0.7.12] — 2026-05-09

### Fixed

- **Plugins → SeeDrift:** Restored **`PlateSolveParallelismOptions`** on the plugin (required for the concurrency **ComboBox** `ItemsSource`). Dropdown lists **1–16** again; explicit **foreground** on items for dark UIs.

## [0.7.10] — 2026-05-09

### Changed

- **Concurrency default** uses **physical CPU cores** via WMI (`Win32_Processor.NumberOfCores`), clamped **1–16**, with fallback to **`Environment.ProcessorCount`** if WMI fails.
- Ship **`System.Management.dll`** next to the plugin (install docs updated).

## [0.7.9] — 2026-05-09

### Changed

- **Concurrency** is a **dropdown** (1–16). **Default** for new settings matches **`Environment.ProcessorCount`**, clamped to **1–16** (not a fixed “4”). Existing `settings.json` values are unchanged.

## [0.7.8] — 2026-05-09

### Changed

- **Night HTML:** Chart **legend** shows the drift path only; **Sequencer (between frames)** is no longer listed (dither/center markers still draw and tooltips work).

## [0.7.7] — 2026-05-09

### Changed

- **Night HTML:** Batch subtitle shows **solved frame count only** (removed **stopped** local time under each batch heading).

## [0.7.6] — 2026-05-09

### Changed

- **Minimum exposures per target** default is now **50** (was 1). Existing `settings.json` values are unchanged until saved.
- **Plugins → SeeDrift:** Removed the **HTML export** section heading above **Night report folder**.

## [0.7.5] — 2026-05-09

### Removed

- **Clear completed targets (session)** button — session reset remains automatic at the start of each **Stop** / **Test report** run.
- **Working folder** (scratch/temp) option — it was never read by the solver (plate solving uses **`IImageDataFactory.CreateFromFile`** on your saved lights); the setting only cluttered **Plugins → SeeDrift**.

## [0.7.4] — 2026-05-09

### Added

- **Plugins → SeeDrift → Minimum exposures:** Night HTML includes **only** FITS targets that have at least **N** solved frames in that batch (1–500; default **1** preserves prior behavior). Batch headings and charts follow the same filter.

### Changed

- **Options UI:** Removed the introductory drift-report paragraph, the read-only NINA image path row, and the **Plate solve speed** section title; **Concurrency** and the new minimum-exposures row appear without that extra heading.

## [0.7.3] — 2026-05-09

### Fixed

- **Night HTML:** Multi-target batch **titles** list `OBJECT` names in **first-seen frame order**, matching the **Target:** subsections (previously the title was alphabetical).

## [0.7.2] — 2026-05-09

### Changed

- **Test report:** The status panel **no longer goes blank** when the run finishes; it keeps the last line (**Complete — …** or **Stopped — …**) until the next run. **Stop** success uses the same **Complete — …** wording on **NINA’s status bar**.

## [0.7.1] — 2026-05-09

### Fixed

- **Night HTML:** Removed Chart.js zoom **limits** clamped to the initial data bounds so wheel zoom can **zoom out** further (e.g. ~200% view / extra margin around the trace). Double-click still resets.

## [0.7.0] — 2026-05-09

### Added

- **Plugins → SeeDrift → Concurrency** (1–16, default **4**): plate-solves multiple LIGHT frames in parallel; each slot uses its **own** `IImageSolver` instance from NINA’s factory (no shared solver across concurrent tasks).

### Changed

- **Performance:** Reuses the **primary FITS header** read during LIGHT filtering so Stop/Test does **not** open-and-parse each header twice before solve.
- Drift samples are still built in **strict log/frame order** after parallel solves complete.

## [0.6.8] — 2026-05-09

### Changed

- **Night HTML drift chart:** **ΔDec** vertical axis uses **`reverse: true`** so “up” on the chart matches common imaging/camera vertical sense; tooltip numbers are unchanged.

## [0.6.7] — 2026-05-09

### Changed

- **Night HTML:** Correlated **dither** (triangle) and **center-after-drift** (square) markers are drawn **along the drift segment between the two frames** they belong to — at the midpoint when there is one event, or **evenly spaced** along that chord when several fall in the same interval. Hover shows the same log detail as the table.

## [0.6.6] — 2026-05-09

### Changed

- **Night HTML:** Drift chart — **start/end** points emphasized (green/orange; single-frame runs use one amber marker); **tooltip** shows **FIT file name** first, then ΔRA/ΔDec. **Sequencer** table uses **wrapped** From/To paths and a fixed column layout so **Detail** stays readable.

## [0.6.5] — 2026-05-09

### Fixed

- **Night HTML:** Saving failures no longer show a false “Done” message; the status line includes the **full output path** on success. Empty **Night report folder** resolves to **`Documents\SeeDrift`** (explicit in options + manual).

## [0.6.4] — 2026-05-09

### Changed

- **Night HTML:** Within each batch, **one drift chart + sequencer block per FITS target** (`OBJECT`). Drift is re-zeroed at each target’s first frame (no mixed-target trace). Sequencer table rows only include edges where **both** frames share that target.

## [0.6.3] — 2026-05-09

### Fixed

- **Log parsing:** Broader **Saved image to** line matching (SaveToDisk variants, optional colon) and **embedded timestamps** when the clock is not at column zero — fixes false “no batch” results on valid NINA logs.
- **XISF:** Session logs pointing at **`.xisf`** lights are no longer skipped by extension (NINA’s common save format).

## [0.6.2] — 2026-05-09

### Changed

- **`settings.json`:** Removed obsolete **Test observation start/end** UTC fields (log-file workflow only); loading settings rewrites the file without those keys.
- **Logging:** SeeDrift messages are mirrored to **`%LocalAppData%\NINA\SeeDrift\SeeDrift.log`** in addition to NINA’s application log.

## [0.6.1] — 2026-05-09

### Fixed

- **Test report:** Clear step-by-step status while reading the log, scanning FITS headers, initializing the solver, and solving; bordered progress panel directly under **Run test report**; dialog when no batch is produced; INFO log line when a run starts.

## [0.6.0] — 2026-05-09

### Changed

- **Breaking (behavior):** Drift batches no longer walk the imaging folder tree by FITS observation window. **SeeDrift Start→Stop** collects **`Saved image to …`** paths from **`%LocalAppData%\NINA\Logs\*.log`** with timestamps between Start and Stop. **Test report** replaces date/time pickers with a **user-selected `.log` file** and solves every light path referenced there.
- **Correlation:** Sequencer annotation parses the **same log file set** (Stop: all logs in folder; Test: chosen file) so triggers align with the frames list.

## [0.5.10] — 2026-05-09

### Changed

- **Test report:** Observation start/end pickers use **local wall clock** (labels and defaults); values are still saved as UTC ISO in `settings.json` for compatibility. Existing saved windows reload as the correct local equivalent.
- **Log correlation:** Chooses log files by **local calendar date** from the first frame (matches typical NINA log filenames and local timestamps). HTML empty-state copy clarifies local vs rotation/path mismatches.

## [0.5.9] — 2026-05-09

### Changed

- **Night HTML:** Tailwind (CDN) layout, dark theme; **Chart.js** + **chartjs-plugin-zoom** + **Hammer.js** for wheel zoom, drag pan, double‑click reset; **Sequencer events** section always rendered with an explanatory empty state when log correlation finds no triggers.
- **Plugin options:** Test-report **progress bar** uses high-contrast colors on dark UI; short hint that progress appears only during **Run test report** (Stop uses NINA’s status bar).

## [0.5.8] — 2026-05-09

### Fixed

- **Plugin options UI:** `ProgressBar` bindings now use **`Mode=OneWay`** for read-only progress properties (`TestReportProgressValue`, etc.), fixing NINA startup **`XamlParseException`** (“TwoWay or OneWayToSource binding cannot work on the read-only property …”).

## [0.5.7] — 2026-05-09

### Fixed

- **Night HTML:** Subtitle now reports **batch count** (each Stop/Test run), not “targets”. Section titles list **distinct FITS OBJECT names** in that batch so mixed-target nights no longer show only the first frame’s target while the sequencer table lists other file names.

## [0.5.6] — 2026-05-09

### Changed

- **Folder scan:** UTC-window discovery follows the active profile’s **NINA LIGHT file pattern** (directory segments derived from **Options → Imaging → File pattern**), matching each level with NINA’s pattern tokens—`$$IMAGETYPE$$` resolves to **LIGHT**—instead of a fixed list of skipped folder names.

## [0.5.5] — 2026-05-09

### Changed

- **Folder scan:** Recursive UTC-window discovery skips subdirectory names used for calibration stacks, rejects, and similar outputs (for example `_rejected`, `calibrated`) instead of walking the entire tree under the NINA image path.

## [0.5.4] — 2026-05-09

### Added

- **Test report:** Progress bar and status line on the plugin options page while **Run test report** scans and plate-solves (same `ApplicationStatus` pipeline as sequencer Stop; Run disabled until finished).

## [0.5.2] — 2026-05-09

### Changed

- **Test report:** Observation window uses **DatePicker** and **hour/minute ComboBoxes** (UTC) instead of typing ISO 8601 text. Settings file still stores the same ISO keys.

## [0.5.1] — 2026-05-09

### Fixed

- **Scan performance:** Recursive folder scan for the UTC window skips reading FITS headers when both file timestamps’ **years** fall outside the window’s year span (fast exit for wrong-year test dates); periodic status text while scanning.

## [0.5.0] — 2026-05-09

### Breaking

- **Removed** live **dockable** drift chart (**OxyPlot**) and **ImageSaved** streaming drift.
- **Removed** cumulative **pixel / SSD registration** path and header-only drift plotting.

### Added

- **Plate-solve batch:** Uses **`IPlateSolverFactory`** / **`IImageSolver`** with **`IImageDataFactory.CreateFromFile`** (same stack as NINA plate solving).
- **Recursive scan** of the **NINA profile image file path** with **UTC inclusive** `[SeeDrift Start, SeeDrift Stop]` filtering (sequencer) or persisted **Test observation** window (options).
- **HTML:** Night report sections include a **Sequencer events** table when **NINA log** correlation finds **between-frame** dither / center-after-drift triggers.

### Changed

- Plugin UI is **options-only** (no dockable); **SeeDrift Stop** instruction awaits **`DisarmAsync`** (batch solve + report).

### Removed

- Unused **`WriteReport`** HTML helper (dead code).
- **`IImageSaveMediator`** dependency from the plugin constructor.

## [0.4.19] — 2026-05-09

### Fixed

- **Pixel registration:** Validate frame-to-frame SSD shift with an **inverse** registration (cur→prev). If `forward + inverse` is not small in pixels, or the forward shift exceeds **42%** of the crop’s shorter side, the step is **rejected** (no change to cumulative offset for that frame) instead of accepting a false minimum. Reduces spurious huge “jumps” / derived arcsec spikes when consecutive images still match visually.

### Documentation

- **MANUAL:** Note bidirectional check and per-frame shift cap.

## [0.4.18] — 2026-05-09

### Fixed

- **Jump detection (pixel registration):** When the trace has plate-scale **derived** ΔRA/ΔDec on every frame (the dockable’s arcsec plot), classify jumps using **derived** frame-to-frame steps instead of cumulative **pixel** steps. The old pixel-only metric could disagree with the drawn path (per-frame `dec` / Alt-Az `q` in `PixelShiftToRaDec`), producing jump diamonds that did not match visible kinks or missing obvious ones.

### Documentation

- **MANUAL:** Explain jump metrics in header vs pixel vs derived-pixel modes.

## [0.4.17] — 2026-05-09

### Changed

- **Plot legend:** Dither/center sequencer series titles drop the redundant “(between frames)” wording.
- **Summary line:** After a successful log read, shows **DitherAfterExposures** and **CenterAfterDrift** counts for triggers whose UTC falls between the **first and last frame** exposure starts (inclusive), plus the existing date-window total and interval count.

### Documentation

- **MANUAL:** Note trace-span trigger counts in the dockable summary.

## [0.4.16] — 2026-05-09

### Changed

- **NINA log — between-frame plot:** Every **DitherAfterExposures** and **CenterAfterDrift** trigger in a strict inter-frame interval gets its own marker on the **segment** between the two frame points (not only the first of each type). Several events in the same gap are **evenly spaced along that line, centered on the midpoint**. Each hover carries log detail for that trigger plus the shared measured frame-to-frame lines; multiple dithers claim distinct `SelectDitherPulse` lines in order when the log provides them.

### Documentation

- **README / MANUAL:** Describe multi-marker between-frame behavior.

## [0.4.15] — 2026-05-09

### Fixed

- **NINA log correlation:** Parse **fractional seconds** in log timestamps (e.g. `T00:09:33.6229`) instead of truncating to whole seconds.
- **Between-frame markers:** Extend the inter-frame **upper bound** by **1.5 s** when matching triggers, `SelectDitherPulse`, and center-drift lines so dithers are not dropped when they fall just after a second-rounded FITS `DATE-OBS` cap (same-second boundary issue).

### Documentation

- **MANUAL:** Note fractional log times and slop for strict windows.

## [0.4.14] — 2026-05-09

### Fixed

- **Plot hovers:** Frame and jump tooltips, and **between-frame** log tooltips, show the **NINA exposure number** from the FITS basename (`…_0019.fits` → 19) when it parses, instead of **trace position** (`FrameIndex + 1`), so labels match file names and logs.

### Documentation

- **MANUAL:** Clarify exposure index vs trace order for hover frame numbers.

## [0.4.13] — 2026-05-09

### Added

- **Plot hovers:** Blue **Frames** and yellow **Jumps** tooltips append the FITS **basename** on the last line (after time/coordinates) for quick cross-check with disk files.

### Documentation

- **MANUAL:** Note filename line on frame/jump hovers.

## [0.4.12] — 2026-05-09

### Fixed

- **Folder import:** Sort **primarily** by filename exposure index (`_0019`, `_0020`, …), then **DATE-OBS** — the previous “DATE-OBS first, filename only on ties” order could still swap consecutive subs when headers differed by even one second, shifting between-frame log markers by one frame.
- **NINA log intervals:** When using paired **SaveToDisk** times, cap **`t1`** at the next frame’s **FITS exposure start** when it falls inside `(save_prev, save_cur)` so the following gap’s trigger is less likely to fall into the wrong interval.
- **Tooltips:** Between-frame line includes **`prev` → `cur` file names** so frame index and FITS name can be checked at a glance.

### Documentation

- **README / MANUAL / plot subtitle / HTML report:** Describe filename-primary sort.

## [0.4.11] — 2026-05-09

### Fixed

- **Folder import order:** After **DATE-OBS** (etc.), tie-break by numeric suffix after the last `_` in the file name (e.g. `…_0019.fits`) so plot **frame numbers** match NINA’s exposure sequence when timestamps are identical or ambiguous — fixes between-frame log markers appearing one frame late (e.g. center/dither shown **20→21** instead of **19→20**).
- **SaveToDisk map:** Use the **earliest** logged save per file name so a later re-save does not move the inter-frame window past the real trigger time.

### Documentation

- **README / MANUAL / plot subtitle / HTML report:** Note sort order includes filename exposure number on ties.

## [0.4.10] — 2026-05-09

### Fixed

- **NINA log — between-frame intervals:** When logs include **`SaveToDisk`** lines for both frames in a pair, the strict `t0`/`t1` window uses those **log save timestamps** (matched by FITS **file name**) instead of **exposure start** times, so dither/center markers are not dropped when FITS observation time and NINA log time use mismatched bases. **PlateSolver** and **`AppData\Local\Temp`** save paths are ignored for pairing.

### Documentation

- **MANUAL:** Described **SaveToDisk**-based interval bounds and timezone note.

## [0.4.9] — 2026-05-09

### Documentation

- **README / MANUAL:** Removed obsolete **Math.NET** install step; noted real dependencies (**OxyPlot**, **Newtonsoft.Json**, etc.) if the host needs them.
- **MANUAL:** Pixel path described accurately (**SSD template** registration, sub-pixel refinement; **not** FFT). **Live vs import:** while **armed**, live uses the same header vs pixel mode as the option (locked at **Start**), not “headers only.”
- **CHANGELOG (0.4.4 note):** The pixel path has never used Math.NET FFT; that line in the log was wrong (retroactive clarification in this release).
- **HTML export:** Pixel-mode report blurb matches the real registration method.

### Changed

- **`FolderPlotMode`:** XML docs now match live + import behavior.
- **Options UI:** Drift source section notes that the choice applies to **folder import** and **live while Recording**.

## [0.4.8] — 2026-05-09

### Changed

- **NINA log UI:** “Center after drift” / “Dither (after exposures)” names and detailed log hover text appear **only** on **between-frame** markers (strict inter-exposure window). Removed **45‑minute nearest-trigger** hints from **frame** dots; **jump** diamonds no longer append the next-frame interval blurb or log trigger names — **JumpReason** stays step-detection only.
- **Subtitle:** Jump/log line counts **next-frame strict intervals** only (wording updated).

### Documentation

- **MANUAL:** Technical notes aligned with strict-interval-only sequencer labeling.

## [0.4.6] — 2026-05-09

### Fixed

- **Plot tracker:** Tooltips now **hide when the pointer leaves** the snap radius around any series point (no need to click or exit the chart).

## [0.4.5] — 2026-05-09

### Added

- **NINA log tooltips:** Between-frame hovers show **dither target** as logged (`from` → `to`, Δx/Δy in guider units, optional pulse **durations**) and **center-after-drift** **Drift / threshold** in **arc minutes** when those lines appear in the session logs.

### Changed

- **Between-frame hover:** Measured lines are ordered **header Δ → derived Δ → cumulative-pixel step** so arcsec comparisons sit together.
- **Dither log parse:** `SelectDitherPulse` lines allow spaces after commas in coordinates (e.g. `(0, 0)`).

## [0.4.4] — 2026-05-09

### Added

- **NINA log — between-frame markers:** Orange **triangles** (dither) and cyan **squares** (center-only) at segment **midpoints** when a sequencer trigger falls between consecutive exposure starts; tooltips show **guider Δ** from `SelectDitherPulse` and **measured** Δ (header / pixels / derived arcsec). Jump diamonds include the next-frame interval blurb when relevant.
- **NINA log — merge all matching log files** for the session date ±1 day (not only the first filename match per pattern).
- **Live — NINA log correlation:** While **armed**, each new saved LIGHT is annotated with the same **jump detection** and **`Starting Trigger:`** log matching (45-minute window) as **folder import**, so tooltips and the plot subtitle can show sequencer hints and log-correlated jumps during capture.
- **Folder import — pixel path:** optional **central-crop registration** (SSD template match, coarse-to-fine + parabolic sub-pixel refinement — not FFT / Math.NET). Plots **cumulative Δx/Δy in detector pixels** starting at (0,0). Slower; shows dither/drift when FITS **RA/Dec** headers do not. Uncompressed primary HDU only.
- **Dither log overlay:** extension point only (`DitherLogOverlay` placeholder) for a future iteration.

### Removed

- Plugin options **Only record Seestar cameras** and **Reset trace when FITS OBJECT name changes**. SeeDrift always records saved **LIGHT** frames with readable pointing keywords; the trace does not auto-clear when **OBJECT** changes — use **Reset trace** or separate folders/sessions as needed.

### Changed

- **Jump detection (pixel registration):** Threshold also uses **MAD** (median absolute deviation) so repeated similar dithers can still be flagged as jumps when they are outliers relative to the bulk of steps.
- **Plot subtitle:** Reports **NINA log** trigger count and **between-frame interval** count; clarifies jump matching (nearest trigger **or** next-frame interval).
- **README:** Clarify that `dotnet build` without `-c` is **Debug**; use `-c Release` for shipping and `bin\Release\...` artifacts.
- Pointing plot **subtitle** shows how many frames were plotted so sparse traces are obvious at a glance.
- Slightly smaller plot markers when many frames are loaded (>100) for readability.

## [0.4.3] — 2026-05-09

### Changed

- **Pixel → ΔRA sign:** Restore the original EQ and Alt/Az formulas (`ΔRA` ∝ `-dx` as before the experimental horizontal flip).

## [0.4.2] — 2026-05-09

### Fixed

- **Plot hover:** Larger tracker snap radius (32 px); series order refactored so **Frames** scatter and **Jumps** draw above the path line and start/end markers — tooltips hit blue dots reliably. Frame tags include optional **SequencerLogHint**.
- **NINA log correlation:** Match only **`Starting Trigger:`** lines for **Center after drift** and **Dither (after exposures)** (no plate-solve / drift-metric spam). Nearest trigger within **45 minutes** of each frame’s exposure time populates **SequencerLogHint** on every frame and augments **JumpReason** on detected jumps.

## [0.4.1] — 2026-05-09

### Fixed

- **NINA log jump correlation:** Recognize NINA 3.2 sequencer wording (`CenterAfterDriftTrigger`, `Starting Trigger: …`, `Slewing from`, etc.). Replaced the ineffective `center.after` pattern (it does not match `CenterAfter*` identifiers). Pattern order prefers **trigger/intent** lines before generic guider “Dither”.
- Widen the jump↔log-event match window to **300 seconds** so long subs can align with trigger lines after **`DATE-OBS`**.

## [0.2.0] — 2026-05-08

### Added

- **Import FITS folder…** on the dockable panel: offline replay of saved `.fits`/`.fit`/`.fts` files (folder scan is non-recursive). Frames sorted by **DATE-OBS** / **DATE** / **EXPSTART** when present, otherwise file time + path.
- Skip replay files whose **IMAGETYP** / **OBSTYPE** clearly indicates non-light when those keywords exist.

### Changed

- Refactor FITS parsing so primary headers can be reused for sorting and filtering.

## [0.1.0] — 2026-05-08

### Added

- Initial plugin: `IImageSaveMediator.ImageSaved` listener, FITS header coordinate extraction, drift samples vs frame index.
- Dockable **SeeDrift** panel with OxyPlot (ΔRA / ΔDec).
- **Export HTML** report (Chart.js via CDN).
- Options: Seestar-only filter, reset trace on FITS `OBJECT` change, HTML export folder.
- Cursor **GitShip** hook (auto-commit/push on agent stop) and shared GitShip documentation rules.
