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
using System.Xml.Linq;
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

        public bool IsCapable(Game game)
        {
            _logger?.Debug($"[ShadPS4] === IsCapable check for '{game?.Name}' ===");

            if (game == null)
            {
                _logger?.Debug($"[ShadPS4] IsCapable: game is null");
                return false;
            }

            // Fast path: check source name
            var src = (game.Source?.Name ?? string.Empty).Trim();
            _logger?.Debug($"[ShadPS4] IsCapable: source name = '{src}'");
            if (src.IndexOf("ShadPS4", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _logger?.Debug($"[ShadPS4] IsCapable: matched by source name");
                return true;
            }

            // Fallback: check if game exists in ShadPS4 game_data
            var cache = GetOrBuildTitleCache();
            _logger?.Debug($"[ShadPS4] IsCapable: cache has {cache?.Count ?? 0} entries");

            if (cache == null || cache.Count == 0)
            {
                _logger?.Debug($"[ShadPS4] IsCapable: cache is empty, returning false");
                return false;
            }

            var gameName = game.Name;
            var normalizedName = NormalizeGameName(gameName);
            _logger?.Debug($"[ShadPS4] IsCapable: game name = '{gameName}', normalized = '{normalizedName}'");

            // Log cache keys for debugging
            _logger?.Debug($"[ShadPS4] IsCapable: cache keys (first 20):");
            foreach (var key in cache.Keys.Take(20))
            {
                _logger?.Debug($"[ShadPS4]   '{key}'");
            }

            var found = !string.IsNullOrWhiteSpace(normalizedName) && cache.ContainsKey(normalizedName);
            _logger?.Debug($"[ShadPS4] IsCapable: cache lookup result = {found}");
            return found;
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

                    var xmlPath = Path.Combine(titleDir, "trophyfiles", "trophy00", "Xml", "TROP.XML");
                    if (!File.Exists(xmlPath))
                    {
                        continue;
                    }

                    try
                    {
                        var doc = XDocument.Load(xmlPath);
                        var titleNameElement = doc.Descendants("title-name").FirstOrDefault();
                        if (titleNameElement != null)
                        {
                            var titleName = titleNameElement.Value?.Trim();
                            if (!string.IsNullOrWhiteSpace(titleName))
                            {
                                var normalizedName = NormalizeGameName(titleName);
                                if (!string.IsNullOrWhiteSpace(normalizedName))
                                {
                                    cache[normalizedName] = titleId;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.Debug(ex, $"[ShadPS4] Failed to parse TROP.XML for {titleId}");
                    }
                }

                _logger?.Debug($"[ShadPS4] Built title cache with {cache.Count} games");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[ShadPS4] Failed to enumerate title directories.");
            }

            return cache;
        }

        internal static string NormalizeGameName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var normalized = name.ToLowerInvariant()
                .Replace(":", "")
                .Replace("-", "")
                .Replace("_", " ")
                .Replace("®", "")
                .Replace("™", "")
                .Replace("©", "")
                .Trim();

            while (normalized.Contains("  "))
            {
                normalized = normalized.Replace("  ", " ");
            }

            // Strip common TROP.XML suffixes (e.g., "Webbed Trophies" -> "Webbed")
            string[] suffixes = { " trophies", " trophy set", " trophy" };
            foreach (var suffix in suffixes)
            {
                if (normalized.EndsWith(suffix))
                {
                    normalized = normalized.Substring(0, normalized.Length - suffix.Length);
                    break;
                }
            }

            return normalized.Trim();
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
