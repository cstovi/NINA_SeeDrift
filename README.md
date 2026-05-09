# SeeDrift — NINA plugin

**Plate-solves** **LIGHT** frames whose paths appear on **NINA “Saved image to …”** lines in your session **logs** (`%LocalAppData%\NINA\Logs`). **SeeDrift Start→Stop** keeps saves whose log timestamp falls between Start and Stop; **Test report** lets you pick a **historic `.log` file** and solves everything it references — no imaging-folder tree scan. Drift is **ΔRA / ΔDec in arcseconds** vs the **first solved frame per FITS target** (one chart per `OBJECT` when a batch mixes targets). Output is **HTML** (**Tailwind**, **Chart.js** with zoom/pan) and optional **dither** / **center-after-drift** rows when logs correlate between consecutive frames of the same target.

There is **no live dockable chart** and **no pixel / header-only drift path** in this version.

## Requirements

- **N.I.N.A.** 3.2+ (targets `NINA.Plugin` **3.2.0.9001**, **.NET 8**)
- Windows (same as NINA)
- A working **plate solve** profile in NINA (same stack as **Plate Solve**)

## Install

1. Build `NINA.Plugin.SeeDrift.csproj`: `dotnet build -c Release`.
2. Copy **`NINA.Plugin.SeeDrift.dll`** from `bin\Release\net8.0-windows\` to:

   `%LOCALAPPDATA%\NINA\Plugins\3.0.0\SeeDrift\`

   If NINA reports a missing dependency, copy the other assemblies from the **same** output folder as well (for example **Newtonsoft.Json**.dll, **FreeImage** / imaging-related DLLs pulled in transitively). This project does **not** use Math.NET.

3. Restart NINA.

The csproj may post-build copy to that folder when NINA is not locking the DLL. Plain `dotnet build` defaults to **Debug**; use **`dotnet build -c Release`** for shipping.

## Usage

### Configure imaging path

Set **Options → Imaging → image file path** in NINA so saved lights land where you expect. SeeDrift resolves the paths recorded in the log; the read-only path on the plugin page is your active profile root.

### Sequencer (recommended)

1. Add **SeeDrift Start** before capture and **SeeDrift Stop** when finished.
2. **Stop** reads NINA log files, collects **Saved image to …** paths between Start and Stop, plate-solves each **LIGHT** (header filter), builds drift samples, and **appends** to the rolling **night HTML** (one drift chart and sequencer block per target when the batch spans multiple `OBJECT` names). **NINA’s status bar** shows the **full path** on success; if **Night report folder** is empty, the default is **`Documents\SeeDrift`** (not Desktop).

### Test report (options panel)

Under **Plugins → SeeDrift**, **Browse** to a **NINA `.log`** file (or paste its path), then **Run test report**. The entire log file is used. **While the run is active**, a progress panel under the button shows each phase (log read, FITS checks, plate solving).

### Session bookkeeping

**Clear completed targets (session)** resets in-memory completed targets for the rolling night file layout (see manual if you re-run the same night).

Under **Plugins → SeeDrift**, **Concurrency** controls how many frames are plate-solved in parallel (default 4). Solver throughput still depends primarily on your **NINA Plate Solve** profile (including any downsampling you set there).

SeeDrift also writes **`%LocalAppData%\NINA\SeeDrift\SeeDrift.log`** (plugin messages, in addition to NINA’s own log).

See **[docs/MANUAL.md](docs/MANUAL.md)** for options, HTML location, and troubleshooting.

## Changelog

See **[CHANGELOG.md](CHANGELOG.md)**.

## Repository

<https://github.com/cstovi/NINA_SeeDrift>
