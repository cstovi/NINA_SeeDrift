using System;
using System.ComponentModel.Composition;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Plugin.SeeDrift.Models;
using NINA.Plugin.SeeDrift.Services;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.ViewModel;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Legends;
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
            SaveReportNowCommand = new RelayCommand(_ => _tracker.SaveReportNow());
            ImportFolderCommand = new RelayCommand(_ => ImportFitsFolder());

            _tracker.Samples.CollectionChanged += (_, _) =>
                System.Windows.Application.Current?.Dispatcher.Invoke(() => RaisePropertyChanged(nameof(IsArmed)));

            RefreshPlot();
        }

        public ICommand ResetCommand { get; }
        public ICommand SaveReportNowCommand { get; }
        public ICommand ImportFolderCommand { get; }

        public bool IsArmed => _tracker.IsArmed;

        private PlotModel _plotModel = null!;
        public PlotModel PlotModel {
            get => _plotModel;
            set { _plotModel = value; RaisePropertyChanged(); }
        }

        private void RefreshPlot() {
            var samples = _tracker.Samples;
            var n = samples.Count;
            var pixelPlot = n > 0 && samples[0].IsPixelPath;

            // Build plate-scale label from the tracker's cached value.
            string? psLabel = _tracker.PlateScaleArcSecPerPx.HasValue
                ? $"{_tracker.PlateScaleArcSecPerPx.Value:F2}\"↗px"
                : null;

            var model = BuildEmptyModel(n, pixelPlot, psLabel);
            var ordered = samples.OrderBy(x => x.FrameIndex).ToList();

            if (pixelPlot) {
                // Small dots so adjacent frames (which can be <1px apart) are visible.
                // User can scroll-zoom on the plot to separate overlapping clusters.
                var dotSize = 2.0;
                var dotColor = OxyColor.FromAColor(160, OxyColor.FromRgb(130, 180, 255));

                // Y is negated for display: FITS rows increase downward, so positive
                // cumulative Y (stars shifted to higher row numbers) appears DOWN on the
                // plot, matching the natural orientation of the sensor.
                static double PlotY(double y) => -y;

                var pathLine = new LineSeries {
                    Title           = "Pixel drift path",
                    Color           = OxyColor.FromAColor(55, dotColor),
                    StrokeThickness = 0.8,
                    MarkerType      = MarkerType.None
                };
                foreach (var s in ordered)
                    pathLine.Points.Add(new DataPoint(
                        s.CumulativePixelX!.Value,
                        PlotY(s.CumulativePixelY!.Value)));
                model.Series.Add(pathLine);

                var scatter = new ScatterSeries {
                    Title          = "Frames",
                    MarkerType     = MarkerType.Circle,
                    MarkerSize     = dotSize,
                    MarkerFill     = dotColor,
                    MarkerStroke   = OxyColors.Transparent
                };
                foreach (var s in ordered)
                    scatter.Points.Add(new ScatterPoint(
                        s.CumulativePixelX!.Value,
                        PlotY(s.CumulativePixelY!.Value)));
                model.Series.Add(scatter);

                if (ordered.Count > 0) {
                    var first = ordered[0];
                    var startDot = new ScatterSeries {
                        Title        = "Start (ref)",
                        MarkerType   = MarkerType.Circle,
                        MarkerSize   = dotSize + 2.5,
                        MarkerFill   = OxyColor.FromRgb(80, 210, 100),
                        MarkerStroke = OxyColor.FromRgb(220, 255, 220),
                        MarkerStrokeThickness = 1.2
                    };
                    startDot.Points.Add(new ScatterPoint(
                        first.CumulativePixelX!.Value,
                        PlotY(first.CumulativePixelY!.Value)));
                    model.Series.Add(startDot);
                }

                if (ordered.Count > 1) {
                    var last = ordered[^1];
                    var endDot = new ScatterSeries {
                        Title        = "End",
                        MarkerType   = MarkerType.Circle,
                        MarkerSize   = dotSize + 2.5,
                        MarkerFill   = OxyColor.FromRgb(255, 130, 40),
                        MarkerStroke = OxyColor.FromRgb(255, 220, 180),
                        MarkerStrokeThickness = 1.2
                    };
                    endDot.Points.Add(new ScatterPoint(
                        last.CumulativePixelX!.Value,
                        PlotY(last.CumulativePixelY!.Value)));
                    model.Series.Add(endDot);
                }

                // Jump markers — gold diamonds drawn on top so they stand out clearly.
                var jumpSamples = ordered.Where(s => s.IsJump).ToList();
                if (jumpSamples.Count > 0) {
                    var jumpSeries = new ScatterSeries {
                        Title                 = $"Jumps ({jumpSamples.Count})",
                        MarkerType            = MarkerType.Diamond,
                        MarkerSize            = dotSize + 2.0,
                        MarkerFill            = OxyColor.FromAColor(210, OxyColor.FromRgb(255, 215, 0)),
                        MarkerStroke          = OxyColor.FromRgb(180, 140, 0),
                        MarkerStrokeThickness = 1.0,
                        TrackerFormatString   = "Jump · frame {Tag}\n{1}: {2:0.##} px\n{3}: {4:0.##} px"
                    };
                    foreach (var s in jumpSamples)
                        jumpSeries.Points.Add(new ScatterPoint(
                            s.CumulativePixelX!.Value,
                            PlotY(s.CumulativePixelY!.Value),
                            tag: $"{s.FrameIndex + 1} — {s.JumpReason ?? "large shift"}"));
                    model.Series.Add(jumpSeries);
                }

                ApplyPixelAxes(model, samples);
            } else {
                var dotColor2  = OxyColor.FromRgb(100, 200, 255);
                var dotSize2   = n > 100 ? 2.5 : 3.5;

                // Header mode: faint line + dots + start/end highlights.
                var line = new LineSeries {
                    Title           = "ΔRA / ΔDec path",
                    Color           = OxyColor.FromAColor(55, dotColor2),
                    StrokeThickness = 0.8,
                    MarkerType      = MarkerType.None
                };
                foreach (var s in ordered)
                    line.Points.Add(new DataPoint(s.DeltaRaArcSec, s.DeltaDecArcSec));
                model.Series.Add(line);

                var scatter2 = new ScatterSeries {
                    Title      = "Frames",
                    MarkerType = MarkerType.Circle,
                    MarkerSize = dotSize2,
                    MarkerFill = dotColor2,
                    MarkerStroke = OxyColors.Transparent
                };
                foreach (var s in ordered)
                    scatter2.Points.Add(new ScatterPoint(s.DeltaRaArcSec, s.DeltaDecArcSec));
                model.Series.Add(scatter2);

                if (ordered.Count > 0) {
                    var first = ordered[0];
                    var startDot2 = new ScatterSeries {
                        Title        = "Start (ref)",
                        MarkerType   = MarkerType.Circle,
                        MarkerSize   = dotSize2 + 2.5,
                        MarkerFill   = OxyColor.FromRgb(80, 210, 100),
                        MarkerStroke = OxyColor.FromRgb(220, 255, 220),
                        MarkerStrokeThickness = 1.2
                    };
                    startDot2.Points.Add(new ScatterPoint(first.DeltaRaArcSec, first.DeltaDecArcSec));
                    model.Series.Add(startDot2);
                }
                if (ordered.Count > 1) {
                    var last = ordered[^1];
                    var endDot2 = new ScatterSeries {
                        Title        = "End",
                        MarkerType   = MarkerType.Circle,
                        MarkerSize   = dotSize2 + 2.5,
                        MarkerFill   = OxyColor.FromRgb(255, 130, 40),
                        MarkerStroke = OxyColor.FromRgb(255, 220, 180),
                        MarkerStrokeThickness = 1.2
                    };
                    endDot2.Points.Add(new ScatterPoint(last.DeltaRaArcSec, last.DeltaDecArcSec));
                    model.Series.Add(endDot2);
                }

                var jumpSamples2 = ordered.Where(s => s.IsJump).ToList();
                if (jumpSamples2.Count > 0) {
                    var jumpSeries2 = new ScatterSeries {
                        Title                 = $"Jumps ({jumpSamples2.Count})",
                        MarkerType            = MarkerType.Diamond,
                        MarkerSize            = dotSize2 + 2.0,
                        MarkerFill            = OxyColor.FromAColor(210, OxyColor.FromRgb(255, 215, 0)),
                        MarkerStroke          = OxyColor.FromRgb(180, 140, 0),
                        MarkerStrokeThickness = 1.0,
                        TrackerFormatString   = "Jump · frame {Tag}\n{1}: {2:0.##}\"\n{3}: {4:0.##}\""
                    };
                    foreach (var s in jumpSamples2)
                        jumpSeries2.Points.Add(new ScatterPoint(
                            s.DeltaRaArcSec, s.DeltaDecArcSec,
                            tag: $"{s.FrameIndex + 1} — {s.JumpReason ?? "large shift"}"));
                    model.Series.Add(jumpSeries2);
                }

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

            // Update Y axis label to reflect the display convention.
            yAxis.Title = "Cumulative Y (px, ↓ = sensor down)";

            if (samples.Count == 0) {
                xAxis.Minimum = -1; xAxis.Maximum = 1;
                yAxis.Minimum = -1; yAxis.Maximum = 1;
                return;
            }

            // Y is negated for display (positive sensor shift = down on screen).
            var xs = samples.Select(s => s.CumulativePixelX!.Value).ToList();
            var ys = samples.Select(s => -s.CumulativePixelY!.Value).ToList();
            const double padRatio = 0.10;
            const double minSpanPx = 4.0;

            var minX = xs.Min(); var maxX = xs.Max();
            var minY = ys.Min(); var maxY = ys.Max();
            var spanX = Math.Max(maxX - minX, minSpanPx);
            var spanY = Math.Max(maxY - minY, minSpanPx);

            xAxis.Minimum = minX - spanX * padRatio;
            xAxis.Maximum = maxX + spanX * padRatio;
            yAxis.Minimum = minY - spanY * padRatio;
            yAxis.Maximum = maxY + spanY * padRatio;
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

        private static PlotModel BuildEmptyModel(int frameCount, bool pixelPlot,
                string? plateScaleLabel = null) {
            var gridMajor = OxyColor.FromAColor(55, OxyColor.FromRgb(180, 180, 190));
            var gridMinor = OxyColor.FromAColor(35, OxyColor.FromRgb(140, 140, 155));
            var axisLine = OxyColor.FromRgb(95, 95, 105);

            var psHint = plateScaleLabel != null ? $" · {plateScaleLabel}" : "";

            string subtitle;
            if (frameCount <= 0)
                subtitle = "";
            else if (pixelPlot)
                subtitle =
                    $"{frameCount} frames · cumulative Δx/Δy in pixels{psHint} · frame 1 = origin · scroll to zoom";
            else
                subtitle =
                    $"{frameCount} frames · ΔRA / ΔDec in arcsec relative to frame 1{psHint} · sorted by DATE-OBS";

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
                Title = pixelPlot ? "Cumulative Y (px, ↓ = sensor down)" : "ΔDec (arcsec)",
                TitleColor = OxyColor.FromRgb(200, 210, 230),
                AxislineColor = axisLine,
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = gridMajor,
                MinorGridlineStyle = LineStyle.Dot,
                MinorGridlineColor = gridMinor,
                TicklineColor = axisLine
            });
            m.Legends.Add(new Legend {
                LegendPosition           = LegendPosition.TopRight,
                LegendBackground         = OxyColor.FromAColor(160, OxyColor.FromRgb(26, 26, 30)),
                LegendBorder             = OxyColor.FromRgb(70, 70, 80),
                LegendBorderThickness    = 1,
                LegendTextColor          = OxyColor.FromRgb(210, 210, 218),
                LegendTitleColor         = OxyColor.FromRgb(210, 210, 218),
                LegendFontSize           = 11,
                LegendItemSpacing        = 4,
                LegendMargin             = 8,
                LegendPadding            = 6,
            });
            return m;
        }

        public int SampleCount => _tracker.Samples.Count;

        public string LastSummary {
            get {
                var samples = _tracker.Samples;
                if (samples.Count == 0)
                    return "No frames yet.";

                var n = samples.Count;
                var last = samples[^1];
                var jumps = _tracker.JumpCount;
                string jumpStr;
                if (jumps == 0) {
                    jumpStr = "";
                } else if (!_tracker.LogWasFound) {
                    jumpStr = $" · {jumps} jump{(jumps == 1 ? "" : "s")} (no NINA log found)";
                } else if (_tracker.LogCorrelatedCount == 0) {
                    jumpStr = $" · {jumps} jump{(jumps == 1 ? "" : "s")} (log found, no events matched)";
                } else {
                    jumpStr = $" · {jumps} jump{(jumps == 1 ? "" : "s")}, {_tracker.LogCorrelatedCount} correlated from log";
                }

                // RA/Dec spread (works for both modes from the delta values).
                var minRa  = samples.Min(s => s.DeltaRaArcSec);
                var maxRa  = samples.Max(s => s.DeltaRaArcSec);
                var minDec = samples.Min(s => s.DeltaDecArcSec);
                var maxDec = samples.Max(s => s.DeltaDecArcSec);
                var raRange  = maxRa  - minRa;
                var decRange = maxDec - minDec;
                var spreadStr = $"ΔRA {minRa:+0.0;-0.0}″…{maxRa:+0.0;-0.0}″ ({raRange:F1}″ pp) · ΔDec {minDec:+0.0;-0.0}″…{maxDec:+0.0;-0.0}″ ({decRange:F1}″ pp)";

                if (samples[0].IsPixelPath) {
                    var maxDist = samples.Max(s =>
                        Math.Sqrt(s.CumulativePixelX!.Value * s.CumulativePixelX.Value +
                                  s.CumulativePixelY!.Value * s.CumulativePixelY.Value));
                    string psExtra = "";
                    if (_tracker.PlateScaleArcSecPerPx.HasValue) {
                        var ps = _tracker.PlateScaleArcSecPerPx.Value;
                        psExtra = $" (≈{maxDist * ps:F0}″ · {ps:F2}″/px)";
                    }
                    return $"{n} frames · max drift {maxDist:F1}px{psExtra}{jumpStr} · {spreadStr} · {last.TargetName}";
                }

                return $"{n} frames · {spreadStr}{jumpStr} · {last.TargetName}";
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
    }
}
