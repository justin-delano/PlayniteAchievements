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
        private readonly IPlayniteAPI _playniteApi;

        private readonly object _initLock = new object();
        private RetroAchievementsApiClient _apiClient;
        private RetroAchievementsHashIndexStore _hashIndexStore;
        private RetroAchievementsScanner _scanner;

        private string _clientUsername;
        private string _clientApiKey;

        public RetroAchievementsDataProvider(ILogger logger, PlayniteAchievementsSettings settings, string pluginUserDataPath, IPlayniteAPI playniteApi)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _pluginUserDataPath = pluginUserDataPath ?? string.Empty;
            _playniteApi = playniteApi;
        }
        public string ProviderName => ResourceProvider.GetString("LOCPlayAch_Provider_RetroAchievements");
        public string ProviderIconKey => "ProviderIconRetroAchievements";
        public string ProviderColorHex => "#FFD700";

        /// <summary>
        /// Checks if RetroAchievements authentication is properly configured.
        /// Requires both RaUsername and RaWebApiKey to be present.
        /// </summary>
        public bool IsAuthenticated =>
            !string.IsNullOrWhiteSpace(_settings.Persisted.RaUsername) &&
            !string.IsNullOrWhiteSpace(_settings.Persisted.RaWebApiKey);

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
            Func<GameAchievementData, Task> onGameScanned,
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
                _scanner = new RetroAchievementsScanner(_logger, _settings, _apiClient, _hashIndexStore, _playniteApi);

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

        private string ResolvePath(Game game, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var p = path.Trim().Trim('"');

            try
            {
                // Get emulator for {EmulatorDir} expansion
                var emulator = GetGameEmulator(game);
                var emulatorDir = emulator?.InstallDir;

                // Expand {EmulatorDir} in game.InstallDirectory first
                var installDir = game?.InstallDirectory;
                if (!string.IsNullOrWhiteSpace(installDir) &&
                    installDir.IndexOf("{EmulatorDir}", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    !string.IsNullOrWhiteSpace(emulatorDir))
                {
                    installDir = ReplaceInsensitive(installDir, "{EmulatorDir}", emulatorDir);
                }

                // Expand standard Playnite variables (includes {InstallDir} -> game.InstallDirectory)
                p = _playniteApi?.ExpandGameVariables(game, p) ?? p;

                // Expand any {EmulatorDir} that remains after standard expansion
                if (p.IndexOf("{EmulatorDir}", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    !string.IsNullOrWhiteSpace(emulatorDir))
                {
                    p = ReplaceInsensitive(p, "{EmulatorDir}", emulatorDir);
                }

                // Handle relative paths using the (now expanded) install directory
                if (!Path.IsPathRooted(p) && !string.IsNullOrWhiteSpace(installDir))
                {
                    p = Path.Combine(installDir, p);
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
                return input;

            var idx = input.IndexOf(oldValue, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return input;

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

        private Emulator GetGameEmulator(Game game)
        {
            if (game?.GameActions == null) return null;

            foreach (var action in game.GameActions)
            {
                if (action?.Type == GameActionType.Emulator && action.EmulatorId != Guid.Empty)
                {
                    return _playniteApi?.Database?.Emulators?.Get(action.EmulatorId);
                }
            }
            return null;
        }

        public void Dispose()
        {
            _apiClient?.Dispose();
        }
    }
}
