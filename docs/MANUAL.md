# SeeDrift — manual

## Overview

SeeDrift listens for **saved LIGHT** images and plots pointing in frame order: **FITS RA / Dec** from headers by default, or **cumulative pixel shifts** from central-crop **template registration** when **Plot cumulative pixel shifts** is enabled (used for **folder import** and for **live** frames while **armed** — the mode is fixed when the SeeDrift **Start** instruction runs). After **Reset trace**, the next accepted frame becomes the reference for offsets and HTML export.

Use it to visualize tracking drift and to see whether **dither** produces visible steps in RA and/or Dec (sharp jumps vs slow drift).

## Installation

See [README.md](../README.md). Copy **`NINA.Plugin.SeeDrift.dll`** into NINA’s plugin folder; if load fails, copy companion assemblies from the same build output (see README). **Math.NET is not used.**

## Options (Plugins → SeeDrift)

| Setting | Meaning |
|--------|---------|
| **Plot cumulative pixel shifts** | When enabled, measures frame-to-frame shifts on a central square crop (**SSD template** registration — see Technical notes) and plots **cumulative pixel X/Y** (detector coordinates). Applies to **folder import** and to **live** lights while **armed** (mode is captured at **Start**). When off, both use **FITS header** RA/Dec for the plotted trace (faster). |
| **Registration crop (px)** | Edge length of the central crop for registration (64–4096, default 800). |
| **Default folder** | Starting folder for **Export HTML…** |

Session discipline (**one scope / one target run**, separate folders for replay) is up to you. Use **Reset trace** when you want a new reference frame or before a new session.

## Workflow

### Live

1. Open the **SeeDrift** dockable on the Imaging tab.
2. Arm recording with the SeeDrift **Start** sequence instruction (and **Stop** when finished), or use your usual workflow so frames are recorded while armed.
3. Click **Reset trace** before or during a run if you want a fresh reference frame.
4. Accumulate lights; the plot updates per saved frame. Jump detection and **strict** NINA log correlation for **between-frame** markers run on the same trace as for folder import. **Hover tooltips** disappear when you move the pointer off the marker (you do not need to click away or leave the chart).
5. Use **Export HTML…** to save an offline copy (needs network once for Chart.js CDN unless you host scripts locally).

### Offline replay

1. Optional: in **Plugins → SeeDrift**, enable **Plot cumulative pixel shifts** and set **Registration crop** if you want the detector-space path (slower).
2. Click **Import FITS folder…** and select a directory that contains your lights (not subfolders). The current trace is cleared and replaced by replayed frames — **every** readable light is plotted in order.
3. Files are sorted by observation time when present (**DATE-OBS** first, then **DATE** / **EXPSTART** / **OBSTIME**); otherwise by file creation time and path. **Header mode:** many subs can share the same header coordinates, so markers **stack**. **Pixel mode:** path reflects motion in **pixel space**; subs with identical shift still add vertices (line segments may have zero length).
4. Files with **IMAGETYP** / **OBSTYPE** set to something other than light-style imaging are skipped when those keywords are present.

## Technical notes

- **Header RA/Dec:** primary HDU FITS keywords (`CRVAL1/2`, `OBJCTRA`/`OBJCTDEC`, or `RA`/`DEC`). If these do not update each sub, the header plot looks flat or stacked.
- **Pixel registration:** uncompressed primary image data only; central crop; **sum-of-squared-differences** template search (coarse-to-fine on downsampled data, then fine search and **parabolic sub-pixel** refinement). **No FFT and no Math.NET** — not frequency-domain phase correlation despite similar goals. Produces a cumulative **detector** trail comparable in intent to cross-correlation stacks in other tools.
- **RA wrap:** handled when computing deltas in arcseconds (small-angle approximation with cos(Dec)).
- **NINA log correlation:** When **armed** (live capture) and on **folder import**, SeeDrift scans `%LocalAppData%\NINA\Logs` (±1 calendar day from the first frame’s date). It reads **all** `.log` files whose names match that window (not only one file per day), parses **`Starting Trigger:`** lines (center-after-drift, dither-after-exposures) and **`DirectGuider` / `SelectDitherPulse`** dither commands.
  - **Where “Center after drift” / “Dither (after exposures)” appear:** Only on **between-frame midpoint** tooltips — when that `Starting Trigger:` timestamp falls **strictly between** the interval bounds for that frame pair. Bounds are normally **consecutive exposure starts**; when the logs contain **`BaseImageData|SaveToDisk|…|Saved image to …`** for **both** frames, SeeDrift matches the path’s **file name** to each sample’s `FileName` and uses those **log save times** as `t0`/`t1` instead (only if the second save is after the first), so triggers stay aligned even when FITS **DATE-OBS** and NINA log wall time sit in different bases (e.g. UT vs UK local). **Blue frame dots** and **jump** diamonds do **not** attach loose “nearest log line” names; **JumpReason** is from step detection only.
  - **Between-frame intervals:** **Orange triangle** = a dither trigger in that interval (if both dither and center fired between the same two subs, the triangle is shown). **Cyan square** = center trigger in the interval **without** a dither trigger in that same gap. Hover for **NINA intent from logs** when present: **dither** — full `SelectDitherPulse` **from → to** guider coordinates, move Δx/Δy, and optional **guide durations** (seconds); **center-after-drift** — `PlatesolvingImageFollower_PropertyChanged` **Drift:** value vs **arc minutes** threshold. Then **measured** frame-to-frame steps: **header** ΔRA/ΔDec, **derived** ΔRA/ΔDec (pixel registration + plate scale, when available), **cumulative-pixel** Δx/Δy. Guider coordinates are **not** arcseconds on sky.
  - **Jumps:** The subtitle **“with next-frame dither/center interval in log”** counts jumps where the **following** frame boundary has one of those strict intervals — useful overlap, not a causal label on the jump diamond itself.
  - **Timezone:** NINA log timestamps are usually interpreted as **local** (then converted to UTC). FITS **DATE-OBS** may be UT. **SaveToDisk** pairing (above) avoids relying on exposure-start alignment for the between-frame window when both saves appear in the session logs.

## Troubleshooting

| Symptom | Likely cause |
|--------|----------------|
| Flat line at zero | Header coordinates identical every frame; verify with a FITS viewer or add plate-based positions in a future version. |
| No points | Not saving **LIGHT** frames; FITS path unreadable at save time; coordinates missing from primary header. |
| Plugin DLL not updating | NINA still has the DLL locked — close NINA and rebuild/copy. |
