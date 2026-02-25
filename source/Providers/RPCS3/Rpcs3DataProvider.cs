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

namespace PlayniteAchievements.Providers.RPCS3
{
    /// <summary>
    /// Data provider for RPCS3 PlayStation 3 emulator trophy tracking.
    /// Parses local trophy files (TROPCONF.SFM + TROPUSR.DAT) from RPCS3 installation.
    /// </summary>
    internal sealed class Rpcs3DataProvider : IDataProvider
    {
        private readonly Rpcs3Scanner _scanner;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly ILogger _logger;
        private readonly IPlayniteAPI _playniteApi;
        private readonly string _pluginUserDataPath;

        private Dictionary<string, string> _trophyFolderCache;
        private readonly object _cacheLock = new object();

        /// <summary>
        /// Default RPCS3 user profile path relative to emulator root.
        /// RPCS3 stores trophies in: {EmulatorRoot}\dev_hdd0\home\00000001\trophy\
        /// </summary>
        private const string DefaultUserPath = "dev_hdd0\\home\\00000001";

        public Rpcs3DataProvider(ILogger logger, PlayniteAchievementsSettings settings, IPlayniteAPI playniteApi, string pluginUserDataPath)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _settings = settings;
            _logger = logger;
            _playniteApi = playniteApi;
            _pluginUserDataPath = pluginUserDataPath;

            _scanner = new Rpcs3Scanner(_logger, _settings, this, _playniteApi, _pluginUserDataPath);
        }

        public string ProviderName
        {
            get
            {
                var value = ResourceProvider.GetString("LOCPlayAch_Provider_RPCS3");
                return string.IsNullOrWhiteSpace(value) ? "RPCS3" : value;
            }
        }

        public string ProviderKey => "RPCS3";

        public string ProviderIconKey => "ProviderIconRPCS3";

        public string ProviderColorHex => "#686DE0";

        /// <summary>
        /// Gets the RPCS3 emulator root directory from settings.
        /// This should be the folder where RPCS3 is installed (e.g., E:\Juegos\RPCS3).
        /// </summary>
        private string EmulatorRoot => _settings?.Persisted?.Rpcs3InstallationFolder;

        /// <summary>
        /// Gets the derived trophy folder path based on emulator root and default user.
        /// Path: {EmulatorRoot}\dev_hdd0\home\00000001\trophy
        /// </summary>
        private string TrophyFolder
        {
            get
            {
                var root = EmulatorRoot;
                if (string.IsNullOrWhiteSpace(root))
                {
                    return null;
                }
                return Path.Combine(root, DefaultUserPath, "trophy");
            }
        }

        public bool IsAuthenticated
        {
            get
            {
                var root = EmulatorRoot;
                _logger?.Debug($"[RPCS3] IsAuthenticated check - EmulatorRoot: '{root ?? "(null)"}'");

                if (string.IsNullOrWhiteSpace(root))
                {
                    _logger?.Debug("[RPCS3] IsAuthenticated = false (no emulator root configured)");
                    return false;
                }

                var trophyPath = TrophyFolder;
                var exists = Directory.Exists(trophyPath);
                _logger?.Debug($"[RPCS3] IsAuthenticated check - Trophy path: '{trophyPath}', Exists: {exists}");
                return exists;
            }
        }

