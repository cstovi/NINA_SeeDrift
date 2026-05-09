using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;

[assembly: AssemblyTitle("SeeDrift")]
[assembly: AssemblyMetadata("MinimumApplicationVersion", "3.2.0.9001")]
[assembly: AssemblyMetadata("FeaturedImageURL", "https://i.ibb.co/PsM1CDGS/Screenshot-2026-05-08-203201.png")]
[assembly: AssemblyMetadata("ChangelogURL", "https://github.com/cstovi/NINA_SeeDrift/releases")]
[assembly: AssemblyDescription("Plots frame-to-frame RA/Dec drift from saved lights — live dockable chart plus HTML export.")]
[assembly: AssemblyMetadata("ShortDescription", "Live drift plot and HTML report from FITS coordinates on each saved light frame.")]
[assembly: AssemblyMetadata("LongDescription", @"SeeDrift listens for saved LIGHT images and plots offsets in arcseconds relative to the first frame of the current run (or after Reset). Use it to see tracking drift and whether mount dither steps appear in both RA and Dec.

Coordinates are read from each FITS primary header (OBJCTRA/OBJCTDEC, RA/DEC, or CRVAL1/CRVAL2 when present).

Export HTML from the dockable panel to archive or share the same trace offline. Import an existing folder of FITS lights (offline replay) for testing without imaging.")]
[assembly: AssemblyCompany("Carl Stovell")]
[assembly: AssemblyProduct("NINA.Plugin.SeeDrift")]
[assembly: AssemblyVersion("0.4.13.0")]
[assembly: AssemblyFileVersion("0.4.13.0")]
[assembly: AssemblyMetadata("id", "E8D4C2F1-6B7A-4E9D-8C3F-1A2B5D7E9F0C")]
[assembly: Guid("E8D4C2F1-6B7A-4E9D-8C3F-1A2B5D7E9F0C")]
[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,
    ResourceDictionaryLocation.SourceAssembly
)]
