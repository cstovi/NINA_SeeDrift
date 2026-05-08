using System;

namespace NINA.Plugin.SeeDrift.Utility {

    /// <summary>
    /// Translation estimate via a coarse-to-fine Sum of Squared Differences search.
    /// Same call signature as the previous phase-correlation implementation.
    /// No external dependencies required.
    /// Positive shiftY = moving image shifted DOWN (more rows); positive shiftX = shifted RIGHT.
    /// </summary>
    internal static class PhaseCorrelation {

        // Coarse pass: downsample factor and search radius (in downsampled pixels).
        private const int CoarseStep   = 4;
        private const int CoarseRadius = 36;   // = ±144 original pixels
        private const int CoarseTplHalf = 20;  // template 41×41 in downsampled space

        // Fine pass: search radius at full resolution around the coarse hit.
        private const int FineRadius   = 4;
        private const int FineTplHalf  = 48;   // template 97×97 at full resolution

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

                // Coarse: downsample both images, search within ±CoarseRadius
                var refD  = Downsample(reference, CoarseStep);
                var movD  = Downsample(moving,    CoarseStep);
                FindBestSSD(refD, movD, 0, 0, CoarseRadius, CoarseTplHalf,
                    out var cDy, out var cDx);

                // Back to full-resolution coordinates
                int fullDy = cDy * CoarseStep;
                int fullDx = cDx * CoarseStep;

                // Fine: search ±FineRadius around coarse result at full resolution
                FindBestSSD(reference, moving, fullDy, fullDx, FineRadius, FineTplHalf,
                    out var fDy, out var fDx);

                shiftY = fDy;
                shiftX = fDx;
                return true;
            } catch {
                return false;
            }
        }

        // -------------------------------------------------------------------
        // Core SSD search: finds the integer (dy, dx) within [baseDy±radius,
        // baseDx±radius] that minimises SSD between the centre template of
        // 'ref' and the corresponding patch in 'mov'.
        // -------------------------------------------------------------------
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
