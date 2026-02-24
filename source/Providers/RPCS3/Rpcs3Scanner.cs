using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Providers.RPCS3.Models;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.RPCS3
{
    /// <summary>
    /// Scanner for RPCS3 PlayStation 3 emulator trophy data.
    /// Orchestrates trophy folder discovery and game matching.
    /// </summary>
    internal sealed class Rpcs3Scanner
    {
        private readonly ILogger _logger;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly Rpcs3DataProvider _provider;
        private readonly IPlayniteAPI _playniteApi;

        // Default rarity estimates by trophy type
        private const double PlatinumRarity = 5.0;
        private const double GoldRarity = 15.0;
        private const double SilverRarity = 30.0;
        private const double BronzeRarity = 60.0;

        public Rpcs3Scanner(ILogger logger, PlayniteAchievementsSettings settings, Rpcs3DataProvider provider = null, IPlayniteAPI playniteApi = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _provider = provider;
            _playniteApi = playniteApi;
        }

        public async Task<RebuildPayload> RefreshAsync(
            List<Game> gamesToRefresh,
            Action<ProviderRefreshUpdate> progressCallback,
            Func<GameAchievementData, Task> OnGameRefreshed,
            CancellationToken cancel)
        {
            var report = progressCallback ?? (_ => { });
            var summary = new RebuildSummary();

            if (gamesToRefresh == null || gamesToRefresh.Count == 0)
            {
                return new RebuildPayload { Summary = summary };
            }

            // Use the provider's cache if available, otherwise build our own
            Dictionary<string, string> trophyFolderCache;
            if (_provider != null)
            {
                trophyFolderCache = _provider.GetOrBuildTrophyFolderCache();
            }
            else
            {
                trophyFolderCache = await BuildTrophyFolderCacheAsync(cancel).ConfigureAwait(false);
            }

            if (trophyFolderCache == null || trophyFolderCache.Count == 0)
            {
                _logger?.Warn("[RPCS3] No trophy folders found in RPCS3 trophy directory.");
                return new RebuildPayload { Summary = summary };
            }

            _logger?.Info($"[RPCS3] Using trophy folder cache with {trophyFolderCache.Count} games.");

            var providerName = GetProviderName();

            for (var i = 0; i < gamesToRefresh.Count; i++)
            {
                cancel.ThrowIfCancellationRequested();
                var game = gamesToRefresh[i];
                report(new ProviderRefreshUpdate { CurrentGameName = game?.Name });

                try
                {
                    var data = await FetchGameDataAsync(game, trophyFolderCache, providerName, cancel).ConfigureAwait(false);
                    if (data != null && OnGameRefreshed != null)
                    {
                        await OnGameRefreshed(data).ConfigureAwait(false);
                    }

                    summary.GamesRefreshed++;
                    if (data != null && data.HasAchievements)
                    {
                        summary.GamesWithAchievements++;
                    }
                    else
                    {
                        summary.GamesWithoutAchievements++;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Debug(ex, $"[RPCS3] Failed to scan {game?.Name}");
                }
            }

            report(new ProviderRefreshUpdate { CurrentGameName = null });
            return new RebuildPayload { Summary = summary };
        }

        /// <summary>
        /// Builds a cache mapping npcommid to trophy folder path.
        /// Trophy folder structure: rpcs3_install/trophy/npcommid/
        /// </summary>
        private async Task<Dictionary<string, string>> BuildTrophyFolderCacheAsync(CancellationToken cancel)
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
                _logger?.Warn($"[RPCS3] Trophy folder not found at {trophyPath}");
                return cache;
            }

            try
            {
                var npcommidDirectories = Directory.GetDirectories(trophyPath);
                foreach (var npcommidDir in npcommidDirectories)
                {
                    cancel.ThrowIfCancellationRequested();

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
                _logger?.Error(ex, "[RPCS3] Failed to enumerate trophy directories.");
            }

            return await Task.FromResult(cache).ConfigureAwait(false);
        }

        private Task<GameAchievementData> FetchGameDataAsync(
            Game game,
            Dictionary<string, string> trophyFolderCache,
            string providerName,
            CancellationToken cancel)
        {
            if (game == null)
            {
                return Task.FromResult<GameAchievementData>(null);
            }

            // Find npcommid for this game
            var npcommid = FindNpCommIdForGame(game, trophyFolderCache, cancel);

            if (string.IsNullOrWhiteSpace(npcommid))
            {
                _logger?.Debug($"[RPCS3] No npcommid found for game '{game.Name}'");
                return Task.FromResult(BuildNoAchievementsData(game, providerName));
            }

            cancel.ThrowIfCancellationRequested();

            // Look up trophy folder
            if (!trophyFolderCache.TryGetValue(npcommid, out var trophyFolderPath))
            {
                _logger?.Debug($"[RPCS3] Trophy folder not found for npcommid '{npcommid}'");
                return Task.FromResult(BuildNoAchievementsData(game, providerName));
            }

            var tropconfPath = Path.Combine(trophyFolderPath, "TROPCONF.SFM");
            var tropusrPath = Path.Combine(trophyFolderPath, "TROPUSR.DAT");

            if (!File.Exists(tropconfPath))
            {
                _logger?.Debug($"[RPCS3] TROPCONF.SFM not found at {tropconfPath}");
                return Task.FromResult(BuildNoAchievementsData(game, providerName));
            }

            try
            {
                // Parse trophy definitions
                var trophies = Rpcs3TrophyParser.ParseTrophyDefinitions(tropconfPath, _logger);

                // Parse unlock data
                if (File.Exists(tropusrPath))
                {
                    Rpcs3TrophyParser.ParseTrophyUnlockData(tropusrPath, trophies, _logger);
                }

                if (trophies.Count == 0)
                {
                    return Task.FromResult(BuildNoAchievementsData(game, providerName));
                }

                // Convert to achievements
                var achievements = new List<AchievementDetail>();
                var unlockedCount = 0;

                foreach (var trophy in trophies)
                {
                    if (trophy.Unlocked)
                    {
                        unlockedCount++;
                    }

                    var iconPath = GetTrophyIconPath(trophyFolderPath, trophy.Id);

                    var normalizedTrophyType = NormalizeTrophyType(trophy.TrophyType);
                    achievements.Add(new AchievementDetail
                    {
                        ApiName = trophy.Id.ToString(),
                        DisplayName = trophy.Name,
                        Description = trophy.Description,
                        UnlockedIconPath = iconPath,
                        LockedIconPath = iconPath,
                        Hidden = trophy.Hidden,
                        Unlocked = trophy.Unlocked,
                        UnlockTimeUtc = trophy.UnlockTimeUtc,
                        GlobalPercentUnlocked = GetRarityByTrophyType(trophy.TrophyType),
                        TrophyType = normalizedTrophyType,
                        IsCapstone = normalizedTrophyType == "platinum"
                    });
                }

                return Task.FromResult(new GameAchievementData
                {
                    ProviderName = providerName,
                    LibrarySourceName = game?.Source?.Name,
                    GameName = game?.Name,
                    PlayniteGameId = game?.Id,
                    HasAchievements = achievements.Count > 0,
                    Achievements = achievements,
                    LastUpdatedUtc = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"[RPCS3] Failed to parse trophy data for {game.Name}");
                return Task.FromResult(BuildNoAchievementsData(game, providerName));
            }
        }

        // PS3 title/serial ID patterns: BLUS, BLES, BCES, NPUB, NPEB, etc.
        private static readonly System.Text.RegularExpressions.Regex Ps3IdPattern =
            new System.Text.RegularExpressions.Regex(@"\b([A-Z]{2,4}\d{5})\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>
        /// Finds the npcommid for a game by extracting PS3 ID from the install path.
        /// </summary>
        private string FindNpCommIdForGame(Game game, Dictionary<string, string> trophyFolderCache, CancellationToken cancel)
        {
            var gameDirectory = ExpandGamePath(game, game?.InstallDirectory);

            // Extract PS3 ID from the path and look it up in cache
            if (!string.IsNullOrWhiteSpace(gameDirectory))
            {
                var match = Ps3IdPattern.Match(gameDirectory);
                if (match.Success)
                {
                    var ps3Id = match.Groups[1].Value.ToUpperInvariant();
                    if (trophyFolderCache.ContainsKey(ps3Id))
                    {
                        return ps3Id;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Expands path variables in game paths using Playnite's variable expansion.
        /// </summary>
        private string ExpandGamePath(Game game, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            // Use provider's expansion if available
            if (_provider != null)
            {
                return _provider.ExpandGamePath(game, path);
            }

            // Fallback: use Playnite API directly if available
            try
            {
                return _playniteApi?.ExpandGameVariables(game, path) ?? path;
            }
            catch
            {
                return path;
            }
        }

        private static GameAchievementData BuildNoAchievementsData(Game game, string providerName)
        {
            return new GameAchievementData
            {
                ProviderName = providerName,
                LibrarySourceName = game?.Source?.Name,
                GameName = game?.Name,
                PlayniteGameId = game?.Id,
                HasAchievements = false,
                LastUpdatedUtc = DateTime.UtcNow
            };
        }

        private static string GetProviderName()
        {
            var value = ResourceProvider.GetString("LOCPlayAch_Provider_RPCS3");
            return string.IsNullOrWhiteSpace(value) ? "RPCS3" : value;
        }

        private static double GetRarityByTrophyType(string trophyType)
        {
            if (string.IsNullOrWhiteSpace(trophyType))
            {
                return BronzeRarity;
            }

            return trophyType.ToUpperInvariant() switch
            {
                "P" => PlatinumRarity,
                "G" => GoldRarity,
                "S" => SilverRarity,
                "B" => BronzeRarity,
                _ => BronzeRarity
            };
        }

        private static string NormalizeTrophyType(string trophyType)
        {
            if (string.IsNullOrWhiteSpace(trophyType))
            {
                return null;
            }

            return trophyType.ToUpperInvariant() switch
            {
                "P" => "platinum",
                "G" => "gold",
                "S" => "silver",
                "B" => "bronze",
                _ => null
            };
        }

        private string GetTrophyIconPath(string trophyFolderPath, int trophyId)
        {
            if (string.IsNullOrWhiteSpace(trophyFolderPath))
            {
                return null;
            }

            try
            {
                // Trophy icons follow TROP###.PNG format with zero-padded ID
                var iconFileName = $"TROP{trophyId.ToString().PadLeft(3, '0')}.PNG";
                var iconPath = Path.Combine(trophyFolderPath, iconFileName);

                if (File.Exists(iconPath))
                {
                    return iconPath;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"[RPCS3] Failed to get icon path for trophy {trophyId}");
                return null;
            }
        }
    }
}
