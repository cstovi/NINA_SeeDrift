using System;
using System.Globalization;
namespace NINA.Plugin.SeeDrift.Utility {

    internal static class RunDurationFormatter {

        /// <summary>Short wall-clock span for status lines and HTML (e.g. <c>3m 12s</c>, <c>45.2s</c>).</summary>
        internal static string ToReadable(TimeSpan elapsed) {
            if (elapsed < TimeSpan.Zero)
                elapsed = TimeSpan.Zero;

            if (elapsed.TotalHours >= 1)
                return string.Format(CultureInfo.InvariantCulture, "{0}h {1}m {2}s",
                    (int)elapsed.TotalHours, elapsed.Minutes, elapsed.Seconds);

            if (elapsed.TotalMinutes >= 1)
                return string.Format(CultureInfo.InvariantCulture, "{0}m {1}s",
                    (int)elapsed.TotalMinutes, elapsed.Seconds);

            if (elapsed.TotalSeconds >= 1)
                return string.Format(CultureInfo.InvariantCulture, "{0:0.#}s", elapsed.TotalSeconds);

            return string.Format(CultureInfo.InvariantCulture, "{0:0}ms", elapsed.TotalMilliseconds);
        }
    }
}
