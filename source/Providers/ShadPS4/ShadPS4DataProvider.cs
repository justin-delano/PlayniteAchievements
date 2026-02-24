using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Providers;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PlayniteAchievements.Common;

namespace PlayniteAchievements.Providers.ShadPS4
{
    internal sealed class ShadPS4DataProvider : IDataProvider
    {
        private readonly ShadPS4Scanner _scanner;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly ILogger _logger;
        private readonly IPlayniteAPI _playniteApi;

        private Dictionary<string, string> _titleCache;
        private readonly object _cacheLock = new object();

        public ShadPS4DataProvider(ILogger logger, PlayniteAchievementsSettings settings, IPlayniteAPI playniteApi)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _settings = settings;
            _logger = logger;
            _playniteApi = playniteApi;

            _scanner = new ShadPS4Scanner(_logger, _settings, this, _playniteApi);
        }

        public string ProviderName
        {
            get
            {
                var value = ResourceProvider.GetString("LOCPlayAch_Provider_ShadPS4");
                return string.IsNullOrWhiteSpace(value) ? "ShadPS4" : value;
            }
        }

        public string ProviderKey => "ShadPS4";

        public string ProviderIconKey => "ProviderIconShadPS4";

        public string ProviderColorHex => "#0070D1";

        public bool IsAuthenticated
        {
            get
            {
                var installFolder = _settings?.Persisted?.ShadPS4InstallationFolder;
                if (string.IsNullOrWhiteSpace(installFolder))
                {
                    return false;
                }

                var gameDataPath = Path.Combine(installFolder, "user", "game_data");
                return Directory.Exists(gameDataPath);
            }
        }

        // PS4 title ID patterns: CUSA (US), BCAS (Asia), PCAS (Asia digital), CUSA (Europe), etc.
        private static readonly System.Text.RegularExpressions.Regex TitleIdPattern =
            new System.Text.RegularExpressions.Regex(@"\b([A-Z]{4}\d{5})\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        public bool IsCapable(Game game)
        {
            if (game == null)
            {
                return false;
            }

            // Fast path: check source name
            var src = (game.Source?.Name ?? string.Empty).Trim();
            if (src.IndexOf("ShadPS4", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            // Check if game is configured to use ShadPS4 emulator
            if (UsesShadPS4Emulator(game))
            {
                return true;
            }

            // Extract title ID from game's install directory and look it up in cache
            var titleId = ExtractTitleIdFromGame(game);
            if (string.IsNullOrWhiteSpace(titleId))
            {
                return false;
            }

            var cache = GetOrBuildTitleCache();
            return cache != null && cache.ContainsKey(titleId);
        }

        /// <summary>
        /// Checks if any game action uses an emulator whose InstallDir matches
        /// the configured ShadPS4 installation folder.
        /// </summary>
        private bool UsesShadPS4Emulator(Game game)
        {
            if (game?.GameActions == null)
            {
                return false;
            }

            var shadps4InstallFolder = _settings?.Persisted?.ShadPS4InstallationFolder;
            if (string.IsNullOrWhiteSpace(shadps4InstallFolder))
            {
                return false;
            }

            foreach (var action in game.GameActions)
            {
                if (action?.Type == GameActionType.Emulator && action.EmulatorId != Guid.Empty)
                {
                    var emulator = _playniteApi?.Database?.Emulators?.Get(action.EmulatorId);
                    if (emulator == null || string.IsNullOrWhiteSpace(emulator.InstallDir))
                    {
                        continue;
                    }

                    // Compare emulator's InstallDir with configured ShadPS4 folder
                    // Use case-insensitive comparison with normalized paths
                    if (PathsEqual(emulator.InstallDir, shadps4InstallFolder))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool PathsEqual(string path1, string path2)
        {
            if (string.IsNullOrWhiteSpace(path1) || string.IsNullOrWhiteSpace(path2))
            {
                return false;
            }

            try
            {
                var full1 = Path.GetFullPath(path1.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var full2 = Path.GetFullPath(path2.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                return string.Equals(full1, full2, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(path1.Trim(), path2.Trim(), StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Extracts the PS4 title ID from the game's install directory path.
        /// PS4 title IDs follow pattern: AAAA12345 (e.g., CUSA00432)
        /// </summary>
        private string ExtractTitleIdFromGame(Game game)
        {
            var installDir = ExpandGamePath(game, game?.InstallDirectory);
            if (string.IsNullOrWhiteSpace(installDir))
            {
                return null;
            }

            // Search for title ID pattern in the path
            var match = TitleIdPattern.Match(installDir);
            if (match.Success)
            {
                return match.Groups[1].Value.ToUpperInvariant();
            }

            return null;
        }

        internal Dictionary<string, string> GetOrBuildTitleCache()
        {
            lock (_cacheLock)
            {
                if (_titleCache != null)
                {
                    return _titleCache;
                }
                _titleCache = BuildTitleIdCache();
                return _titleCache;
            }
        }

        internal void ClearTitleCache()
        {
            lock (_cacheLock)
            {
                _titleCache = null;
            }
        }

        /// <summary>
        /// Builds a cache of title ID to trophy data directory path.
        /// Cache structure: title_id (e.g., "CUSA00432") -> full path to game_data directory
        /// </summary>
        private Dictionary<string, string> BuildTitleIdCache()
        {
            var cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var installFolder = _settings?.Persisted?.ShadPS4InstallationFolder;
            if (string.IsNullOrWhiteSpace(installFolder))
            {
                return cache;
            }

            var gameDataPath = Path.Combine(installFolder, "user", "game_data");
            if (!Directory.Exists(gameDataPath))
            {
                _logger?.Debug($"[ShadPS4] user/game_data folder not found at {gameDataPath}");
                return cache;
            }

            try
            {
                var titleDirectories = Directory.GetDirectories(gameDataPath);
                foreach (var titleDir in titleDirectories)
                {
                    var titleId = Path.GetFileName(titleDir);
                    if (string.IsNullOrWhiteSpace(titleId))
                    {
                        continue;
                    }

                    // Verify trophy data exists
                    var xmlPath = Path.Combine(titleDir, "trophyfiles", "trophy00", "Xml", "TROP.XML");
                    if (File.Exists(xmlPath))
                    {
                        cache[titleId.ToUpperInvariant()] = titleDir;
                    }
                }

                _logger?.Debug($"[ShadPS4] Built title ID cache with {cache.Count} games");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[ShadPS4] Failed to enumerate title directories.");
            }

            return cache;
        }

        /// <summary>
        /// Expands path variables in game paths using Playnite's variable expansion.
        /// </summary>
        internal string ExpandGamePath(Game game, string path)
        {
            return PathExpansion.ExpandGamePath(_playniteApi, game, path);
        }

        public Task<RebuildPayload> RefreshAsync(
            List<Game> gamesToRefresh,
            Action<ProviderRefreshUpdate> progressCallback,
            Func<GameAchievementData, Task> OnGameRefreshed,
            CancellationToken cancel)
        {
            return _scanner.RefreshAsync(gamesToRefresh, progressCallback, OnGameRefreshed, cancel);
        }
    }
}
