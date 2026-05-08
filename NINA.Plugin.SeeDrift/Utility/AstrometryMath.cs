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
    }
}
