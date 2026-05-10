using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NINA.Plugin.SeeDrift.Models;

namespace NINA.Plugin.SeeDrift.Services {

    internal static class SeestarDeviceInfoService {
        private static readonly Regex RxSeestarTelescope = new(
            @"Discovered\s+Alpaca\s+Device\s+Seestar\s+([A-Za-z0-9]+_[A-Za-z0-9]+)\s+Telescope",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Alpaca telephoto camera connect (CameraVM); discovery may omit "… Telescope".
        private static readonly Regex RxSeestarTelephotoCameraConnect = new(
            @"Successfully\s+connected\s+Camera\.[^\n]*?\bSeestar\s+([A-Za-z0-9]+_[A-Za-z0-9]+)\s+Telephoto\s+Camera\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// ASCOM disconnect (AscomDevice); session may lack connect lines if already connected at startup.
        /// </summary>
        private static readonly Regex RxSeestarTelephotoCameraDisconnect = new(
            @"Disconnecting\s+from[^\n]*?\bSeestar\s+([A-Za-z0-9]+_[A-Za-z0-9]+)\s+Telephoto\s+Camera\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static SeestarDeviceInfo FromLogFiles(IEnumerable<string>? logPaths) {
            var devices = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            if (logPaths != null) {
                foreach (var path in logPaths) {
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                        continue;
                    try {
                        foreach (var line in File.ReadLines(path)) {
                            var match = RxSeestarTelescope.Match(line);
                            if (match.Success) {
                                devices.Add(match.Groups[1].Value.Trim());
                                continue;
                            }

                            match = RxSeestarTelephotoCameraConnect.Match(line);
                            if (match.Success) {
                                devices.Add(match.Groups[1].Value.Trim());
                                continue;
                            }

                            match = RxSeestarTelephotoCameraDisconnect.Match(line);
                            if (match.Success)
                                devices.Add(match.Groups[1].Value.Trim());
                        }
                    } catch {
                        // Missing/locked logs should not block reports; scope identity is advisory metadata.
                    }
                }
            }

            if (devices.Count == 0)
                return SeestarDeviceInfo.Unknown;
            if (devices.Count > 1)
                return SeestarDeviceInfo.Mixed;

            return WithSafeFileNameToken(SeestarDeviceInfo.FromId(devices.First()));
        }

        private static SeestarDeviceInfo WithSafeFileNameToken(SeestarDeviceInfo info) {
            if (!info.IsKnown || string.IsNullOrWhiteSpace(info.FileNameToken))
                return info;

            var invalid = Path.GetInvalidFileNameChars();
            var token = new string(info.FileNameToken
                .Select(ch => invalid.Contains(ch) ? '_' : ch)
                .ToArray());
            return new SeestarDeviceInfo {
                Model = info.Model,
                Serial = info.Serial,
                DisplayName = info.DisplayName,
                FileNameToken = token,
                IsKnown = info.IsKnown,
                IsMixed = info.IsMixed
            };
        }
    }
}
