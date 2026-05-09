using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace NINA.Plugin.SeeDrift.Utility {

    /// <summary>
    /// Puts <c>SeeDrift_Icon</c> into <see cref="Application.Current"/>'s resource dictionary so sequencer items
    /// show the same geometry in the instruction palette list and on each dropped block (same pattern as SeeDew).
    /// </summary>
    internal static class SeeDriftIconRegistration {

        private static bool _registered;
        private static bool _deferPending;
        private static int _deferAttempts;
        private const int MaxDeferAttempts = 30;

        internal static void Register() {
            if (_registered)
                return;

            Application? app;
            try {
                app = Application.Current;
            } catch {
                ScheduleDeferredRegister();
                return;
            }

            if (app == null) {
                ScheduleDeferredRegister();
                return;
            }

            lock (typeof(SeeDriftIconRegistration)) {
                if (_registered)
                    return;
                if (app.Resources.Contains("SeeDrift_Icon")) {
                    _registered = true;
                    _deferAttempts = 0;
                    return;
                }

                try {
                    var rd = new Resources();
                    if (rd["SeeDrift_Icon"] is GeometryGroup src) {
                        var copy = (GeometryGroup)src.Clone();
                        copy.Freeze();
                        app.Resources["SeeDrift_Icon"] = copy;
                        _registered = true;
                        _deferAttempts = 0;
                        return;
                    }
                } catch {
                    // Fall through to inline geometry
                }

                app.Resources["SeeDrift_Icon"] = BuildFallbackIcon();
                _registered = true;
                _deferAttempts = 0;
            }
        }

        /// <summary>
        /// Host may compose plugins before <see cref="Application.Current"/> exists; retry once on the UI idle queue.
        /// </summary>
        private static void ScheduleDeferredRegister() {
            if (_registered || _deferPending || _deferAttempts >= MaxDeferAttempts)
                return;
            _deferPending = true;
            try {
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => {
                    _deferPending = false;
                    _deferAttempts++;
                    Register();
                });
            } catch {
                _deferPending = false;
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
