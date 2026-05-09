using System.Windows;
using System.Windows.Threading;

namespace NINA.Plugin.SeeDrift.Utility {

    /// <summary>
    /// Puts <see cref="SeeDriftIcons.InstructionIcon"/> into <see cref="Application.Current"/> as <c>SeeDrift_Icon</c>
    /// for hosts that resolve <see cref="System.ComponentModel.Composition.ExportMetadataAttribute"/> icon keys.
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

                app.Resources["SeeDrift_Icon"] = SeeDriftIcons.InstructionIcon;
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

    }
}
