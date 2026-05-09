using System.Reflection;

var astroPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".nuget", "packages", "nina.astrometry", "3.2.0.9001",
    "lib", "net8.0-windows7.0", "NINA.Astrometry.dll");
var astro = Assembly.LoadFrom(astroPath);
var e = astro.GetType("NINA.Astrometry.Coordinates+RAType");
Console.WriteLine(string.Join(", ", Enum.GetNames(e!)));
