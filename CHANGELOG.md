# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- **Folder import — pixel path:** optional **phase correlation** on central image crops (like a Python `phase_cross_correlation` stack). Plots **cumulative Δx/Δy in detector pixels** starting at (0,0). Slower; shows dither/drift when FITS **RA/Dec** headers do not. Uses **Math.NET** FFT. Uncompressed primary HDU only.
- **Dither log overlay:** extension point only (`DitherLogOverlay` placeholder) for a future iteration.

### Removed

- Plugin options **Only record Seestar cameras** and **Reset trace when FITS OBJECT name changes**. SeeDrift always records saved **LIGHT** frames with readable pointing keywords; the trace does not auto-clear when **OBJECT** changes — use **Reset trace** or separate folders/sessions as needed.

### Changed

- Pointing plot **subtitle** shows how many frames were plotted so sparse traces are obvious at a glance.
- Slightly smaller plot markers when many frames are loaded (>100) for readability.

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
