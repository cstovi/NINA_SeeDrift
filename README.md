# SeeDrift — NINA plugin

**Plate-solves** saved **LIGHT** frames under your **NINA image file directory**, walking only the **folder layout implied by your Imaging file pattern for LIGHT frames** (active profile), filtered by an **observation time window** (FITS times vs UTC bounds internally). Drift is reported as **ΔRA / ΔDec in arcseconds** versus the first solved frame of that window. Output is **HTML** with a **Tailwind**-styled page, **Chart.js** drift plot (**pan**, **wheel zoom**, double‑click reset), and when session logs match the run, **dither** / **center-after-drift** rows in a **table** (same correlation logic — strict between-frame intervals). If logs do not correlate, the report still explains why the sequencer section may be empty.

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

Set **Options → Imaging → image file path** in NINA to the folder where lights are saved. SeeDrift reads this path from the **active profile** (shown read-only under **Plugins → SeeDrift**).

### Sequencer (recommended)

1. Add **SeeDrift Start** before capture and **SeeDrift Stop** when finished (same target / session as needed).
2. **Stop** runs the batch: finds LIGHT `.fits` / `.fit` / `.fts` **only under paths that match your NINA LIGHT file pattern** (see manual), keeps files whose observation time is **inside** `[Start, Stop]` (inclusive; FITS keywords converted to UTC), sorts them (filename exposure index, then header times), plate-solves each frame, builds drift samples, and **appends** a section to the rolling **night HTML** in your configured export folder.

### Test report (options panel)

Under **Plugins → SeeDrift**, choose **observation start/end** with the **date picker** and **hour/minute** dropdowns (**your PC’s local time**). Click **Run test report**. Uses the same pipeline as Stop but with your persisted window instead of Arm timestamps. **While the run is active**, status text and a **progress bar** appear on this page (hidden when idle). If the year in your window does not match the files on disk (for example 2025 vs 2026), SeeDrift skips opening those FITS headers and finishes quickly.

### Session bookkeeping

**Clear completed targets (session)** resets in-memory completed targets for the rolling night file layout (see manual if you re-run the same night).

See **[docs/MANUAL.md](docs/MANUAL.md)** for options, HTML location, and troubleshooting.

## Changelog

See **[CHANGELOG.md](CHANGELOG.md)**.

## Repository

<https://github.com/cstovi/NINA_SeeDrift>
