using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace NINA.Plugin.SeeDrift.Utility {

    /// <summary>
    /// Loads a central square crop from the primary HDU image data (uncompressed FITS).
    /// </summary>
    internal static class FitsImageCrop {

        public static bool TryLoadCentralCrop(string filePath, int cropSize, out double[,]? crop) {
            crop = null;
            try {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var cards = new List<string>();
                var buffer = new byte[2880];
                long headerEnd = 0;
                while (fs.Read(buffer, 0, 2880) == 2880) {
                    for (var i = 0; i < 2880; i += 80) {
                        var line = System.Text.Encoding.ASCII.GetString(buffer, i, 80).TrimEnd();
                        if (line.StartsWith("END ", StringComparison.Ordinal) || line == "END") {
                            headerEnd = fs.Position;
                            goto HeaderDone;
                        }
                        cards.Add(line);
                    }
                }
                HeaderDone:
                ;

                var dict = ParseCards(cards);
                if (!dict.TryGetValue("NAXIS", out var naxisStr) || !int.TryParse(naxisStr.Trim(), out var naxis) || naxis < 2)
                    return false;

                dict.TryGetValue("NAXIS1", out var wStr);
                dict.TryGetValue("NAXIS2", out var hStr);
                if (!int.TryParse(wStr?.Trim(), out var width) || !int.TryParse(hStr?.Trim(), out var height))
                    return false;

                if (!dict.TryGetValue("BITPIX", out var bpStr) || !int.TryParse(bpStr.Trim(), out var bitpix))
                    return false;

                dict.TryGetValue("BSCALE", out var bsStr);
                dict.TryGetValue("BZERO", out var bzStr);
                var bscale = double.TryParse(bsStr?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var bs) ? bs : 1.0;
                var bzero = double.TryParse(bzStr?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var bz) ? bz : 0.0;

                var pixelsPerPlane = width * height;
                var bytesPerPixel = BytesPerPixel(bitpix);
                if (bytesPerPixel == 0)
                    return false;

                var dataOffset = headerEnd;
                fs.Position = dataOffset;

                var planeBytes = pixelsPerPlane * bytesPerPixel;
                fs.Position = dataOffset;

                var raw = new byte[planeBytes];
                var read = fs.Read(raw, 0, planeBytes);
                if (read < planeBytes)
                    return false;

                var img = new double[height, width];
                DecodePlane(raw, width, height, bitpix, bscale, bzero, img);

                crop = ExtractCrop(img, cropSize);
                return crop != null;
            } catch {
                return false;
            }
        }

        private static double[,]? ExtractCrop(double[,] img, int cropSize) {
            var h = img.GetLength(0);
            var w = img.GetLength(1);
            var cw = Math.Min(cropSize, w);
            var ch = Math.Min(cropSize, h);
            if (cw < 32 || ch < 32)
                return null;

            var ox = (w - cw) / 2;
            var oy = (h - ch) / 2;
            var crop = new double[ch, cw];
            for (var y = 0; y < ch; y++)
                for (var x = 0; x < cw; x++)
                    crop[y, x] = img[oy + y, ox + x];

            return crop;
        }

        private static int BytesPerPixel(int bitpix) => bitpix switch {
            8 => 1,
            16 => 2,
            32 => 4,
            -32 => 4,
            -64 => 8,
            _ => 0
        };

        private static void DecodePlane(byte[] raw, int width, int height, int bitpix, double bscale, double bzero, double[,] img) {
            var i = 0;
            for (var y = 0; y < height; y++) {
                for (var x = 0; x < width; x++) {
                    double v = 0;
                    switch (bitpix) {
                        case 8:
                            v = raw[i++];
                            break;
                        case 16:
                            v = ReadInt16Big(raw, ref i);
                            v = bzero + bscale * v;
                            break;
                        case 32:
                            v = ReadInt32Big(raw, ref i);
                            v = bzero + bscale * v;
                            break;
                        case -32:
                            v = ReadFloatBig(raw, ref i);
                            break;
                        case -64:
                            v = ReadDoubleBig(raw, ref i);
                            break;
                    }
                    img[y, x] = double.IsFinite(v) ? v : 0;
                }
            }
        }

        private static short ReadInt16Big(byte[] raw, ref int i) {
            var b0 = raw[i++];
            var b1 = raw[i++];
            return (short)((b0 << 8) | b1);
        }

        private static int ReadInt32Big(byte[] raw, ref int i) {
            var v = (raw[i] << 24) | (raw[i + 1] << 16) | (raw[i + 2] << 8) | raw[i + 3];
            i += 4;
            return v;
        }

        private static float ReadFloatBig(byte[] raw, ref int i) {
            var b = new byte[4];
            Buffer.BlockCopy(raw, i, b, 0, 4);
            i += 4;
            if (BitConverter.IsLittleEndian)
                Array.Reverse(b);
            return BitConverter.ToSingle(b, 0);
        }

        private static double ReadDoubleBig(byte[] raw, ref int i) {
            var b = new byte[8];
            Buffer.BlockCopy(raw, i, b, 0, 8);
            i += 8;
            if (BitConverter.IsLittleEndian)
                Array.Reverse(b);
            return BitConverter.ToDouble(b, 0);
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
    }
}
