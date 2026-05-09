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
        /// Non-recursive scan; sorts primarily by numeric suffix after the last underscore in the file name
        /// (NINA <c>$$EXPOSURENUMBER$$</c>, e.g. <c>…_0019.fits</c>) when present, then FITS observation time
        /// (else file creation UTC), then path — so order follows the sequencer even when <c>DATE-OBS</c> is
        /// out of order between consecutive subs (tie-break-only sorting was not enough).
        /// Files without a parseable suffix sort after numbered lights (suffix <c>int.MaxValue</c> sentinel).
        /// </summary>
        public static IReadOnlyList<FitsReplayEntry> EnumerateSorted(string folderPath) {
            if (!Directory.Exists(folderPath))
                return Array.Empty<FitsReplayEntry>();

            var list = new List<(string path, DateTime sortUtc, DateTime exposureUtc, string target, int seq)>();

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

                list.Add((path, sortUtc, exposureUtc, target, ExposureSequenceTieBreak(path)));
            }

            return list
                .OrderBy(x => x.seq)
                .ThenBy(x => x.sortUtc)
                .ThenBy(x => x.path, StringComparer.OrdinalIgnoreCase)
                .Select(x => new FitsReplayEntry(x.path, x.sortUtc, x.exposureUtc, x.target))
                .ToList();
        }

        /// <summary>Trailing <c>_NNN</c> before extension (e.g. <c>…_20.00s_0019.fits</c> → 19); else <see cref="int.MaxValue"/>.</summary>
        private static int ExposureSequenceTieBreak(string fullPath) {
            var fn = Path.GetFileName(fullPath);
            var dot = fn.LastIndexOf('.');
            if (dot <= 0)
                return int.MaxValue;
            var stem = fn.AsSpan(0, dot);
            var us = stem.LastIndexOf('_');
            if (us < 0 || us >= stem.Length - 1)
                return int.MaxValue;
            var tail = stem[(us + 1)..];
            return int.TryParse(tail, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n)
                ? n
                : int.MaxValue;
        }
    }
}
