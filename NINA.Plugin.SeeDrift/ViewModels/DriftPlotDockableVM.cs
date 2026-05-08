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

            PlotModel = BuildEmptyModel(0, false);
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
            var samples = _tracker.Samples;
            var n = samples.Count;
            var pixelPlot = n > 0 && samples[0].IsPixelPath;
            var model = BuildEmptyModel(n, pixelPlot);
            var ordered = samples.OrderBy(x => x.FrameIndex).ToList();

            if (pixelPlot) {
                // Scatter plot (no connecting line) with colour gradient by frame order.
                // Earlier frames = cool blue; later frames = warm orange — same visual
                // language as Siril's registration plot.
                model.Axes.Add(new LinearColorAxis {
                    Key            = "frameOrder",
                    Palette        = OxyPalettes.Jet(256),
                    Minimum        = 0,
                    Maximum        = Math.Max(n - 1, 1),
                    IsAxisVisible  = false
                });

                var scatter = new ScatterSeries {
                    Title          = "Pixel shift per frame (colour = frame order, blue→red)",
                    ColorAxisKey   = "frameOrder",
                    MarkerType     = MarkerType.Circle,
                    MarkerSize     = n > 200 ? 2.5 : (n > 80 ? 3.5 : 4.5),
                    MarkerStroke   = OxyColors.Transparent
                };

                foreach (var s in ordered)
                    scatter.Points.Add(new ScatterPoint(
                        s.CumulativePixelX!.Value,
                        s.CumulativePixelY!.Value,
                        double.NaN,
                        s.FrameIndex));

                model.Series.Add(scatter);
                ApplyPixelAxes(model, samples);
            } else {
                // Header mode: connected path so pointing direction is visible.
                var line = new LineSeries {
                    Title          = "ΔRA / ΔDec from FITS headers (arcsec, relative to frame 1)",
                    Color          = OxyColor.FromRgb(100, 200, 255),
                    StrokeThickness = 1.5,
                    MarkerType     = MarkerType.Circle,
                    MarkerSize     = n > 100 ? 2.5 : 3.5,
                    MarkerFill     = OxyColor.FromRgb(100, 200, 255),
                    MarkerStroke   = OxyColors.Transparent
                };
                foreach (var s in ordered)
                    line.Points.Add(new DataPoint(s.DeltaRaArcSec, s.DeltaDecArcSec));
                model.Series.Add(line);
                ApplyPointingAxes(model, samples);
            }

            WarnIfFlatTrace(pixelPlot);
            PlotModel = model;
            RaisePropertyChanged(nameof(SampleCount));
            RaisePropertyChanged(nameof(LastSummary));
        }

        private static void ApplyPointingAxes(PlotModel model,
            ObservableCollection<DriftSample> samples) {
            var xAxis = model.Axes.OfType<LinearAxis>().First(a => a.Position == AxisPosition.Bottom);
            var yAxis = model.Axes.OfType<LinearAxis>().First(a => a.Position == AxisPosition.Left);

            const double padRatio = 0.12;
            const double minSpanArcSec = 2.0;

            if (samples.Count == 0) {
                xAxis.Minimum = -1;
                xAxis.Maximum = 1;
                yAxis.Minimum = -1;
                yAxis.Maximum = 1;
                return;
            }

            var dRa = samples.Select(s => s.DeltaRaArcSec).ToList();
            var dDec = samples.Select(s => s.DeltaDecArcSec).ToList();
            var minX = dRa.Min();
            var maxX = dRa.Max();
            var minY = dDec.Min();
            var maxY = dDec.Max();
            var spanX = Math.Max(maxX - minX, minSpanArcSec);
            var spanY = Math.Max(maxY - minY, minSpanArcSec);
            var padX = spanX * padRatio;
            var padY = spanY * padRatio;

            xAxis.Minimum = minX - padX;
            xAxis.Maximum = maxX + padX;
            yAxis.Minimum = minY - padY;
            yAxis.Maximum = maxY + padY;
        }

        private static void ApplyPixelAxes(PlotModel model, ObservableCollection<DriftSample> samples) {
            var xAxis = model.Axes.OfType<LinearAxis>().First(a => a.Position == AxisPosition.Bottom);
            var yAxis = model.Axes.OfType<LinearAxis>().First(a => a.Position == AxisPosition.Left);
            const double padRatio = 0.08;
            const double minSpanPx = 2.0;

            if (samples.Count == 0) {
                xAxis.Minimum = -1;
                xAxis.Maximum = 1;
                yAxis.Minimum = -1;
                yAxis.Maximum = 1;
                return;
            }

            var xs = samples.Select(s => s.CumulativePixelX!.Value).ToList();
            var ys = samples.Select(s => s.CumulativePixelY!.Value).ToList();
            var minX = xs.Min();
            var maxX = xs.Max();
            var minY = ys.Min();
            var maxY = ys.Max();
            var spanX = Math.Max(maxX - minX, minSpanPx);
            var spanY = Math.Max(maxY - minY, minSpanPx);
            var padX = spanX * padRatio;
            var padY = spanY * padRatio;

            xAxis.Minimum = minX - padX;
            xAxis.Maximum = maxX + padX;
            yAxis.Minimum = minY - padY;
            yAxis.Maximum = maxY + padY;
        }

        private void WarnIfFlatTrace(bool pixelPlot) {
            var samples = _tracker.Samples;
            if (samples.Count == 0)
                _warnedFlatTrace = false;
            if (samples.Count < 3)
                return;

            bool flat;
            if (pixelPlot) {
                var xs = samples.Select(s => s.CumulativePixelX!.Value).ToList();
                var ys = samples.Select(s => s.CumulativePixelY!.Value).ToList();
                flat = xs.Max() - xs.Min() < 1e-9 && ys.Max() - ys.Min() < 1e-9;
            } else {
                var raRange = samples.Max(s => s.DeltaRaArcSec) - samples.Min(s => s.DeltaRaArcSec);
                var decRange = samples.Max(s => s.DeltaDecArcSec) - samples.Min(s => s.DeltaDecArcSec);
                flat = raRange < 1e-9 && decRange < 1e-9;
            }

            if (!flat) {
                _warnedFlatTrace = false;
                return;
            }
            if (_warnedFlatTrace)
                return;
            _warnedFlatTrace = true;
            if (pixelPlot) {
                Logger.Warning("SeeDrift: cumulative pixel path is numerically flat — check data or crop size.");
            } else {
                Logger.Warning(
                    "SeeDrift: all frames share identical pointing in FITS headers (often fixed CRVAL/OBJCT). " +
                    "Prefer per-frame solved RA/DEC in metadata if you expect dither or tracking motion, or use pixel registration for folder import.");
            }
        }

        private static PlotModel BuildEmptyModel(int frameCount, bool pixelPlot) {
            var gridMajor = OxyColor.FromAColor(55, OxyColor.FromRgb(180, 180, 190));
            var gridMinor = OxyColor.FromAColor(35, OxyColor.FromRgb(140, 140, 155));
            var axisLine = OxyColor.FromRgb(95, 95, 105);

            string subtitle;
            if (frameCount <= 0)
                subtitle = "";
            else if (pixelPlot)
                subtitle =
                    $"{frameCount} frames · cumulative Δx/Δy in pixels from phase correlation · frame 1 = origin (0, 0)";
            else
                subtitle =
                    $"{frameCount} frames · ΔRA / ΔDec in arcsec relative to frame 1 · sorted by DATE-OBS";

            var m = new PlotModel {
                Title = pixelPlot
                    ? "Detector drift — cumulative pixel shifts from frame 1"
                    : "Pointing drift — ΔRA / ΔDec arcsec from frame 1",
                Subtitle = subtitle,
                TitleColor = OxyColor.FromRgb(230, 230, 235),
                SubtitleColor = OxyColor.FromRgb(170, 175, 185),
                Background = OxyColor.FromRgb(26, 26, 30),
                TextColor = OxyColor.FromRgb(210, 210, 218),
                PlotAreaBorderColor = OxyColor.FromRgb(58, 58, 66)
            };
            m.Axes.Add(new LinearAxis {
                Position = AxisPosition.Bottom,
                Title = pixelPlot ? "Cumulative X (px)" : "ΔRA (arcsec)",
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
                Title = pixelPlot ? "Cumulative Y (px)" : "ΔDec (arcsec)",
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
                var first = _tracker.Samples[0];
                var last = _tracker.Samples[^1];
                if (first.IsPixelPath) {
                    return $"Pixel drift — end: ({last.CumulativePixelX:F1}, {last.CumulativePixelY:F1}) px from origin · {_tracker.Samples.Count} frames · {last.TargetName}";
                }
                return $"Coord drift — end: ΔRA {last.DeltaRaArcSec:+0.0;-0.0}\" ΔDec {last.DeltaDecArcSec:+0.0;-0.0}\" from frame 1 · {_tracker.Samples.Count} frames · {last.TargetName}";
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

                var pixel = _tracker.Samples[0].IsPixelPath;
                var dlg = new Microsoft.Win32.SaveFileDialog {
                    Title = "Export SeeDrift HTML",
                    Filter = "HTML|*.html",
                    FileName = pixel
                        ? $"SeeDrift_pixels_{DateTime.Now:yyyyMMdd_HHmmss}.html"
                        : $"SeeDrift_{DateTime.Now:yyyyMMdd_HHmmss}.html",
                    InitialDirectory = Directory.Exists(folder) ? folder : Environment.CurrentDirectory
                };
                if (dlg.ShowDialog() != true)
                    return;

                var chartTitle = pixel ? "SeeDrift — cumulative pixel path" : "SeeDrift — pointing path";
                HtmlReportExporter.WriteReport(dlg.FileName, _tracker.Samples.ToList(), chartTitle);
                _plugin.HtmlExportFolder = Path.GetDirectoryName(dlg.FileName) ?? folder;
                _plugin.SyncSettingsFromProperties();
            } catch (Exception ex) {
                Logger.Error($"SeeDrift HTML export failed: {ex.Message}");
            }
        }
    }
}
