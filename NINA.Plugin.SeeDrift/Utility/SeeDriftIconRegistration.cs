using System.Windows;
using System.Windows.Media;

namespace NINA.Plugin.SeeDrift.Utility {

    /// <summary>
    /// Puts <c>SeeDrift_Icon</c> into <see cref="Application.Current"/>'s resource dictionary so sequencer items
    /// (palette + dropped blocks) resolve the same geometry SeeDark/SeeDew get from app-wide resources.
    /// </summary>
    internal static class SeeDriftIconRegistration {

        private static bool _registered;

        internal static void Register() {
            if (_registered)
                return;

            Application? app;
            try {
                app = Application.Current;
            } catch {
                return;
            }

            if (app == null)
                return;

            lock (typeof(SeeDriftIconRegistration)) {
                if (_registered)
                    return;
                if (app.Resources.Contains("SeeDrift_Icon")) {
                    _registered = true;
                    return;
                }

                try {
                    var rd = new Resources();
                    if (rd["SeeDrift_Icon"] is GeometryGroup src) {
                        var copy = (GeometryGroup)src.Clone();
                        copy.Freeze();
                        app.Resources["SeeDrift_Icon"] = copy;
                        _registered = true;
                        return;
                    }
                } catch {
                    // Fall through to inline geometry
                }

                app.Resources["SeeDrift_Icon"] = BuildFallbackIcon();
                _registered = true;
            }
        }

        private static GeometryGroup BuildFallbackIcon() {
            var gg = new GeometryGroup { FillRule = FillRule.Nonzero };
            gg.Children.Add(PathGeometry.Parse("M3,12 L21,12 M12,3 L12,21 M6,6 L18,18 M18,6 L6,18"));
            gg.Freeze();
            return gg;
        }
    }
}
