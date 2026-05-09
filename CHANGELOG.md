# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

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
