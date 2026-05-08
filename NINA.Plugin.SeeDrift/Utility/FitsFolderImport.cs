using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NINA.Plugin.SeeDrift.Utility {

    internal readonly struct FitsReplayEntry {
        public FitsReplayEntry(string path, DateTime sortUtc, DateTime exposureUtc, string targetLabel) {
            Path = path;
            SortUtc = sortUtc;
            ExposureUtc = exposureUtc;
            TargetLabel = targetLabel;
        }

        public string Path { get; }
        public DateTime SortUtc { get; }
        public DateTime ExposureUtc { get; }
        public string TargetLabel { get; }
    }

    internal static class FitsFolderImport {

        private static readonly string[] Extensions = { ".fits", ".fit", ".fts" };

        /// <summary>
        /// Non-recursive scan; sorts by FITS observation time when possible, else file creation UTC, then path.
        /// </summary>
        public static IReadOnlyList<FitsReplayEntry> EnumerateSorted(string folderPath) {
            if (!Directory.Exists(folderPath))
                return Array.Empty<FitsReplayEntry>();

            var list = new List<(string path, DateTime sortUtc, DateTime exposureUtc, string target)>();

            foreach (var path in Directory.EnumerateFiles(folderPath)) {
                var ext = Path.GetExtension(path);
                if (string.IsNullOrEmpty(ext) || Extensions.All(e => !ext.Equals(e, StringComparison.OrdinalIgnoreCase)))
                    continue;

                if (!FitsCoordinates.TryReadPrimaryHeader(path, out var cards))
                    continue;

                if (!FitsCoordinates.PassesLightFilterForReplay(cards))
                    continue;

                DateTime? obsUtc = null;
                if (FitsCoordinates.TryParseObservationUtc(cards, out var parsedUtc))
                    obsUtc = parsedUtc;

                var sortUtc = obsUtc ?? File.GetCreationTimeUtc(path);
                var exposureUtc = obsUtc ?? File.GetLastWriteTimeUtc(path);

                cards.TryGetValue("OBJECT", out var obj);
                var target = string.IsNullOrWhiteSpace(obj) ? Path.GetFileNameWithoutExtension(path) : obj.Trim();

                list.Add((path, sortUtc, exposureUtc, target));
            }

            return list
                .OrderBy(x => x.sortUtc)
                .ThenBy(x => x.path, StringComparer.OrdinalIgnoreCase)
                .Select(x => new FitsReplayEntry(x.path, x.sortUtc, x.exposureUtc, x.target))
                .ToList();
        }
    }
}
