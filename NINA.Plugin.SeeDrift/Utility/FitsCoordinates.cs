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

        /// <summary>Reads primary HDU keyword/value pairs (single header pass).</summary>
        public static bool TryReadPrimaryHeader(string filePath, out Dictionary<string, string> cards) {
            cards = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                cards = ReadPrimaryHeaderDictionary(fs);
                return cards.Count > 0;
            } catch {
                return false;
            }
        }

        public static bool TryReadCoordinates(string filePath,
            out double raHours, out double decDeg,
            out string? objectName, out string? instrument) {
            raHours = 0;
            decDeg = 0;
            objectName = null;
            instrument = null;

            if (!TryReadPrimaryHeader(filePath, out var cards))
                return false;

            return TryParsePointing(cards, out raHours, out decDeg, out objectName, out instrument);
        }

        /// <summary>Parses DATE-OBS / DATE / EXPSTART when present (UTC best-effort).</summary>
        public static bool TryParseObservationUtc(Dictionary<string, string> cards, out DateTime utc) {
            utc = default;
            foreach (var key in new[] { "DATE-OBS", "DATE", "EXPSTART", "OBSTIME" }) {
                if (!cards.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
                    continue;
                raw = raw.Trim().Trim('\'', '"');
                if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)) {
                    utc = parsed.Kind == DateTimeKind.Utc ? parsed : parsed.ToUniversalTime();
                    return true;
                }
                if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)) {
                    utc = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Parses DATE-LOC (local time at the observatory) from FITS headers. Returns raw clock value with Unspecified kind.</summary>
        public static bool TryParseObservationLocal(Dictionary<string, string> cards, out DateTime local) {
            local = default;
            foreach (var key in new[] { "DATE-LOC", "DATE_LOC", "LOCALTIME", "LST" }) {
                if (!cards.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
                    continue;
                raw = raw.Trim().Trim('\'', '"');
                if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) {
                    local = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
                    return true;
                }
            }
            return false;
        }
        /// <summary>
        /// Excludes obvious calibration frames when IMAGETYP / OBSTYPE is set; includes unknown types so folders without keywords still load.
        /// </summary>
        public static bool PassesLightFilterForReplay(Dictionary<string, string> cards) {
            static bool CalKeyword(string v) {
                v = v.Trim('\'', '"').Trim();
                return v.Equals("BIAS", StringComparison.OrdinalIgnoreCase)
                    || v.Equals("DARK", StringComparison.OrdinalIgnoreCase)
                    || v.Equals("FLAT", StringComparison.OrdinalIgnoreCase)
                    || v.Equals("CALIBRATION", StringComparison.OrdinalIgnoreCase)
                    || v.Equals("CALIB", StringComparison.OrdinalIgnoreCase);
            }

            if (cards.TryGetValue("IMAGETYP", out var im) && !string.IsNullOrWhiteSpace(im)) {
                var v = im.Trim('\'', '"').Trim();
                if (CalKeyword(v))
                    return false;
            }
            if (cards.TryGetValue("OBSTYPE", out var ob) && !string.IsNullOrWhiteSpace(ob)) {
                var v = ob.Trim('\'', '"').Trim();
                if (CalKeyword(v))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Derives the image plate scale from XPIXSZ (µm) and FOCALLEN (mm).
        /// Formula: 206.265 × pixel_size_µm / focal_length_mm = arcsec/px.
        /// </summary>
        public static bool TryReadPlateScale(Dictionary<string, string> cards, out double arcSecPerPx) {
            arcSecPerPx = 0;
            if (!cards.TryGetValue("XPIXSZ", out var xp) || !cards.TryGetValue("FOCALLEN", out var fl))
                return false;
            if (!double.TryParse(xp.Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var pixUm) || pixUm <= 0)
                return false;
            if (!double.TryParse(fl.Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var focalMm) || focalMm <= 0)
                return false;
            arcSecPerPx = 206.265 * pixUm / focalMm;
            return arcSecPerPx > 0;
        }

        public static bool TryReadExposureSeconds(Dictionary<string, string> cards, out double exposureSeconds) {
            exposureSeconds = 0;
            foreach (var key in new[] { "EXPTIME", "EXPOSURE", "EXP_TIME", "EXPOSURETIME" }) {
                if (!cards.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
                    continue;
                raw = raw.Trim().Trim('\'', '"');
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed > 0) {
                    exposureSeconds = parsed;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Reads Alt/Az orientation keywords needed for parallactic angle calculation.
        /// Returns false if any required keyword is missing or unparseable.
        /// </summary>
        public static bool TryReadAltAzOrientation(Dictionary<string, string> cards,
            out double altDeg, out double azDeg, out double siteLatDeg, out double decDeg) {
            altDeg = azDeg = siteLatDeg = decDeg = 0;
            if (!cards.TryGetValue("CENTALT",  out var ca)) return false;
            if (!cards.TryGetValue("CENTAZ",   out var cz)) return false;
            if (!cards.TryGetValue("SITELAT",  out var sl)) return false;
            if (!cards.TryGetValue("DEC",       out var dc)) return false;
            if (!double.TryParse(ca.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out altDeg))   return false;
            if (!double.TryParse(cz.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out azDeg))    return false;
            if (!double.TryParse(sl.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out siteLatDeg)) return false;
            if (!double.TryParse(dc.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out decDeg))   return false;
            return true;
        }

        internal static bool TryParsePointing(Dictionary<string, string> cards,
            out double raHours, out double decDeg,
            out string? objectName, out string? instrument) {
            raHours = 0;
            decDeg = 0;
            objectName = null;
            instrument = null;

            try {
                if (cards.TryGetValue("OBJECT", out var obj))
                    objectName = obj.Trim();
                if (cards.TryGetValue("INSTRUME", out var ins))
                    instrument = ins.Trim();

                // Prefer keywords that usually change per frame (solved center / mount).
                // CRVAL/OBJCTRA are often copied from target/reference and identical across subs → flat drift.
                if (TryParseRaDecKeywords(cards, out raHours, out decDeg))
                    return true;
                if (TryParseTelRaDecKeywords(cards, out raHours, out decDeg))
                    return true;
                if (TryCrval(cards, out raHours, out decDeg))
                    return true;

                if (cards.TryGetValue("OBJCTRA", out var ora) && cards.TryGetValue("OBJCTDEC", out var odec)) {
                    if (TryParseRaHours(ora, out raHours) && TryParseDecDeg(odec, out decDeg))
                        return true;
                }

                return false;
            } catch {
                return false;
            }
        }

        /// <summary>RA/DEC numeric pair — often plate-solved image center (decimal deg) or hours+deg.</summary>
        private static bool TryParseRaDecKeywords(Dictionary<string, string> cards,
            out double raHours, out double decDeg) {
            raHours = 0;
            decDeg = 0;
            if (!cards.TryGetValue("RA", out var rsa) || !cards.TryGetValue("DEC", out var rdec))
                return false;
            if (!double.TryParse(rsa.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var rvRa)
                || !double.TryParse(rdec.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var rvDec))
                return false;
            if (rvRa <= 24.0 && Math.Abs(rvRa) < 25.0) {
                raHours = rvRa;
                decDeg = rvDec;
                return true;
            }
            raHours = rvRa / 15.0;
            decDeg = rvDec;
            return true;
        }

        /// <summary>TELRA/TELDEC — mount/telescope coords; typically decimal degrees in FITS.</summary>
        private static bool TryParseTelRaDecKeywords(Dictionary<string, string> cards,
            out double raHours, out double decDeg) {
            raHours = 0;
            decDeg = 0;
            if (!cards.TryGetValue("TELRA", out var tra) || !cards.TryGetValue("TELDEC", out var tdec))
                return false;
            if (!double.TryParse(tra.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var telRa)
                || !double.TryParse(tdec.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var telDec))
                return false;

            // RA field is hours when |RA| ≤ 24 (common ASCOM-style); otherwise decimal degrees (e.g. 280°).
            if (Math.Abs(telRa) > 24.0) {
                raHours = telRa / 15.0;
                decDeg = telDec;
                return true;
            }

            raHours = telRa;
            decDeg = telDec;
            return true;
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
            var t1 = ct1 ?? "";

            if (t1.IndexOf("RA", StringComparison.OrdinalIgnoreCase) >= 0 && v1 <= 360.0 && v1 >= -360.0) {
                raHours = v1 / 15.0;
                decDeg = v2;
                return true;
            }

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
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var dec)) {
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
