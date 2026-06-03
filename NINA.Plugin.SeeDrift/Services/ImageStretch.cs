using System;

namespace NINA.Plugin.SeeDrift.Services {

    /// <summary>
    /// Applies auto-stretch and debayering to linear FITS image data for video preview generation.
    /// Output is BGR24 byte array (byte per channel, 3 bytes per pixel) suitable for FFmpeg rawvideo pipe.
    /// </summary>
    internal static class ImageStretch {

        private const double ClipLowPercentile = 0.01;    // 1% low clip (push sky background to black)
        private const double ClipHighPercentile = 0.999;  // 99.9% high clip (ignore hot pixels)
        private const double DefaultMidtone = 0.30;       // MTF midtone parameter (0..1, 0.5=linear, lower=brighter stretch, higher=darker)

        /// <summary>
        /// Processes a FITS image: debayers if Bayer pattern is given, then auto-stretches,
        /// and returns BGR24 pixel data.
        /// </summary>
        /// <param name="data">Linear pixel data from FITSImageReader.</param>
        /// <param name="width">Image width in pixels.</param>
        /// <param name="height">Image height in pixels.</param>
        /// <param name="channels">1 for mono/Bayer, 3 for RGB.</param>
        /// <param name="bayerPattern">Bayer pattern string (e.g. "GRBG") or null for already-color.</param>
        /// <returns>BGR24 byte array (length = width * height * 3).</returns>
        public static byte[] ProcessToBgr24(ushort[] data, int width, int height, int channels, string? bayerPattern) {
            if (channels == 3) {
                // Already RGB (NAXIS3=3) — data is interleaved R,G,B per pixel
                return StretchRgbToBgr24(data, width, height);
            }

            // Mono or Bayer
            if (!string.IsNullOrEmpty(bayerPattern)) {
                return DebayerAndStretch(data, width, height, bayerPattern!);
            }

            // Mono, no Bayer — convert to grayscale BGR
            return StretchMonoToBgr24(data, width, height);
        }

        /// <summary>Stretches a mono (single-channel) image into BGR24 grayscale.</summary>
        private static byte[] StretchMonoToBgr24(ushort[] data, int width, int height) {
            var (low, high) = ComputePercentileClipping(data);
            var mtfLookup = BuildMtfLookup(low, high, DefaultMidtone);
            var pixels = width * height;
            var result = new byte[pixels * 3];

            for (var i = 0; i < pixels; i++) {
                var b = mtfLookup[data[i]];
                result[i * 3] = b;      // B
                result[i * 3 + 1] = b;  // G
                result[i * 3 + 2] = b;  // R
            }

            return result;
        }

        /// <summary>Stretches an already-debayered RGB image (interleaved R,G,B per pixel) into BGR24.</summary>
        private static byte[] StretchRgbToBgr24(ushort[] data, int width, int height) {
            var pixels = width * height;
            var result = new byte[pixels * 3];

            // Extract per-channel data for percentile clipping
            var rChannel = new ushort[pixels];
            var gChannel = new ushort[pixels];
            var bChannel = new ushort[pixels];
            for (var i = 0; i < pixels; i++) {
                rChannel[i] = data[i * 3];
                gChannel[i] = data[i * 3 + 1];
                bChannel[i] = data[i * 3 + 2];
            }

            var (rLow, rHigh) = ComputePercentileClipping(rChannel);
            var (gLow, gHigh) = ComputePercentileClipping(gChannel);
            var (bLow, bHigh) = ComputePercentileClipping(bChannel);

            var rLookup = BuildMtfLookup(rLow, rHigh, DefaultMidtone);
            var gLookup = BuildMtfLookup(gLow, gHigh, DefaultMidtone);
            var bLookup = BuildMtfLookup(bLow, bHigh, DefaultMidtone);

            for (var i = 0; i < pixels; i++) {
                result[i * 3] = bLookup[bChannel[i]];      // B
                result[i * 3 + 1] = gLookup[gChannel[i]];  // G
                result[i * 3 + 2] = rLookup[rChannel[i]];  // R
            }

            return result;
        }

