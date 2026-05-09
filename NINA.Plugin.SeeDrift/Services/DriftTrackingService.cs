using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Image.Interfaces;
using NINA.PlateSolving;
using NINA.PlateSolving.Interfaces;
using NINA.Plugin.SeeDrift.Models;
using NINA.Plugin.SeeDrift.Utility;
namespace NINA.Plugin.SeeDrift.Services {

    /// <summary>
    /// Plate-solves saved LIGHT files discovered under the NINA image directory for a UTC observation window
    /// (SeeDrift Start→Stop, or the Test report window) and writes the rolling night HTML report.
    /// </summary>
    public sealed class DriftTrackingService : IDisposable {

        public sealed class CompletedTarget {
            public string Name { get; init; } = "";
            public DateTime StoppedUtc { get; init; }
            public IReadOnlyList<DriftSample> Samples { get; init; } = Array.Empty<DriftSample>();
        }

        private sealed class TraceState {
            public double? RefRaHours;
            public double? RefDecDeg;
            public int NextFrameIndex;
            public string? RefTargetName;
        }

        private readonly SeeDriftPlugin _plugin;
        private readonly IPlateSolverFactory _plateSolverFactory;
        private readonly IImageDataFactory _imageDataFactory;
        private bool _disposed;

        private DateTime? _armUtc;

        public List<CompletedTarget> CompletedTargets { get; } = new();

        public bool IsArmed { get; private set; }

        public int JumpCount { get; private set; }
        public int LogCorrelatedCount { get; private set; }
        public bool LogWasFound { get; private set; }
        public int LogTriggerCount { get; private set; }
        public int LogSequencerEdgeCount { get; private set; }
        public int LogTraceDitherTriggerCount { get; private set; }
        public int LogTraceCenterTriggerCount { get; private set; }

        public DriftTrackingService(
                SeeDriftPlugin plugin,
                IPlateSolverFactory plateSolverFactory,
                IImageDataFactory imageDataFactory) {
            _plugin = plugin;
            _plateSolverFactory = plateSolverFactory;
            _imageDataFactory = imageDataFactory;
        }

        public void Arm() {
            Application.Current?.Dispatcher.Invoke(() => {
                _armUtc = DateTime.UtcNow;
                IsArmed = true;
                Logger.Info("SeeDrift: armed (plate-solve on Stop).");
            });
        }

        /// <summary>Process files from NINA default image path between <see cref="_armUtc"/> and disarm time.</summary>
        public async Task DisarmAsync(IProgress<ApplicationStatus>? progress, CancellationToken token) {
            if (!IsArmed) {
                Logger.Warning("SeeDrift: Disarm called while not armed — ignored.");
                return;
            }

            var disarmUtc = DateTime.UtcNow;
            var startUtc = _armUtc ?? disarmUtc;

            await Application.Current!.Dispatcher.InvokeAsync(() => { IsArmed = false; });

            try {
                await RunDiskBatchAndReportAsync(startUtc, disarmUtc, resetSession: false, progress, token)
                    .ConfigureAwait(false);
            } finally {
                _armUtc = null;
            }
        }

        /// <summary>Test report: same pipeline using an observation UTC window (typically from plugin Test fields).</summary>
        public async Task RunTestReportAsync(
                DateTime obsStartUtc,
                DateTime obsEndUtc,
                IProgress<ApplicationStatus>? progress,
                CancellationToken token) {
            var a = obsStartUtc.Kind == DateTimeKind.Utc ? obsStartUtc : obsStartUtc.ToUniversalTime();
            var b = obsEndUtc.Kind == DateTimeKind.Utc ? obsEndUtc : obsEndUtc.ToUniversalTime();
            await RunDiskBatchAndReportAsync(a, b, resetSession: true, progress, token).ConfigureAwait(false);
        }

        public void ResetSession() {
            CompletedTargets.Clear();
            IsArmed = false;
            _armUtc = null;
            JumpCount = 0;
            LogCorrelatedCount = 0;
            LogWasFound = false;
            LogTriggerCount = 0;
            LogSequencerEdgeCount = 0;
            LogTraceDitherTriggerCount = 0;
            LogTraceCenterTriggerCount = 0;
        }

