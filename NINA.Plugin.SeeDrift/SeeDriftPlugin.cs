using System;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using NINA.Equipment.Interfaces.Mediator;
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

        public ICameraMediator CameraMediator { get; }
        public IImageSaveMediator ImageSaveMediator { get; }
        public IProfileService ProfileService { get; }

        public SeeDriftSettings Settings { get; }
        public DriftTrackingService DriftTracker { get; }

        [ImportingConstructor]
        public SeeDriftPlugin(
            ICameraMediator cameraMediator,
            IImageSaveMediator imageSaveMediator,
            IProfileService profileService) {
            CameraMediator = cameraMediator;
            ImageSaveMediator = imageSaveMediator;
            ProfileService = profileService;

            Settings = SeeDriftSettings.Load();
            DriftTracker = new DriftTrackingService(this, ImageSaveMediator);

            _isInitializing = true;
            OnlySeestarCameras = Settings.OnlySeestarCameras;
            AutoResetOnTargetChange = Settings.AutoResetOnTargetChange;
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
                Settings.OnlySeestarCameras = _onlySeestarCameras;
                Settings.AutoResetOnTargetChange = _autoResetOnTargetChange;
                Settings.HtmlExportFolder = _htmlExportFolder;
                Settings.Save();
            } finally {
                _isSyncing = false;
            }
        }

        private bool _onlySeestarCameras = true;
        public bool OnlySeestarCameras {
            get => _onlySeestarCameras;
            set { _onlySeestarCameras = value; RaisePropertyChanged(); SyncSettingsFromProperties(); }
        }

        private bool _autoResetOnTargetChange = true;
        public bool AutoResetOnTargetChange {
            get => _autoResetOnTargetChange;
            set { _autoResetOnTargetChange = value; RaisePropertyChanged(); SyncSettingsFromProperties(); }
        }

        private string _htmlExportFolder = "";
        public string HtmlExportFolder {
            get => _htmlExportFolder;
            set { _htmlExportFolder = value; RaisePropertyChanged(); SyncSettingsFromProperties(); }
        }
    }
}
