# SeeDrift — NINA plugin

Plots **RA/Dec drift** from **saved LIGHT** frames (any camera): offsets in arcseconds relative to the first frame in the current trace. Optional **pixel registration** (central-crop template matching) can drive the trace for **folder import** and for **live** capture while **armed**, depending on the plugin option. Includes a **live dockable** chart (OxyPlot), **offline FITS folder replay**, and **HTML export** (Chart.js).

## Requirements

- **N.I.N.A.** 3.2+ (targets `NINA.Plugin` **3.2.0.9001**, **.NET 8**)
- Windows (same as NINA)

## Install

1. Build `NINA.Plugin.SeeDrift.csproj` (`dotnet build -c Release`).
2. Copy **`NINA.Plugin.SeeDrift.dll`** from `bin\Release\net8.0-windows\` (or your build output folder) to:

   `%LOCALAPPDATA%\NINA\Plugins\3.0.0\SeeDrift\`

   If NINA reports a missing dependency, copy the other assemblies from the **same** output folder as well (e.g. **OxyPlot**.dll / **OxyPlot.Wpf**.dll, **Newtonsoft.Json**.dll). The project does **not** use Math.NET.

3. Restart NINA.

The csproj includes a post-build copy to that folder when NINA is not locking the DLL. That copy uses **whichever configuration you built**: plain `dotnet build` defaults to **Debug** (`bin\Debug\net8.0-windows\`); use **`dotnet build -c Release`** for shipping (output under `bin\Release\...`).

## Usage

### Live imaging

1. Open **Imaging** → dock **SeeDrift** (Tools/Info panel depending on NINA layout).
2. Run your sequence; each saved **LIGHT** updates the trace (ΔRA and ΔDec vs frame index).
3. **Reset trace** clears the reference frame; **Export HTML…** saves the same data as a standalone HTML file.

### Offline testing (no camera)

1. Open **SeeDrift**.
2. Click **Import FITS folder…** and choose a folder with existing lights (`.fits` / `.fit` / `.fts`, non-recursive). The trace resets and replays files sorted by filename exposure number (`_0019`, …) when present, then by **DATE-OBS** (etc.).
3. Use **Export HTML…** if you want a report file.

Coordinates are read from each FITS primary header (`CRVAL` / `OBJCTRA`+`OBJCTDEC` / `RA`+`DEC`). If headers do not move frame-to-frame, the plot stays flat until metadata or optional plate solving is added.

With **NINA logs** available, **dither** / **center-after-drift** labels and guider detail appear **only** on **between-exposure** midpoint markers (orange/cyan), not on every frame or jump — see the manual.

See **[docs/MANUAL.md](docs/MANUAL.md)** for options and troubleshooting.

## Changelog

See **[CHANGELOG.md](CHANGELOG.md)**.

## Repository

<https://github.com/cstovi/NINA_SeeDrift>
