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
            MountMode = Settings.MountMode;
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
                Settings.MountMode = _mountMode;
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
                RaisePropertyChanged(nameof(FolderImportUsesHeaderCoordinates));
                SyncSettingsFromProperties();
            }
        }

        /// <summary>UI helper — true when pixel registration mode is selected.</summary>
        public bool FolderImportUsesPixelRegistration {
            get => FolderImportPlotMode == FolderPlotMode.PixelRegistration;
            set => FolderImportPlotMode = value ? FolderPlotMode.PixelRegistration : FolderPlotMode.FitsHeaderCoordinates;
        }

        /// <summary>UI helper — true when FITS header coordinates mode is selected.</summary>
        public bool FolderImportUsesHeaderCoordinates {
            get => FolderImportPlotMode == FolderPlotMode.FitsHeaderCoordinates;
            set => FolderImportPlotMode = value ? FolderPlotMode.FitsHeaderCoordinates : FolderPlotMode.PixelRegistration;
        }

        private MountMode _mountMode = MountMode.EQ;
        public MountMode MountMode {
            get => _mountMode;
            set {
                if (_mountMode == value) return;
                _mountMode = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(MountModeIsEq));
                RaisePropertyChanged(nameof(MountModeIsAltAz));
                SyncSettingsFromProperties();
            }
        }

        /// <summary>UI helper — true when EQ mode is selected.</summary>
        public bool MountModeIsEq {
            get => MountMode == MountMode.EQ;
            set => MountMode = value ? MountMode.EQ : MountMode.AltAz;
        }

        /// <summary>UI helper — true when Alt/Az mode is selected.</summary>
        public bool MountModeIsAltAz {
            get => MountMode == MountMode.AltAz;
            set => MountMode = value ? MountMode.AltAz : MountMode.EQ;
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
