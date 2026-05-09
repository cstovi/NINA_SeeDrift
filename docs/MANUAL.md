# SeeDrift — manual

## Overview

SeeDrift measures **on-sky drift** by **plate solving** each **LIGHT** frame in a batch. **Frames are chosen from your NINA session log**: lines like **Saved image to …** under `BaseImageData.SaveToDisk` give exact paths on disk (no recursive imaging-folder scan). The trace is **ΔRA / ΔDec in arcseconds** relative to the **first solved frame for that FITS target** (each `OBJECT` gets its own chart when a batch mixes targets).

It does **not** subscribe to live saves for plotting; work happens when **SeeDrift Stop** finishes (or when you run **Previous session report** from plugin options).

## Installation

See [README.md](../README.md). Copy **`NINA.Plugin.SeeDrift.dll`** (and dependencies from the same build output if the host asks).

## Options (Plugins → SeeDrift)

For **Night report folder**, **Discord webhook**, **Concurrency**, and **Minimum exposures**, hover anywhere on the row (label or field) to see the full hint in a tooltip.

| Setting | Meaning |
|--------|---------|
| **Night report folder** | Directory for the rolling HTML report (`SeeDrift_ranYYYYMMDD_sessYYYYMMDD.html`: **`ran`** = local day the file was written; **`sess`** = session day from **NINA log file name** `yyyyMMdd-…` when listed paths match that pattern, else earliest solved exposure, else log file times). The HTML **title** and **Session Log** heading date use the same **`sess`** rule (not the generation day). Created if missing. If you leave this blank, SeeDrift uses **`Documents\SeeDrift`** (your **Documents** folder, not Desktop). When a run finishes successfully, **NINA’s status bar** shows the **full path** to the file that was written. |
| **Discord webhook** | Optional **Discord Integrations → Webhooks** URL (`https://discord.com/api/webhooks/…` or legacy `discordapp.com`). After **SeeDrift Stop** saves night HTML, SeeDrift POSTs that file to the channel (multipart). **Previous session report** never uploads. The URL embeds a **secret** token — SeeDrift **never** writes it to the log. Empty = disabled. If the HTML exceeds Discord’s attachment limit (~25 MiB), SeeDrift sends a short text-only webhook message instead (full path is **not** echoed to Discord; check **`SeeDrift.log`** locally). Failures are log-only. |
| **Concurrency** | Choose concurrent plate solves from **1** up to **floor(80% × physical cores)** (minimum **1**). Physical cores come from **`GetLogicalProcessorInformation`** (`RelationProcessorCore` entries); if that API fails, SeeDrift uses **`Environment.ProcessorCount`** (logical processors) for both the percentage cap and the default. On a **new** install with no saved value, the default matches physical cores **clamped** to that ceiling (e.g. 12 cores → max **9**, default **9**). Each concurrent slot gets its **own** solver instance; higher values use more RAM and CPU. Use **1** to mimic older sequential behavior. Image **downsampling** for solves comes only from **NINA Options → Plate Solve** (same profile as the Plate Solve tool)—SeeDrift does not add a separate downsample step. |
| **Minimum exposures per target** | Each **Stop** or **Previous session report** batch can mix several FITS **OBJECT** names. SeeDrift lists **only** targets that have at least this many **solved** frames in that batch (1–500, default **50**). Targets below the threshold are omitted from the batch **heading**, drift charts, and sequencer blocks for that run. If **no** target meets the threshold, the batch section explains that instead of drawing empty charts. |
| **Log file path** (under **Previous session Log**) | Used only by **Run previous session report**—Browse or paste a saved `.log` (Browse starts in `%LocalAppData%\NINA\Logs`). Persisted in settings. |

SeeDrift also appends its own messages to **`%LocalAppData%\NINA\SeeDrift\SeeDrift.log`** (same folder as `settings.json`). The main NINA application log still receives the same lines.

**Tip:** Plate solving uses your **NINA plate-solve profile** (same engines/settings as the Plate Solve tool). To reduce solve time, tune that profile (solver choice, timeouts, downsampling); then adjust **Concurrency** if your PC can sustain multiple solves.

