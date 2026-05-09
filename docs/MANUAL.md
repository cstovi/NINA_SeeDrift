# SeeDrift — manual

## Overview

SeeDrift measures **on-sky drift** by **plate solving** each **LIGHT** frame in a **time-bounded** set of files under your **NINA default image directory** (bounds use FITS observation times converted to UTC internally). The trace is **ΔRA / ΔDec in arcseconds** relative to the **first solved frame** in that batch.

It does **not** subscribe to live saves for plotting; work happens when **SeeDrift Stop** finishes (or when you run **Test report** from plugin options).

## Installation

See [README.md](../README.md). Copy **`NINA.Plugin.SeeDrift.dll`** (and dependencies from the same build output if the host asks).

## Options (Plugins → SeeDrift)

| Setting | Meaning |
|--------|---------|
| **NINA image folder** | Read-only display of `ActiveProfile.ImageFileSettings.FilePath`. Lights must land here (configure **NINA Options → Imaging → image file path**). |
| **Night report folder** | Directory for the rolling **night HTML** file (`SeeDrift_night_YYYYMMDD.html`). Created if missing. |
| **Working folder** | Reserved for possible future scratch use when solving; solving currently uses **`IImageDataFactory.CreateFromFile`** on originals. Default on first run: `%TEMP%\SeeDrift`. |
| **Observation start / end (local time)** | **Date picker** plus **hour** and **minute** in **your PC’s timezone**; used only by **Run test report**. The same instants are stored in settings as **UTC ISO** strings for compatibility with older builds. |

**Tip:** Plate solving uses your **NINA plate-solve profile** (same engines/settings as the Plate Solve tool).

## Workflow

### Automated window (sequencer)

1. Insert **SeeDrift Start** so the plugin records an **arm time** (UTC internally).
2. Capture lights into the configured NINA image folder (subfolders allowed).
3. Insert **SeeDrift Stop** — the plugin records **disarm** time, scans files **along the folder layout implied by your NINA Imaging file pattern for LIGHT frames** (same settings as **Options → Imaging** — see Technical notes), filters by observation UTC ∈ **[arm, disarm]** inclusive, solves, correlates logs, and updates the **night HTML**.

### Offline-style test (same disk folder)

1. Set **Observation start** and **Observation end** using the calendar and time lists (**your PC’s local timezone**, same numbers as on your clock).
2. Click **Run test report**. **Only while the run is in progress**, status text and a **progress bar** appear on this options page (they are hidden when idle). Indeterminate while scanning; determinate while solving frames. **SeeDrift Stop** does not use this row — progress appears in **NINA’s main status bar**.

### Rolling HTML file

Each **SeeDrift Stop** or **Test report** completion adds or refreshes content in the night report under **Night report folder**. The subtitle counts **batches** (runs), not astronomical targets. A single batch can include frames from **several** FITS **OBJECT** names if they all fall in your observation window—the heading lists the distinct target names; **Sequencer events** rows always show the actual file names from disk.

The report page loads **Tailwind CSS**, **Chart.js**, **Hammer.js**, and **chartjs-plugin-zoom** from CDNs (needs network the first time you open the file, same as before). The drift chart supports **wheel zoom**, **drag pan**, and **double‑click to reset zoom**.

### Sequencer events in HTML

When **`%LocalAppData%\NINA\Logs`** contains matching session lines, **between-frame** **dither** and **center-after-drift** triggers appear in a table under **Sequencer events (NINA logs)** — same strict interval rules as earlier SeeDrift builds (paired save times when present, fractional seconds, etc.). Log timestamps without a `Z` suffix are interpreted as **local wall time** (aligned with FITS times after conversion). If nothing correlates, you still see that section with a short explanation (empty table is not a failed drift run).

## Technical notes

- **Folder scan:** The batch only visits paths that match your active profile’s **LIGHT** file pattern (NINA’s **File pattern** / `GetFilePattern("LIGHT")`: the string with backslashes in **Options → Imaging**). The last segment of that pattern is the file name; each earlier segment is a directory level. `$$IMAGETYPE$$` in a directory segment is treated as **LIGHT** (NINA’s folder name for light frames), so other type folders (for example `FLAT`) and unrelated siblings are not followed when the pattern puts image type in its own folder. If the pattern has **no** directory segments, only the image root is searched. If a directory segment is a “free” token (for example `$$TARGETNAME$$` only), any single folder name can match—move third-party outputs outside that level or add a more specific NINA pattern if needed.
- **Sort order:** Numeric suffix after the last `_` in the file name when present (NINA-style `_0019`), then **DATE-OBS** / fallbacks — see `FitsFolderImport`.
- **Wrong observation window / empty window:** While scanning, SeeDrift applies a **file-year quick check** before opening each FITS: if both the file’s creation and last-write **years** lie entirely outside the calendar years spanned by your window, the header is not read. That makes a mistaken year (for example 2025 vs 2026 data) return almost immediately instead of reading every header on disk. NINA’s status line shows periodic **Scanning folder…** progress. Rare copies with FITS **DATE-OBS** inside the window but filesystem timestamps from another year could be skipped — widen the window if that applies.
- **Observation filter:** FITS time keywords converted to UTC; bounds are **inclusive**.
- **Solve failures:** Frames that do not solve are skipped for the trace (implementation logs errors).
- **Jump detection:** Still runs on the solved-sample sequence where applicable.

## Troubleshooting

| Symptom | Likely cause |
|--------|----------------|
| Empty or tiny trace | No LIGHT files in the folder tree in the observation window; observation times missing/wrong; plate solve failing — check NINA logs and solver profile. |
| Wrong folder | Active profile image path differs from where files were saved — verify Options → Imaging and the read-only path on the plugin page. |
| No sequencer table | Log files missing, wrong date window, or triggers not in strict between-frame intervals — expected when logs do not align. |
| Plugin DLL not updating | NINA has the DLL locked — close NINA and rebuild/copy. |
