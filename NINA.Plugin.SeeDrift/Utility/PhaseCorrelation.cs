using System;
using System.Numerics;
using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using MathNet.Numerics.LinearAlgebra;

namespace NINA.Plugin.SeeDrift.Utility {

    /// <summary>
    /// Translation estimate via normalized phase correlation (similar to skimage registration.phase_cross_correlation, integer shift).
    /// </summary>
    internal static class PhaseCorrelation {

        /// <summary>
        /// Shift to apply in pixel space: positive X = moving shifted right vs reference; positive Y = moving shifted down (row axis).
        /// </summary>
        public static bool TryEstimateShift(double[,] reference, double[,] moving, out double shiftY, out double shiftX) {
            shiftY = 0;
            shiftX = 0;

            var r0 = reference.GetLength(0);
            var c0 = reference.GetLength(1);
            var r1 = moving.GetLength(0);
            var c1 = moving.GetLength(1);
            var rows = Math.Min(r0, r1);
            var cols = Math.Min(c0, c1);
            if (rows < 16 || cols < 16)
                return false;

            double[,] refS = reference;
            double[,] movS = moving;
            if (r0 != rows || c0 != cols)
                refS = CropCenter(reference, rows, cols);
            if (r1 != rows || c1 != cols)
                movS = CropCenter(moving, rows, cols);

            var meanA = Mean(refS);
            var meanB = Mean(movS);

            var Fa = Matrix<Complex>.Build.Dense(rows, cols);
            var Fb = Matrix<Complex>.Build.Dense(rows, cols);
            for (var i = 0; i < rows; i++) {
                for (var j = 0; j < cols; j++) {
                    Fa[i, j] = new Complex(refS[i, j] - meanA, 0);
                    Fb[i, j] = new Complex(movS[i, j] - meanB, 0);
                }
            }

            Fourier.Forward2D(Fa, FourierOptions.Matlab);
            Fourier.Forward2D(Fb, FourierOptions.Matlab);

            var cross = Matrix<Complex>.Build.Dense(rows, cols);
            for (var i = 0; i < rows; i++) {
                for (var j = 0; j < cols; j++) {
                    var prod = Fa[i, j] * Complex.Conjugate(Fb[i, j]);
                    var m = prod.Magnitude;
                    cross[i, j] = m > 1e-30 ? prod / m : Complex.Zero;
                }
            }

            Fourier.Inverse2D(cross, FourierOptions.Matlab);

            var corr = new double[rows, cols];
            for (var i = 0; i < rows; i++)
                for (var j = 0; j < cols; j++)
                    corr[i, j] = cross[i, j].Real;

            corr = FftShift2DClone(corr);

            var max = double.MinValue;
            var pi = 0;
            var pj = 0;
            for (var i = 0; i < rows; i++) {
                for (var j = 0; j < cols; j++) {
                    if (corr[i, j] > max) {
                        max = corr[i, j];
                        pi = i;
                        pj = j;
                    }
                }
            }

            shiftY = pi - (rows - 1) / 2.0;
            shiftX = pj - (cols - 1) / 2.0;
            return true;
        }

        private static double Mean(double[,] a) {
            double s = 0;
            var r = a.GetLength(0);
            var c = a.GetLength(1);
            var n = r * c;
            for (var i = 0; i < r; i++)
                for (var j = 0; j < c; j++)
                    s += a[i, j];
            return n > 0 ? s / n : 0;
        }

        private static double[,] CropCenter(double[,] img, int rows, int cols) {
            var rMax = img.GetLength(0);
            var cMax = img.GetLength(1);
            var ro = (rMax - rows) / 2;
            var co = (cMax - cols) / 2;
            var o = new double[rows, cols];
            for (var i = 0; i < rows; i++)
                for (var j = 0; j < cols; j++)
                    o[i, j] = img[ro + i, co + j];
            return o;
        }

        private static double[,] FftShift2DClone(double[,] input) {
            var r = input.GetLength(0);
            var c = input.GetLength(1);
            var o = new double[r, c];
            var rh = r / 2;
            var ch = c / 2;
            for (var i = 0; i < r; i++) {
                for (var j = 0; j < c; j++)
                    o[(i + rh) % r, (j + ch) % c] = input[i, j];
            }
            return o;
        }
    }
}
