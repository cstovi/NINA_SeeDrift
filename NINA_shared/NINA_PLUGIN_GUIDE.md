# NINA plugin guide (See\* shared notes)

Cross-plugin conventions learned while shipping **SeeDrift** and related **See\*** plugins. Pair with [NINA_IMAGE_PATH_ENUMERATION.md](./NINA_IMAGE_PATH_ENUMERATION.md) for disk layout under **`IImageFileSettings`**.

## Session-scoped exports (HTML, CSV, filenames)

### Do not use “today” for session labeling

If an export describes an **imaging session** or **log-derived batch**, avoid **`DateTime.Now`** (or local “generation day”) for the **primary title**, **visible session date**, or **filename segment** that users read as “when this run happened.”

Users often **re-run** processing later (**previous session report**, re-opened logs, stacked workflows). Showing the **generation** date implies the wrong night.

Prefer signals tied to the session:

| Signal | Typical use |
|--------|-------------|
| **NINA log file name** | Standard logs start with **`yyyyMMdd-HHmmss-…`** before the first `-`. The eight-digit prefix is a reliable **local session day** for that file. |
| **FITS / exposure times** | Earliest **`DATE-OBS`** (or equivalent) after solving or header read — local calendar day for “first light in this batch.” |
| **Log line timestamps** | Fallback when filenames are non-standard or paths are missing. |

Pick one ordering (e.g. **log filename date first**, then earliest exposure, then file mtimes) and document it in the plugin manual.

### One helper for filename + UI

If both **saved file names** and **HTML headings** expose a “session” date, compute it **once** from the same inputs (e.g. shared static helper fed **`CompletedTarget`**-like batches with **`SourceLogPaths`** + samples). Duplicating logic causes **headers** and **`sessYYYYMMDD`** stamps to drift apart.

### Rolling append vs overwrite

When the same **`ran`** + **`sess`** pair maps to one path, multiple writes **overwrite**. When **`sess`** differs (different log prefix or exposure night), you get **additional files**. Say so in docs so users are not surprised by folder growth or overwrites.

## NINA log paths users see

Surface **full paths** to NINA logs used for correlation (header list, status tooltips). That makes it obvious which **`yyyyMMdd`** prefix drives session labeling when debugging “wrong date” reports.

## Documentation parity

When changing export naming or header copy: update **`README.md`**, **`CHANGELOG.md`**, **`docs/MANUAL.md`**, and bump **assembly version** when shipping — same rule as [gitship.mdc](./gitship.mdc).