## Workflow

### Automated window (sequencer)

**SeeDrift Start** and **SeeDrift Stop** use a small **SeeDrift** vector icon (axes + drift trace) in the sequencer instruction list and on each step block; the geometry is assigned in code so it always appears.

1. Insert **SeeDrift Start** so the plugin records **arm** time (UTC internally).
2. Capture lights; NINA writes **Saved image to …** lines into the active session log under **`%LocalAppData%\NINA\Logs`**.
3. Insert **SeeDrift Stop** — the plugin records **disarm** time, reads **all** `*.log` files in that folder, keeps lines whose timestamp falls in **[arm, disarm]** inclusive, collects FITS paths from **Saved image to …**, skips calibration-looking frames when FITS keywords say so, plate-solves in log order, correlates **CenterAfterDrift** / **DitherAfterExposures** from the same logs, and updates the **night HTML**.

### Previous session (pick a log file)

1. Under **Previous session Log**, Browse or paste a full path to a `.log` file.
2. Click **Run previous session report**. SeeDrift collects **every** saved-light path from that file (full file, no time picker), then solves and correlates like Stop.

**While the run is in progress**, a **status panel** appears directly under **Run previous session report** (reading log → checking FITS headers → solving each frame). When the run ends, the **last status line stays visible** (for example **Complete — …** with elapsed time) until you start another run or leave the panel cleared by starting again. **Open** shows the saved HTML **file name** as an underlined control—click it to open the report in your default browser (hover for the full path). If nothing is added to the HTML, a dialog explains common causes. **SeeDrift Stop** uses **NINA’s main status bar** for progress and completion; that bar is plain text (not clickable). After Stop, open **Plugins → SeeDrift** to use **Open** for the same night HTML file when the run succeeded.

### Rolling HTML file

Each **SeeDrift Stop** or **Previous session report** completion is one **run**: it appends a new section to the rolling night report under **Night report folder**. (**Previous session report** reads exactly **one** `.log` file that you choose. **Stop** reads **every** `*.log` currently under `%LocalAppData%\NINA\Logs` and filters lines by your arm/disarm window—not a single file.) The page header shows the **same featured screenshot** bundled inside the plugin DLL (**embedded PNG**, inlined into the HTML as a **data URI** so it works **offline** with no external image fetch). If that embedded asset were ever missing from a custom build, SeeDrift falls back to **FeaturedImageURL** from assembly metadata, then the **`SeeDrift_Icon`** vector (`Resources.xaml`). The **Session Log** line uses the **session date** from NINA log file naming when possible (same basis as **`sess`** in the saved file name), not necessarily the day you generated the report. Header layout also includes the **generated** time, log path(s), and how to **reset chart zoom**. For **Stop**, one run may reference **many** log files; for **Previous session report**, one run references **one** file. When the HTML lists several paths, that is usually the **union** across multiple runs on the same calendar-night page. Under each **run** heading (targets summary), the next line is **solved frame count** and **wall time** for that run (log read through saving HTML). For each **Target** chart, **Start** and **End** (or a single **Exposure** line) show the first and last **exposure start** time among solved frames for that target, in **local** wall time (from FITS / the same times used to order frames). When a run spans **several** FITS **OBJECT** names, the **heading lists only targets that meet Minimum exposures per target**, in the same first-seen frame order as the **Target:** subsections below. The HTML shows **one drift chart and one Sequencer events block per listed target**; drift is zeroed at each target’s first solved frame, and sequencer rows only list intervals between **two consecutive frames of that target**. File names in rows remain the actual saved paths.