        /// <summary>
        /// Simple bilinear debayer from Bayer CFA to full color, then stretch to BGR24.
        /// Input data is Width × Height (1 channel with Bayer pattern).
        /// </summary>
        private static byte[] DebayerAndStretch(ushort[] data, int width, int height, string bayerPattern) {
            var pixels = width * height;
            var rChannel = new ushort[pixels];
            var gChannel = new ushort[pixels];
            var bChannel = new ushort[pixels];

            // Determine Bayer phase offsets (which pixel in 2x2 block is R, G, G, B)
            // Common patterns: GRBG, RGGB, BGGR, GBRG
            var (r0, r1) = GetBayerPhase(bayerPattern, 'R');
            var (b0, b1) = GetBayerPhase(bayerPattern, 'B');
            // G has two positions: the two remaining in the 2x2

            for (var y = 0; y < height; y++) {
                for (var x = 0; x < width; x++) {
                    var i = y * width + x;
                    var phaseX = x & 1;
                    var phaseY = y & 1;

                    if (phaseX == r1 && phaseY == r0) {
                        // Red pixel
                        rChannel[i] = data[i];
                        // Interpolate G from neighbors
                        gChannel[i] = InterpolateGreen(data, width, height, x, y);
                        // Interpolate B from neighbors
                        bChannel[i] = InterpolateBAtR(data, width, height, x, y);
                    } else if (phaseX == b1 && phaseY == b0) {
                        // Blue pixel
                        bChannel[i] = data[i];
                        gChannel[i] = InterpolateGreen(data, width, height, x, y);
                        rChannel[i] = InterpolateRAtB(data, width, height, x, y);
                    } else {
                        // Green pixel (two positions in 2x2)
                        gChannel[i] = data[i];
                        // Interpolate R and B from neighbors
                        rChannel[i] = InterpolateRAtG(data, width, height, x, y, phaseX, phaseY);
                        bChannel[i] = InterpolateBAtG(data, width, height, x, y, phaseX, phaseY);
                    }
                }
            }

            // Stretch each channel independently
            var (rLow, rHigh) = ComputePercentileClipping(rChannel);
            var (gLow, gHigh) = ComputePercentileClipping(gChannel);
            var (bLow, bHigh) = ComputePercentileClipping(bChannel);

            var rLookup = BuildMtfLookup(rLow, rHigh, DefaultMidtone);
            var gLookup = BuildMtfLookup(gLow, gHigh, DefaultMidtone);
            var bLookup = BuildMtfLookup(bLow, bHigh, DefaultMidtone);

            var result = new byte[pixels * 3];
            for (var i = 0; i < pixels; i++) {
                result[i * 3] = bLookup[bChannel[i]];      // B
                result[i * 3 + 1] = gLookup[gChannel[i]];  // G
                result[i * 3 + 2] = rLookup[rChannel[i]];  // R
            }

            return result;
        }

        /// <summary>
        /// Returns the (row, col) parity for a given color in a 2x2 Bayer block.
        /// row parity is the y-mod-2, col parity is the x-mod-2.
        /// </summary>
        private static (int rowParity, int colParity) GetBayerPhase(string pattern, char color) {
            // pattern like "GRBG" means:
            // Row0: G R, Row1: B G
            // So G=0,0 R=0,1 B=1,0 G=1,1
            // Phase: (y%2, x%2)
            for (var row = 0; row < 2; row++) {
                for (var col = 0; col < 2; col++) {
                    if (char.ToUpperInvariant(pattern[row * 2 + col]) == color)
                        return (row, col);
                }
            }
            return (0, 0); // fallback
        }

        private static ushort InterpolateGreen(ushort[] data, int width, int height, int x, int y) {
            // G at R/B: average the 4 neighboring G pixels (N, S, E, W)
            var count = 0;
            var sum = 0;
            if (y > 0) { sum += data[(y - 1) * width + x]; count++; }
            if (y < height - 1) { sum += data[(y + 1) * width + x]; count++; }
            if (x > 0) { sum += data[y * width + (x - 1)]; count++; }
            if (x < width - 1) { sum += data[y * width + (x + 1)]; count++; }
            return count > 0 ? (ushort)(sum / count) : data[y * width + x];
        }

        private static ushort InterpolateBAtR(ushort[] data, int width, int height, int x, int y) {
            // B at R: average the 4 diagonal B pixels
            var count = 0;
            var sum = 0;
            if (x > 0 && y > 0) { sum += data[(y - 1) * width + (x - 1)]; count++; }
            if (x < width - 1 && y > 0) { sum += data[(y - 1) * width + (x + 1)]; count++; }
            if (x > 0 && y < height - 1) { sum += data[(y + 1) * width + (x - 1)]; count++; }
            if (x < width - 1 && y < height - 1) { sum += data[(y + 1) * width + (x + 1)]; count++; }
            return count > 0 ? (ushort)(sum / count) : data[y * width + x];
        }

        private static ushort InterpolateRAtB(ushort[] data, int width, int height, int x, int y) {
            // R at B: average the 4 diagonal R pixels (same as B at R)
            return InterpolateBAtR(data, width, height, x, y);
        }

