# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- **`NINA_shared/`** documentation: [NINA_IMAGE_PATH_ENUMERATION.md](NINA_shared/NINA_IMAGE_PATH_ENUMERATION.md) and [gitship.mdc](NINA_shared/gitship.mdc) describe how See\* plugins should enumerate files under NINA’s image directory using **`GetFilePattern(imageType)`** and **`$$IMAGETYPE$$`** (LIGHT vs DARK vs FLAT vs BIAS) instead of blind recursion.

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
