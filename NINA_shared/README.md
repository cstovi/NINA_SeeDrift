# NINA_shared

Documentation and conventions shared across **See\*** NINA plugins (SeeDrift, SeeDark, …). Copy or symlink this folder into sibling plugin repos if you keep multiple plugins in one workspace.

| Document | Purpose |
|----------|---------|
| [NINA_PLUGIN_GUIDE.md](./NINA_PLUGIN_GUIDE.md) | Cross-plugin lessons: **session-scoped** exports (avoid “today” for session dates), **log filename** `yyyyMMdd-…` prefixes, **one helper** for filenames + HTML headings, rolling-file behavior. |
| [NINA_IMAGE_PATH_ENUMERATION.md](./NINA_IMAGE_PATH_ENUMERATION.md) | How to scan **`IImageFileSettings.FilePath`** using NINA’s **file pattern** per frame type (LIGHT vs DARK vs FLAT vs BIAS), including `GetFilePattern` and `$$IMAGETYPE$$`. |
| [gitship.mdc](./gitship.mdc) | Short GitShip pointer + link to the enumeration guide (optional Cursor rule content). |
