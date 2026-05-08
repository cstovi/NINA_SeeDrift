# SeeDrift — manual

## Overview

SeeDrift listens for **saved LIGHT** images and plots **FITS RA / Dec** (degrees) in frame order so you can follow how pointing moved. After **Reset trace**, live capture still uses the first accepted frame as reference for internal offsets and HTML export options.

Use it to visualize tracking drift and to see whether **dither** produces visible steps in RA and/or Dec (sharp jumps vs slow drift).

## Installation

See [README.md](../README.md). Only the plugin **DLL** is copied into NINA’s plugin folder; dependencies come from NINA.

## Options (Plugins → SeeDrift)

| Setting | Meaning |
|--------|---------|
| **Default folder** | Starting folder for **Export HTML…** |

Session discipline (**one scope / one target run**, separate folders for replay) is up to you. Use **Reset trace** when you want a new reference frame or before a new session.

## Workflow

### Live

1. Open the **SeeDrift** dockable on the Imaging tab.
2. Click **Reset trace** before or during a run if you want a fresh reference frame.
3. Accumulate lights; the plot updates per saved frame.
4. Use **Export HTML…** to save an offline copy (needs network once for Chart.js CDN unless you host scripts locally).

### Offline replay

1. Click **Import FITS folder…** and select a directory that contains your lights (not subfolders). The current trace is cleared and replaced by replayed frames — **every** readable light is plotted in order.
2. Files are ordered by **DATE-OBS** / **DATE** / **EXPSTART** when those keywords exist; otherwise by file creation time and name.
3. Files with **IMAGETYP** / **OBSTYPE** set to something other than light-style imaging are skipped when those keywords are present.

## Technical notes

- **Position source:** primary HDU FITS keywords (`CRVAL1/2`, `OBJCTRA`/`OBJCTDEC`, or `RA`/`DEC`). If these do not update each sub, offsets stay near zero.
- **RA wrap:** handled when computing deltas in arcseconds (small-angle approximation with cos(Dec)).

## Troubleshooting

| Symptom | Likely cause |
|--------|----------------|
| Flat line at zero | Header coordinates identical every frame; verify with a FITS viewer or add plate-based positions in a future version. |
| No points | Not saving **LIGHT** frames; FITS path unreadable at save time; coordinates missing from primary header. |
| Plugin DLL not updating | NINA still has the DLL locked — close NINA and rebuild/copy. |
