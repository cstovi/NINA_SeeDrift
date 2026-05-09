# SeeDrift — manual

## Overview

SeeDrift measures **on-sky drift** by **plate solving** each **LIGHT** frame in a **UTC-bounded** set of files under your **NINA default image directory**. The trace is **ΔRA / ΔDec in arcseconds** relative to the **first solved frame** in that batch.

It does **not** subscribe to live saves for plotting; work happens when **SeeDrift Stop** finishes (or when you run **Test report** from plugin options).

## Installation

See [README.md](../README.md). Copy **`NINA.Plugin.SeeDrift.dll`** (and dependencies from the same build output if the host asks).

## Options (Plugins → SeeDrift)

| Setting | Meaning |
|--------|---------|
| **NINA image folder** | Read-only display of `ActiveProfile.ImageFileSettings.FilePath`. Lights must land here (configure **NINA Options → Imaging → image file path**). |
| **Night report folder** | Directory for the rolling **night HTML** file (`SeeDrift-night-YYYY-MM-DD.html`). Created if missing. |
| **Working folder** | Reserved for possible future scratch use when solving; solving currently uses **`IImageDataFactory.CreateFromFile`** on originals. Default on first run: `%TEMP%\SeeDrift`. |
| **Observation start / end (UTC)** | ISO 8601 strings used only by **Run test report** (for example `2026-05-09T22:00:00Z`). |

**Tip:** Plate solving uses your **NINA plate-solve profile** (same engines/settings as the Plate Solve tool).

## Workflow

### Automated window (sequencer)

1. Insert **SeeDrift Start** so the plugin records **arm UTC**.
2. Capture lights into the configured NINA image folder (subfolders allowed).
3. Insert **SeeDrift Stop** — the plugin records **disarm UTC**, scans **recursively**, filters by observation UTC ∈ **[arm, disarm]** inclusive, solves, correlates logs, and updates the **night HTML**.

### Offline-style test (same disk folder)

1. Set **Observation start** and **Observation end** to bracket your subs (UTC).
2. Click **Run test report**.

### Rolling HTML file

Each completed batch adds or refreshes content in the night report under **Night report folder**. Open the HTML in a browser; Chart.js loads once from the CDN (needs network unless you host scripts locally).

### Sequencer events in HTML

When **`%LocalAppData%\NINA\Logs`** contains matching session lines, **between-frame** **dither** and **center-after-drift** triggers appear under **Sequencer events (NINA logs)** — same strict interval rules as earlier SeeDrift builds (paired save times when present, fractional seconds, etc.). If logs do not match the interval, the chart still shows drift; the table may be empty.

## Technical notes

- **Sort order:** Numeric suffix after the last `_` in the file name when present (NINA-style `_0019`), then **DATE-OBS** / fallbacks — see `FitsFolderImport`.
- **Observation filter:** FITS time keywords converted to UTC; bounds are **inclusive**.
- **Solve failures:** Frames that do not solve are skipped for the trace (implementation logs errors).
- **Jump detection:** Still runs on the solved-sample sequence where applicable.

## Troubleshooting

| Symptom | Likely cause |
|--------|----------------|
| Empty or tiny trace | No LIGHT files in the folder tree in the UTC window; observation times missing/wrong; plate solve failing — check NINA logs and solver profile. |
| Wrong folder | Active profile image path differs from where files were saved — verify Options → Imaging and the read-only path on the plugin page. |
| No sequencer table | Log files missing, wrong date window, or triggers not in strict between-frame intervals — expected when logs do not align. |
| Plugin DLL not updating | NINA has the DLL locked — close NINA and rebuild/copy. |
