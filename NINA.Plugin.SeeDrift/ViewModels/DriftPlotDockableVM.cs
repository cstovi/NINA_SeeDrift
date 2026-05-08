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
            var pathSeries = new LineSeries {
                Title = pixelPlot
                    ? "Cumulative pixel shift (phase correlation)"
                    : "RA° / Dec° from FITS primary header",
                Color = OxyColor.FromRgb(100, 200, 255),
                StrokeThickness = 1.75,
                MarkerType = MarkerType.Circle,
                MarkerSize = n > 100 ? 2.25 : 3.5,
                MarkerFill = OxyColor.FromRgb(100, 200, 255),
                MarkerStroke = OxyColors.Transparent
            };

            foreach (var s in samples.OrderBy(x => x.FrameIndex)) {
                if (pixelPlot)
                    pathSeries.Points.Add(new DataPoint(s.CumulativePixelX!.Value, s.CumulativePixelY!.Value));
                else
                    pathSeries.Points.Add(new DataPoint(RaHoursToDegrees(s.RawRaHours), s.RawDecDeg));
            }

            if (pixelPlot)
                ApplyPixelAxes(model, samples);
            else
                ApplyPointingAxes(model, samples);

            model.Series.Add(pathSeries);

            WarnIfFlatTrace(pixelPlot);

            PlotModel = model;
            RaisePropertyChanged(nameof(SampleCount));
            RaisePropertyChanged(nameof(LastSummary));
        }

        private static double RaHoursToDegrees(double raHours) => raHours * 15.0;

        private static void ApplyPointingAxes(PlotModel model,
            ObservableCollection<DriftSample> samples) {
            var xAxis = model.Axes.OfType<LinearAxis>().First(a => a.Position == AxisPosition.Bottom);
            var yAxis = model.Axes.OfType<LinearAxis>().First(a => a.Position == AxisPosition.Left);

            const double padRatio = 0.08;
            const double minSpanDeg = 0.02;

            if (samples.Count == 0) {
                xAxis.Minimum = 0;
                xAxis.Maximum = 1;
                yAxis.Minimum = 0;
                yAxis.Maximum = 1;
                return;
            }

            var raDeg = samples.Select(s => RaHoursToDegrees(s.RawRaHours)).ToList();
            var decDeg = samples.Select(s => s.RawDecDeg).ToList();
            var minRa = raDeg.Min();
            var maxRa = raDeg.Max();
            var minDec = decDeg.Min();
            var maxDec = decDeg.Max();
            var spanRa = Math.Max(maxRa - minRa, minSpanDeg);
            var spanDec = Math.Max(maxDec - minDec, minSpanDeg);
            var padRa = spanRa * padRatio;
            var padDec = spanDec * padRatio;

            xAxis.Minimum = minRa - padRa;
            xAxis.Maximum = maxRa + padRa;
            yAxis.Minimum = minDec - padDec;
            yAxis.Maximum = maxDec + padDec;
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
                var raDeg = samples.Select(s => RaHoursToDegrees(s.RawRaHours)).ToList();
                var raRange = raDeg.Max() - raDeg.Min();
                var decRange = samples.Max(s => s.RawDecDeg) - samples.Min(s => s.RawDecDeg);
                flat = raRange < 1e-12 && decRange < 1e-12;
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
                    $"{frameCount} frames · cumulative Δx/Δy in pixels (phase correlation on central crop) · order = DATE-OBS when in header";
            else
                subtitle =
                    $"{frameCount} frames · RA° & Dec° from each primary header · order (live = save order; folder = DATE-OBS when present) · identical coords overlap";

            var m = new PlotModel {
                Title = pixelPlot
                    ? "Detector path — cumulative pixel shifts"
                    : "Pointing path — RA° vs Dec° per FITS frame",
                Subtitle = subtitle,
                TitleColor = OxyColor.FromRgb(230, 230, 235),
                SubtitleColor = OxyColor.FromRgb(170, 175, 185),
                Background = OxyColor.FromRgb(26, 26, 30),
                TextColor = OxyColor.FromRgb(210, 210, 218),
                PlotAreaBorderColor = OxyColor.FromRgb(58, 58, 66)
            };
            m.Axes.Add(new LinearAxis {
                Position = AxisPosition.Bottom,
                Title = pixelPlot ? "Cumulative X (px)" : "RA (°)",
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
                Title = pixelPlot ? "Cumulative Y (px)" : "Dec (°)",
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
                    return $"Pixel path start ({first.CumulativePixelX:F2}, {first.CumulativePixelY:F2}) px → end ({last.CumulativePixelX:F2}, {last.CumulativePixelY:F2}) px · {last.TargetName}";
                }
                var ra0 = RaHoursToDegrees(first.RawRaHours);
                var ra1 = RaHoursToDegrees(last.RawRaHours);
                return $"Start RA {ra0:F4}° Dec {first.RawDecDeg:F4}° → end RA {ra1:F4}° Dec {last.RawDecDeg:F4}° · {last.TargetName}";
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