The report page loads **Tailwind CSS**, **Chart.js**, **Hammer.js**, and **chartjs-plugin-zoom** from CDNs (needs network the first time you open the file). Each drift chart uses the **same arcsecond span** on ΔRA and ΔDec (the wider axis range sets the span for both, centered on the data) and a **square** plot so **1″ horizontally equals 1″ vertically**. The drift chart **legend** lists the target trace only (not dither/center markers). Under each chart, **total ΔRA and ΔDec movement** along the trace is **Σ|Δstep|** on each sky axis between **consecutive** plotted frames (not net displacement from first to last frame; not “jumps only”). Values match the chart’s plate-solved offsets (arcseconds). **Pixel** line: if the run used **detector** cumulative registration, **Σ|Δx|** and **Σ|Δy|** are sums of absolute pixel steps; otherwise **≈** pixel totals divide those arcsecond sums by a **median** **″/px** from each frame’s FITS **XPIXSZ**+**FOCALLEN** (rough axis-aligned equivalent, not a full projection). The drift chart supports **wheel zoom** (including zooming **out** past the tight data bounds for extra context), **drag pan**, **double‑click** on the chart to reset zoom, and keyboard **R** or **Esc** to reset zoom on **all** drift charts on the page (ignored while typing in an input). The **ΔDec** axis is **reversed** vertically so motion on screen aligns with typical camera/imaging “up/down”; hover still shows the same numeric ΔRA/ΔDec as before. Points along the path show **start** (green) and **end** (orange); a single-frame target uses one **amber** marker. **Hover** on the path shows the **file name** and arcsecond offsets. When logs correlate **dither** or **center-after-drift** between two frames, **purple triangles** and **pink squares** appear **on the line segment between those frames** (midpoint for a single event; evenly spaced if several). Hover those markers for log detail. The **Sequencer events** table wraps long **From/To** paths so the **Detail** column keeps space.

### Sequencer events in HTML

When log lines match **between-frame** intervals, **dither** and **center-after-drift** triggers appear in **Sequencer events (NINA logs)** — same strict rules as before (paired save times when present, fractional seconds, etc.). Timestamps without `Z` are treated as **local wall time**. If nothing correlates, you still see an explanatory empty state.

## Technical notes

- **LIGHT filter:** After resolving paths from the log, SeeDrift reads each FITS/XISF primary header and skips frames whose **IMAGETYP** / **OBSTYPE** indicate calibration (bias/dark/flat) when those keywords are present — same as earlier builds. File extensions **.fits**, **.fit**, **.fts**, and **.xisf** are accepted from the log.
- **Minimum exposures gate:** After LIGHT frames are identified from FITS headers, SeeDrift checks whether **any single OBJECT** has at least **Minimum exposures per target** LIGHT frames in that run. If **none** do, it **does not plate-solve** (nothing useful would chart). If solves complete but **no target** has enough **successful** solves for that threshold, SeeDrift **still skips correlating NINA sequencer logs** into those frames (saves time on large log sets). The night HTML and **Complete —** status explain both cases.
- **Stop vs previous session report:** Stop restricts **Saved image to …** lines to **[arm, disarm]** by log timestamp. Previous session report uses **the entire chosen log file**.
- **Logs folder:** Stop scans **`%LocalAppData%\NINA\Logs\*.log`** only — fast compared to walking the image tree. In practice you often get **one log file per imaging session**, but NINA may **start a new file** when it rotates logs (for example at a **month** boundary), so one session can span **two** files. Reading **every** `.log` in that folder keeps **Stop** from missing **Saved image to …** lines that fell into the “tail” of one file and the “head” of the next.
- **Solve failures:** Frames that do not solve are skipped for the trace (implementation logs errors).
- **Jump detection:** Runs on the solved-sample sequence where applicable.

## Troubleshooting

| Symptom | Likely cause |
|--------|----------------|
| Empty or tiny trace | Fewer than two **Saved image to …** lines in range (Stop) or in the file (previous session report); paths missing on disk; plate solve failing — check logs and solver profile. |
| “Done” but no HTML where I looked | Default folder is **`Documents\SeeDrift`**, not Desktop, unless you set **Night report folder**. Copy the path from **NINA’s status bar** after a successful run, or check **`%LocalAppData%\NINA\SeeDrift\SeeDrift.log`** if the status line reports a save error. |
| Paths not found | Log contains paths from another PC or moved folders — files must exist where the log says. |
| No sequencer table | Triggers not in strict between-frame intervals for this ordering — expected sometimes. |
| Plugin DLL not updating | NINA has the DLL locked — close NINA and rebuild/copy. |