        private async Task RunDiskBatchAndReportAsync(
                DateTime obsStartUtc, DateTime obsEndUtc, bool resetSession,
                IProgress<ApplicationStatus>? progress, CancellationToken token) {

            if (resetSession)
                ResetSession();

            var root = NinaPaths.TryGetDefaultImageDirectory(_plugin.ProfileService);
            if (string.IsNullOrEmpty(root)) {
                Logger.Error("SeeDrift: NINA image file path is not set or folder does not exist. Check Options → General → file path.");
                return;
            }

            var profile = _plugin.ProfileService.ActiveProfile;
            if (profile?.PlateSolveSettings == null) {
                Logger.Error("SeeDrift: No active NINA profile or plate-solve settings.");
                return;
            }

            progress?.Report(new ApplicationStatus {
                Source = "SeeDrift",
                Status = "Scanning image folder for LIGHT files in your UTC window…",
                Progress = 0,
                MaxProgress = 0
            });

            var windowed = FitsFolderImport.EnumerateSortedRecursiveForUtcWindow(
                root,
                obsStartUtc,
                obsEndUtc,
                msg => progress?.Report(new ApplicationStatus {
                    Source = "SeeDrift",
                    Status = msg,
                    Progress = 0,
                    MaxProgress = 0
                }),
                token);

            if (windowed.Count < 2) {
                Logger.Warning($"SeeDrift: fewer than 2 LIGHT frames in window — no report ({windowed.Count} found).");
                return;
            }

            progress?.Report(new ApplicationStatus {
                Source = "SeeDrift",
                Status = $"Plate solving {windowed.Count} lights…",
                Progress = 0,
                MaxProgress = windowed.Count
            });

            var primary = _plateSolverFactory.GetPlateSolver(profile.PlateSolveSettings);
            var blind = _plateSolverFactory.GetBlindSolver(profile.PlateSolveSettings);
            var imageSolver = _plateSolverFactory.GetImageSolver(primary, blind);

            var built = new List<DriftSample>(windowed.Count);
            var trace = new TraceState();
            var i = 0;
            foreach (var entry in windowed) {
                token.ThrowIfCancellationRequested();
                i++;
                progress?.Report(new ApplicationStatus {
                    Source = "SeeDrift",
                    Status = $"Solving {i}/{windowed.Count} — {Path.GetFileName(entry.Path)}",
                    Progress = i,
                    MaxProgress = windowed.Count
                });

                if (!FitsCoordinates.TryReadPrimaryHeader(entry.Path, out var cards))
                    continue;

                var ps = PlateSolveHelper.BuildPlateSolveParameter(cards);

                NINA.Image.Interfaces.IImageData? img = null;
                try {
                    img = await _imageDataFactory.CreateFromFile(
                        entry.Path, 32, false, RawConverterEnum.FREEIMAGE, token).ConfigureAwait(false);
                } catch (Exception ex) {
                    Logger.Warning($"SeeDrift: could not load image for solve — {entry.Path}: {ex.Message}");
                    continue;
                }

                PlateSolveResult? result = null;
                try {
                    result = await imageSolver.Solve(img, ps, progress, token).ConfigureAwait(false);
                } catch (Exception ex) {
                    Logger.Warning($"SeeDrift: plate solve failed — {entry.Path}: {ex.Message}");
                } finally {
                    if (img is IDisposable d)
                        d.Dispose();
                }

                if (result == null || !PlateSolveHelper.TryResultToRaDecHours(result, out var raH, out var decD))
                    continue;

                var label = entry.TargetLabel;
                AccumulateFromParsed(raH, decD, entry.ExposureUtc, entry.Path, label, trace, null, null, out var sample);
                built.Add(sample);
            }

            if (built.Count < 2) {
                Logger.Warning($"SeeDrift: fewer than 2 plate-solved frames — no report segment.");
                return;
            }

            ApplyJumpAndLogAnnotation(built);

            var targetName = windowed[0].TargetLabel;
            if (string.IsNullOrWhiteSpace(targetName))
                targetName = "Unknown";

            await Application.Current!.Dispatcher.InvokeAsync(() => {
                CompletedTargets.Add(new CompletedTarget {
                    Name = targetName,
                    StoppedUtc = DateTime.UtcNow,
                    Samples = built
                });
                WriteNightReport();
            });

            Logger.Info($"SeeDrift: batch complete — {built.Count} solved frames → night report.");
        }

        private void ApplyJumpAndLogAnnotation(List<DriftSample> frames) {
            JumpDetector.AnnotateJumps(frames);
            var (logMatched, logFound, triggersLoaded, sequencerEdges, traceDithers, traceCenters) =
                NinaLogCorrelator.AnnotateWithLogEvents(frames);
            JumpCount = JumpDetector.CountJumps(frames);
            LogCorrelatedCount = logMatched;
            LogWasFound = logFound;
            LogTriggerCount = triggersLoaded;
            LogSequencerEdgeCount = sequencerEdges;
            LogTraceDitherTriggerCount = traceDithers;
            LogTraceCenterTriggerCount = traceCenters;
        }

        private void WriteNightReport() {
            if (CompletedTargets.Count == 0) return;
            try {
                var folder = _plugin.HtmlExportFolder;
                if (string.IsNullOrWhiteSpace(folder))
                    folder = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SeeDrift");
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, $"SeeDrift_night_{DateTime.Now:yyyyMMdd}.html");
                HtmlReportExporter.WriteNightReport(CompletedTargets, path);
                Logger.Info($"SeeDrift: night report saved → {path}");
            } catch (Exception ex) {
                Logger.Error($"SeeDrift: failed to write night report: {ex.Message}");
            }
        }

        private static void AccumulateFromParsed(
            double raHours,
            double decDeg,
            DateTime exposureUtc,
            string path,
            string label,
            TraceState st,
            double? cumulativePixelX,
            double? cumulativePixelY,
            out DriftSample sample) {

            if (st.RefRaHours == null || st.RefDecDeg == null) {
                st.RefRaHours = raHours;
                st.RefDecDeg = decDeg;
                st.RefTargetName = label;
            }

            AstrometryMath.DeltaArcSec(st.RefRaHours.Value, st.RefDecDeg.Value, raHours, decDeg,
                out var dRa, out var dDec);

            var utc = exposureUtc.Kind == DateTimeKind.Utc ? exposureUtc : exposureUtc.ToUniversalTime();

            sample = new DriftSample {
                FrameIndex = st.NextFrameIndex++,
                ExposureStartUtc = utc,
                FileName = Path.GetFileName(path),
                TargetName = label,
                DeltaRaArcSec = dRa,
                DeltaDecArcSec = dDec,
                RawRaHours = raHours,
                RawDecDeg = decDeg,
                CumulativePixelX = cumulativePixelX,
                CumulativePixelY = cumulativePixelY
            };
        }

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
