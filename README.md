# SeeDrift — NINA plugin

**Plate-solves** **LIGHT** frames whose paths appear on **NINA “Saved image to …”** lines in your session **logs** (`%LocalAppData%\NINA\Logs`). **SeeDrift Start→Stop** keeps saves whose log timestamp falls between Start and Stop; **Previous session report** (plugin options) lets you pick a **saved `.log` file** from an earlier session and solves everything it references — no imaging-folder tree scan. Drift is **ΔRA / ΔDec in arcseconds** vs the **first solved frame per FITS target** (one chart per `OBJECT` when a batch mixes targets). Output is **HTML** (**Tailwind**, **Chart.js** with zoom/pan) with detected Seestar model/serial when NINA logged the connected Alpaca telescope, **dither** / **center-after-drift** markers, possible missing/unsolved-frame markers, advisory effectiveness metrics, drift-rate summaries, star-shape and walking-noise drift risk, session-quality timeline, and settings hints when logs correlate between consecutive frames of the same target. Suspect tracking jumps are shown but excluded from dither effectiveness scoring.

There is **no live dockable chart** and **no pixel / header-only drift path** in this version.

## Requirements

- **N.I.N.A.** 3.2+ (targets `NINA.Plugin` **3.2.0.9001**, **.NET 8**)
- Windows (same as NINA)
- A working **plate solve** profile in NINA (same stack as **Plate Solve**)

## Install

1. Build `NINA.Plugin.SeeDrift.csproj`: `dotnet build -c Release`.
2. Copy **`NINA.Plugin.SeeDrift.dll`** (includes embedded **`Assets/SeeDrift_featured.png`** for the offline night-report header image; rebuild after replacing that file if you change artwork) from `bin\Release\net8.0-windows\` to:

   `%LOCALAPPDATA%\NINA\Plugins\3.0.0\SeeDrift\`

   If NINA reports another missing dependency, copy it from the **same** output folder as well (for example **Newtonsoft.Json**.dll, **FreeImage** / imaging-related DLLs pulled in transitively). This project does **not** use Math.NET.

3. Restart NINA.

The csproj may post-build copy to that folder when NINA is not locking the DLL. Plain `dotnet build` defaults to **Debug**; use **`dotnet build -c Release`** for shipping.

## Usage

### Configure imaging path

Set **Options → Imaging → image file path** in NINA so saved lights land where you expect. SeeDrift resolves the paths recorded in the session log.

### Sequencer (recommended)

1. Add **SeeDrift Start** before capture and **SeeDrift Stop** when finished.
2. **Stop** reads NINA log files, collects **Saved image to …** paths between Start and Stop, plate-solves each **LIGHT** (header filter), builds drift samples, and **appends** to the rolling **night HTML** (one drift chart and sequencer block per target when the batch spans multiple `OBJECT` names). Reports are stored in **`%LocalAppData%\NINA\SeeDrift\Reports`**. If the contributing log says NINA discovered a Seestar Alpaca telescope, report filenames include the compact identity, for example `S30_0ac17a9b`. **NINA’s status bar** shows the **full path** on success (plain text), and **Plugins → SeeDrift** shows **Open** with the HTML **file name** as a click target after a successful run. If you set **Discord webhook**, Stop also uploads that HTML to your channel (**Previous session report** never uploads).

### Previous session Log (options panel)

Under **Plugins → SeeDrift**, choose one of the recent NINA logs from the last 14 days, or **Browse** to any **NINA `.log`** file (or paste its path), then **Run previous session report**. The recent list summarizes each log by detected Seestar identity, target count, usable image count, and duration; the entire selected log file is used for the report. **While the run is active**, a progress panel under the button shows each phase (log read, FITS checks, plate solving). When the run finishes successfully, **Open** appears as an underlined **file name** you can click to launch the night HTML.

Successful runs show **processing time** (log read through plate solves and HTML save) in the **night HTML** batch line and in the **completion** line (**NINA status bar** after **Stop**; **Previous session report status** in the plugin panel).

**Concurrency** is a **dropdown** from **1** up to **80% of physical cores** (rounded down, min **1**); on a fresh install it defaults to **physical core count** clamped to that maximum. Physical cores come from **`GetLogicalProcessorInformation`**; if that fails, SeeDrift uses **`Environment.ProcessorCount`** (logical processors) for the cap and default. **Minimum exposures per target** hides targets with fewer solved frames in each batch’s night HTML section (default **50**). Solver throughput still depends primarily on your **NINA Plate Solve** profile (including any downsampling you set there).

Under **Compare saved reports**, pick two SeeDrift HTML reports made by a schema-compatible analytics build and click **Compare saved reports**. The saved-report dropdown reads **`%LocalAppData%\NINA\SeeDrift\Reports`** and displays the imaging session date plus detected Seestar identity from embedded report metadata; Browse remains available for HTML saved elsewhere. SeeDrift reads the embedded report data from the HTML files and writes a whole-report average comparison of dither RA/Dec behavior and center-after-drift recovery without matching target names, scanning FITS files, or running plate solves again. Report HTML includes generator version/schema metadata, and new report filenames include the plugin version. If before/after reports came from different Seestars, the comparison report shows an advisory to read scale-sensitive metrics cautiously.

SeeDrift also writes **`%LocalAppData%\NINA\SeeDrift\SeeDrift.log`** (plugin messages, in addition to NINA’s own log).

See **[docs/MANUAL.md](docs/MANUAL.md)** for options, HTML location, and troubleshooting.

## Changelog

See **[CHANGELOG.md](CHANGELOG.md)**.

## Repository

<https://github.com/cstovi/NINA_SeeDrift>
