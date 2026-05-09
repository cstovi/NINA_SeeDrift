# SeeDrift — manual

## Overview

SeeDrift measures **on-sky drift** by **plate solving** each **LIGHT** frame in a batch. **Frames are chosen from your NINA session log**: lines like **Saved image to …** under `BaseImageData.SaveToDisk` give exact paths on disk (no recursive imaging-folder scan). The trace is **ΔRA / ΔDec in arcseconds** relative to the **first solved frame for that FITS target** (each `OBJECT` gets its own chart when a batch mixes targets).

It does **not** subscribe to live saves for plotting; work happens when **SeeDrift Stop** finishes (or when you run **Test report** from plugin options).

## Installation

See [README.md](../README.md). Copy **`NINA.Plugin.SeeDrift.dll`** (and dependencies from the same build output if the host asks).

## Options (Plugins → SeeDrift)

| Setting | Meaning |
|--------|---------|
| **NINA image folder** | Read-only display of `ActiveProfile.ImageFileSettings.FilePath`. Paths in the log must still resolve on disk (configure **NINA Options → Imaging → image file path** so saves land where you expect). |
| **Night report folder** | Directory for the rolling **night HTML** file (`SeeDrift_night_YYYYMMDD.html`). Created if missing. If you leave this blank, SeeDrift uses **`Documents\SeeDrift`** (your **Documents** folder, not Desktop). When a run finishes successfully, **NINA’s status bar** shows the **full path** to the file that was written. |
| **Working folder** | Reserved for possible future scratch use when solving; solving currently uses **`IImageDataFactory.CreateFromFile`** on originals. Default on first run: `%TEMP%\SeeDrift`. |
| **NINA log file** | Used only by **Run test report**: path to a saved `.log` file (Browse opens `%LocalAppData%\NINA\Logs`). Persisted in settings. |

SeeDrift also appends its own messages to **`%LocalAppData%\NINA\SeeDrift\SeeDrift.log`** (same folder as `settings.json`). The main NINA application log still receives the same lines.

**Tip:** Plate solving uses your **NINA plate-solve profile** (same engines/settings as the Plate Solve tool).

## Workflow

### Automated window (sequencer)

1. Insert **SeeDrift Start** so the plugin records **arm** time (UTC internally).
2. Capture lights; NINA writes **Saved image to …** lines into the active session log under **`%LocalAppData%\NINA\Logs`**.
3. Insert **SeeDrift Stop** — the plugin records **disarm** time, reads **all** `*.log` files in that folder, keeps lines whose timestamp falls in **[arm, disarm]** inclusive, collects FITS paths from **Saved image to …**, skips calibration-looking frames when FITS keywords say so, plate-solves in log order, correlates **CenterAfterDrift** / **DitherAfterExposures** from the same logs, and updates the **night HTML**.

### Historic test (pick a log file)

1. Under **Test report**, set **NINA log file** (Browse or paste a full path to a `.log` file).
2. Click **Run test report**. SeeDrift collects **every** saved-light path from that file (full file, no time picker), then solves and correlates like Stop.

**While the run is in progress**, a **progress panel** appears directly under **Run test report** (reading log → checking FITS headers → solving each frame). If nothing is added to the HTML, a dialog explains common causes. **SeeDrift Stop** uses **NINA’s main status bar** instead.

### Rolling HTML file

Each **SeeDrift Stop** or **Test report** completion adds or refreshes content in the night report under **Night report folder**. The subtitle counts **batches** (runs). When a batch spans **several** FITS **OBJECT** names, the HTML shows **one drift chart and one Sequencer events block per target**; drift is zeroed at each target’s first solved frame, and sequencer rows only list intervals between **two consecutive frames of that target**. File names in rows remain the actual saved paths.

The report page loads **Tailwind CSS**, **Chart.js**, **Hammer.js**, and **chartjs-plugin-zoom** from CDNs (needs network the first time you open the file). The drift chart supports **wheel zoom**, **drag pan**, and **double‑click to reset zoom**.

### Sequencer events in HTML

When log lines match **between-frame** intervals, **dither** and **center-after-drift** triggers appear in **Sequencer events (NINA logs)** — same strict rules as before (paired save times when present, fractional seconds, etc.). Timestamps without `Z` are treated as **local wall time**. If nothing correlates, you still see an explanatory empty state.

## Technical notes

- **LIGHT filter:** After resolving paths from the log, SeeDrift reads each FITS/XISF primary header and skips frames whose **IMAGETYP** / **OBSTYPE** indicate calibration (bias/dark/flat) when those keywords are present — same as earlier builds. File extensions **.fits**, **.fit**, **.fts**, and **.xisf** are accepted from the log.
- **Stop vs Test:** Stop restricts **Saved image to …** lines to **[arm, disarm]** by log timestamp. Test uses **the entire chosen log file**.
- **Logs folder:** Stop scans **`%LocalAppData%\NINA\Logs\*.log`** only — fast compared to walking the image tree.
- **Solve failures:** Frames that do not solve are skipped for the trace (implementation logs errors).
- **Jump detection:** Runs on the solved-sample sequence where applicable.

## Troubleshooting

| Symptom | Likely cause |
|--------|----------------|
| Empty or tiny trace | Fewer than two **Saved image to …** lines in range (Stop) or in the file (Test); paths missing on disk; plate solve failing — check logs and solver profile. |
| “Done” but no HTML where I looked | Default folder is **`Documents\SeeDrift`**, not Desktop, unless you set **Night report folder**. Copy the path from **NINA’s status bar** after a successful run, or check **`%LocalAppData%\NINA\SeeDrift\SeeDrift.log`** if the status line reports a save error. |
| Paths not found | Log contains paths from another PC or moved folders — files must exist where the log says. |
| No sequencer table | Triggers not in strict between-frame intervals for this ordering — expected sometimes. |
| Plugin DLL not updating | NINA has the DLL locked — close NINA and rebuild/copy. |
