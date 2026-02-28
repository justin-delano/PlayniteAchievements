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
        /// 1. User settings (validated game_data folder)
        /// 2. Game's emulator config
        /// 3. First ShadPS4 emulator in database
        /// </summary>
        public string GetGameDataPath(Game game = null)
        {
            _logger?.Debug($"[ShadPS4] GetGameDataPath: called for game '{game?.Name}'");

            // Priority 1: From settings (user-configured game_data path)
            var settingsGameDataPath = _settings?.Persisted?.ShadPS4GameDataPath;
            _logger?.Debug($"[ShadPS4] GetGameDataPath: settings ShadPS4GameDataPath = '{settingsGameDataPath}'");
            if (!string.IsNullOrWhiteSpace(settingsGameDataPath) && Directory.Exists(settingsGameDataPath))
            {
                _logger?.Debug($"[ShadPS4] GetGameDataPath: using settings path (exists): '{settingsGameDataPath}'");
                return settingsGameDataPath;
            }

            // Priority 2: From game's emulator config
            var emulatorRoot = GetEmulatorRootFromGame(game);
            _logger?.Debug($"[ShadPS4] GetGameDataPath: emulator root from game = '{emulatorRoot}'");
            if (!string.IsNullOrWhiteSpace(emulatorRoot))
            {
                var gameDataPath = Path.Combine(emulatorRoot, "user", "game_data");
                if (Directory.Exists(gameDataPath))
                {
                    _logger?.Debug($"[ShadPS4] GetGameDataPath: using game emulator path (exists): '{gameDataPath}'");
                    return gameDataPath;
                }
                _logger?.Debug($"[ShadPS4] GetGameDataPath: game emulator derived path does not exist: '{gameDataPath}'");
            }

            // Priority 3: From first ShadPS4 emulator in database
            emulatorRoot = FindAnyShadps4EmulatorRoot();
            _logger?.Debug($"[ShadPS4] GetGameDataPath: any ShadPS4 emulator root from database = '{emulatorRoot}'");
            if (!string.IsNullOrWhiteSpace(emulatorRoot))
            {
                var gameDataPath = Path.Combine(emulatorRoot, "user", "game_data");
                _logger?.Debug($"[ShadPS4] GetGameDataPath: returning database emulator path: '{gameDataPath}'");
                return gameDataPath;
            }

            _logger?.Debug($"[ShadPS4] GetGameDataPath: no game_data path found, returning null");
            return null;
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
                            return emulator.InstallDir;
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Checks if an emulator is ShadPS4 by built-in ID, name, or install directory.
        /// </summary>
        private bool IsShadps4Emulator(Emulator emulator)
        {
            var builtInId = emulator.BuiltInConfigId ?? string.Empty;
            var name = emulator.Name ?? string.Empty;
            var installDir = emulator.InstallDir ?? string.Empty;
            return builtInId.IndexOf("shadps4", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("shadps4", StringComparison.OrdinalIgnoreCase) >= 0 ||
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
                _logger?.Debug("[ShadPS4] IsCapable: game is null, returning false");
                return false;
            }

            _logger?.Debug($"[ShadPS4] IsCapable: checking game '{game.Name}' (Id: {game.Id})");

            // Check if game is configured to use ShadPS4 emulator (by path or name)
            var usesShadPs4 = UsesShadPS4Emulator(game);
            _logger?.Debug($"[ShadPS4] IsCapable: UsesShadPS4Emulator = {usesShadPs4}");

            if (usesShadPs4)
            {
                // Emulator matches, but still verify title ID can be extracted
                var titleId = ExtractTitleIdFromGame(game);
                _logger?.Debug($"[ShadPS4] IsCapable: extracted titleId = '{titleId}' (from emulator path)");

                if (!string.IsNullOrWhiteSpace(titleId))
                {
                    var cache = GetOrBuildTitleCache();
                    _logger?.Debug($"[ShadPS4] IsCapable: cache has {cache?.Count ?? 0} entries");
                    if (cache != null && cache.ContainsKey(titleId))
                    {
                        _logger?.Debug($"[ShadPS4] IsCapable: titleId '{titleId}' found in cache, returning true");
                        return true;
                    }
                    _logger?.Debug($"[ShadPS4] IsCapable: titleId '{titleId}' NOT in cache");
                }
                // Even without cache match, if emulator is ShadPS4, game may be capable
                _logger?.Debug($"[ShadPS4] IsCapable: emulator is ShadPS4, returning true (may be capable)");
                return true;
            }

            // Extract title ID from game's install directory and look it up in cache
            var titleId2 = ExtractTitleIdFromGame(game);
            _logger?.Debug($"[ShadPS4] IsCapable: extracted titleId = '{titleId2}' (from install dir)");

            if (string.IsNullOrWhiteSpace(titleId2))
            {
                _logger?.Debug($"[ShadPS4] IsCapable: no titleId extracted, returning false");
                return false;
            }

            var cache2 = GetOrBuildTitleCache();
            _logger?.Debug($"[ShadPS4] IsCapable: cache has {cache2?.Count ?? 0} entries");
            var found = cache2 != null && cache2.ContainsKey(titleId2);
            _logger?.Debug($"[ShadPS4] IsCapable: titleId '{titleId2}' in cache = {found}, returning {found}");
            return found;
        }

        /// <summary>
        /// Checks if any game action uses a ShadPS4 emulator.
        /// Checks by: 1) Path matching settings, 2) Emulator name containing "shadps4"
        /// </summary>
        private bool UsesShadPS4Emulator(Game game)
        {
            if (game?.GameActions == null)
            {
                _logger?.Debug($"[ShadPS4] UsesShadPS4Emulator: game.GameActions is null for '{game?.Name}'");
                return false;
            }

            _logger?.Debug($"[ShadPS4] UsesShadPS4Emulator: checking {game.GameActions.Count} actions for game '{game.Name}'");

            // Get settings path for comparison - derive emulator root from game_data path
            var shadps4GameDataPath = _settings?.Persisted?.ShadPS4GameDataPath;
            string shadps4InstallFolder = null;
            if (!string.IsNullOrWhiteSpace(shadps4GameDataPath))
            {
                // game_data is under user/, so go up two levels to get emulator root
                shadps4InstallFolder = Path.GetDirectoryName(Path.GetDirectoryName(shadps4GameDataPath));
                _logger?.Debug($"[ShadPS4] UsesShadPS4Emulator: settings game_data path = '{shadps4GameDataPath}', derived emulator root = '{shadps4InstallFolder}'");
            }
            else
            {
                _logger?.Debug($"[ShadPS4] UsesShadPS4Emulator: no ShadPS4GameDataPath in settings");
            }

            foreach (var action in game.GameActions)
            {
                _logger?.Debug($"[ShadPS4] UsesShadPS4Emulator: action type = {action?.Type}, emulatorId = {action?.EmulatorId}");

                if (action?.Type == GameActionType.Emulator && action.EmulatorId != Guid.Empty)
                {
                    var emulator = _playniteApi?.Database?.Emulators?.Get(action.EmulatorId);
                    if (emulator == null || string.IsNullOrWhiteSpace(emulator.InstallDir))
                    {
                        _logger?.Debug($"[ShadPS4] UsesShadPS4Emulator: emulator not found or no InstallDir for id {action.EmulatorId}");
                        continue;
                    }

                    _logger?.Debug($"[ShadPS4] UsesShadPS4Emulator: found emulator '{emulator.Name}', InstallDir = '{emulator.InstallDir}', BuiltInId = '{emulator.BuiltInConfigId}'");

                    // Check by emulator name (more reliable than path matching)
                    if (IsShadps4Emulator(emulator))
                    {
                        _logger?.Debug($"[ShadPS4] UsesShadPS4Emulator: emulator '{emulator.Name}' matches ShadPS4 pattern, returning true");
                        return true;
                    }

                    // Also check by path matching settings (if settings path is configured)
                    if (!string.IsNullOrWhiteSpace(shadps4InstallFolder) &&
                        PathsEqual(emulator.InstallDir, shadps4InstallFolder))
                    {
                        _logger?.Debug($"[ShadPS4] UsesShadPS4Emulator: emulator path matches settings path, returning true");
                        return true;
                    }
                }
            }

            _logger?.Debug($"[ShadPS4] UsesShadPS4Emulator: no matching ShadPS4 emulator found for game '{game.Name}'");
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
            var rawInstallDir = game?.InstallDirectory;
            var installDir = ExpandGamePath(game, rawInstallDir);
            _logger?.Debug($"[ShadPS4] ExtractTitleIdFromGame: raw install dir = '{rawInstallDir}', expanded = '{installDir}'");

            if (string.IsNullOrWhiteSpace(installDir))
            {
                _logger?.Debug($"[ShadPS4] ExtractTitleIdFromGame: install dir is empty, returning null");
                return null;
            }

            // Search for title ID pattern in the path
            var match = TitleIdPattern.Match(installDir);
            if (match.Success)
            {
                var titleId = match.Groups[1].Value.ToUpperInvariant();
                _logger?.Debug($"[ShadPS4] ExtractTitleIdFromGame: found titleId '{titleId}' in path");
                return titleId;
            }

            _logger?.Debug($"[ShadPS4] ExtractTitleIdFromGame: no title ID pattern match in path, returning null");
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
                return cache;
            }

            if (!Directory.Exists(gameDataPath))
            {
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
