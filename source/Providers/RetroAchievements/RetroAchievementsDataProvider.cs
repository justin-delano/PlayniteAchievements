using PlayniteAchievements.Models;
using PlayniteAchievements.Providers;
using PlayniteAchievements.Providers.RetroAchievements.Hashing;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.RetroAchievements
{
    internal sealed class RetroAchievementsDataProvider : IDataProvider, IDisposable
    {
        private readonly ILogger _logger;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly string _pluginUserDataPath;

        private readonly object _initLock = new object();
        private RetroAchievementsApiClient _apiClient;
        private RetroAchievementsHashIndexStore _hashIndexStore;
        private RetroAchievementsScanner _scanner;

        private string _clientUsername;
        private string _clientApiKey;

        public RetroAchievementsDataProvider(ILogger logger, PlayniteAchievementsSettings settings, string pluginUserDataPath)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _pluginUserDataPath = pluginUserDataPath ?? string.Empty;
        }
        public string ProviderName => "RetroAchievements";
        public bool IsCapable(Game game)
        {
            if (game == null) return false;

            if (string.IsNullOrWhiteSpace(_settings.Persisted.RaUsername) || string.IsNullOrWhiteSpace(_settings.Persisted.RaWebApiKey))
            {
                return false;
            }

            if (!RaConsoleIdResolver.TryResolve(game, out var consoleId))
            {
                return false;
            }

            var hasher = RaHasherFactory.Create(consoleId, _settings, _logger);
            if (hasher == null)
            {
                return false;
            }

            return ResolveCandidateFilePaths(game).Any(p =>
                !string.IsNullOrWhiteSpace(p) &&
                (File.Exists(p) || ArchiveUtils.IsArchivePath(p)));
        }

        public Task<RebuildPayload> ScanAsync(
            List<Game> gamesToScan,
            Action<ProviderScanUpdate> progressCallback,
            Action<GameAchievementData> onGameScanned,
            CancellationToken cancel)
        {
            EnsureInitialized();
            return _scanner.ScanAsync(gamesToScan, progressCallback, onGameScanned, cancel);
        }

        private void EnsureInitialized()
        {
            var username = _settings.Persisted.RaUsername?.Trim() ?? string.Empty;
            var apiKey = _settings.Persisted.RaWebApiKey?.Trim() ?? string.Empty;

            lock (_initLock)
            {
                if (_scanner != null && string.Equals(username, _clientUsername, StringComparison.Ordinal) &&
                    string.Equals(apiKey, _clientApiKey, StringComparison.Ordinal))
                {
                    return;
                }

                _apiClient?.Dispose();
                _apiClient = new RetroAchievementsApiClient(_logger, username, apiKey);
                _hashIndexStore = new RetroAchievementsHashIndexStore(_logger, _settings, _apiClient, _pluginUserDataPath);
                _scanner = new RetroAchievementsScanner(_logger, _settings, _apiClient, _hashIndexStore);

                _clientUsername = username;
                _clientApiKey = apiKey;
            }
        }

        // private bool TryResolveConsoleId(Game game, out int consoleId)
        //     => RaConsoleIdResolver.TryResolve(game, out consoleId);

        private IEnumerable<string> ResolveCandidateFilePaths(Game game)
        {
            if (game?.Roms != null)
            {
                foreach (var rom in game.Roms)
                {
                    var p = ResolvePath(game, rom?.Path);
                    if (!string.IsNullOrWhiteSpace(p))
                    {
                        yield return p;
                    }
                }
            }

            if (game?.GameActions != null)
            {
                foreach (var act in game.GameActions)
                {
                    var p = ResolvePath(game, act?.Path);
                    if (!string.IsNullOrWhiteSpace(p) && !p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        yield return p;
                    }
                }
            }
        }

        private static string ResolvePath(Game game, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var p = path.Trim().Trim('"');

            try
            {
                if (p.IndexOf("{InstallDir}", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    !string.IsNullOrWhiteSpace(game?.InstallDirectory))
                {
                    p = ReplaceInsensitive(p, "{InstallDir}", game.InstallDirectory);
                }

                if (!Path.IsPathRooted(p) && !string.IsNullOrWhiteSpace(game?.InstallDirectory))
                {
                    p = Path.Combine(game.InstallDirectory, p);
                }

                return p;
            }
            catch
            {
                return null;
            }
        }

        private static string ReplaceInsensitive(string input, string oldValue, string newValue)
        {
            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(oldValue))
            {
                return input;
            }

            var idx = input.IndexOf(oldValue, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                return input;
            }

            var sb = new System.Text.StringBuilder(input.Length);
            var start = 0;
            while (idx >= 0)
            {
                sb.Append(input.Substring(start, idx - start));
                sb.Append(newValue ?? string.Empty);
                start = idx + oldValue.Length;
                idx = input.IndexOf(oldValue, start, StringComparison.OrdinalIgnoreCase);
            }
            sb.Append(input.Substring(start));
            return sb.ToString();
        }

        public void Dispose()
        {
            _apiClient?.Dispose();
        }
    }
}
