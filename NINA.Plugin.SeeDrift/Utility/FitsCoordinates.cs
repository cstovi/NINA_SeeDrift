using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace NINA.Plugin.SeeDrift.Utility {

    /// <summary>
    /// Minimal FITS primary-HDU header scan for pointing keywords (no full FITS library dependency).
    /// </summary>
    internal static class FitsCoordinates {

        public static bool TryReadCoordinates(string filePath,
            out double raHours, out double decDeg,
            out string? objectName, out string? instrument) {
            raHours = 0;
            decDeg = 0;
            objectName = null;
            instrument = null;

            try {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var cards = ReadPrimaryHeaderDictionary(fs);
                if (cards.Count == 0)
                    return false;

                if (cards.TryGetValue("OBJECT", out var obj))
                    objectName = obj.Trim();
                if (cards.TryGetValue("INSTRUME", out var ins))
                    instrument = ins.Trim();

                // Prefer WCS CRVAL if clearly degrees / RA Dec.
                if (TryCrval(cards, out raHours, out decDeg))
                    return true;

                // OBJCTRA / OBJCTDEC strings (hours / degrees sexagesimal or decimal).
                if (cards.TryGetValue("OBJCTRA", out var ora) && cards.TryGetValue("OBJCTDEC", out var odec)) {
                    if (TryParseRaHours(ora, out raHours) && TryParseDecDeg(odec, out decDeg))
                        return true;
                }

                // Generic RA / DEC numeric (heuristic; devices vary).
                if (cards.TryGetValue("RA", out var rsa) && cards.TryGetValue("DEC", out var rdec)) {
                    if (double.TryParse(rsa.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var rvRa)
                        && double.TryParse(rdec.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var rvDec)) {
                        if (rvRa <= 24.0 && Math.Abs(rvRa) < 25.0) {
                            raHours = rvRa;
                            decDeg = rvDec;
                            return true;
                        }
                        raHours = rvRa / 15.0;
                        decDeg = rvDec;
                        return true;
                    }
                }

                return false;
            } catch {
                return false;
            }
        }

        private static bool TryCrval(Dictionary<string, string> cards, out double raHours, out double decDeg) {
            raHours = 0;
            decDeg = 0;
            if (!cards.TryGetValue("CRVAL1", out var c1) || !cards.TryGetValue("CRVAL2", out var c2))
                return false;
            if (!double.TryParse(c1.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v1)
                || !double.TryParse(c2.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v2))
                return false;

            cards.TryGetValue("CTYPE1", out var ct1);
            cards.TryGetValue("CTYPE2", out var ct2);
            var t1 = ct1 ?? "";
            var t2 = ct2 ?? "";

            // Equatorial degrees is common for imaging stacks with RA---TAN / DEC--TAN.
            if (t1.IndexOf("RA", StringComparison.OrdinalIgnoreCase) >= 0 && v1 <= 360.0 && v1 >= -360.0) {
                raHours = v1 / 15.0;
                decDeg = v2;
                return true;
            }

            // Already hours / degrees pair on some devices (fallback).
            if (Math.Abs(v1) <= 24.0 && Math.Abs(v2) <= 90.0) {
                raHours = v1;
                decDeg = v2;
                return true;
            }

            raHours = v1 / 15.0;
            decDeg = v2;
            return true;
        }

        private static Dictionary<string, string> ReadPrimaryHeaderDictionary(Stream fs) {
            var cards = new List<string>();
            var buffer = new byte[2880];
            while (fs.Read(buffer, 0, 2880) == 2880) {
                for (var i = 0; i < 2880; i += 80) {
                    var line = System.Text.Encoding.ASCII.GetString(buffer, i, 80).TrimEnd();
                    if (line.StartsWith("END ", StringComparison.Ordinal) || line == "END")
                        return ParseCards(cards);
                    cards.Add(line);
                }
            }
            return ParseCards(cards);
        }

        private static Dictionary<string, string> ParseCards(List<string> rawLines) {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in rawLines) {
                if (line.Length < 9)
                    continue;
                var key = line.Substring(0, 8).TrimEnd();
                var eq = line.IndexOf('=');
                if (eq < 0)
                    continue;
                var value = line.Substring(eq + 1).Trim();
                value = Regex.Replace(value, "/.*$", "").Trim();
                value = value.Trim('\'', ' ', '"');
                if (!dict.ContainsKey(key))
                    dict[key] = value;
            }
            return dict;
        }

        private static bool TryParseRaHours(string s, out double hours) {
            hours = 0;
            s = s.Trim();
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var dec))
            {
                hours = dec;
                return dec >= 0 && dec <= 24.0;
            }

            var parts = Regex.Split(s, @"[\s:]+").Where(p => !string.IsNullOrEmpty(p)).ToArray();
            if (parts.Length < 2)
                return false;

            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var h))
                return false;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var m))
                return false;
            var sec = 0.0;
            if (parts.Length >= 3)
                double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out sec);

            hours = h + m / 60.0 + sec / 3600.0;
            return hours >= 0 && hours < 24.0;
        }

        private static bool TryParseDecDeg(string s, out double decDeg) {
            decDeg = 0;
            s = s.Trim();
            var sign = 1;
            if (s.StartsWith('-')) { sign = -1; s = s.Substring(1).Trim(); }
            else if (s.StartsWith('+')) s = s.Substring(1).Trim();

            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var decOnly)) {
                decDeg = sign * decOnly;
                return Math.Abs(decDeg) <= 90.0;
            }

            var parts = Regex.Split(s, @"[\s:]+").Where(p => !string.IsNullOrEmpty(p)).ToArray();
            if (parts.Length < 2)
                return false;

            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                return false;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var m))
                return false;
            var sec = 0.0;
            if (parts.Length >= 3)
                double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out sec);

            decDeg = sign * (Math.Abs(d) + m / 60.0 + sec / 3600.0);
            return Math.Abs(decDeg) <= 90.0;
        }
    }
}
