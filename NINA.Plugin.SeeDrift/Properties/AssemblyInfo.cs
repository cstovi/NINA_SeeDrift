using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;

[assembly: AssemblyTitle("SeeDrift")]
[assembly: AssemblyMetadata("MinimumApplicationVersion", "3.2.0.9001")]
[assembly: AssemblyMetadata("FeaturedImageURL", "https://i.ibb.co/PsM1CDGS/Screenshot-2026-05-08-203201.png")]
[assembly: AssemblyMetadata("ChangelogURL", "https://github.com/cstovi/NINA_SeeDrift/releases")]
[assembly: AssemblyDescription("Plate-solves saved LIGHT frames under the NINA image folder for a UTC window and writes drift HTML (Chart.js) with optional NINA log markers.")]
[assembly: AssemblyMetadata("ShortDescription", "Plate-solved drift HTML from LIGHT frames in your NINA image directory.")]
[assembly: AssemblyMetadata("LongDescription", @"SeeDrift finds LIGHT FITS under your NINA image file path (recursive), filtered by the UTC arm window between SeeDrift Start and Stop (or a Test window in plugin options). Each frame is plate-solved using your NINA plate-solve profile; drift is shown as ΔRA/ΔDec arcseconds vs the first solved frame.

When NINA session logs match the observation interval, between-frame dither and center-after-drift triggers are listed in the HTML report.")]
[assembly: AssemblyCompany("Carl Stovell")]
[assembly: AssemblyProduct("NINA.Plugin.SeeDrift")]
[assembly: AssemblyVersion("0.5.2.0")]
[assembly: AssemblyFileVersion("0.5.2.0")]
[assembly: AssemblyMetadata("id", "E8D4C2F1-6B7A-4E9D-8C3F-1A2B5D7E9F0C")]
[assembly: Guid("E8D4C2F1-6B7A-4E9D-8C3F-1A2B5D7E9F0C")]
[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,
    ResourceDictionaryLocation.SourceAssembly
)]
