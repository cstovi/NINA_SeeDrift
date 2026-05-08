# SeeDrift — manual

## Overview

SeeDrift listens for **saved LIGHT** images and computes **ΔRA** and **ΔDec** in **arcseconds** relative to the **first accepted frame** after you click **Reset trace** (or after an automatic reset when the FITS **OBJECT** name changes, if that option is enabled).

Use it to visualize tracking drift and to see whether **dither** produces visible steps in RA and/or Dec (sharp jumps vs slow drift).

## Installation

See [README.md](../README.md). Only the plugin **DLL** is copied into NINA’s plugin folder; dependencies come from NINA.

## Options (Plugins → SeeDrift)

| Setting | Meaning |
|--------|---------|
| **Only record Seestar cameras** | Requires **Seestar** in the camera name (`ICameraMediator`) or in FITS **INSTRUME**. Turn off to record all cameras that write coordinates. |
| **Reset trace when FITS OBJECT name changes** | Clears the trace when the **OBJECT** keyword changes between frames (cheap target-change detection). |
| **Default folder** | Starting folder for **Export HTML…** |

## Workflow

1. Open the **SeeDrift** dockable on the Imaging tab.
2. Click **Reset trace** before or during a run if you want a fresh reference frame.
3. Accumulate lights; the plot updates per saved frame.
4. Use **Export HTML…** to save an offline copy (needs network once for Chart.js CDN unless you host scripts locally).

## Technical notes

- **Position source:** primary HDU FITS keywords (`CRVAL1/2`, `OBJCTRA`/`OBJCTDEC`, or `RA`/`DEC`). If these do not update each sub, offsets stay near zero.
- **RA wrap:** handled when computing deltas in arcseconds (small-angle approximation with cos(Dec)).

## Troubleshooting

| Symptom | Likely cause |
|--------|----------------|
| Flat line at zero | Header coordinates identical every frame; verify with a FITS viewer or add plate-based positions in a future version. |
| No points | Not saving **LIGHT** frames; **Seestar-only** filter excludes your camera; FITS path unreadable at save time. |
| Plugin DLL not updating | NINA still has the DLL locked — close NINA and rebuild/copy. |
