using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NINA.Plugin.SeeDrift.Utility;

namespace NINA.Plugin.SeeDrift.Services {

    /// <summary>Describes a single frame for video generation: FITS path + drift offset from frame 1.</summary>
    internal sealed record FrameInfo(string FitsPath, double DeltaRaArcSec, double DeltaDecArcSec);

    /// <summary>
    /// Generates per-target MP4 video previews from FITS image files.
    /// Streams stretched/debayered frames directly to FFmpeg stdin — no temporary files on disk.
    /// </summary>
    internal class FitsVideoGenerator {

        private readonly FFmpegManager _ffmpegManager;

        /// <summary>Frame rate for the output video (frames per second).</summary>
        public int FrameRate { get; init; } = 10;

        /// <summary>FFmpeg preset: "ultrafast", "fast", "medium", "slow".</summary>
        public string EncoderPreset { get; init; } = "fast";

        /// <summary>
        /// Optional target output width. If null, uses native FITS width.
        /// Height is computed proportionally.
        /// </summary>
        public int? TargetWidth { get; init; }

        /// <summary>
        /// When true, overlays a green drift reticle (+) at the FOV center position
        /// (adjusted by per-frame DeltaRaArcSec/DeltaDecArcSec) on each frame.
        /// </summary>
        public bool DrawDriftMarker { get; init; } = true;

        /// <summary>
        /// Fallback pixel scale (arcsec/pixel) used when the FITS header does not
        /// contain PIXSCALE, CD1_1, or CDELT1. Set per-device model (S50, S30, etc.).
        /// </summary>
        public double DefaultPixelScaleArcSec { get; init; } = 2.39;

        public FitsVideoGenerator(FFmpegManager ffmpegManager) {
            _ffmpegManager = ffmpegManager ?? throw new ArgumentNullException(nameof(ffmpegManager));
        }

