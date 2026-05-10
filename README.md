# SeeDrift — NINA plugin

**Plate-solves** **LIGHT** frames whose paths appear on **NINA “Saved image to …”** lines in your session **logs** (`%LocalAppData%\NINA\Logs`). **SeeDrift Start→Stop** keeps saves whose log timestamp falls between Start and Stop; **Previous session report** (plugin options) lets you pick a **saved `.log` file** from an earlier session and solves everything it references — no imaging-folder tree scan. Drift is **ΔRA / ΔDec in arcseconds** vs the **first solved frame per FITS target** (one chart per `OBJECT` when a batch mixes targets). Output is **HTML** (**Tailwind**, **Chart.js** with zoom/pan) with **dither** / **center-after-drift** markers, advisory effectiveness metrics, drift-rate summaries, session-quality timeline, and settings hints when logs correlate between consecutive frames of the same target.

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
2. **Stop** reads NINA log files, collects **Saved image to …** paths between Start and Stop, plate-solves each **LIGHT** (header filter), builds drift samples, and **appends** to the rolling **night HTML** (one drift chart and sequencer block per target when the batch spans multiple `OBJECT` names). **NINA’s status bar** shows the **full path** on success (plain text). **Plugins → SeeDrift** also shows **Open** with the HTML **file name** as a click target after a successful run; if **Night report folder** is empty, the default is **`Documents\SeeDrift`** (not Desktop). If you set **Discord webhook**, Stop also uploads that HTML to your channel (**Previous session report** never uploads).

### Previous session Log (options panel)

Under **Plugins → SeeDrift**, **Browse** to a **NINA `.log`** file (or paste its path), then **Run previous session report**. The entire log file is used. **While the run is active**, a progress panel under the button shows each phase (log read, FITS checks, plate solving). When the run finishes successfully, **Open** appears as an underlined **file name** you can click to launch the night HTML.

Successful runs show **wall-clock time** (log read through plate solves and HTML save) in the **night HTML** batch line and in the **completion** line (**NINA status bar** after **Stop**; **Previous session report status** in the plugin panel).

**Concurrency** is a **dropdown** from **1** up to **80% of physical cores** (rounded down, min **1**); on a fresh install it defaults to **physical core count** clamped to that maximum. Physical cores come from **`GetLogicalProcessorInformation`**; if that fails, SeeDrift uses **`Environment.ProcessorCount`** (logical processors) for the cap and default. **Minimum exposures per target** hides targets with fewer solved frames in each batch’s night HTML section (default **50**). Solver throughput still depends primarily on your **NINA Plate Solve** profile (including any downsampling you set there).

Under **Compare saved reports**, pick two SeeDrift HTML reports made by this analytics build and click **Compare saved reports**. SeeDrift reads the embedded report data from the HTML files and writes a comparison HTML without scanning FITS files or running plate solves again.

SeeDrift also writes **`%LocalAppData%\NINA\SeeDrift\SeeDrift.log`** (plugin messages, in addition to NINA’s own log).

See **[docs/MANUAL.md](docs/MANUAL.md)** for options, HTML location, and troubleshooting.

## Changelog

See **[CHANGELOG.md](CHANGELOG.md)**.

## Repository

<https://github.com/cstovi/NINA_SeeDrift>
