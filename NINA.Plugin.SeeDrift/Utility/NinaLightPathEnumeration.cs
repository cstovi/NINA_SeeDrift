using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using NINA.Core.Model;
using NINA.Profile;
using NINA.Profile.Interfaces;

namespace NINA.Plugin.SeeDrift.Utility {

    /// <summary>
    /// Enumerates files under the profile image root following the active profile’s **LIGHT** file pattern
    /// (NINA <see cref="IImageFileSettings.FilePattern"/> for LIGHT — same selection as <c>GetFilePattern("LIGHT")</c>), same as NINA when saving lights.
    /// Directory levels are matched per pattern segment; <c>$$IMAGETYPE$$</c> must match <c>LIGHT</c>, so sibling
    /// folders such as calibration stacks are never entered when the pattern separates types by folder.
    /// </summary>
    internal static class NinaLightPathEnumeration {

        private static readonly char[] PathSeparators = { '\\', '/' };

        /// <summary>Same LIGHT pattern NINA uses when saving (main <see cref="IImageFileSettings.FilePattern"/> unless overridden).</summary>
        internal static string GetLightFilePattern(IImageFileSettings settings) {
            if (settings is ImageFileSettings concrete)
                return concrete.GetFilePattern("LIGHT") ?? "";
            return settings.FilePattern ?? "";
        }

        /// <summary>Path segments from the pattern above the filename (last segment).</summary>
        internal static IReadOnlyList<string> GetDirectorySegments(string lightFilePattern) {
            if (string.IsNullOrWhiteSpace(lightFilePattern))
                return Array.Empty<string>();

            var parts = lightFilePattern
                .Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToArray();

            if (parts.Length <= 1)
                return Array.Empty<string>();

            return parts.Take(parts.Length - 1).ToArray();
        }

        /// <summary>
        /// FITS-like files under <paramref name="imageRoot"/> that lie in directories allowed by the LIGHT pattern.
        /// When the pattern defines no subfolders, only files directly under <paramref name="imageRoot"/> are returned.
        /// </summary>
        internal static IEnumerable<string> EnumerateFiles(string imageRoot, IImageFileSettings settings, CancellationToken token) {
            var pattern = GetLightFilePattern(settings);
            var dirSegments = GetDirectorySegments(pattern);
            var regexes = dirSegments.Select(BuildRegexForDirectorySegment).ToArray();

            var stack = new Stack<(string Dir, int Depth)>();
            stack.Push((imageRoot, 0));

            while (stack.Count > 0) {
                token.ThrowIfCancellationRequested();
                var (dir, depth) = stack.Pop();

                if (depth == regexes.Length) {
                    string[] files;
                    try {
                        files = Directory.GetFiles(dir);
                    } catch {
                        continue;
                    }
                    foreach (var f in files)
                        yield return f;
                    continue;
                }

                string[] subdirs;
                try {
                    subdirs = Directory.GetDirectories(dir);
                } catch {
                    continue;
                }

                var rx = regexes[depth];
                foreach (var sd in subdirs) {
                    var name = Path.GetFileName(sd);
                    if (string.IsNullOrEmpty(name))
                        continue;
                    if (!rx.IsMatch(name))
                        continue;
                    stack.Push((sd, depth + 1));
                }
            }
        }

        /// <summary>
        /// Builds a regex for one directory segment. Replaces <c>$$IMAGETYPE$$</c> with NINA’s LIGHT folder name
        /// before expanding other tokens to wildcards (same keys as <see cref="ImagePatterns"/>).
        /// </summary>
        internal static Regex BuildRegexForDirectorySegment(string segmentTemplate) {
            if (string.IsNullOrEmpty(segmentTemplate))
                return new Regex("^$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            var afterLight = segmentTemplate.Replace(ImagePatternKeys.ImageType, "LIGHT", StringComparison.Ordinal);

            var sb = new StringBuilder("^");
            var i = 0;
            while (i < afterLight.Length) {
                var start = afterLight.IndexOf("$$", i, StringComparison.Ordinal);
                if (start < 0) {
                    sb.Append(Regex.Escape(afterLight[i..]));
                    break;
                }
                if (start > i)
                    sb.Append(Regex.Escape(afterLight[i..start]));

                var end = afterLight.IndexOf("$$", start + 2, StringComparison.Ordinal);
                if (end < 0) {
                    sb.Append(Regex.Escape(afterLight[start..]));
                    break;
                }

                var token = afterLight.Substring(start, end - start + 2);
                sb.Append(TokenToRegexFragment(token));
                i = end + 2;
            }

            sb.Append('$');
            return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static string TokenToRegexFragment(string token) {
            if (token == ImagePatternKeys.Date
                || token == ImagePatternKeys.DateUtc
                || token == ImagePatternKeys.DateMinus12
                || token == ImagePatternKeys.ApplicationStartDate)
                return @"\d{4}-\d{2}-\d{2}";

            if (token == ImagePatternKeys.DateTime)
                return @"\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}";

            if (token == ImagePatternKeys.Time || token == ImagePatternKeys.TimeUtc)
                return @"\d{2}-\d{2}-\d{2}";

            if (token == ImagePatternKeys.MJD)
                return @"\d+(?:\.\d+)?";

            if (token == ImagePatternKeys.FrameNr)
                return @"\d+";

            if (token == ImagePatternKeys.Binning)
                return @"\d+x\d+";

            if (token == ImagePatternKeys.Gain || token == ImagePatternKeys.Offset || token == ImagePatternKeys.USBLimit
                || token == ImagePatternKeys.StarCount)
                return @"\d+";

            if (token == ImagePatternKeys.SensorTemp || token == ImagePatternKeys.TemperatureSetPoint
                || token == ImagePatternKeys.RMS || token == ImagePatternKeys.RMSArcSec
                || token == ImagePatternKeys.PeakRA || token == ImagePatternKeys.PeakRAArcSec
                || token == ImagePatternKeys.PeakDec || token == ImagePatternKeys.PeakDecArcSec
                || token == ImagePatternKeys.FocuserPosition || token == ImagePatternKeys.HFR
                || token == ImagePatternKeys.SQM || token == ImagePatternKeys.RotatorAngle
                || token == ImagePatternKeys.FocuserTemp || token == ImagePatternKeys.ExposureTime)
                return @"-?\d+(?:\.\d+)?";

            if (token == ImagePatternKeys.Filter || token == ImagePatternKeys.TargetName
                || token == ImagePatternKeys.Camera || token == ImagePatternKeys.Telescope
                || token == ImagePatternKeys.ReadoutMode || token == ImagePatternKeys.SequenceTitle)
                return @"[^\\/]+";

            // Unknown / future token: single path segment, non-empty
            return @"[^\\/]+";
        }
    }
}