        /// <summary>
        /// Generates a video for a single target.
        /// </summary>
        /// <param name="targetName">Target name (used for output filename).</param>
        /// <param name="frames">Ordered list of frame info (FITS path + drift deltas) for this target.</param>
        /// <param name="reportDirectory">Directory where the MP4 will be written.</param>
        /// <param name="isMultiTarget">True when the session has multiple targets (uses "{target}_preview.mp4" naming).</param>
        /// <param name="reportBaseName">Report filename base (e.g. "SeeDrift_v1_0_0_ran20260603_sess20260603") to uniquify output filenames.</param>
        /// <param name="progress">Progress reporter (0-100).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The path to the generated MP4 file.</returns>
        public async Task<string> GenerateVideoForTargetAsync(
            string targetName,
            IReadOnlyList<FrameInfo> frames,
            string reportDirectory,
            bool isMultiTarget,
            IProgress<int>? progress,
            CancellationToken cancellationToken,
            string? reportBaseName = null) {

            if (string.IsNullOrWhiteSpace(targetName))
                throw new ArgumentException("Target name is required", nameof(targetName));
            if (frames == null || frames.Count == 0)
                throw new ArgumentException("At least one frame is required", nameof(frames));
            if (string.IsNullOrWhiteSpace(reportDirectory))
                throw new ArgumentException("Report directory is required", nameof(reportDirectory));

            Directory.CreateDirectory(reportDirectory);
            var totalFrames = frames.Count;

            // Determine output filename
            var outputPath = BuildOutputPath(reportDirectory, targetName, isMultiTarget, reportBaseName);

            // Read the first FITS to determine dimensions and pixel scale
            var firstImage = FitsImageReader.TryReadImageData(frames[0].FitsPath);
            if (firstImage == null)
                throw new InvalidOperationException($"Cannot read FITS image data from first file: {frames[0].FitsPath}");

            var width = firstImage.Width;
            var height = firstImage.Height;

            // Apply resolution scaling if configured
            var (outWidth, outHeight) = ComputeOutputDimensions(width, height);
            var scaleFilter = (outWidth != width || outHeight != height)
                ? $",scale={outWidth}:{outHeight}:flags=bilinear"
                : "";

            // Pixel scale for crosshair positioning (arcsec/pixel, cached from first frame)
            var pixelScale = firstImage.PixelScaleArcSec ?? DefaultPixelScaleArcSec;
            SeeDriftLog.Debug($"Using pixel scale {pixelScale:F3} arcsec/px for reticle overlay" +
                (firstImage.PixelScaleArcSec.HasValue ? " (from FITS header)" : $" (fallback for device)"));

            SeeDriftLog.Info($"Starting video generation for target '{targetName}': {totalFrames} frames, {width}x{height} -> {outWidth}x{outHeight}, {FrameRate} fps");

            // Build and launch FFmpeg process
            var ffmpegPath = _ffmpegManager.GetFFmpegPath();
            var ffmpegArgs = BuildFFmpegArgs(outWidth, outHeight, FrameRate, EncoderPreset, outputPath, scaleFilter);

            using var process = new Process {
                StartInfo = new ProcessStartInfo(ffmpegPath, ffmpegArgs) {
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            // Capture stderr asynchronously for logging
            var stderrBuilder = new System.Text.StringBuilder();
            process.ErrorDataReceived += (_, e) => {
                if (e.Data != null) {
                    lock (stderrBuilder) stderrBuilder.AppendLine(e.Data);
                }
            };

            try {
                process.Start();
                process.BeginErrorReadLine();

                // We must use a separate task to close stdin after all frames are written,
                // because we're writing to StandardInput.BaseStream from this thread.
                var stdin = process.StandardInput.BaseStream;

                var centerX = outWidth / 2;
                var centerY = outHeight / 2;

                for (var frameIndex = 0; frameIndex < totalFrames; frameIndex++) {
                    cancellationToken.ThrowIfCancellationRequested();

                    var frame = frames[frameIndex];
                    SeeDriftLog.Debug($"Video frame {frameIndex + 1}/{totalFrames}: {Path.GetFileName(frame.FitsPath)}");

                    byte[] bgr24;
                    try {
                        var imageData = FitsImageReader.TryReadImageData(frame.FitsPath);
                        if (imageData == null) {
                            // If a frame fails, output a blank frame rather than failing entirely
                            bgr24 = GenerateBlankFrame(outWidth, outHeight);
                            SeeDriftLog.Warning($"Could not read FITS for frame {frameIndex + 1}, using blank: {frame.FitsPath}");
                        } else {
                            bgr24 = ImageStretch.ProcessToBgr24Sqrt(
                                imageData.Data, imageData.Width, imageData.Height,
                                imageData.Channels, imageData.BayerPattern);

                            // Resize if needed (software bilinear)
                            if (outWidth != imageData.Width || outHeight != imageData.Height) {
                                bgr24 = ResizeBgr24(bgr24, imageData.Width, imageData.Height, outWidth, outHeight);
                            }

                            // Draw drift reticle at the arcsec → pixel offset position
                            if (DrawDriftMarker) {
                                var px = (int)Math.Round(frame.DeltaRaArcSec / pixelScale);
                                var py = (int)Math.Round(frame.DeltaDecArcSec / pixelScale);
                                var crosshairX = centerX + px;
                                var crosshairY = centerY - py; // Dec positive = up in image, but Y=down in buffer
                                DrawCrosshair(bgr24, outWidth, outHeight, crosshairX, crosshairY);
                            }
                        }
                    } catch (Exception ex) {
                        SeeDriftLog.Warning($"Error processing frame {frameIndex + 1}, using blank: {ex.Message}");
                        bgr24 = GenerateBlankFrame(outWidth, outHeight);
                    }

                    await stdin.WriteAsync(bgr24, 0, bgr24.Length, cancellationToken).ConfigureAwait(false);
                    await stdin.FlushAsync(cancellationToken).ConfigureAwait(false);

                    // Report progress
                    var pct = (int)((double)(frameIndex + 1) / totalFrames * 100);
                    progress?.Report(pct);
                }

                // Close stdin to signal end of input to FFmpeg
                stdin.Close();
                await process.WaitForExitAsync().ConfigureAwait(false);

                if (process.ExitCode != 0) {
                    lock (stderrBuilder) {
                        var stderr = stderrBuilder.ToString();
                        SeeDriftLog.Error($"FFmpeg exited with code {process.ExitCode}. Stderr: {Truncate(stderr, 1000)}");
                    }
                    throw new FFmpegProcessException(
                        $"FFmpeg exited with code {process.ExitCode}. See SeeDrift.log for details.");
                }

                SeeDriftLog.Info($"Video generation complete for target '{targetName}': {outputPath} ({new FileInfo(outputPath).Length / 1024} KB)");
            } catch (OperationCanceledException) {
                SeeDriftLog.Info($"Video generation cancelled for target '{targetName}'");
                try { process.Kill(entireProcessTree: true); } catch { }
                // Delete partial output
                try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
                throw;
            } catch (FFmpegProcessException) {
                throw;
            } catch (Exception ex) {
                SeeDriftLog.Error($"Video generation failed for target '{targetName}': {ex.Message}");
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new VideoGenerationException($"Video generation failed for target '{targetName}': {ex.Message}", ex);
            }

            return outputPath;
        }

        /// <summary>
        /// Synchronous wrapper for generating video during report writing.
        /// </summary>
        public string GenerateVideoForTarget(
            string targetName,
            IReadOnlyList<FrameInfo> frames,
            string reportDirectory,
            bool isMultiTarget,
            string? reportBaseName = null) {

            try {
                var task = GenerateVideoForTargetAsync(
                    targetName, frames, reportDirectory, isMultiTarget, null, CancellationToken.None, reportBaseName);
                task.GetAwaiter().GetResult();
                return BuildOutputPath(reportDirectory, targetName, isMultiTarget, reportBaseName);
            } catch (AggregateException ae) {
                throw ae.InnerException ?? ae;
            }
        }

        /// <summary>
        /// Builds the output path for a preview video, incorporating the report base name for uniqueness.
        /// </summary>
        internal static string BuildOutputPath(string reportDirectory, string targetName, bool isMultiTarget, string? reportBaseName) {
            var prefix = string.IsNullOrEmpty(reportBaseName) ? "" : $"{SanitizeFileName(reportBaseName)}_";
            var sanitized = SanitizeFileName(targetName);
            var outputFilename = isMultiTarget
                ? $"{prefix}{sanitized}_preview.mp4"
                : $"{prefix}preview.mp4";
            return Path.Combine(reportDirectory, outputFilename);
        }

        private static string BuildFFmpegArgs(int width, int height, int fps, string preset, string outputPath, string scaleFilter) {
            // -y: overwrite output
            // -f rawvideo: input format is raw video
            // -vcodec rawvideo: input codec is raw
            // -s WxH: input frame size
            // -pix_fmt bgr24: input pixel format (BGR 8-bit per channel)
            // -r fps: input frame rate
            // -i -: read from stdin
            // -c:v libx264: output codec
            // -preset: encoding speed preset
            // -pix_fmt yuv420p: output pixel format (compatible with all browsers)
            // -vf: video filter (scale if needed)
            var vf = string.IsNullOrEmpty(scaleFilter)
                ? ""
                : $" -vf \"format=yuv420p{scaleFilter}\"";
            // Default: just ensure yuv420p for browser compatibility
            var defaultVf = string.IsNullOrEmpty(scaleFilter) ? " -vf \"format=yuv420p\"" : "";

            return $"-y -f rawvideo -vcodec rawvideo -s {width}x{height} -pix_fmt bgr24 -r {fps} -i - " +
                   $"-c:v libx264 -preset {preset} -crf 26 -tune grain{defaultVf}{vf} \"{outputPath}\"";
        }

        private static (int width, int height) ComputeOutputDimensions(int nativeWidth, int nativeHeight, int? targetWidth = null) {
            // If no target width specified, use native resolution
            if (!targetWidth.HasValue)
                return (nativeWidth, nativeHeight);

            var tw = targetWidth.Value;
            if (tw >= nativeWidth)
                return (nativeWidth, nativeHeight);

            var ratio = (double)tw / nativeWidth;
            var th = (int)(nativeHeight * ratio);
            // Ensure even dimensions (required by many codecs)
            if (th % 2 != 0) th++;
            return (tw % 2 != 0 ? tw + 1 : tw, th);
        }

        /// <summary>Sanitizes a target name for use in filenames.</summary>
        internal static string SanitizeFileName(string name) {
            if (string.IsNullOrWhiteSpace(name))
                return "Unknown";
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = string.Concat(name.Trim().Select(c => invalid.Contains(c) ? '_' : c));
            return string.IsNullOrWhiteSpace(sanitized) ? "Unknown" : sanitized;
        }

        /// <summary>Generates a dark gray blank frame (BGR24).</summary>
        private static byte[] GenerateBlankFrame(int width, int height) {
            var size = width * height * 3;
            var frame = new byte[size];
            // Medium gray (128) for visibility
            Array.Fill<byte>(frame, 128);
            return frame;
        }

        /// <summary>
        /// Draws a 15px red crosshair (+) on a BGR24 buffer at the specified pixel coordinates.
        /// Coordinates are clamped to image bounds. Uses bright green so it stands out against
        /// the dark sky background and doesn't blend with red/blue star colors.
        /// </summary>
        private static void DrawCrosshair(byte[] bgr24, int width, int height, int cx, int cy) {
            const int armLen = 7; // pixels from center to each arm tip
            const byte r = 0;     // BGR: R channel
            const byte g = 255;   // BGR: G channel (bright green)
            const byte b = 0;     // BGR: B channel

            // Vertical arm (x = cx, y from cy-armLen to cy+armLen)
            for (var dy = -armLen; dy <= armLen; dy++) {
                var py = cy + dy;
                if (py < 0 || py >= height) continue;
                if (cx < 0 || cx >= width) continue;
                var idx = (py * width + cx) * 3;
                bgr24[idx]     = b;
                bgr24[idx + 1] = g;
                bgr24[idx + 2] = r;
            }

            // Horizontal arm (y = cy, x from cx-armLen to cx+armLen)
            for (var dx = -armLen; dx <= armLen; dx++) {
                var px = cx + dx;
                if (px < 0 || px >= width) continue;
                if (cy < 0 || cy >= height) continue;
                var idx = (cy * width + px) * 3;
                bgr24[idx]     = b;
                bgr24[idx + 1] = g;
                bgr24[idx + 2] = r;
            }
        }

        /// <summary>
        /// Simple bilinear resize for BGR24 images.
        /// </summary>
        private static byte[] ResizeBgr24(byte[] src, int srcW, int srcH, int dstW, int dstH) {
            var dst = new byte[dstW * dstH * 3];
            var xRatio = (double)srcW / dstW;
            var yRatio = (double)srcH / dstH;

            for (var dy = 0; dy < dstH; dy++) {
                var srcYf = dy * yRatio;
                var srcYi = (int)srcYf;
                var yFrac = srcYf - srcYi;
                var sy0 = Math.Clamp(srcYi, 0, srcH - 1);
                var sy1 = Math.Clamp(srcYi + 1, 0, srcH - 1);

                for (var dx = 0; dx < dstW; dx++) {
                    var srcXf = dx * xRatio;
                    var srcXi = (int)srcXf;
                    var xFrac = srcXf - srcXi;
                    var sx0 = Math.Clamp(srcXi, 0, srcW - 1);
                    var sx1 = Math.Clamp(srcXi + 1, 0, srcW - 1);

                    var di = (dy * dstW + dx) * 3;
                    for (var c = 0; c < 3; c++) {
                        var p00 = src[(sy0 * srcW + sx0) * 3 + c];
                        var p10 = src[(sy0 * srcW + sx1) * 3 + c];
                        var p01 = src[(sy1 * srcW + sx0) * 3 + c];
                        var p11 = src[(sy1 * srcW + sx1) * 3 + c];

                        var top = p00 * (1 - xFrac) + p10 * xFrac;
                        var bot = p01 * (1 - xFrac) + p11 * xFrac;
                        dst[di + c] = (byte)Math.Round(top * (1 - yFrac) + bot * yFrac);
                    }
                }
            }

            return dst;
        }

        private static string Truncate(string s, int maxLen) =>
            s == null ? "" : (s.Length <= maxLen ? s : s.Substring(0, maxLen) + "...");
    }

    /// <summary>Thrown when the FFmpeg process fails during video generation.</summary>
    internal class FFmpegProcessException : Exception {
        public FFmpegProcessException(string message) : base(message) { }
        public FFmpegProcessException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>Thrown when video generation fails for reasons other than FFmpeg process errors.</summary>
    internal class VideoGenerationException : Exception {
        public VideoGenerationException(string message) : base(message) { }
        public VideoGenerationException(string message, Exception inner) : base(message, inner) { }
    }
}
