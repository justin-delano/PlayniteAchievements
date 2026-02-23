using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Providers;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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

        private Dictionary<string, string> _trophyFolderCache;
        private readonly object _cacheLock = new object();

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

        public string ProviderColorHex => "#0070D1";

        public bool IsAuthenticated
        {
            get
            {
                var installFolder = _settings?.Persisted?.Rpcs3InstallationFolder;
                if (string.IsNullOrWhiteSpace(installFolder))
                {
                    return false;
                }

                var trophyPath = Path.Combine(installFolder, "trophy");
                return Directory.Exists(trophyPath);
            }
        }

        public bool IsCapable(Game game)
        {
            if (game == null)
            {
                return false;
            }

            // Fast path: check source name
            var src = (game.Source?.Name ?? string.Empty).Trim();
            if (src.IndexOf("RPCS3", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            // Check platform (existing behavior)
            if (IsPs3Platform(game))
            {
                return true;
            }

            // Fallback: check if game has matching trophy data in RPCS3
            return HasTrophyDataInRpcs3(game);
        }

        private static bool IsPs3Platform(Game game)
        {
            var platforms = game.Platforms;
            if (platforms == null)
            {
                return false;
            }

            foreach (var platform in platforms)
            {
                if (platform == null) continue;

                var platformName = (platform.Name ?? string.Empty).Trim();
                if (platformName.IndexOf("PlayStation 3", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    platformName.IndexOf("PS3", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasTrophyDataInRpcs3(Game game)
        {
            var gameDirectory = ExpandGamePath(game, game?.InstallDirectory);
            if (string.IsNullOrWhiteSpace(gameDirectory) || !Directory.Exists(gameDirectory))
            {
                return false;
            }

            var cache = GetOrBuildTrophyFolderCache();
            if (cache == null || cache.Count == 0)
            {
                return false;
            }

            try
            {
                // Search for TROPHY.TRP files
                var trophyTrpFiles = Directory.GetFiles(gameDirectory, "TROPHY.TRP", SearchOption.AllDirectories);
                foreach (var trophyTrpPath in trophyTrpFiles)
                {
                    var npcommid = Rpcs3TrophyParser.ExtractNpCommId(trophyTrpPath, _logger);
                    if (!string.IsNullOrWhiteSpace(npcommid) && cache.ContainsKey(npcommid))
                    {
                        return true;
                    }
                }

                // Fallback: match by directory name
                var dirName = Path.GetFileName(gameDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (!string.IsNullOrWhiteSpace(dirName) && cache.ContainsKey(dirName))
                {
                    return true;
                }
            }
            catch
            {
                // Ignore errors during directory traversal
            }

            return false;
        }

        /// <summary>
        /// Expands path variables in game paths using Playnite's variable expansion.
        /// </summary>
        internal string ExpandGamePath(Game game, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            try
            {
                // Use Playnite's built-in variable expansion
                var expanded = _playniteApi?.ExpandGameVariables(game, path) ?? path;

                // Handle additional custom variables
                if (expanded.IndexOf("{InstallDir}", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var installDir = game?.InstallDirectory;
                    if (!string.IsNullOrWhiteSpace(installDir))
                    {
                        expanded = ReplaceInsensitive(expanded, "{InstallDir}", installDir);
                    }
                }

                return expanded;
            }
            catch
            {
                return path;
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

            var sb = new StringBuilder(input.Length);
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

            var installFolder = _settings?.Persisted?.Rpcs3InstallationFolder;
            if (string.IsNullOrWhiteSpace(installFolder))
            {
                return cache;
            }

            var trophyPath = Path.Combine(installFolder, "trophy");
            if (!Directory.Exists(trophyPath))
            {
                _logger?.Debug($"[RPCS3] Trophy folder not found at {trophyPath}");
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

                _logger?.Debug($"[RPCS3] Built trophy folder cache with {cache.Count} games");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[RPCS3] Failed to enumerate trophy directories.");
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
