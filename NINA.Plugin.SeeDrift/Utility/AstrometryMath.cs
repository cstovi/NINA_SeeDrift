using System;

namespace NINA.Plugin.SeeDrift.Utility {

    internal static class AstrometryMath {

        /// <summary>
        /// Offset from (refRaHours, refDecDeg) to (raHours, decDeg). RA difference uses cosine(Dec) for small-field approximation.
        /// </summary>
        public static void DeltaArcSec(double refRaHours, double refDecDeg, double raHours, double decDeg,
            out double deltaRaArcSec, out double deltaDecArcSec) {
            var dRaHours = NormalizeRaHoursDifference(raHours - refRaHours);
            var decMidRad = (refDecDeg + decDeg) * 0.5 * (Math.PI / 180.0);
            deltaRaArcSec = dRaHours * 15.0 * 3600.0 * Math.Cos(decMidRad);
            deltaDecArcSec = (decDeg - refDecDeg) * 3600.0;
        }

        /// <summary>Normalize ΔRA in hours to roughly [-12, 12] via reference wrapping.</summary>
        private static double NormalizeRaHoursDifference(double dHours) {
            while (dHours > 12.0) dHours -= 24.0;
            while (dHours < -12.0) dHours += 24.0;
            return dHours;
        }

        /// <summary>
        /// Parallactic angle in radians: the angle between the zenith direction and
        /// celestial North at the target's position. Used for Alt/Az mounts only.
        /// Uses CENTAZ to derive sin(HA), avoiding the arccos sign ambiguity.
        /// Assumes azimuth measured North-through-East (standard FITS convention).
        /// </summary>
        public static double ParallacticAngle(double latDeg, double altDeg, double azDeg, double decDeg) {
            var lat  = latDeg  * D2R;
            var alt  = altDeg  * D2R;
            var az   = azDeg   * D2R;
            var dec  = decDeg  * D2R;

            var cosHA = (Math.Sin(alt) - Math.Sin(dec) * Math.Sin(lat))
                        / (Math.Cos(dec) * Math.Cos(lat) + 1e-15);
            var sinHA = -Math.Sin(az) * Math.Cos(alt) / (Math.Cos(dec) + 1e-15);

            return Math.Atan2(sinHA, Math.Cos(dec) * Math.Tan(lat) - Math.Sin(dec) * cosHA);
        }

        private const double D2R = Math.PI / 180.0;

        /// <summary>
        /// Converts cumulative pixel shift (dx, dy) to ΔRA/ΔDec arcseconds.
        ///
        /// EQ mode  — sensor ≈ North up; pure scale, no rotation:
        ///   ΔDec = -dy × scale           (FITS Y-down → negate)
        ///   ΔRA  = -dx × scale / cos(dec)
        ///
        /// Alt/Az mode — apply parallactic angle rotation first (Codex-reviewed matrix):
        ///   east  = scale × (−dx·cos q − dy·sin q)
        ///   ΔDec  = scale × ( dx·sin q − dy·cos q)
        ///   ΔRA   = east / cos(dec)
        /// </summary>
        public static void PixelShiftToRaDec(
            double dx, double dy,
            double plateScaleArcSecPerPx,
            double decDeg,
            bool isEq,
            double parallacticAngleRad,
            out double deltaRaArcSec,
            out double deltaDecArcSec) {

            var dec    = decDeg * D2R;
            var cosDec = Math.Cos(dec);
            if (Math.Abs(cosDec) < 1e-6) cosDec = 1e-6; // guard near pole

            var s = plateScaleArcSecPerPx;

            if (isEq) {
                deltaDecArcSec = -dy * s;
                deltaRaArcSec  =  dx * s / cosDec;
            } else {
                var q    = parallacticAngleRad;
                var cosQ = Math.Cos(q);
                var sinQ = Math.Sin(q);
                var east = s * (dx * cosQ - dy * sinQ);
                deltaDecArcSec = s * (dx * sinQ - dy * cosQ);
                deltaRaArcSec  = east / cosDec;
            }
        }
    }
}