        public bool IsCapable(Game game)
        {
            if (game == null)
            {
                _logger?.Debug("[RPCS3] IsCapable = false (game is null)");
                return false;
            }

            _logger?.Debug($"[RPCS3] IsCapable check for game '{game.Name}' (Id: {game.Id})");

            // Fast path: check source name
            var src = (game.Source?.Name ?? string.Empty).Trim();
            _logger?.Debug($"[RPCS3] IsCapable check - Source name: '{src}'");
            if (src.IndexOf("RPCS3", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _logger?.Debug($"[RPCS3] IsCapable = true for '{game.Name}' (matched source name 'RPCS3')");
                return true;
            }

            // Check if game is configured to use RPCS3 emulator
            if (UsesRpcs3Emulator(game))
            {
                _logger?.Debug($"[RPCS3] IsCapable = true for '{game.Name}' (uses RPCS3 emulator)");
                return true;
            }

            _logger?.Debug($"[RPCS3] IsCapable = false for '{game.Name}' (no RPCS3 match found)");
            return false;
        }

        /// <summary>
        /// Checks if any game action uses an emulator whose InstallDir matches
        /// the configured RPCS3 installation folder.
        /// </summary>
        private bool UsesRpcs3Emulator(Game game)
        {
            if (game?.GameActions == null)
            {
                _logger?.Debug($"[RPCS3] UsesRpcs3Emulator = false for '{game?.Name ?? "(null)"}' (no game actions)");
                return false;
            }

            var rpcs3Root = EmulatorRoot;
            if (string.IsNullOrWhiteSpace(rpcs3Root))
            {
                _logger?.Debug("[RPCS3] UsesRpcs3Emulator = false (no RPCS3 emulator root configured)");
                return false;
            }

            _logger?.Debug($"[RPCS3] UsesRpcs3Emulator check - Configured emulator root: '{rpcs3Root}'");
            _logger?.Debug($"[RPCS3] UsesRpcs3Emulator check - Game has {game.GameActions.Count} game actions");

            foreach (var action in game.GameActions)
            {
                _logger?.Debug($"[RPCS3] UsesRpcs3Emulator check - Action: '{action?.Name ?? "(null)"}', Type: {action?.Type ?? GameActionType.URL}, EmulatorId: {action?.EmulatorId ?? Guid.Empty}");

                if (action?.Type == GameActionType.Emulator && action.EmulatorId != Guid.Empty)
                {
                    var emulator = _playniteApi?.Database?.Emulators?.Get(action.EmulatorId);
                    if (emulator == null || string.IsNullOrWhiteSpace(emulator.InstallDir))
                    {
                        _logger?.Debug($"[RPCS3] UsesRpcs3Emulator check - Emulator not found or no InstallDir for EmulatorId: {action.EmulatorId}");
                        continue;
                    }

                    _logger?.Debug($"[RPCS3] UsesRpcs3Emulator check - Emulator '{emulator.Name}' InstallDir: '{emulator.InstallDir}'");

                    // Compare emulator's InstallDir with configured RPCS3 root
                    if (PathsEqual(emulator.InstallDir, rpcs3Root))
                    {
                        _logger?.Debug($"[RPCS3] UsesRpcs3Emulator = true (emulator InstallDir matches RPCS3 root)");
                        return true;
                    }
                }
            }

            _logger?.Debug($"[RPCS3] UsesRpcs3Emulator = false for '{game.Name}' (no matching emulator found)");
            return false;
        }

        private bool PathsEqual(string path1, string path2)
        {
            if (string.IsNullOrWhiteSpace(path1) || string.IsNullOrWhiteSpace(path2))
            {
                _logger?.Debug($"[RPCS3] PathsEqual = false (null/empty path) - path1: '{path1 ?? "(null)"}', path2: '{path2 ?? "(null)"}'");
                return false;
            }

            try
            {
                var full1 = Path.GetFullPath(path1.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var full2 = Path.GetFullPath(path2.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var equal = string.Equals(full1, full2, StringComparison.OrdinalIgnoreCase);
                _logger?.Debug($"[RPCS3] PathsEqual: '{full1}' vs '{full2}' = {equal}");
                return equal;
            }
            catch (Exception ex)
            {
                var equal = string.Equals(path1.Trim(), path2.Trim(), StringComparison.OrdinalIgnoreCase);
                _logger?.Debug($"[RPCS3] PathsEqual (fallback): '{path1}' vs '{path2}' = {equal}, Exception: {ex.Message}");
                return equal;
            }
        }

        /// <summary>
        /// Expands path variables in game paths using Playnite's variable expansion.
        /// </summary>
        internal string ExpandGamePath(Game game, string path)
        {
            return PathExpansion.ExpandGamePath(_playniteApi, game, path);
        }

        internal Dictionary<string, string> GetOrBuildTrophyFolderCache()
        {
            lock (_cacheLock)
            {
                if (_trophyFolderCache != null)
                {
                    return _trophyFolderCache;
                }
                _trophyFolderCache = BuildTrophyFolderCache();
                return _trophyFolderCache;
            }
        }

        internal void ClearTrophyFolderCache()
        {
            lock (_cacheLock)
            {
                _trophyFolderCache = null;
            }
        }

        private Dictionary<string, string> BuildTrophyFolderCache()
        {
            _logger?.Debug("[RPCS3] BuildTrophyFolderCache - Starting cache build");
            var cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var trophyPath = TrophyFolder;
            if (string.IsNullOrWhiteSpace(trophyPath))
            {
                _logger?.Debug("[RPCS3] BuildTrophyFolderCache - No RPCS3 emulator root configured");
                return cache;
            }

            _logger?.Debug($"[RPCS3] BuildTrophyFolderCache - Emulator root: '{EmulatorRoot}'");
            _logger?.Debug($"[RPCS3] BuildTrophyFolderCache - Trophy path: '{trophyPath}'");

            if (!Directory.Exists(trophyPath))
            {
                _logger?.Debug($"[RPCS3] BuildTrophyFolderCache - Trophy folder not found at '{trophyPath}'");
                return cache;
            }

            try
            {
                var npcommidDirectories = Directory.GetDirectories(trophyPath);
                _logger?.Debug($"[RPCS3] BuildTrophyFolderCache - Found {npcommidDirectories.Length} directories in trophy folder");

                foreach (var npcommidDir in npcommidDirectories)
                {
                    var npcommid = Path.GetFileName(npcommidDir);
                    if (string.IsNullOrWhiteSpace(npcommid))
                    {
                        _logger?.Debug($"[RPCS3] BuildTrophyFolderCache - Skipping directory with empty name: '{npcommidDir}'");
                        continue;
                    }

                    // Verify TROPCONF.SFM exists
                    var tropconfPath = Path.Combine(npcommidDir, "TROPCONF.SFM");
                    if (File.Exists(tropconfPath))
                    {
                        cache[npcommid] = npcommidDir;
                        _logger?.Debug($"[RPCS3] BuildTrophyFolderCache - Added npcommid '{npcommid}' -> '{npcommidDir}'");
                    }
                    else
                    {
                        _logger?.Debug($"[RPCS3] BuildTrophyFolderCache - Skipping '{npcommid}' (no TROPCONF.SFM at '{tropconfPath}')");
                    }
                }

                _logger?.Debug($"[RPCS3] BuildTrophyFolderCache - Complete with {cache.Count} valid trophy folders");
                _logger?.Debug($"[RPCS3] BuildTrophyFolderCache - NPCommIDs found: [{string.Join(", ", cache.Keys)}]");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"[RPCS3] BuildTrophyFolderCache - Failed to enumerate trophy directories at '{trophyPath}'");
            }

            return cache;
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
