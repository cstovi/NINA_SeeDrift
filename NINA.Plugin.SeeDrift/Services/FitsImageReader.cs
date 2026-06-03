using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NINA.Plugin.SeeDrift.Utility;

namespace NINA.Plugin.SeeDrift.Services {

    /// <summary>Result of reading a FITS image file's pixel data.</summary>
    internal sealed class FitsImageData {
        /// <summary>Image width in pixels (NAXIS1).</summary>
        public int Width { get; init; }

        /// <summary>Image height in pixels (NAXIS2).</summary>
        public int Height { get; init; }

        /// <summary>Number of channels: 1 (mono/Bayer) or 3 (RGB).</summary>
        public int Channels { get; init; }

        /// <summary>Linear pixel data. For mono: length = Width * Height. For RGB: length = Width * Height * 3.</summary>
        public ushort[] Data { get; init; } = Array.Empty<ushort>();

        /// <summary>Bayer pattern keyword value (e.g. "GRBG", "RGGB") or null if not Bayer/color.</summary>
        public string? BayerPattern { get; init; }

        /// <summary>True when the image is monochrome (single channel, possibly Bayer).</summary>
        public bool IsMono => Channels == 1;

        /// <summary>True when the image is already RGB (NAXIS3 == 3).</summary>
        public bool IsRgb => Channels == 3;
    }

    /// <summary>
    /// Minimal FITS image data reader. Parses the primary HDU header to determine dimensions
    /// and data type, then reads and converts the pixel data into unsigned 16-bit values.
    /// Supports BITPIX 8, 16, 32, -32, -64 with BZERO/BSCALE conversion.
    /// No external FITS library dependency.
    /// </summary>
    internal static class FitsImageReader {

        /// <summary>
        /// Reads the primary HDU image data from a FITS file.
        /// Returns null if the file is not a valid image FITS or lacks NAXIS1/NAXIS2.
        /// </summary>
        public static FitsImageData? TryReadImageData(string filePath) {
            try {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return ReadImageData(fs);
            } catch (Exception ex) {
                SeeDriftLog.Debug($"FitsImageReader failed for '{filePath}': {ex.Message}");
                return null;
            }
        }

        private static FitsImageData? ReadImageData(Stream stream) {
            var cards = ReadHeaderCards(stream);
            var headerSize = cards.HeaderBlockCount * 2880;

            // Required keywords
            if (!cards.Values.TryGetValue("NAXIS", out var naxisStr)
                || !int.TryParse(naxisStr, out var naxis) || naxis < 2) {
                return null;
            }
            if (!cards.Values.TryGetValue("NAXIS1", out var n1Str)
                || !int.TryParse(n1Str, out var width) || width <= 0) {
                return null;
            }
            if (!cards.Values.TryGetValue("NAXIS2", out var n2Str)
                || !int.TryParse(n2Str, out var height) || height <= 0) {
                return null;
            }

            var channels = 1;
            if (naxis >= 3 && cards.Values.TryGetValue("NAXIS3", out var n3Str)) {
                if (!int.TryParse(n3Str, out var n3)) n3 = 1;
                channels = Math.Max(1, n3);
            }

            if (!cards.Values.TryGetValue("BITPIX", out var bitpixStr)
                || !int.TryParse(bitpixStr, out var bitpix)) {
                return null;
            }

            // BZERO / BSCALE
            double bzero = 0;
            double bscale = 1;
            if (cards.Values.TryGetValue("BZERO", out var bzStr))
                double.TryParse(bzStr, out bzero);
            if (cards.Values.TryGetValue("BSCALE", out var bsStr))
                double.TryParse(bsStr, out bscale);

            // Bayer pattern (mono sensors)
            cards.Values.TryGetValue("BAYERPAT", out var bayerPat);
            bayerPat = bayerPat?.Trim('\'', '"', ' ');

            // Data offset: next 2880-byte boundary after header
            var dataOffset = headerSize;

            // Calculate data dimensions
            var totalPixels = width * height * channels;

            // Read raw data
            var rawBytes = ReadDataBlock(stream, dataOffset, bitpix, totalPixels);
            if (rawBytes == null)
                return null;

            // Convert to ushort[]
            var data = ConvertToUshort(rawBytes, bitpix, bzero, bscale, totalPixels);

            return new FitsImageData {
                Width = width,
                Height = height,
                Channels = channels,
                Data = data,
                BayerPattern = bayerPat
            };
        }

