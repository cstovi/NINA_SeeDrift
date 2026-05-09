using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;

[assembly: SupportedOSPlatform("windows")]

[assembly: AssemblyTitle("SeeDrift")]
[assembly: AssemblyMetadata("MinimumApplicationVersion", "3.2.0.9001")]
[assembly: AssemblyMetadata("FeaturedImageURL", "https://i.ibb.co/PsM1CDGS/Screenshot-2026-05-08-203201.png")]
[assembly: AssemblyMetadata("ChangelogURL", "https://github.com/cstovi/NINA_SeeDrift/releases")]
[assembly: AssemblyDescription("Plate-solves LIGHT frames referenced by NINA session logs (Saved image to …) and writes drift HTML (Chart.js) with optional sequencer markers.")]
[assembly: AssemblyMetadata("ShortDescription", "Plate-solved drift HTML from LIGHT frames in your NINA image directory.")]
[assembly: AssemblyMetadata("LongDescription", @"SeeDrift reads NINA session logs (%LocalAppData%\NINA\Logs): Saved image to … lines give the FITS paths to solve. Start→Stop keeps lines between arm and disarm times; Test report lets you pick a historic .log file. Each frame is plate-solved using your NINA plate-solve profile; drift is shown as ΔRA/ΔDec arcseconds vs the first solved frame.

When log lines correlate between exposures, dither and center-after-drift triggers appear in the HTML report.")]
[assembly: AssemblyCompany("Carl Stovell")]
[assembly: AssemblyProduct("NINA.Plugin.SeeDrift")]
[assembly: AssemblyVersion("0.7.13.0")]
[assembly: AssemblyFileVersion("0.7.13.0")]
[assembly: AssemblyMetadata("id", "E8D4C2F1-6B7A-4E9D-8C3F-1A2B5D7E9F0C")]
[assembly: Guid("E8D4C2F1-6B7A-4E9D-8C3F-1A2B5D7E9F0C")]
[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,
    ResourceDictionaryLocation.SourceAssembly
)]
