using System;
using System.Collections.Generic;
using System.Globalization;
using NINA.Astrometry;
using NINA.PlateSolving;

namespace NINA.Plugin.SeeDrift.Utility {

    internal static class PlateSolveHelper {

        public static bool TryResultToRaDecHours(PlateSolveResult result, out double raHours, out double decDeg) {
            raHours = 0;
            decDeg = 0;
            if (!result.Success || result.Coordinates == null)
                return false;
            var c = result.Coordinates;
            decDeg = c.Dec;
            raHours = Math.Abs(c.RADegrees) > double.Epsilon ? c.RADegrees / 15.0 : c.RA;
            return true;
        }

        /// <summary>Build solve parameters from FITS header hints plus profile defaults.</summary>
        public static PlateSolveParameter BuildPlateSolveParameter(Dictionary<string, string> cards) {
            var p = new PlateSolveParameter {
                BlindFailoverEnabled = true,
                DisableNotifications = true,
            };

            if (cards.TryGetValue("FOCALLEN", out var fl) &&
                double.TryParse(fl.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var focalMm) &&
                focalMm > 0)
                p.FocalLength = focalMm;

            if (cards.TryGetValue("XPIXSZ", out var xp) &&
                double.TryParse(xp.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var pixUm) &&
                pixUm > 0)
                p.PixelSize = pixUm;

            if (cards.TryGetValue("XBINNING", out var xb) &&
                int.TryParse(xb.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var bx) &&
                bx > 0)
                p.Binning = bx;

            if (FitsCoordinates.TryParsePointing(cards, out var raHours, out var decDeg, out _, out _))
                p.Coordinates = new Coordinates(raHours * 15.0, decDeg, Epoch.J2000, Coordinates.RAType.Degrees);

            return p;
        }
    }
}
