# SeeDrift — NINA plugin

Measures mount drift for Seestar devices by plate-solving LIGHT frames referenced in NINA session logs. Generates an interactive HTML report with drift charts, dither scoring, star-shape and walking-noise risk assessment, and optional preview videos.

## Requirements

- **N.I.N.A.** 3.2+ (.NET 8)
- Windows
- A working **plate solve** profile in NINA

## Install

1. `dotnet build -c Release`
2. Copy `NINA.Plugin.SeeDrift.dll` from `bin\Release\net8.0-windows\` to:
   `%LOCALAPPDATA%\NINA\Plugins\3.0.0\SeeDrift\`
3. Restart NINA

## Quick start

### Screenshots

<img width="2069" height="1366" alt="image" src="https://github.com/user-attachments/assets/8158081d-a6dc-43ad-8384-95156630d72b" />

<img width="2041" height="1186" alt="image" src="https://github.com/user-attachments/assets/9c83840b-5536-49b9-b9c4-fb82a06e79ae" />


### Sequencer (recommended)
Add **SeeDrift Start** before capture and **SeeDrift Stop** when finished. Stop reads the NINA log, plate-solves each LIGHT frame, and appends a drift report to the rolling night HTML. Reports are stored in `%LocalAppData%\NINA\SeeDrift\Reports`.

### Options panel
Under **Plugins → SeeDrift**, pick a recent NINA log from the dropdown (or browse for any `.log`), then click **Run report**.

### Preview videos (optional)
Enable **Auto-generate video** in plugin settings to create an MP4 preview of each target's drift, with an optional drift reticle overlay.

## Documentation

See **[docs/MANUAL.md](docs/MANUAL.md)** for full details on options, report layout, and troubleshooting.

## Changelog

See **[CHANGELOG.md](CHANGELOG.md)**.

## Support

If you use and like anything I've done, support on [Ko-fi](https://ko-fi.com/turnpike47298) is appreciated to encourage me to keep going!
