using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Providers;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        public string ProviderColorHex => "#752bfd";

        public bool IsAuthenticated
        {
            get
            {
                var gameDataPath = GetGameDataPath();
                return !string.IsNullOrWhiteSpace(gameDataPath) && Directory.Exists(gameDataPath);
            }
        }

        /// <summary>
        /// Gets the game data path using priority order:
        /// 1. User settings (validated)
        /// 2. Game's emulator config
        /// 3. First ShadPS4 emulator in database
        /// </summary>
        public string GetGameDataPath(Game game = null)
        {
            string emulatorRoot = null;

            // Priority 1: From settings (user-configured)
            var settingsRoot = _settings?.Persisted?.ShadPS4InstallationFolder;
            if (!string.IsNullOrWhiteSpace(settingsRoot))
            {
                var gameDataPath = Path.Combine(settingsRoot, "user", "game_data");
                if (Directory.Exists(gameDataPath))
                {
                    emulatorRoot = settingsRoot;
                    _logger?.Debug($"[ShadPS4] GetGameDataPath - Using validated settings path: '{emulatorRoot}'");
                }
                else
                {
                    _logger?.Debug($"[ShadPS4] GetGameDataPath - Settings path invalid (no game_data folder)");
                }
            }

            // Priority 2: From game's emulator config
            if (string.IsNullOrWhiteSpace(emulatorRoot) && game != null)
            {
                emulatorRoot = GetEmulatorRootFromGame(game);
                if (!string.IsNullOrWhiteSpace(emulatorRoot))
                {
                    _logger?.Debug($"[ShadPS4] GetGameDataPath - Using game emulator path: '{emulatorRoot}'");
                }
            }

            // Priority 3: From first ShadPS4 emulator in database
            if (string.IsNullOrWhiteSpace(emulatorRoot))
            {
                emulatorRoot = FindAnyShadps4EmulatorRoot();
                if (!string.IsNullOrWhiteSpace(emulatorRoot))
                {
                    _logger?.Debug($"[ShadPS4] GetGameDataPath - Using discovered emulator path: '{emulatorRoot}'");
                }
            }

            if (string.IsNullOrWhiteSpace(emulatorRoot))
            {
                _logger?.Debug("[ShadPS4] GetGameDataPath - No emulator root found");
                return null;
            }

            var finalPath = Path.Combine(emulatorRoot, "user", "game_data");
            _logger?.Debug($"[ShadPS4] GetGameDataPath - Final game_data path: '{finalPath}'");
            return finalPath;
        }

        /// <summary>
        /// Gets the ShadPS4 emulator root from the game's emulator action.
        /// </summary>
        private string GetEmulatorRootFromGame(Game game)
        {
            if (game?.GameActions == null) return null;

            foreach (var action in game.GameActions)
            {
                if (action?.Type == GameActionType.Emulator && action.EmulatorId != Guid.Empty)
                {
                    var emulator = _playniteApi?.Database?.Emulators?.Get(action.EmulatorId);
                    if (emulator != null && !string.IsNullOrWhiteSpace(emulator.InstallDir))
                    {
                        // Check if this looks like ShadPS4
                        if (IsShadps4Emulator(emulator))
                        {
                            _logger?.Debug($"[ShadPS4] GetEmulatorRootFromGame - Found ShadPS4 emulator '{emulator.Name}' with InstallDir: '{emulator.InstallDir}'");
                            return emulator.InstallDir;
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Checks if an emulator is ShadPS4 by name or install directory.
        /// </summary>
        private bool IsShadps4Emulator(Emulator emulator)
        {
            var name = emulator.Name ?? string.Empty;
            var installDir = emulator.InstallDir ?? string.Empty;
            return name.IndexOf("shadps4", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   installDir.IndexOf("shadps4", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Finds any ShadPS4 emulator in the Playnite database.
        /// </summary>
        private string FindAnyShadps4EmulatorRoot()
        {
            var emulators = _playniteApi?.Database?.Emulators;
            if (emulators == null) return null;

            foreach (var emulator in emulators)
            {
                if (IsShadps4Emulator(emulator) && !string.IsNullOrWhiteSpace(emulator.InstallDir))
                {
                    _logger?.Debug($"[ShadPS4] FindAnyShadps4EmulatorRoot - Found ShadPS4 emulator '{emulator.Name}' with InstallDir: '{emulator.InstallDir}'");
                    return emulator.InstallDir;
                }
            }
            return null;
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

            var gameDataPath = GetGameDataPath();
            if (string.IsNullOrWhiteSpace(gameDataPath))
            {
                _logger?.Debug("[ShadPS4] No valid game_data path found");
                return cache;
            }

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
            IReadOnlyList<Game> gamesToRefresh,
            Action<Game> onGameStarting,
            Func<Game, GameAchievementData, Task> onGameCompleted,
            CancellationToken cancel)
        {
            return _scanner.RefreshAsync(gamesToRefresh, onGameStarting, onGameCompleted, cancel);
        }
    }
}
