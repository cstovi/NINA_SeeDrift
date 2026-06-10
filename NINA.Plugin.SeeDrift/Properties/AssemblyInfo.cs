using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;

[assembly: SupportedOSPlatform("windows")]

[assembly: AssemblyTitle("SeeDrift")]
[assembly: AssemblyMetadata("MinimumApplicationVersion", "3.2.0.9001")]
[assembly: AssemblyMetadata("FeaturedImageURL", "https://i.ibb.co/PsM1CDGS/Screenshot-2026-05-08-203201.png")]
[assembly: AssemblyMetadata("ChangelogURL", "https://github.com/cstovi/NINA_SeeDrift/releases")]
[assembly: AssemblyMetadata("Homepage", "https://ko-fi.com/turnpike47298")]
[assembly: AssemblyDescription("Measures mount drift for Seestar devices by plate-solving LIGHT frames referenced in NINA session logs. Generates HTML reports with drift timelines, dither scoring, star-shape and walking-noise risk assessment, and optional preview videos.")]
[assembly: AssemblyMetadata("ShortDescription", "Drift analysis for Seestar devices using NINA session logs.")]
[assembly: AssemblyMetadata("LongDescription", @"SeeDrift reads NINA session logs and plate-solves every LIGHT frame to measure mount drift. Results are shown as ΔRA/ΔDec arcseconds in an interactive HTML report with drift timelines, quality metrics, and per-target analytics.

Dither, center-after-drift, and SeeDither events are correlated from the NINA sequencer log. Star-shape and walking-noise drift risk are summarized separately. Optional preview videos with a drift reticle overlay can be generated using the embedded FFmpeg.

Reports are stored in a local library and can be exported or compared. If you use and like anything I've done, support on Ko-fi (https://ko-fi.com/turnpike47298) is appreciated to encourage me to keep going!")]
[assembly: AssemblyCompany("Carl Stovell")]
[assembly: AssemblyProduct("NINA.Plugin.SeeDrift")]
[assembly: AssemblyVersion("1.7.0.0")]
[assembly: AssemblyFileVersion("1.7.0.0")]
[assembly: InternalsVisibleTo("NINA.Plugin.SeeDrift.Tests")]
[assembly: AssemblyMetadata("id", "E8D4C2F1-6B7A-4E9D-8C3F-1A2B5D7E9F0C")]
[assembly: Guid("E8D4C2F1-6B7A-4E9D-8C3F-1A2B5D7E9F0C")]
[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,
    ResourceDictionaryLocation.SourceAssembly
)]