        private static ushort InterpolateRAtG(ushort[] data, int width, int height, int x, int y, int phaseX, int phaseY) {
            // R at G depends on which G in the 2x2 block
            // If G is at (0,1) in 2x2 (R is at 0,0 and B is at 1,1):
            //   R neighbors are vertical: (y-1,x) and (y+1,x)
            // If G is at (1,0) in 2x2 (R is at 0,0 and B is at 1,1):
            //   R neighbors are horizontal: (y,x-1) and (y,x+1)
            var count = 0;
            var sum = 0;

            // G at (0,1) means phaseY=0, phaseX=1 → R is at (0,0), so R neighbors are horizontal
            // G at (1,0) means phaseY=1, phaseX=0 → R is at (0,0), so R neighbors are vertical
            if (phaseY == 0 && phaseX == 1) {
                // Horizontal neighbors
                if (x > 0) { sum += data[y * width + (x - 1)]; count++; }
                if (x < width - 1) { sum += data[y * width + (x + 1)]; count++; }
            } else {
                // Vertical neighbors
                if (y > 0) { sum += data[(y - 1) * width + x]; count++; }
                if (y < height - 1) { sum += data[(y + 1) * width + x]; count++; }
            }

            return count > 0 ? (ushort)(sum / count) : data[y * width + x];
        }

        private static ushort InterpolateBAtG(ushort[] data, int width, int height, int x, int y, int phaseX, int phaseY) {
            // B at G depends on which G in the 2x2 block
            // G at (0,1) means phaseY=0, phaseX=1 → B is at (1,1), so B neighbors are vertical
            // G at (1,0) means phaseY=1, phaseX=0 → B is at (1,1), so B neighbors are horizontal
            if (phaseY == 0 && phaseX == 1) {
                // Vertical neighbors
                var count = 0;
                var sum = 0;
                if (y > 0) { sum += data[(y - 1) * width + x]; count++; }
                if (y < height - 1) { sum += data[(y + 1) * width + x]; count++; }
                return count > 0 ? (ushort)(sum / count) : data[y * width + x];
            } else {
                // Horizontal neighbors
                var count = 0;
                var sum = 0;
                if (x > 0) { sum += data[y * width + (x - 1)]; count++; }
                if (x < width - 1) { sum += data[y * width + (x + 1)]; count++; }
                return count > 0 ? (ushort)(sum / count) : data[y * width + x];
            }
        }

        /// <summary>
        /// Computes percentile-based clipping bounds. Uses a histogram to find
        /// the value at the <paramref name="lowPct"/> and <paramref name="highPct"/> percentiles.
        /// </summary>
        private static (ushort low, ushort high) ComputePercentileClipping(
            ushort[] data,
            double lowPct = ClipLowPercentile,
            double highPct = ClipHighPercentile) {

            // Build histogram (16-bit → 65536 bins)
            var hist = new int[ushort.MaxValue + 1];
            foreach (var v in data) {
                hist[v]++;
            }

            var total = data.Length;
            var lowCount = (int)(total * lowPct);
            var highCount = (int)(total * highPct);

            var accum = 0;
            ushort lowVal = 0;
            for (var i = 0; i < hist.Length; i++) {
                accum += hist[i];
                if (accum >= lowCount) { lowVal = (ushort)i; break; }
            }

            accum = 0;
            ushort highVal = ushort.MaxValue;
            for (var i = 0; i < hist.Length; i++) {
                accum += hist[i];
                if (accum >= highCount) { highVal = (ushort)i; break; }
            }

            // Ensure range is non-empty
            if (highVal <= lowVal)
                highVal = (ushort)Math.Min(lowVal + 1, ushort.MaxValue);

            return (lowVal, highVal);
        }

        /// <summary>
        /// Builds a precomputed MTF (Midtone Transfer Function) lookup table for all possible 16-bit input values.
        /// Maps [low, high] → [0, 255] using the MTF curve, clipped to the percentile range.
        /// </summary>
        private static byte[] BuildMtfLookup(ushort low, ushort high, double midtone) {
            var lookup = new byte[ushort.MaxValue + 1];
            var range = high - low;

            if (range <= 0) {
                // Degenerate case: all pixels same value
                for (var i = 0; i < lookup.Length; i++)
                    lookup[i] = (byte)(i >= high ? 255 : 0);
                return lookup;
            }

            for (var i = 0; i < lookup.Length; i++) {
                if (i <= low) {
                    lookup[i] = 0;
                } else if (i >= high) {
                    lookup[i] = 255;
                } else {
                    var normalized = (double)(i - low) / range; // 0..1
                    var mtf = MidtoneTransferFunction(normalized, midtone);
                    lookup[i] = (byte)Math.Round(Math.Clamp(mtf * 255.0, 0, 255));
                }
            }

            return lookup;
        }

        /// <summary>
        /// Midtone Transfer Function (MTF) stretch.
        /// Formula: ((m - 1) * x) / ((2m - 1) * x - m)
        /// where m = midtone point (0..1, lower = darker midtones preserved).
        /// </summary>
        private static double MidtoneTransferFunction(double x, double m) {
            if (m <= 0 || m >= 1)
                return x; // linear fallback
            // MTF: ((m-1)*x) / ((2m-1)*x - m)
            var denom = (2.0 * m - 1.0) * x - m;
            if (Math.Abs(denom) < 1e-10)
                return x;
            return ((m - 1.0) * x) / denom;
        }
    }
}
