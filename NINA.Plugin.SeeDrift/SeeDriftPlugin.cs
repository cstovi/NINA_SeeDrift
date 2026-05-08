using System;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Plugin.SeeDrift.Models;
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
            DriftTracker = new DriftTrackingService(this, ImageSaveMediator);

            _isInitializing = true;
            HtmlExportFolder = Settings.HtmlExportFolder;
            FolderImportPlotMode = Settings.FolderImportPlotMode;
            RegistrationCropSize = Settings.RegistrationCropSize;
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
                Settings.FolderImportPlotMode = _folderImportPlotMode;
                Settings.RegistrationCropSize = _registrationCropSize;
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

        private FolderPlotMode _folderImportPlotMode = FolderPlotMode.FitsHeaderCoordinates;
        public FolderPlotMode FolderImportPlotMode {
            get => _folderImportPlotMode;
            set {
                if (_folderImportPlotMode == value) return;
                _folderImportPlotMode = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(FolderImportUsesPixelRegistration));
                SyncSettingsFromProperties();
            }
        }

        /// <summary>UI helper for folder import mode (<see cref="FolderPlotMode"/>).</summary>
        public bool FolderImportUsesPixelRegistration {
            get => FolderImportPlotMode == FolderPlotMode.PixelRegistration;
            set => FolderImportPlotMode = value ? FolderPlotMode.PixelRegistration : FolderPlotMode.FitsHeaderCoordinates;
        }

        private int _registrationCropSize = 800;
        public int RegistrationCropSize {
            get => _registrationCropSize;
            set {
                var v = value;
                if (v < 64) v = 64;
                if (v > 4096) v = 4096;
                _registrationCropSize = v;
                RaisePropertyChanged();
                SyncSettingsFromProperties();
            }
        }
    }
}
