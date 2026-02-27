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

namespace PlayniteAchievements.Providers.RPCS3
{
    /// <summary>
    /// Result of RPCS3 path validation for UI display.
    /// </summary>
    public class Rpcs3ValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; }
        public string UserId { get; set; }
        public int TrophyFolderCount { get; set; }
    }

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

        private Dictionary<string, string> _trophyFolderCache;
        private readonly object _cacheLock = new object();

        // Cached user ID discovery
        private string _cachedUserId;
        private string _cachedEmulatorRoot;

        public Rpcs3DataProvider(ILogger logger, PlayniteAchievementsSettings settings, IPlayniteAPI playniteApi)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _settings = settings;
            _logger = logger;
            _playniteApi = playniteApi;

            _scanner = new Rpcs3Scanner(_logger, _settings, this, _playniteApi);
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
        /// Validates an RPCS3 installation path has the expected structure.
        /// Returns validation result with error message, discovered user ID, and trophy folder count.
        /// </summary>
        public Rpcs3ValidationResult ValidateRpcs3Path(string path)
        {
            var result = new Rpcs3ValidationResult();

            if (string.IsNullOrWhiteSpace(path))
            {
                result.ErrorMessage = ResourceProvider.GetString("LOCPlayAch_Rpcs3Validation_InvalidPath");
                if (string.IsNullOrWhiteSpace(result.ErrorMessage))
                    result.ErrorMessage = "Path is required.";
                return result;
            }

            if (!Directory.Exists(path))
            {
                result.ErrorMessage = string.Format(
                    ResourceProvider.GetString("LOCPlayAch_Rpcs3Validation_InvalidPath") ?? "Directory does not exist: {0}",
                    path);
                return result;
            }

            // Check for dev_hdd0/home structure
            var homePath = Path.Combine(path, "dev_hdd0", "home");
            if (!Directory.Exists(homePath))
            {
                result.ErrorMessage = ResourceProvider.GetString("LOCPlayAch_Rpcs3Validation_NotRpcs3")
                    ?? "Not a valid RPCS3 installation. Missing: dev_hdd0\\home";
                return result;
            }

            // Find user ID
            var userId = DiscoverUserId(path);
            if (string.IsNullOrWhiteSpace(userId))
            {
                result.ErrorMessage = ResourceProvider.GetString("LOCPlayAch_Rpcs3Validation_NoUser")
                    ?? "No user profile found in dev_hdd0\\home";
                return result;
            }

            // Check for trophy folder
            var trophyPath = Path.Combine(path, "dev_hdd0", "home", userId, "trophy");
            if (!Directory.Exists(trophyPath))
            {
                result.ErrorMessage = string.Format(
                    ResourceProvider.GetString("LOCPlayAch_Rpcs3Validation_NoTrophyFolder")
                        ?? "Trophy folder not found: dev_hdd0\\home\\{0}\\trophy",
                    userId);
                return result;
            }

            // Count trophy folders
            try
            {
                var count = Directory.GetDirectories(trophyPath)
                    .Count(d => File.Exists(Path.Combine(d, "TROPCONF.SFM")));
                result.TrophyFolderCount = count;
            }
            catch
            {
                result.TrophyFolderCount = 0;
            }

            result.IsValid = true;
            result.UserId = userId;
            return result;
        }

        /// <summary>
        /// Auto-discovers the user ID by scanning dev_hdd0/home for numeric directories.
        /// RPCS3 user IDs are 8-digit numeric strings (e.g., 00000001).
        /// </summary>
        private string DiscoverUserId(string emulatorRoot)
        {
            if (string.IsNullOrWhiteSpace(emulatorRoot))
                return null;

            // Return cached value if emulator root hasn't changed
            if (!string.IsNullOrWhiteSpace(_cachedUserId) && _cachedEmulatorRoot == emulatorRoot)
            {
                return _cachedUserId;
            }

            var homePath = Path.Combine(emulatorRoot, "dev_hdd0", "home");
            if (!Directory.Exists(homePath))
                return null;

            try
            {
                foreach (var dir in Directory.GetDirectories(homePath))
                {
                    var name = Path.GetFileName(dir);
                    // RPCS3 user IDs are 8-digit numeric strings
                    if (!string.IsNullOrWhiteSpace(name) && name.Length == 8 && name.All(char.IsDigit))
                    {
                        _cachedUserId = name;
                        _cachedEmulatorRoot = emulatorRoot;
                        return name;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"[RPCS3] Error scanning '{homePath}'");
            }

            return null;
        }

        /// <summary>
        /// Gets the RPCS3 emulator root from the game's emulator action.
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
                        // Check if this looks like RPCS3
                        if (IsRpcs3Emulator(emulator))
                        {
                            return emulator.InstallDir;
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Checks if an emulator is RPCS3 by name or install directory.
        /// </summary>
        private bool IsRpcs3Emulator(Emulator emulator)
        {
            var name = emulator.Name ?? string.Empty;
            var installDir = emulator.InstallDir ?? string.Empty;
            return name.IndexOf("rpcs3", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   installDir.IndexOf("rpcs3", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Finds any RPCS3 emulator in the Playnite database.
        /// </summary>
        private string FindAnyRpcs3EmulatorRoot()
        {
            var emulators = _playniteApi?.Database?.Emulators;
            if (emulators == null) return null;

            foreach (var emulator in emulators)
            {
                if (IsRpcs3Emulator(emulator) && !string.IsNullOrWhiteSpace(emulator.InstallDir))
                {
                    return emulator.InstallDir;
                }
            }
            return null;
        }

        /// <summary>
        /// Gets the emulator root directory from an executable path.
        /// </summary>
        private string GetEmulatorRootFromExePath(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath))
                return null;

            if (!File.Exists(exePath))
                return null;

            return Path.GetDirectoryName(exePath);
        }

        /// <summary>
        /// Gets the trophy folder path using priority order:
        /// 1. User settings (validated)
        /// 2. Game's emulator config
        /// 3. First RPCS3 emulator in database
        /// </summary>
        public string GetTrophyFolder(Game game = null)
        {
            string emulatorRoot = null;

            // Priority 1: From settings (user-configured, validated)
            var settingsExePath = _settings?.Persisted?.Rpcs3ExecutablePath;
            if (!string.IsNullOrWhiteSpace(settingsExePath))
            {
                var settingsRoot = GetEmulatorRootFromExePath(settingsExePath);
                if (!string.IsNullOrWhiteSpace(settingsRoot))
                {
                    var validation = ValidateRpcs3Path(settingsRoot);
                    if (validation.IsValid)
                    {
                        emulatorRoot = settingsRoot;
                    }
                }
            }

            // Priority 2: From game's emulator config
            if (string.IsNullOrWhiteSpace(emulatorRoot) && game != null)
            {
                emulatorRoot = GetEmulatorRootFromGame(game);
            }

            // Priority 3: From first RPCS3 emulator in database
            if (string.IsNullOrWhiteSpace(emulatorRoot))
            {
                emulatorRoot = FindAnyRpcs3EmulatorRoot();
            }

            if (string.IsNullOrWhiteSpace(emulatorRoot))
            {
                return null;
            }

            var userId = DiscoverUserId(emulatorRoot);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return null;
            }

            var trophyFolder = Path.Combine(emulatorRoot, "dev_hdd0", "home", userId, "trophy");
            return trophyFolder;
        }

        public bool IsAuthenticated
        {
            get
            {
                var trophyFolder = GetTrophyFolder();

                if (string.IsNullOrWhiteSpace(trophyFolder))
                {
                    return false;
                }

                var exists = Directory.Exists(trophyFolder);
                return exists;
            }
        }

        public bool IsCapable(Game game)
        {
            if (game == null)
            {
                return false;
            }

            // Check source name
            var src = game.Source?.Name ?? string.Empty;
            if (src.IndexOf("RPCS3", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Source matches, but still verify trophy data exists
                return CanFindTrophyDataForGame(game);
            }

            // Check if game uses RPCS3 emulator
            var emulatorRoot = GetEmulatorRootFromGame(game);
            if (!string.IsNullOrWhiteSpace(emulatorRoot))
            {
                // Emulator matches, but still verify trophy data exists
                return CanFindTrophyDataForGame(game);
            }

            return false;
        }

        /// <summary>
        /// Checks if trophy data can be found for a game by verifying the npcommid exists in cache.
        /// </summary>
        private bool CanFindTrophyDataForGame(Game game)
        {
            var cache = GetOrBuildTrophyFolderCache();
            if (cache == null || cache.Count == 0)
            {
                return false;
            }

            // Try to find npcommid using path extraction
            var installDir = ExpandGamePath(game, game?.InstallDirectory);
            if (!string.IsNullOrWhiteSpace(installDir))
            {
                var match = Ps3IdPattern.Match(installDir);
                if (match.Success)
                {
                    var ps3Id = match.Groups[1].Value.ToUpperInvariant();
                    if (cache.ContainsKey(ps3Id))
                    {
                        return true;
                    }
                }
            }

            // If we can't verify by ID, but have a valid emulator source, still return true
            // This allows name-based matching during the actual scan
            return true;
        }

        // PS3 title/serial ID patterns: BLUS, BLES, BCES, NPUB, NPEB, etc.
        private static readonly System.Text.RegularExpressions.Regex Ps3IdPattern =
            new System.Text.RegularExpressions.Regex(@"\b([A-Z]{2,4}\d{5})\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

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
            var cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var trophyPath = GetTrophyFolder();
            if (string.IsNullOrWhiteSpace(trophyPath))
            {
                return cache;
            }

            if (!Directory.Exists(trophyPath))
            {
                return cache;
            }

            try
            {
                var npcommidDirectories = Directory.GetDirectories(trophyPath);

                foreach (var npcommidDir in npcommidDirectories)
                {
                    var npcommid = Path.GetFileName(npcommidDir);
                    if (string.IsNullOrWhiteSpace(npcommid))
                    {
                        continue;
                    }

                    // Verify TROPCONF.SFM exists
                    var tropconfPath = Path.Combine(npcommidDir, "TROPCONF.SFM");
                    if (File.Exists(tropconfPath))
                    {
                        cache[npcommid] = npcommidDir;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"[RPCS3] Failed to enumerate trophy directories at '{trophyPath}'");
            }

            return cache;
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
