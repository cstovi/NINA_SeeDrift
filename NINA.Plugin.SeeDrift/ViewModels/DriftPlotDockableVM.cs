using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Plugin.SeeDrift.Services;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.ViewModel;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace NINA.Plugin.SeeDrift.ViewModels {

    [Export(typeof(IDockableVM))]
    public class DriftPlotDockableVM : DockableVM {
        private readonly DriftTrackingService _tracker;
        private readonly SeeDriftPlugin _plugin;

        [ImportingConstructor]
        public DriftPlotDockableVM(IProfileService profileService, SeeDriftPlugin plugin)
            : base(profileService) {
            _plugin = plugin;
            _tracker = plugin.DriftTracker;

            Title = "SeeDrift";

            PlotModel = BuildEmptyModel();
            _tracker.Samples.CollectionChanged += (_, _) =>
                Application.Current?.Dispatcher.Invoke(RefreshPlot);

            ResetCommand = new RelayCommand(_ => _tracker.ResetSession());
            ExportHtmlCommand = new RelayCommand(_ => ExportHtml());

            RefreshPlot();
        }

        public ICommand ResetCommand { get; }
        public ICommand ExportHtmlCommand { get; }

        private PlotModel _plotModel = null!;
        public PlotModel PlotModel {
            get => _plotModel;
            set { _plotModel = value; RaisePropertyChanged(); }
        }

        private void RefreshPlot() {
            var model = BuildEmptyModel();
            var raSeries = new LineSeries {
                Title = "ΔRA (″)",
                Color = OxyColors.DeepSkyBlue,
                StrokeThickness = 1.5,
                MarkerType = MarkerType.Circle,
                MarkerSize = 3,
                MarkerFill = OxyColors.DeepSkyBlue
            };
            var decSeries = new LineSeries {
                Title = "ΔDec (″)",
                Color = OxyColors.LightPink,
                StrokeThickness = 1.5,
                MarkerType = MarkerType.Circle,
                MarkerSize = 3,
                MarkerFill = OxyColors.LightPink
            };

            foreach (var s in _tracker.Samples) {
                raSeries.Points.Add(new DataPoint(s.FrameIndex, s.DeltaRaArcSec));
                decSeries.Points.Add(new DataPoint(s.FrameIndex, s.DeltaDecArcSec));
            }

            model.Series.Add(raSeries);
            model.Series.Add(decSeries);
            PlotModel = model;
            RaisePropertyChanged(nameof(SampleCount));
            RaisePropertyChanged(nameof(LastSummary));
        }

        private static PlotModel BuildEmptyModel() {
            var m = new PlotModel {
                Title = "Drift vs frame # (arcsec from first accepted frame)",
                Background = OxyColors.Transparent,
                TextColor = OxyColors.LightGray,
                PlotAreaBorderColor = OxyColors.Gray
            };
            m.Axes.Add(new LinearAxis {
                Position = AxisPosition.Bottom,
                Title = "Frame index",
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColor.FromAColor(80, OxyColors.Gray),
                MinorGridlineStyle = LineStyle.Dot,
                MinorGridlineColor = OxyColor.FromAColor(40, OxyColors.Gray)
            });
            m.Axes.Add(new LinearAxis {
                Position = AxisPosition.Left,
                Title = "Δ arcsec",
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColor.FromAColor(80, OxyColors.Gray)
            });
            m.IsLegendVisible = true;
            return m;
        }

        public int SampleCount => _tracker.Samples.Count;

        public string LastSummary {
            get {
                if (_tracker.Samples.Count == 0)
                    return "No frames yet.";
                var last = _tracker.Samples[^1];
                return $"Last: ΔRA {last.DeltaRaArcSec:+0.0;-0.0;0}″ · ΔDec {last.DeltaDecArcSec:+0.0;-0.0;0}″ · {last.TargetName}";
            }
        }

        private void ExportHtml() {
            if (_tracker.Samples.Count == 0)
                return;
            try {
                var folder = _plugin.HtmlExportFolder;
                if (string.IsNullOrWhiteSpace(folder))
                    folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                var dlg = new SaveFileDialog {
                    Title = "Export SeeDrift HTML",
                    Filter = "HTML|*.html",
                    FileName = $"SeeDrift_{DateTime.Now:yyyyMMdd_HHmmss}.html",
                    InitialDirectory = Directory.Exists(folder) ? folder : Environment.CurrentDirectory
                };
                if (dlg.ShowDialog() != true)
                    return;

                HtmlReportExporter.WriteReport(dlg.FileName, _tracker.Samples.ToList(), "SeeDrift — drift trace");
                _plugin.HtmlExportFolder = Path.GetDirectoryName(dlg.FileName) ?? folder;
                _plugin.SyncSettingsFromProperties();
            } catch (Exception ex) {
                Logger.Error($"SeeDrift HTML export failed: {ex.Message}");
            }
        }
    }
}
