using System;

namespace NINA.Plugin.SeeDrift.Utility {

    /// <summary>
    /// Translation estimate via coarse-to-fine sum-of-squared-differences template search
    /// with parabolic sub-pixel refinement — not FFT phase correlation (despite the type name).
    /// Returns float shifts so cumulative sums remain distinct even when
    /// per-frame motion is sub-pixel.
    /// Positive shiftY = moving image shifted DOWN; positive shiftX = shifted RIGHT.
    /// </summary>
    internal static class PhaseCorrelation {

        private const int CoarseStep    = 4;
        private const int CoarseRadius  = 36;   // ±144 original pixels
        private const int CoarseTplHalf = 20;   // 41×41 template in downsampled space

        private const int FineRadius    = 4;
        private const int FineTplHalf   = 48;   // 97×97 template at full resolution

        public static bool TryEstimateShift(
            double[,] reference, double[,] moving,
            out double shiftY, out double shiftX) {

            shiftY = 0;
            shiftX = 0;
            try {
                var rows = reference.GetLength(0);
                var cols = reference.GetLength(1);
                if (rows != moving.GetLength(0) || cols != moving.GetLength(1)) return false;
                if (rows < 64 || cols < 64) return false;

                // Coarse pass on downsampled images.
                var refD = Downsample(reference, CoarseStep);
                var movD = Downsample(moving,    CoarseStep);
                FindBestSSD(refD, movD, 0, 0, CoarseRadius, CoarseTplHalf,
                    out var cDy, out var cDx);

                // Fine pass at full resolution around the coarse hit.
                int fullDy = cDy * CoarseStep;
                int fullDx = cDx * CoarseStep;
                FindBestSSD(reference, moving, fullDy, fullDx, FineRadius, FineTplHalf,
                    out var fDy, out var fDx);

                // Sub-pixel refinement: fit a parabola through the SSD values at
                // (bestDy-1, bestDx), (bestDy, bestDx), (bestDy+1, bestDx) for Y
                // and similarly for X, then find the parabolic minimum.
                var cy = rows / 2;
                var cx = cols / 2;

                var ssdYm = Ssd(reference, moving, cy, cx, FineTplHalf, fDy - 1, fDx, rows, cols);
                var ssdY0 = Ssd(reference, moving, cy, cx, FineTplHalf, fDy,     fDx, rows, cols);
                var ssdYp = Ssd(reference, moving, cy, cx, FineTplHalf, fDy + 1, fDx, rows, cols);

                var ssdXm = Ssd(reference, moving, cy, cx, FineTplHalf, fDy, fDx - 1, rows, cols);
                var ssdXp = Ssd(reference, moving, cy, cx, FineTplHalf, fDy, fDx + 1, rows, cols);

                shiftY = fDy + ParabolicPeak(ssdYm, ssdY0, ssdYp);
                shiftX = fDx + ParabolicPeak(ssdXm, ssdY0, ssdXp);
                return true;
            } catch {
                return false;
            }
        }

        /// <summary>
        /// Given SSD values at offset -1, 0, +1, returns the sub-pixel offset of the
        /// parabolic minimum relative to 0.  Clamps to ±0.5 so we never move more
        /// than half a pixel from the integer minimum.
        /// </summary>
        private static double ParabolicPeak(double sm, double s0, double sp) {
            var denom = sm - 2.0 * s0 + sp;   // 2a
            if (Math.Abs(denom) < 1e-12) return 0;
            // If any neighbour is MaxValue (out-of-bounds) skip refinement.
            if (sm >= double.MaxValue / 2 || sp >= double.MaxValue / 2) return 0;
            var offset = -(sp - sm) / (2.0 * denom);
            return Math.Max(-0.5, Math.Min(0.5, offset));
        }

        private static void FindBestSSD(
            double[,] refImg, double[,] movImg,
            int baseDy, int baseDx, int radius, int tplHalf,
            out int bestDy, out int bestDx) {

            var rows = refImg.GetLength(0);
            var cols = refImg.GetLength(1);
            var cy   = rows / 2;
            var cx   = cols / 2;

            double bestSsd = double.MaxValue;
            bestDy = baseDy;
            bestDx = baseDx;

            for (var dy = baseDy - radius; dy <= baseDy + radius; dy++) {
                for (var dx = baseDx - radius; dx <= baseDx + radius; dx++) {
                    var ssd = Ssd(refImg, movImg, cy, cx, tplHalf, dy, dx, rows, cols);
                    if (ssd < bestSsd) {
                        bestSsd = ssd;
                        bestDy  = dy;
                        bestDx  = dx;
                    }
                }
            }
        }

        private static double Ssd(
            double[,] refImg, double[,] movImg,
            int cy, int cx, int half,
            int dy, int dx,
            int rows, int cols) {

            double sum = 0;
            for (var ry = -half; ry <= half; ry++) {
                var rRow = cy + ry;
                var mRow = cy + ry + dy;
                if ((uint)rRow >= (uint)rows || (uint)mRow >= (uint)rows)
                    return double.MaxValue;
                for (var rx = -half; rx <= half; rx++) {
                    var rCol = cx + rx;
                    var mCol = cx + rx + dx;
                    if ((uint)rCol >= (uint)cols || (uint)mCol >= (uint)cols)
                        return double.MaxValue;
                    var d = refImg[rRow, rCol] - movImg[mRow, mCol];
                    sum += d * d;
                }
            }
            return sum;
        }

        private static double[,] Downsample(double[,] src, int factor) {
            var rows = src.GetLength(0) / factor;
            var cols = src.GetLength(1) / factor;
            var dst  = new double[rows, cols];
            var inv  = 1.0 / (factor * factor);
            for (var r = 0; r < rows; r++) {
                for (var c = 0; c < cols; c++) {
                    double s = 0;
                    for (var dr = 0; dr < factor; dr++)
                        for (var dc = 0; dc < factor; dc++)
                            s += src[r * factor + dr, c * factor + dc];
                    dst[r, c] = s * inv;
                }
            }
            return dst;
        }
    }
}