        private static (int HeaderBlockCount, Dictionary<string, string> Values) ReadHeaderCards(Stream stream) {
            var cards = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var buffer = new byte[2880];
            var blockCount = 0;

            while (stream.Read(buffer, 0, 2880) == 2880) {
                blockCount++;
                for (var i = 0; i < 2880; i += 80) {
                    var line = Encoding.ASCII.GetString(buffer, i, 80).TrimEnd();
                    if (line.StartsWith("END ", StringComparison.Ordinal) || line == "END")
                        return (blockCount, cards);
                    if (line.Length < 9) continue;

                    var key = line.Substring(0, 8).TrimEnd();
                    var eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    var value = line.Substring(eq + 1).Trim();
                    // Remove trailing comment after /
                    var slash = value.IndexOf('/');
                    if (slash >= 0) value = value.Substring(0, slash);
                    value = value.Trim('\'', '"', ' ');

                    if (!cards.ContainsKey(key))
                        cards[key] = value;
                }
            }
            return (blockCount, cards);
        }

        private static byte[]? ReadDataBlock(Stream stream, long dataOffset, int bitpix, int totalPixels) {
            var bytesPerPixel = BytesPerPixel(bitpix);
            var dataBytes = (long)totalPixels * bytesPerPixel;
            // FITS data is padded to 2880-byte boundary
            var paddedBytes = ((dataBytes + 2879) / 2880) * 2880;

            var buffer = new byte[paddedBytes];
            stream.Seek(dataOffset, SeekOrigin.Begin);
            var read = stream.Read(buffer, 0, (int)Math.Min(paddedBytes, int.MaxValue));
            if (read < dataBytes)
                return null;

            return buffer;
        }

        private static int BytesPerPixel(int bitpix) => bitpix switch {
            8 => 1,
            16 => 2,
            32 => 4,
            -32 => 4,
            -64 => 8,
            _ => 2 // fallback
        };

        /// <summary>
        /// Converts raw FITS pixel data to ushort[] using the standard FITS physical-value formula:
        ///   physical_value = BZERO + BSCALE * stored_value
        /// </summary>
        private static ushort[] ConvertToUshort(byte[] raw, int bitpix, double bzero, double bscale, int totalPixels) {
            var result = new ushort[totalPixels];

            switch (bitpix) {
                case 8: {
                    // Unsigned byte
                    for (var i = 0; i < totalPixels && i < raw.Length; i++) {
                        var val = bzero + bscale * raw[i];
                        result[i] = ClampUshort(val);
                    }
                    break;
                }
                case 16: {
                    // Signed 16-bit big-endian
                    for (var i = 0; i < totalPixels && i * 2 + 1 < raw.Length; i++) {
                        var rawVal = (short)((raw[i * 2] << 8) | raw[i * 2 + 1]);
                        var val = bzero + bscale * rawVal;
                        result[i] = ClampUshort(val);
                    }
                    break;
                }
                case 32: {
                    // Signed 32-bit big-endian
                    for (var i = 0; i < totalPixels && i * 4 + 3 < raw.Length; i++) {
                        var rawVal = (raw[i * 4] << 24) | (raw[i * 4 + 1] << 16)
                                   | (raw[i * 4 + 2] << 8) | raw[i * 4 + 3];
                        var val = bzero + bscale * rawVal;
                        result[i] = ClampUshort(val);
                    }
                    break;
                }
                case -32: {
                    // IEEE single-precision big-endian
                    var floatBytes = new byte[4];
                    for (var i = 0; i < totalPixels && i * 4 + 3 < raw.Length; i++) {
                        floatBytes[0] = raw[i * 4];
                        floatBytes[1] = raw[i * 4 + 1];
                        floatBytes[2] = raw[i * 4 + 2];
                        floatBytes[3] = raw[i * 4 + 3];
                        if (BitConverter.IsLittleEndian)
                            Array.Reverse(floatBytes);
                        var val = bzero + bscale * BitConverter.ToSingle(floatBytes, 0);
                        result[i] = ClampUshort(val);
                    }
                    break;
                }
                case -64: {
                    // IEEE double-precision big-endian
                    var doubleBytes = new byte[8];
                    for (var i = 0; i < totalPixels && i * 8 + 7 < raw.Length; i++) {
                        Buffer.BlockCopy(raw, i * 8, doubleBytes, 0, 8);
                        if (BitConverter.IsLittleEndian)
                            Array.Reverse(doubleBytes);
                        var val = bzero + bscale * BitConverter.ToDouble(doubleBytes, 0);
                        result[i] = ClampUshort(val);
                    }
                    break;
                }
            }

            return result;
        }

        private static ushort ClampUshort(double val) {
            if (val < 0) return 0;
            if (val > ushort.MaxValue) return ushort.MaxValue;
            return (ushort)Math.Round(val);
        }
    }
}
