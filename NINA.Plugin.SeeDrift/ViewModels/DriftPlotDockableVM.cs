using System;
using System.ComponentModel.Composition;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Microsoft.Win32;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Plugin.SeeDrift.Models;
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
        private bool _warnedFlatTrace;

        [ImportingConstructor]
        public DriftPlotDockableVM(IProfileService profileService, SeeDriftPlugin plugin)
            : base(profileService) {
            _plugin = plugin;
            _tracker = plugin.DriftTracker;

            Title = "SeeDrift";

            PlotModel = BuildEmptyModel();
            _tracker.Samples.CollectionChanged += (_, _) =>
                System.Windows.Application.Current?.Dispatcher.Invoke(RefreshPlot);

            ResetCommand = new RelayCommand(_ => _tracker.ResetSession());
            ExportHtmlCommand = new RelayCommand(_ => ExportHtml());
            ImportFolderCommand = new RelayCommand(_ => ImportFitsFolder());

            RefreshPlot();
        }

        public ICommand ResetCommand { get; }
        public ICommand ExportHtmlCommand { get; }
        public ICommand ImportFolderCommand { get; }

        private PlotModel _plotModel = null!;
        public PlotModel PlotModel {
            get => _plotModel;
            set { _plotModel = value; RaisePropertyChanged(); }
        }

        private void RefreshPlot() {
            var model = BuildEmptyModel();
            var pathSeries = new LineSeries {
                Title = "Drift path (frame order)",
                Color = OxyColor.FromRgb(100, 200, 255),
                StrokeThickness = 1.75,
                MarkerType = MarkerType.Circle,
                MarkerSize = 3.5,
                MarkerFill = OxyColor.FromRgb(100, 200, 255),
                MarkerStroke = OxyColors.Transparent
            };

            foreach (var s in _tracker.Samples.OrderBy(x => x.FrameIndex))
                pathSeries.Points.Add(new DataPoint(s.DeltaRaArcSec, s.DeltaDecArcSec));

            ApplySquareArcSecAxes(model, _tracker.Samples);

            model.Series.Add(pathSeries);

            WarnIfFlatTrace();

            PlotModel = model;
            RaisePropertyChanged(nameof(SampleCount));
            RaisePropertyChanged(nameof(LastSummary));
        }

        /// <summary>
        /// Same scale on X and Y so arcsecond drift matches sky geometry (square plot).
        /// </summary>
        private static void ApplySquareArcSecAxes(PlotModel model,
            ObservableCollection<DriftSample> samples) {
            var xAxis = model.Axes.OfType<LinearAxis>().First(a => a.Position == AxisPosition.Bottom);
            var yAxis = model.Axes.OfType<LinearAxis>().First(a => a.Position == AxisPosition.Left);

            const double minHalfExtentArcSec = 0.35;
            const double padFactor = 1.12;

            if (samples.Count == 0) {
                xAxis.Minimum = -minHalfExtentArcSec;
                xAxis.Maximum = minHalfExtentArcSec;
                yAxis.Minimum = -minHalfExtentArcSec;
                yAxis.Maximum = minHalfExtentArcSec;
                return;
            }

            var maxAbsRa = samples.Max(s => Math.Abs(s.DeltaRaArcSec));
            var maxAbsDec = samples.Max(s => Math.Abs(s.DeltaDecArcSec));
            var half = Math.Max(Math.Max(maxAbsRa, maxAbsDec), minHalfExtentArcSec) * padFactor;

            xAxis.Minimum = -half;
            xAxis.Maximum = half;
            yAxis.Minimum = -half;
            yAxis.Maximum = half;
        }

        private void WarnIfFlatTrace() {
            var samples = _tracker.Samples;
            if (samples.Count == 0)
                _warnedFlatTrace = false;
            if (samples.Count < 3)
                return;
            var raRange = samples.Max(s => s.DeltaRaArcSec) - samples.Min(s => s.DeltaRaArcSec);
            var decRange = samples.Max(s => s.DeltaDecArcSec) - samples.Min(s => s.DeltaDecArcSec);
            var flat = raRange < 1e-9 && decRange < 1e-9;
            if (!flat) {
                _warnedFlatTrace = false;
                return;
            }
            if (_warnedFlatTrace)
                return;
            _warnedFlatTrace = true;
            Logger.Warning(
                "SeeDrift: all frames share identical pointing in FITS headers (often fixed CRVAL/OBJCT). " +
                "Prefer per-frame solved RA/DEC, or turn off Seestar-only if headers lack INSTRUME.");
        }

        private static PlotModel BuildEmptyModel() {
            var gridMajor = OxyColor.FromAColor(55, OxyColor.FromRgb(180, 180, 190));
            var gridMinor = OxyColor.FromAColor(35, OxyColor.FromRgb(140, 140, 155));
            var axisLine = OxyColor.FromRgb(95, 95, 105);

            var m = new PlotModel {
                Title = "Drift in focal plane (″ relative to first frame)",
                TitleColor = OxyColor.FromRgb(230, 230, 235),
                Background = OxyColor.FromRgb(26, 26, 30),
                TextColor = OxyColor.FromRgb(210, 210, 218),
                PlotAreaBorderColor = OxyColor.FromRgb(58, 58, 66)
            };
            m.Axes.Add(new LinearAxis {
                Position = AxisPosition.Bottom,
                Title = "ΔRA (″)",
                TitleColor = OxyColor.FromRgb(200, 210, 230),
                AxislineColor = axisLine,
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = gridMajor,
                MinorGridlineStyle = LineStyle.Dot,
                MinorGridlineColor = gridMinor,
                TicklineColor = axisLine
            });
            m.Axes.Add(new LinearAxis {
                Position = AxisPosition.Left,
                Title = "ΔDec (″)",
                TitleColor = OxyColor.FromRgb(200, 210, 230),
                AxislineColor = axisLine,
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = gridMajor,
                MinorGridlineStyle = LineStyle.Dot,
                MinorGridlineColor = gridMinor,
                TicklineColor = axisLine
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

        private void ImportFitsFolder() {
            try {
                using var dlg = new System.Windows.Forms.FolderBrowserDialog {
                    Description = "Select folder containing FITS lights (.fits / .fit / .fts)",
                    UseDescriptionForTitle = true
                };
                var initial = _plugin.HtmlExportFolder;
                if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial))
                    dlg.SelectedPath = initial;

                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dlg.SelectedPath))
                    return;

                _tracker.ImportFitsFolder(dlg.SelectedPath);
            } catch (Exception ex) {
                Logger.Error($"SeeDrift folder import failed: {ex.Message}");
            }
        }

        private void ExportHtml() {
            if (_tracker.Samples.Count == 0)
                return;
            try {
                var folder = _plugin.HtmlExportFolder;
                if (string.IsNullOrWhiteSpace(folder))
                    folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                var dlg = new Microsoft.Win32.SaveFileDialog {
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
