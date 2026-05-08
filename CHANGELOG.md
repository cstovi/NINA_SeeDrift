# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Fixed

- **Import FITS folder** no longer clears the replay when **Reset trace when OBJECT changes** is on but **OBJECT** strings differ slightly between files (replay now ignores that option so every frame in the folder is plotted).
- **Only record Seestar cameras** during folder replay no longer drops frames when **INSTRUME** is missing from the FITS primary header (offline import cannot know the camera model from NINA).
- Pointing plot **subtitle** shows how many frames were plotted so sparse traces are obvious at a glance.

### Changed

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
