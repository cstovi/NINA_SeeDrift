using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Input;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Plugin.SeeDrift.Models;
using NINA.Plugin.SeeDrift.Services;
using NINA.Plugin.SeeDrift.Utility;
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
        private readonly IProfileService _profileService;
        private bool _warnedFlatTrace;
        private string? _lastImportFolder;

        // Expose a controller with a generous snap radius so hovering near (not
        // exactly on) a small dot still fires the tracker tooltip.
        public IPlotController PlotController { get; } = BuildController();

        private static IPlotController BuildController() {
            var c = new PlotController();
            c.UnbindMouseEnter();
            c.BindMouseEnter(new DelegatePlotCommand<OxyMouseEventArgs>(
                (view, ctrl, args) =>
                    ctrl.AddHoverManipulator(view,
                        new DismissWhenAwayTrackerManipulator(view) {
                            Snap = false,
                            PointsOnly = true,
                            FiresDistance = 32.0
                        },
                        args)));
            return c;
        }

        [ImportingConstructor]
        public DriftPlotDockableVM(IProfileService profileService, SeeDriftPlugin plugin)
            : base(profileService) {
            _plugin = plugin;
            _profileService = profileService;
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

            // Keep the mode label in sync when the user changes the option.
            _plugin.PropertyChanged += (_, e) => {
                if (e.PropertyName == nameof(SeeDriftPlugin.FolderImportPlotMode))
                    System.Windows.Application.Current?.Dispatcher.Invoke(() => RaisePropertyChanged(nameof(DriftModeLabel)));
            };

            RefreshPlot();
        }

        public ICommand ResetCommand { get; }
        public ICommand SaveReportNowCommand { get; }
        public ICommand ImportFolderCommand { get; }

        public bool IsArmed => _tracker.IsArmed;

        public string DriftModeLabel => _plugin.FolderImportPlotMode == Models.FolderPlotMode.PixelRegistration
            ? "pixel reg"
            : "header coords";

        /// <summary>
        /// Frame number for hovers: NINA exposure index from file name when parseable, else trace order (1-based).
        /// </summary>
        private static string FrameOrdinalForUi(DriftSample s) =>
            FitsFolderImport.TryExposureSequenceFromFileName(s.FileName, out var n)
                ? n.ToString(CultureInfo.InvariantCulture)
                : (s.FrameIndex + 1).ToString(CultureInfo.InvariantCulture);

        /// <summary>First line for blue frame-dot tooltips (time; jump note when the frame is also a jump).</summary>
        private static string BuildFrameScatterHead(DriftSample s) {
            var t = $"{FrameOrdinalForUi(s)} · {s.ExposureStartUtc.ToLocalTime():HH:mm:ss}";
            if (s.IsJump)
                return $"{t} · Jump: {s.JumpReason ?? "large shift"}";
            return t;
        }

        /// <summary>Appends FITS file name (basename) at the end of tracker hover text when present.</summary>
        private static string AppendHoverFileTail(DriftSample s, string body) {
            var fn = s.FileName?.Trim();
            if (string.IsNullOrEmpty(fn))
                return body;
            return body + "\n" + fn;
        }

        private static string BuildFrameTooltipHeaderArcSec(DriftSample s) =>
            AppendHoverFileTail(s,
                $"Frame {BuildFrameScatterHead(s)}\nΔRA (arcsec): {s.DeltaRaArcSec:0.###}\nΔDec (arcsec): {s.DeltaDecArcSec:0.###}");

        private static string BuildFrameTooltipPixelDerived(DriftSample s, double dRa, double dDec) =>
            AppendHoverFileTail(s,
                $"Frame {BuildFrameScatterHead(s)}\nΔRA: {dRa:0.##}\"\nΔDec: {dDec:0.##}\"");

        private static string BuildFrameTooltipPixelRaw(DriftSample s, double x, double y) =>
            AppendHoverFileTail(s,
                $"Frame {BuildFrameScatterHead(s)}\nCumulative X (px): {x:0.##}\nCumulative Y (px, ↓ = sensor down): {y:0.##}");

        private static string BuildJumpTooltipHeaderArcSec(DriftSample s) =>
            AppendHoverFileTail(s,
                $"Jump · frame {FrameOrdinalForUi(s)} — {s.JumpReason ?? "large shift"}\nΔRA (arcsec): {s.DeltaRaArcSec:0.##}\nΔDec (arcsec): {s.DeltaDecArcSec:0.##}");

        private static string BuildJumpTooltipPixelDerived(DriftSample s, double dRa, double dDec) =>
            AppendHoverFileTail(s,
                $"Jump · frame {FrameOrdinalForUi(s)} — {s.JumpReason ?? "large shift"}\nΔRA: {dRa:0.##}\"\nΔDec: {dDec:0.##}\"");

        private static string BuildJumpTooltipPixelRaw(DriftSample s, double x, double y) =>
            AppendHoverFileTail(s,
                $"Jump · frame {FrameOrdinalForUi(s)} — {s.JumpReason ?? "large shift"}\nCumulative X (px): {x:0.##}\nCumulative Y (px, ↓ = sensor down): {y:0.##}");

        private PlotModel _plotModel = null!;
        public PlotModel PlotModel {
            get => _plotModel;
            set { _plotModel = value; RaisePropertyChanged(); }
        }

        private void RefreshPlot() {
            var samples = _tracker.Samples;
            var n = samples.Count;
            var pixelPlot = n > 0 && samples[0].IsPixelPath;
            var ordered   = samples.OrderBy(x => x.FrameIndex).ToList();

            // RA/Dec derived mode: pixel reg + conversion succeeded on the first frame.
            var useRaDec = pixelPlot && ordered.Count > 0 && ordered[0].HasPixelDerivedRaDec;

            // Build plate-scale label from the tracker's cached value.
            string? psLabel = _tracker.PlateScaleArcSecPerPx.HasValue
                ? $"{_tracker.PlateScaleArcSecPerPx.Value:F2}\"↗px"
                : null;

            var mountLabel = _plugin.Settings.MountMode == Models.MountMode.EQ ? "EQ" : "Alt/Az";
            var model = BuildEmptyModel(n, pixelPlot, useRaDec, mountLabel, psLabel);

            if (pixelPlot) {
                var dotSize  = 2.0;
                var dotColor = OxyColor.FromAColor(160, OxyColor.FromRgb(130, 180, 255));

                // Coordinate helpers: RA/Dec arcsec when available, else raw pixels.
                // Raw pixel Y is negated so positive sensor shift = downward on plot.
                Func<DriftSample, double> GetX = useRaDec
                    ? (s => s.PixelDerivedRaArcSec!.Value)
                    : (s => s.CumulativePixelX!.Value);
                Func<DriftSample, double> GetY = useRaDec
                    ? (s => s.PixelDerivedDecArcSec!.Value)
                    : (s => -s.CumulativePixelY!.Value);

                var pathFmt  = useRaDec
                    ? "Path\nΔRA: {2:0.##}\"\nΔDec: {4:0.##}\""
                    : "Path\n{1}: {2:0.##} px\n{3}: {4:0.##} px";
                const string frameJumpTrackerFmt = "{Tag}";

                var pathLine = new LineSeries {
                    Title                        = useRaDec ? "ΔRA / ΔDec path" : "Pixel drift path",
                    Color                        = OxyColor.FromAColor(55, dotColor),
                    StrokeThickness              = 0.8,
                    MarkerType                   = MarkerType.None,
                    CanTrackerInterpolatePoints  = false,
                    TrackerFormatString          = pathFmt
                };
                foreach (var s in ordered)
                    pathLine.Points.Add(new DataPoint(GetX(s), GetY(s)));
                model.Series.Add(pathLine);

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
                    startDot.Points.Add(new ScatterPoint(GetX(first), GetY(first)));
                    model.Series.Add(startDot);
                }

                if (ordered.Count > 1) {
                    var last = ordered[^1];
                    var endDot = new ScatterSeries {
                        Title        = "End",
                        MarkerType   = MarkerType.Circle,
                        MarkerSize   = dotSize + 5.0,
                        MarkerFill   = OxyColor.FromAColor(200, OxyColor.FromRgb(255, 130, 40)),
                        MarkerStroke = OxyColor.FromRgb(255, 220, 180),
                        MarkerStrokeThickness = 1.5
                    };
                    endDot.Points.Add(new ScatterPoint(GetX(last), GetY(last)));
                    model.Series.Add(endDot);
                }

                var scatter = new ScatterSeries {
                    Title               = "Frames",
                    MarkerType          = MarkerType.Circle,
                    MarkerSize          = dotSize,
                    MarkerFill          = dotColor,
                    MarkerStroke        = OxyColors.Transparent,
                    TrackerFormatString = frameJumpTrackerFmt
                };
                foreach (var s in ordered) {
                    var tag = useRaDec
                        ? BuildFrameTooltipPixelDerived(s, GetX(s), GetY(s))
                        : BuildFrameTooltipPixelRaw(s, GetX(s), GetY(s));
                    scatter.Points.Add(new ScatterPoint(GetX(s), GetY(s), tag: tag));
                }
                model.Series.Add(scatter);

                var jumpSamples = ordered.Where(s => s.IsJump).ToList();
                if (jumpSamples.Count > 0) {
                    var jumpSeries = new ScatterSeries {
                        Title                 = $"Jumps ({jumpSamples.Count})",
                        MarkerType            = MarkerType.Diamond,
                        MarkerSize            = dotSize + 2.0,
                        MarkerFill            = OxyColor.FromAColor(210, OxyColor.FromRgb(255, 215, 0)),
                        MarkerStroke          = OxyColor.FromRgb(180, 140, 0),
                        MarkerStrokeThickness = 1.0,
                        TrackerFormatString   = frameJumpTrackerFmt
                    };
                    foreach (var s in jumpSamples) {
                        var tag = useRaDec
                            ? BuildJumpTooltipPixelDerived(s, GetX(s), GetY(s))
                            : BuildJumpTooltipPixelRaw(s, GetX(s), GetY(s));
                        jumpSeries.Points.Add(new ScatterPoint(GetX(s), GetY(s), tag: tag));
                    }
                    model.Series.Add(jumpSeries);
                }

                AddSequencerMidpoints(model, ordered, GetX, GetY, dotSize);
                ApplyPixelAxes(model, ordered, GetX, GetY, useRaDec);
            } else {
                var dotColor2  = OxyColor.FromRgb(100, 200, 255);
                var dotSize2   = n > 100 ? 2.5 : 3.5;

                // Header mode: faint path, ref/end (under frames), frame dots, jump diamonds on top.
                var line = new LineSeries {
                    Title                        = "ΔRA / ΔDec path",
                    Color                        = OxyColor.FromAColor(55, dotColor2),
                    StrokeThickness              = 0.8,
                    MarkerType                   = MarkerType.None,
                    CanTrackerInterpolatePoints  = false,
                    TrackerFormatString          = "Path\nΔRA: {2:0.##}\"\nΔDec: {4:0.##}\""
                };
                foreach (var s in ordered)
                    line.Points.Add(new DataPoint(s.DeltaRaArcSec, s.DeltaDecArcSec));
                model.Series.Add(line);

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
                        MarkerSize   = dotSize2 + 5.0,
                        MarkerFill   = OxyColor.FromAColor(200, OxyColor.FromRgb(255, 130, 40)),
                        MarkerStroke = OxyColor.FromRgb(255, 220, 180),
                        MarkerStrokeThickness = 1.5
                    };
                    endDot2.Points.Add(new ScatterPoint(last.DeltaRaArcSec, last.DeltaDecArcSec));
                    model.Series.Add(endDot2);
                }

                var scatter2 = new ScatterSeries {
                    Title               = "Frames",
                    MarkerType          = MarkerType.Circle,
                    MarkerSize          = dotSize2,
                    MarkerFill          = dotColor2,
                    MarkerStroke        = OxyColors.Transparent,
                    TrackerFormatString = "{Tag}"
                };
                foreach (var s in ordered)
                    scatter2.Points.Add(new ScatterPoint(
                        s.DeltaRaArcSec, s.DeltaDecArcSec,
                        tag: BuildFrameTooltipHeaderArcSec(s)));
                model.Series.Add(scatter2);

                var jumpSamples2 = ordered.Where(s => s.IsJump).ToList();
                if (jumpSamples2.Count > 0) {
                    var jumpSeries2 = new ScatterSeries {
                        Title                 = $"Jumps ({jumpSamples2.Count})",
                        MarkerType            = MarkerType.Diamond,
                        MarkerSize            = dotSize2 + 2.0,
                        MarkerFill            = OxyColor.FromAColor(210, OxyColor.FromRgb(255, 215, 0)),
                        MarkerStroke          = OxyColor.FromRgb(180, 140, 0),
                        MarkerStrokeThickness = 1.0,
                        TrackerFormatString   = "{Tag}"
                    };
                    foreach (var s in jumpSamples2)
                        jumpSeries2.Points.Add(new ScatterPoint(s.DeltaRaArcSec, s.DeltaDecArcSec,
                            tag: BuildJumpTooltipHeaderArcSec(s)));
                    model.Series.Add(jumpSeries2);
                }

                AddSequencerMidpoints(model, ordered, s => s.DeltaRaArcSec, s => s.DeltaDecArcSec, dotSize2);
                ApplyPointingAxes(model, samples);
            }

            WarnIfFlatTrace(pixelPlot);
            PlotModel = model;
            RaisePropertyChanged(nameof(SampleCount));
            RaisePropertyChanged(nameof(LastSummary));
        }

        /// <summary>
        /// Markers on the segment between consecutive frames when the NINA log shows one or more dither/center triggers in that interval.
        /// Several events in the same gap are drawn evenly along the chord, centered on the midpoint.
        /// </summary>
        private static void AddSequencerMidpoints(
                PlotModel model,
                IReadOnlyList<DriftSample> ordered,
                Func<DriftSample, double> getX,
                Func<DriftSample, double> getY,
                double baseMarkerSize) {

            var ditherSeries = new ScatterSeries {
                Title               = "Dither (between frames)",
                MarkerType          = MarkerType.Triangle,
                MarkerSize          = baseMarkerSize + 2.5,
                MarkerFill          = OxyColor.FromAColor(220, OxyColor.FromRgb(255, 152, 0)),
                MarkerStroke        = OxyColor.FromRgb(200, 100, 0),
                MarkerStrokeThickness = 1.0,
                TrackerFormatString = "{Tag}"
            };
            var centerSeries = new ScatterSeries {
                Title               = "Center (between frames)",
                MarkerType          = MarkerType.Square,
                MarkerSize          = baseMarkerSize + 2.0,
                MarkerFill          = OxyColor.FromAColor(210, OxyColor.FromRgb(0, 188, 212)),
                MarkerStroke        = OxyColor.FromRgb(0, 120, 140),
                MarkerStrokeThickness = 1.0,
                TrackerFormatString = "{Tag}"
            };

            for (var i = 1; i < ordered.Count; i++) {
                var prev = ordered[i - 1];
                var cur = ordered[i];
                var markers = cur.EdgeSequencerMarkers;
                if (markers == null || markers.Count == 0)
                    continue;
                var px = getX(prev);
                var py = getY(prev);
                var cx = getX(cur);
                var cy = getY(cur);
                var n = markers.Count;
                for (var j = 0; j < n; j++) {
                    var u = InterFrameMarkerFraction(j, n);
                    var mx = px + u * (cx - px);
                    var my = py + u * (cy - py);
                    var m = markers[j];
                    if (m.IsDither)
                        ditherSeries.Points.Add(new ScatterPoint(mx, my, tag: m.Tooltip));
                    else
                        centerSeries.Points.Add(new ScatterPoint(mx, my, tag: m.Tooltip));
                }
            }

            if (ditherSeries.Points.Count > 0)
                model.Series.Add(ditherSeries);
            if (centerSeries.Points.Count > 0)
                model.Series.Add(centerSeries);
        }

        /// <summary>Fraction along prev→cur (0 = at prev, 1 = at cur); multiple markers cluster around 0.5.</summary>
        private static double InterFrameMarkerFraction(int index, int count) {
            if (count <= 1)
                return 0.5;
            var span = Math.Min(0.35, 0.2 * (count - 1));
            var step = span / (count - 1);
            var start = 0.5 - span / 2;
            var u = start + index * step;
            if (u < 0.06) return 0.06;
            if (u > 0.94) return 0.94;
            return u;
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

        private static void ApplyPixelAxes(
                PlotModel model,
                List<DriftSample> samples,
                Func<DriftSample, double> getX,
                Func<DriftSample, double> getY,
                bool useRaDec) {
            var xAxis = model.Axes.OfType<LinearAxis>().First(a => a.Position == AxisPosition.Bottom);
            var yAxis = model.Axes.OfType<LinearAxis>().First(a => a.Position == AxisPosition.Left);

            if (useRaDec) {
                xAxis.Title = "ΔRA (arcsec)";
                yAxis.Title = "ΔDec (arcsec)";
            } else {
                yAxis.Title = "Cumulative Y (px, ↓ = sensor down)";
            }

            if (samples.Count == 0) {
                xAxis.Minimum = -1; xAxis.Maximum = 1;
                yAxis.Minimum = -1; yAxis.Maximum = 1;
                return;
            }

            var xs = samples.Select(getX).ToList();
            var ys = samples.Select(getY).ToList();
            var padRatio = 0.10;
            var minSpan  = useRaDec ? 2.0 : 4.0; // arcsec vs pixels

            var minX = xs.Min(); var maxX = xs.Max();
            var minY = ys.Min(); var maxY = ys.Max();
            var spanX = Math.Max(maxX - minX, minSpan);
            var spanY = Math.Max(maxY - minY, minSpan);

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
                    "Prefer per-frame solved RA/DEC in metadata if you expect dither or tracking motion, or enable pixel registration in plugin options.");
            }
        }

        private static PlotModel BuildEmptyModel(int frameCount, bool pixelPlot,
                bool useRaDec = false, string? mountLabel = null, string? plateScaleLabel = null) {
            var gridMajor = OxyColor.FromAColor(55, OxyColor.FromRgb(180, 180, 190));
            var gridMinor = OxyColor.FromAColor(35, OxyColor.FromRgb(140, 140, 155));
            var axisLine  = OxyColor.FromRgb(95, 95, 105);

            var psHint     = plateScaleLabel != null ? $" · {plateScaleLabel}" : "";
            var modeHint   = mountLabel      != null ? $" ({mountLabel} mode)" : "";

            string subtitle;
            if (frameCount <= 0)
                subtitle = "";
            else if (useRaDec)
                subtitle =
                    $"{frameCount} frames · ΔRA / ΔDec in arcsec{psHint}{modeHint} · frame 1 = origin · scroll to zoom";
            else if (pixelPlot)
                subtitle =
                    $"{frameCount} frames · cumulative Δx/Δy in pixels{psHint} · frame 1 = origin · scroll to zoom";
            else
                subtitle =
                    $"{frameCount} frames · ΔRA / ΔDec in arcsec relative to frame 1{psHint} · sorted by filename #, then DATE-OBS";

            var m = new PlotModel {
                Title = useRaDec
                    ? "Detector drift — ΔRA / ΔDec arcsec (pixel reg)"
                    : pixelPlot
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
                Title = (pixelPlot && !useRaDec) ? "Cumulative X (px)" : "ΔRA (arcsec)",
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
                Title = (pixelPlot && !useRaDec) ? "Cumulative Y (px, ↓ = sensor down)" : "ΔDec (arcsec)",
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
                    jumpStr = $" · {jumps} jump{(jumps == 1 ? "" : "s")} (0 with next-frame log interval; see orange/cyan markers)";
                } else {
                    jumpStr = $" · {jumps} jump{(jumps == 1 ? "" : "s")}, {_tracker.LogCorrelatedCount} with next-frame dither/center interval in log";
                }

                string logStr;
                if (!_tracker.LogWasFound)
                    logStr = " · NINA log: not found";
                else if (_tracker.LogTriggerCount == 0)
                    logStr = " · NINA log: read, 0 sequencer triggers in date window";
                else
                    logStr = $" · NINA log: {_tracker.LogTriggerCount} trigger(s), {_tracker.LogSequencerEdgeCount} between-frame interval(s)";

                // RA/Dec spread (works for both modes from the delta values).
                var minRa  = samples.Min(s => s.DeltaRaArcSec);
                var maxRa  = samples.Max(s => s.DeltaRaArcSec);
                var minDec = samples.Min(s => s.DeltaDecArcSec);
                var maxDec = samples.Max(s => s.DeltaDecArcSec);
                var raRange  = maxRa  - minRa;
                var decRange = maxDec - minDec;
                var spreadStr = $"ΔRA {minRa:+0.0;-0.0}″…{maxRa:+0.0;-0.0}″ ({raRange:F1}″ pp) · ΔDec {minDec:+0.0;-0.0}″…{maxDec:+0.0;-0.0}″ ({decRange:F1}″ pp)";

                if (samples[0].IsPixelPath) {
                    // Prefer derived RA/Dec spread when conversion succeeded.
                    string pixSpreadStr;
                    if (samples[0].HasPixelDerivedRaDec) {
                        var minPxRa  = samples.Min(s => s.PixelDerivedRaArcSec!.Value);
                        var maxPxRa  = samples.Max(s => s.PixelDerivedRaArcSec!.Value);
                        var minPxDec = samples.Min(s => s.PixelDerivedDecArcSec!.Value);
                        var maxPxDec = samples.Max(s => s.PixelDerivedDecArcSec!.Value);
                        pixSpreadStr = $"ΔRA {minPxRa:+0.0;-0.0}″…{maxPxRa:+0.0;-0.0}″ ({maxPxRa - minPxRa:F1}″ pp) · " +
                                       $"ΔDec {minPxDec:+0.0;-0.0}″…{maxPxDec:+0.0;-0.0}″ ({maxPxDec - minPxDec:F1}″ pp)" +
                                       $" [{_plugin.Settings.MountMode}]";
                    } else {
                        var maxDist = samples.Max(s =>
                            Math.Sqrt(s.CumulativePixelX!.Value * s.CumulativePixelX.Value +
                                      s.CumulativePixelY!.Value * s.CumulativePixelY.Value));
                        string psExtra = "";
                        if (_tracker.PlateScaleArcSecPerPx.HasValue) {
                            var ps = _tracker.PlateScaleArcSecPerPx.Value;
                            psExtra = $" (≈{maxDist * ps:F0}″ · {ps:F2}″/px)";
                        }
                        pixSpreadStr = $"max drift {maxDist:F1}px{psExtra}";
                    }
                    return $"{n} frames · {pixSpreadStr}{jumpStr}{logStr} · {last.TargetName}";
                }

                return $"{n} frames · {spreadStr}{jumpStr}{logStr} · {last.TargetName}";
            }
        }

        private void ImportFitsFolder() {
            try {
                using var dlg = new System.Windows.Forms.FolderBrowserDialog {
                    Description = "Select folder containing FITS lights (.fits / .fit / .fts)",
                    UseDescriptionForTitle = true
                };

                // Priority: last folder used this session → NINA image save path → HTML export folder → Documents
                var initial = _lastImportFolder;
                if (string.IsNullOrWhiteSpace(initial))
                    initial = _profileService.ActiveProfile?.ImageFileSettings?.FilePath;
                if (string.IsNullOrWhiteSpace(initial))
                    initial = _plugin.HtmlExportFolder;
                if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial))
                    dlg.SelectedPath = initial;

                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dlg.SelectedPath))
                    return;

                _lastImportFolder = dlg.SelectedPath;
                _tracker.ImportFitsFolder(dlg.SelectedPath);
            } catch (Exception ex) {
                Logger.Error($"SeeDrift folder import failed: {ex.Message}");
            }
        }
    }
}
