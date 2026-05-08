using System;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Plugin.SeeDrift.Services;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;

namespace NINA.Plugin.SeeDrift {

    [Export(typeof(IPluginManifest))]
    [Export]
    public class SeeDriftPlugin : PluginBase, IPluginManifest, INotifyPropertyChanged {
        private bool _isInitializing;
        private bool _isSyncing;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public IImageSaveMediator ImageSaveMediator { get; }
        public IProfileService ProfileService { get; }

        public SeeDriftSettings Settings { get; }
        public DriftTrackingService DriftTracker { get; }

        [ImportingConstructor]
        public SeeDriftPlugin(
            IImageSaveMediator imageSaveMediator,
            IProfileService profileService) {
            ImageSaveMediator = imageSaveMediator;
            ProfileService = profileService;

            Settings = SeeDriftSettings.Load();
            DriftTracker = new DriftTrackingService(ImageSaveMediator);

            _isInitializing = true;
            HtmlExportFolder = Settings.HtmlExportFolder;
            _isInitializing = false;
        }

        public override Task Teardown() {
            DriftTracker.Dispose();
            return base.Teardown();
        }

        public void SyncSettingsFromProperties() {
            if (_isInitializing || _isSyncing) return;
            _isSyncing = true;
            try {
                Settings.HtmlExportFolder = _htmlExportFolder;
                Settings.Save();
            } finally {
                _isSyncing = false;
            }
        }

        private string _htmlExportFolder = "";
        public string HtmlExportFolder {
            get => _htmlExportFolder;
            set { _htmlExportFolder = value; RaisePropertyChanged(); SyncSettingsFromProperties(); }
        }
    }
}
