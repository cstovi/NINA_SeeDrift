# Enumerating files under NINA’s image path (See\* plugins)

NINA does **not** document a fixed folder tree under **Options → Imaging → Image file path**. It saves files according to the **File pattern** string (macros like `$$DATEMINUS12$$`, `$$IMAGETYPE$$`, `$$TARGETNAME$$`, …). Plugins that scan disk should **follow that pattern for the same frame type** they process (LIGHT vs DARK vs FLAT vs BIAS), instead of using a hard-coded skip list or `SearchOption.AllDirectories` over the entire tree.

This matches how NINA builds paths internally (`ImagePatterns` / `ImagePatternKeys` in **NINA.Core**, `ImageFileSettings` in **NINA.Profile**).

## 1. Pick the pattern string for the frame type you want

Use **`IImageFileSettings`** from the active profile (`ActiveProfile.ImageFileSettings`).

On the concrete type **`NINA.Profile.ImageFileSettings`**, **`GetFilePattern(string imageType)`** selects which pattern field applies:

| `imageType` (typical) | Pattern used (same logic as NINA) |
|----------------------|-----------------------------------|
| **`LIGHT`** | `FilePattern` (main imaging pattern) |
| **`DARK`** | `FilePatternDARK` if non-empty; otherwise **`FilePattern`** |
| **`FLAT`** | `FilePatternFLAT` if non-empty; otherwise **`FilePattern`** |
| **`BIAS`** | `FilePatternBIAS` if non-empty; otherwise **`FilePattern`** |

So: if your plugin works on **darks**, derive directory layout from **`GetFilePattern("DARK")`**, not from the LIGHT-only assumption.

Cast to **`ImageFileSettings`** when you need `GetFilePattern`; if you only have the interface, fall back to reading `FilePattern` (and optionally replicate the DARK/FLAT/BIAS override logic yourself).

## 2. Split pattern into directory segments vs filename

The pattern is one string with `\` or `/` separating **folder segments** and the **final filename template**.

- Split on `\` and `/` (after trim), drop empty parts.
- **Last part** = filename template (may contain multiple macros, e.g. `$$DATETIME$$_$$FILTER$$_…`).
- **All preceding parts** = directory segments, in order from the image root (`FilePath`).

If there is only one part (filename only), there are **no** directory segments: valid files for that pattern live **directly** under `FilePath` (do not recurse into arbitrary subfolders).

## 3. Resolve `$$IMAGETYPE$$` for the scan you are doing

When a **directory segment** contains **`$$IMAGETYPE$$`**, NINA substitutes the **current frame type’s folder label** (e.g. **`LIGHT`**, **`DARK`**) when saving.

For enumeration:

- Scanning for **lights**: treat that segment as matching folder names equivalent to **`LIGHT`** (case-insensitive on Windows).
- Scanning for **darks**: use **`DARK`** for the same macro when building matchers.

Use the **same** `imageType` string you passed to `GetFilePattern` so pattern selection and `$$IMAGETYPE$$` substitution stay consistent.

## 4. Match each directory level; stop at pattern depth

Walk from **`FilePath`**:

1. **Depth 0..N-1**: For each subdirectory name, test whether it matches **only** the corresponding directory segment template for your chosen frame type (see below).
2. **Depth N** (where **N** = number of directory segments): **only** list files in that folder (then filter by extension, FITS type, UTC window, etc.). **Do not** recurse deeper—anything deeper is outside NINA’s declared layout for that pattern.

This automatically avoids unrelated siblings (e.g. tool-created `_rejected` / `calibrated` folders) **when** they sit beside a branch that does not match the segment (notably when `$$IMAGETYPE$$` is its own folder: only `LIGHT` or `DARK` matches).

## 5. Building a matcher for one segment

After fixing **`$$IMAGETYPE$$`** to the literal for your scan (`LIGHT`, `DARK`, …):

- **Literal text** in the segment → escape and match exactly (regex `Regex.Escape`).
- Remaining **`$$TOKEN$$` macros** → map each key to an appropriate wildcard (same conceptual keys as **`NINA.Core.Model.ImagePatternKeys`** / NINA’s **`ImagePatterns`** table): dates, `[^\\/]+` for free-text tokens (target, filter, camera), numeric patterns where appropriate.

Implementations can compile one **`Regex`** per segment (anchors `^…$`, **`RegexOptions.IgnoreCase`** on Windows).

## 6. FITS / header filter still required

Pattern walking restricts **where** files may appear. You should still:

- Filter by extension (`.fits`, `.fit`, `.fts`, …).
- Use FITS headers or NINA metadata rules to ensure the frame is really **LIGHT** / **DARK** / … as intended.

## 7. Edge cases

- **No `$$IMAGETYPE$$` in the path**: lights and flats may share the same folders; separation is by filename / metadata only—narrowing folders is limited.
- **Very permissive segments** (e.g. a segment that is only `$$TARGETNAME$$`): any single folder name can match—including manually created folders. Prefer patterns that separate types (`$$IMAGETYPE$$`) when users mix captures and processed outputs on the same disk.

## References (NINA source / packages)

- **`NINA.Profile.ImageFileSettings`**: `GetFilePattern(string imageType)`, `FilePattern`, `FilePatternDARK`, `FilePatternFLAT`, `FilePatternBIAS`.
- **`NINA.Core.Model.ImagePatternKeys`**: macro strings (`$$DATE$$`, `$$IMAGETYPE$$`, …).
- **`NINA.Core.Model.ImagePatterns`**: how macros expand (`GetImageFileString`, optional `imageType` for `$$IMAGETYPE$$`).

## SeeDrift implementation

See **`NINA.Plugin.SeeDrift/Utility/NinaLightPathEnumeration.cs`** — LIGHT-only scan using `GetFilePattern("LIGHT")` (via **`ImageFileSettings.GetFilePattern("LIGHT")`** when available).
