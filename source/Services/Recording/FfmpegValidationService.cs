using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Playnite.SDK;

namespace PlayniteAchievements.Services.Recording
{
    /// <summary>
    /// Validates a user-supplied ffmpeg build: parses -version, probes -encoders for the H.264
    /// encoders the capture command can use, and optionally runs a 1-second gdigrab smoke test to
    /// the null muxer. Results are cached per path for the session; drives both the settings Test
    /// button and the recording service's Auto encoder selection.
    /// </summary>
    internal sealed class FfmpegValidationService
    {
        private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

        private static readonly string[] KnownEncoders =
        {
            "h264_nvenc",
            "h264_qsv",
            "h264_amf",
            "libx264"
        };

        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<string, FfmpegValidationResult> _cache =
            new ConcurrentDictionary<string, FfmpegValidationResult>(StringComparer.OrdinalIgnoreCase);

        public FfmpegValidationService(ILogger logger)
        {
            _logger = logger;
        }

        public sealed class FfmpegValidationResult
        {
            public bool IsValid { get; set; }

            public string Version { get; set; }

            public IReadOnlyList<string> AvailableEncoders { get; set; } = new List<string>();

            /// <summary>Diagnostic detail for the settings status line when invalid.</summary>
            public string Error { get; set; }
        }

        /// <summary>
        /// Validates the ffmpeg at the given path. Cached per path per session (the smoke test
        /// only upgrades a cached probe-only result, never repeats).
        /// </summary>
        public async Task<FfmpegValidationResult> ValidateAsync(string ffmpegPath, bool runSmokeTest = false)
        {
            var path = ffmpegPath?.Trim();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return new FfmpegValidationResult { IsValid = false, Error = "file not found" };
            }

            if (_cache.TryGetValue(path, out var cached))
            {
                return cached;
            }

            var result = await ProbeAsync(path, runSmokeTest).ConfigureAwait(false);
            _cache[path] = result;
            return result;
        }

        private async Task<FfmpegValidationResult> ProbeAsync(string path, bool runSmokeTest)
        {
            var result = new FfmpegValidationResult();

            var versionLines = await RunProbeAsync(path, RecordingCommandBuilder.VersionProbeArguments)
                .ConfigureAwait(false);
            result.Version = ParseVersion(versionLines);
            if (result.Version == null)
            {
                result.Error = "-version probe failed";
                return result;
            }

            var encoderLines = await RunProbeAsync(path, RecordingCommandBuilder.EncodersProbeArguments)
                .ConfigureAwait(false);
            result.AvailableEncoders = ParseEncoders(encoderLines);
            if (result.AvailableEncoders.Count == 0)
            {
                result.Error = "no usable H.264 encoder";
                return result;
            }

            if (runSmokeTest)
            {
                var smokeOk = await RunSmokeTestAsync(path).ConfigureAwait(false);
                if (!smokeOk)
                {
                    result.Error = "screen capture test failed";
                    return result;
                }
            }

            result.IsValid = true;
            return result;
        }

        /// <summary>Runs one short-lived probe and returns its stdout lines (null on failure).</summary>
        private async Task<IReadOnlyList<string>> RunProbeAsync(string path, string arguments)
        {
            using (var host = new FfmpegProcessHost(path, arguments, _logger, captureStdOut: true))
            {
                if (!host.Start())
                {
                    return null;
                }

                var exitCode = await host.WaitForExitAsync(ProbeTimeout).ConfigureAwait(false);
                return exitCode == 0 ? host.StdOutLines : null;
            }
        }

        private async Task<bool> RunSmokeTestAsync(string path)
        {
            using (var host = new FfmpegProcessHost(
                       path,
                       RecordingCommandBuilder.BuildSmokeTestArguments(),
                       _logger))
            {
                if (!host.Start())
                {
                    return false;
                }

                var exitCode = await host.WaitForExitAsync(ProbeTimeout).ConfigureAwait(false);
                if (exitCode != 0)
                {
                    _logger?.Debug($"ffmpeg smoke test failed (exit={exitCode}): {host.StdErrTail}");
                    return false;
                }

                return true;
            }
        }

        internal static string ParseVersion(IReadOnlyList<string> stdOutLines)
        {
            var first = stdOutLines?.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
            if (first == null)
            {
                return null;
            }

            var match = Regex.Match(first, @"ffmpeg version (\S+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        internal static IReadOnlyList<string> ParseEncoders(IReadOnlyList<string> stdOutLines)
        {
            if (stdOutLines == null)
            {
                return new List<string>();
            }

            return KnownEncoders
                .Where(encoder => stdOutLines.Any(line =>
                    line != null &&
                    Regex.IsMatch(line, $@"\b{Regex.Escape(encoder)}\b")))
                .ToList();
        }
    }
}
