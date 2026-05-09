# SeeDrift — manual

## Overview

SeeDrift listens for **saved LIGHT** images and plots pointing in frame order: **FITS RA / Dec** (degrees) by default, or **cumulative pixel shifts** from phase correlation when you enable that mode for **folder import**. After **Reset trace**, live capture still uses the first accepted frame as reference for internal offsets and HTML export options.

Use it to visualize tracking drift and to see whether **dither** produces visible steps in RA and/or Dec (sharp jumps vs slow drift).

## Installation

See [README.md](../README.md). Copy the plugin **and** **MathNet.Numerics.dll** into NINA’s plugin folder; other dependencies come from NINA.

## Options (Plugins → SeeDrift)

| Setting | Meaning |
|--------|---------|
| **Plot cumulative pixel shifts** | **Folder import only:** when enabled, measures frame-to-frame shifts on a central square crop (phase correlation) and plots **cumulative pixel X/Y** (detector coordinates). When off, folder import uses **FITS header** RA/Dec (fast). Live capture always uses header coordinates. |
| **Registration crop (px)** | Edge length of the central crop for phase correlation (64–4096, default 800). |
| **Default folder** | Starting folder for **Export HTML…** |

Session discipline (**one scope / one target run**, separate folders for replay) is up to you. Use **Reset trace** when you want a new reference frame or before a new session.

## Workflow

### Live

1. Open the **SeeDrift** dockable on the Imaging tab.
2. Click **Reset trace** before or during a run if you want a fresh reference frame.
3. Accumulate lights; the plot updates per saved frame.
4. Use **Export HTML…** to save an offline copy (needs network once for Chart.js CDN unless you host scripts locally).

### Offline replay

1. Optional: in **Plugins → SeeDrift**, enable **Plot cumulative pixel shifts** and set **Registration crop** if you want the detector-space path (slower).
2. Click **Import FITS folder…** and select a directory that contains your lights (not subfolders). The current trace is cleared and replaced by replayed frames — **every** readable light is plotted in order.
3. Files are sorted by observation time when present (**DATE-OBS** first, then **DATE** / **EXPSTART** / **OBSTIME**); otherwise by file creation time and path. **Header mode:** many subs can share the same header coordinates, so markers **stack**. **Pixel mode:** path reflects motion in **pixel space**; subs with identical shift still add vertices (line segments may have zero length).
4. Files with **IMAGETYP** / **OBSTYPE** set to something other than light-style imaging are skipped when those keywords are present.

## Technical notes

- **Header RA/Dec:** primary HDU FITS keywords (`CRVAL1/2`, `OBJCTRA`/`OBJCTDEC`, or `RA`/`DEC`). If these do not update each sub, the header plot looks flat or stacked.
- **Pixel registration:** uncompressed primary image data only; central crop; normalized phase correlation (integer pixel shifts). Not identical to external tools’ sub-pixel tuning but matches the intent of a cumulative **detector** trail.
- **RA wrap:** handled when computing deltas in arcseconds (small-angle approximation with cos(Dec)).
- **NINA log correlation:** On folder import, SeeDrift scans `%LocalAppData%\NINA\Logs` (±1 calendar day) and matches jump frames to log lines within **300 seconds** of **DATE-OBS** (exposure start). Labels favour sequencer **triggers** (e.g. `CenterAfterDriftTrigger`, `DitherAfterExposures`) when those lines appear; guider and slew lines are fallbacks. Log file timestamps are interpreted as **local** time and converted to UTC for comparison with FITS times.

## Troubleshooting

| Symptom | Likely cause |
|--------|----------------|
| Flat line at zero | Header coordinates identical every frame; verify with a FITS viewer or add plate-based positions in a future version. |
| No points | Not saving **LIGHT** frames; FITS path unreadable at save time; coordinates missing from primary header. |
| Plugin DLL not updating | NINA still has the DLL locked — close NINA and rebuild/copy. |
