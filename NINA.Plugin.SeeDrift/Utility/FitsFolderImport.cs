using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NINA.Profile.Interfaces;

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
        /// Recursive scan under <paramref name="rootFolderPath"/> following the active profile’s LIGHT
        /// <see cref="IImageFileSettings.FilePattern"/> (same layout NINA uses when saving lights),
        /// keeping only LIGHT frames whose observation UTC lies in <paramref name="windowStartUtc"/>…<paramref name="windowEndUtc"/>
        /// (inclusive). Folder levels match pattern segments (for example <c>$$DATEMINUS12$$</c>, <c>$$IMAGETYPE$$</c> → only
        /// the <c>LIGHT</c> branch); paths outside that structure are not scanned.
        /// Applies a fast path that skips reading FITS headers when both file creation and last-write
        /// **years** fall strictly outside the window’s calendar-year span (catches wrong-year windows quickly).
        /// Rare edge case: a file whose DATE-OBS is inside the window but both timestamps are from another year
        /// (e.g. bulk copy) could be skipped — widen the UTC window slightly if needed.
        /// </summary>
        public static IReadOnlyList<FitsReplayEntry> EnumerateSortedRecursiveForUtcWindow(
                string rootFolderPath,
                IImageFileSettings imageFileSettings,
                DateTime windowStartUtc,
                DateTime windowEndUtc,
                Action<string>? onScanProgress,
                CancellationToken token) {

            static DateTime AsUtc(DateTime t) =>
                t.Kind == DateTimeKind.Utc ? t : t.ToUniversalTime();

            if (!Directory.Exists(rootFolderPath))
                return Array.Empty<FitsReplayEntry>();

            var a = AsUtc(windowStartUtc);
            var b = AsUtc(windowEndUtc);
            if (b < a)
                (a, b) = (b, a);

            var yearLo = Math.Min(a.Year, b.Year);
            var yearHi = Math.Max(a.Year, b.Year);

            var list = new List<(string path, DateTime sortUtc, DateTime exposureUtc, string target, int seq)>();
            var fitsLike = 0;
            var skippedYear = 0;
            var headersRead = 0;
            const int progressEvery = 200;

            foreach (var path in NinaLightPathEnumeration.EnumerateFiles(rootFolderPath, imageFileSettings, token)) {
                token.ThrowIfCancellationRequested();

                var ext = Path.GetExtension(path);
                if (string.IsNullOrEmpty(ext) || Extensions.All(e => !ext.Equals(e, StringComparison.OrdinalIgnoreCase)))
                    continue;

                fitsLike++;

                if (SkipHeaderByFileYear(path, yearLo, yearHi)) {
                    skippedYear++;
                    if (fitsLike % progressEvery == 0) {
                        onScanProgress?.Invoke(
                            $"Scanning folder… {fitsLike} FITS-like files, {list.Count} in UTC window " +
                            $"({skippedYear} skipped by file-year quick check)");
                    }
                    continue;
                }

                if (!FitsCoordinates.TryReadPrimaryHeader(path, out var cards))
                    continue;

                headersRead++;

                if (!FitsCoordinates.PassesLightFilterForReplay(cards))
                    continue;

                DateTime? obsUtc = null;
                if (FitsCoordinates.TryParseObservationUtc(cards, out var parsedUtc))
                    obsUtc = parsedUtc;

                var sortUtc = obsUtc ?? File.GetCreationTimeUtc(path);
                var exposureUtc = obsUtc ?? File.GetLastWriteTimeUtc(path);

                var exp = AsUtc(exposureUtc);
                if (exp < a || exp > b)
                    continue;

                cards.TryGetValue("OBJECT", out var obj);
                var target = string.IsNullOrWhiteSpace(obj) ? Path.GetFileNameWithoutExtension(path) : obj.Trim();

                list.Add((path, sortUtc, exposureUtc, target, ExposureSequenceTieBreak(path)));

                if (fitsLike % progressEvery == 0) {
                    onScanProgress?.Invoke(
                        $"Scanning folder… {fitsLike} FITS-like files, {list.Count} in UTC window " +
                        $"({skippedYear} skipped by file-year quick check)");
                }
            }

            onScanProgress?.Invoke(
                $"Scan done — {fitsLike} FITS-like files, {list.Count} in UTC window " +
                $"({skippedYear} skipped by file-year quick check, {headersRead} headers read)");

            return list
                .OrderBy(x => x.seq)
                .ThenBy(x => x.sortUtc)
                .ThenBy(x => x.path, StringComparer.OrdinalIgnoreCase)
                .Select(x => new FitsReplayEntry(x.path, x.sortUtc, x.exposureUtc, x.target))
                .ToList();
        }

        /// <summary>
        /// Builds LIGHT replay entries from ordered “Saved image to …” paths (see NINA logs). Preserves log order,
        /// skips duplicates, missing files, non-FITS extensions, and calibration frames per <see cref="FitsCoordinates.PassesLightFilterForReplay"/>.
        /// </summary>
        public static IReadOnlyList<FitsReplayEntry> BuildEntriesFromLogSaveOrder(
                IReadOnlyList<(string path, DateTime logLineUtc)> orderedSaves) {

            var result = new List<FitsReplayEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (path, logUtc) in orderedSaves) {
                if (string.IsNullOrWhiteSpace(path) || !seen.Add(path))
                    continue;

                var ext = Path.GetExtension(path);
                if (string.IsNullOrEmpty(ext) || Extensions.All(e => !ext.Equals(e, StringComparison.OrdinalIgnoreCase)))
                    continue;

                try {
                    if (!File.Exists(path))
                        continue;
                } catch {
                    continue;
                }

                if (!FitsCoordinates.TryReadPrimaryHeader(path, out var cards))
                    continue;

                if (!FitsCoordinates.PassesLightFilterForReplay(cards))
                    continue;

                DateTime? obsUtc = null;
                if (FitsCoordinates.TryParseObservationUtc(cards, out var parsedUtc))
                    obsUtc = parsedUtc;

                var sortUtc = obsUtc ?? logUtc;
                var exposureUtc = obsUtc ?? File.GetLastWriteTimeUtc(path);

                cards.TryGetValue("OBJECT", out var obj);
                var target = string.IsNullOrWhiteSpace(obj) ? Path.GetFileNameWithoutExtension(path) : obj.Trim();

                result.Add(new FitsReplayEntry(path, sortUtc, exposureUtc, target));
            }

            return result;
        }

        /// <summary>
        /// Skip reading the FITS header when both filesystem timestamps’ **years** are strictly below
        /// <paramref name="yearLo"/> or strictly above <paramref name="yearHi"/> (window calendar-year span).
        /// </summary>
        private static bool SkipHeaderByFileYear(string path, int yearLo, int yearHi) {
            try {
                var wy = File.GetLastWriteTimeUtc(path).Year;
                var cy = File.GetCreationTimeUtc(path).Year;
                if (wy < yearLo && cy < yearLo)
                    return true;
                if (wy > yearHi && cy > yearHi)
                    return true;
                return false;
            } catch {
                return false;
            }
        }

        /// <summary>
        /// Filters by FITS observation time (UTC). Inclusive on both ends.
        /// </summary>
        public static IReadOnlyList<FitsReplayEntry> FilterObservationUtcInclusive(
                IReadOnlyList<FitsReplayEntry> entries, DateTime startUtc, DateTime endUtc) {
            static DateTime AsUtc(DateTime t) =>
                t.Kind == DateTimeKind.Utc ? t : t.ToUniversalTime();

            var a = AsUtc(startUtc);
            var b = AsUtc(endUtc);
            if (b < a)
                (a, b) = (b, a);

            var list = new List<FitsReplayEntry>();
            foreach (var e in entries) {
                var u = AsUtc(e.ExposureUtc);
                if (u < a || u > b)
                    continue;
                list.Add(e);
            }
            return list;
        }

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

        /// <summary>
        /// Trailing digits after the last underscore before extension in the **basename**
        /// (e.g. <c>…_20.00s_0019.fits</c> → 19). Used for sort keys and UI labels aligned with NINA
        /// <c>$$EXPOSURENUMBER$$</c>.
        /// </summary>
        internal static bool TryExposureSequenceFromFileName(string? fileName, out int sequence) {
            sequence = 0;
            if (string.IsNullOrWhiteSpace(fileName))
                return false;
            var fn = Path.GetFileName(fileName.Trim());
            var dot = fn.LastIndexOf('.');
            if (dot <= 0)
                return false;
            var stem = fn.AsSpan(0, dot);
            var us = stem.LastIndexOf('_');
            if (us < 0 || us >= stem.Length - 1)
                return false;
            var tail = stem[(us + 1)..];
            if (!int.TryParse(tail, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n))
                return false;
            sequence = n;
            return true;
        }

        /// <summary>Sort key: exposure sequence, or <see cref="int.MaxValue"/> if not parseable.</summary>
        private static int ExposureSequenceTieBreak(string fullPath) {
            var fn = Path.GetFileName(fullPath);
            return TryExposureSequenceFromFileName(fn, out var n) ? n : int.MaxValue;
        }
    }
}
