# SeeDrift — NINA plugin

Plots **RA/Dec drift** for **Seestar** (and optionally other cameras) from **saved LIGHT** frames: offsets in arcseconds relative to the first frame in the current trace. Includes a **live dockable** chart (OxyPlot) and **HTML export** (Chart.js).

## Requirements

- **N.I.N.A.** 3.2+ (targets `NINA.Plugin` **3.2.0.9001**, **.NET 8**)
- Windows (same as NINA)

## Install

1. Build `NINA.Plugin.SeeDrift.csproj` (`dotnet build -c Release`).
2. Copy **`NINA.Plugin.SeeDrift.dll`** only to:

   `%LOCALAPPDATA%\NINA\Plugins\3.0.0\SeeDrift\`

3. Restart NINA.

The csproj includes a post-build copy to that folder when NINA is not locking the DLL.

## Usage

1. Open **Imaging** → dock **SeeDrift** (Tools/Info panel depending on NINA layout).
2. Run your sequence; each saved **LIGHT** updates the trace (ΔRA and ΔDec vs frame index).
3. **Reset trace** clears the reference frame; **Export HTML…** saves the same data as a standalone HTML file.

Coordinates are read from each FITS primary header (`CRVAL` / `OBJCTRA`+`OBJCTDEC` / `RA`+`DEC`). If headers do not move frame-to-frame, the plot stays flat until metadata or optional plate solving is added.

See **[docs/MANUAL.md](docs/MANUAL.md)** for options and troubleshooting.

## Changelog

See **[CHANGELOG.md](CHANGELOG.md)**.

## Repository

<https://github.com/cstovi/NINA_SeeDrift>
