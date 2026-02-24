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
            _logger?.Debug("[RPCS3] RefreshAsync - Starting refresh");
            var report = progressCallback ?? (_ => { });
            var summary = new RebuildSummary();

            if (gamesToRefresh == null || gamesToRefresh.Count == 0)
            {
                _logger?.Debug("[RPCS3] RefreshAsync - No games to refresh");
                return new RebuildPayload { Summary = summary };
            }

            _logger?.Debug($"[RPCS3] RefreshAsync - Processing {gamesToRefresh.Count} games");

            // Use the provider's cache if available, otherwise build our own
            Dictionary<string, string> trophyFolderCache;
            if (_provider != null)
            {
                _logger?.Debug("[RPCS3] RefreshAsync - Using provider's trophy folder cache");
                trophyFolderCache = _provider.GetOrBuildTrophyFolderCache();
            }
            else
            {
                _logger?.Debug("[RPCS3] RefreshAsync - Building trophy folder cache directly");
                trophyFolderCache = await BuildTrophyFolderCacheAsync(cancel).ConfigureAwait(false);
            }

            if (trophyFolderCache == null || trophyFolderCache.Count == 0)
            {
                _logger?.Warn("[RPCS3] RefreshAsync - No trophy folders found in RPCS3 trophy directory.");
                return new RebuildPayload { Summary = summary };
            }

            _logger?.Info($"[RPCS3] RefreshAsync - Using trophy folder cache with {trophyFolderCache.Count} games.");
            _logger?.Debug($"[RPCS3] RefreshAsync - Cache NPCommIDs: [{string.Join(", ", trophyFolderCache.Keys)}]");

            var providerName = GetProviderName();

            for (var i = 0; i < gamesToRefresh.Count; i++)
            {
                cancel.ThrowIfCancellationRequested();
                var game = gamesToRefresh[i];
                _logger?.Debug($"[RPCS3] RefreshAsync - Processing game {i + 1}/{gamesToRefresh.Count}: '{game?.Name ?? "(null)"}'");
                report(new ProviderRefreshUpdate { CurrentGameName = game?.Name });

                try
                {
                    var data = await FetchGameDataAsync(game, trophyFolderCache, providerName, cancel).ConfigureAwait(false);
                    _logger?.Debug($"[RPCS3] RefreshAsync - FetchGameDataAsync result for '{game?.Name}': HasData={data != null}, HasAchievements={data?.HasAchievements ?? false}, AchievementCount={data?.Achievements?.Count ?? 0}");

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
                catch (OperationCanceledException)
                {
                    _logger?.Debug($"[RPCS3] RefreshAsync - Operation cancelled at game {i + 1}/{gamesToRefresh.Count}");
                    throw;
                }
                catch (Exception ex)
                {
                    _logger?.Debug(ex, $"[RPCS3] RefreshAsync - Failed to scan '{game?.Name}'");
                }
            }

            _logger?.Debug($"[RPCS3] RefreshAsync - Complete. GamesRefreshed={summary.GamesRefreshed}, GamesWithAchievements={summary.GamesWithAchievements}, GamesWithoutAchievements={summary.GamesWithoutAchievements}");
            report(new ProviderRefreshUpdate { CurrentGameName = null });
            return new RebuildPayload { Summary = summary };
        }

        /// <summary>
        /// Builds a cache mapping npcommid to trophy folder path.
        /// Trophy folder structure: rpcs3_install/trophy/npcommid/
        /// </summary>
        private async Task<Dictionary<string, string>> BuildTrophyFolderCacheAsync(CancellationToken cancel)
        {
            _logger?.Debug("[RPCS3] BuildTrophyFolderCacheAsync - Starting");
            var cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var installFolder = _settings?.Persisted?.Rpcs3InstallationFolder;
            if (string.IsNullOrWhiteSpace(installFolder))
            {
                _logger?.Debug("[RPCS3] BuildTrophyFolderCacheAsync - No RPCS3 installation folder configured");
                return cache;
            }

            _logger?.Debug($"[RPCS3] BuildTrophyFolderCacheAsync - Install folder: '{installFolder}'");
            var trophyPath = Path.Combine(installFolder, "trophy");
            _logger?.Debug($"[RPCS3] BuildTrophyFolderCacheAsync - Trophy path: '{trophyPath}'");

            if (!Directory.Exists(trophyPath))
            {
                _logger?.Warn($"[RPCS3] BuildTrophyFolderCacheAsync - Trophy folder not found at '{trophyPath}'");
                return cache;
            }

            try
            {
                var npcommidDirectories = Directory.GetDirectories(trophyPath);
                _logger?.Debug($"[RPCS3] BuildTrophyFolderCacheAsync - Found {npcommidDirectories.Length} directories in trophy folder");

                foreach (var npcommidDir in npcommidDirectories)
                {
                    cancel.ThrowIfCancellationRequested();

                    var npcommid = Path.GetFileName(npcommidDir);
                    if (string.IsNullOrWhiteSpace(npcommid))
                    {
                        _logger?.Debug($"[RPCS3] BuildTrophyFolderCacheAsync - Skipping directory with empty name");
                        continue;
                    }

                    // Verify TROPCONF.SFM exists
                    var tropconfPath = Path.Combine(npcommidDir, "TROPCONF.SFM");
                    if (File.Exists(tropconfPath))
                    {
                        cache[npcommid] = npcommidDir;
                        _logger?.Debug($"[RPCS3] BuildTrophyFolderCacheAsync - Added '{npcommid}' -> '{npcommidDir}'");
                    }
                    else
                    {
                        _logger?.Debug($"[RPCS3] BuildTrophyFolderCacheAsync - Skipping '{npcommid}' (no TROPCONF.SFM)");
                    }
                }

                _logger?.Debug($"[RPCS3] BuildTrophyFolderCacheAsync - Complete with {cache.Count} valid trophy folders");
            }
            catch (OperationCanceledException)
            {
                _logger?.Debug("[RPCS3] BuildTrophyFolderCacheAsync - Operation cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"[RPCS3] BuildTrophyFolderCacheAsync - Failed to enumerate trophy directories at '{trophyPath}'");
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
                _logger?.Debug("[RPCS3] FetchGameDataAsync - Game is null, returning null");
                return Task.FromResult<GameAchievementData>(null);
            }

            _logger?.Debug($"[RPCS3] FetchGameDataAsync - Starting for game '{game.Name}' (Id: {game.Id})");
            _logger?.Debug($"[RPCS3] FetchGameDataAsync - Game Source: '{game.Source?.Name ?? "(null)"}'");
            _logger?.Debug($"[RPCS3] FetchGameDataAsync - Game InstallDirectory: '{game.InstallDirectory ?? "(null)"}'");

            // Find npcommid for this game
            var npcommid = FindNpCommIdForGame(game, trophyFolderCache, cancel);
            _logger?.Debug($"[RPCS3] FetchGameDataAsync - Found npcommid: '{npcommid ?? "(null)"}'");

            if (string.IsNullOrWhiteSpace(npcommid))
            {
                _logger?.Debug($"[RPCS3] FetchGameDataAsync - No npcommid found for game '{game.Name}', returning no achievements");
                return Task.FromResult(BuildNoAchievementsData(game, providerName));
            }

            cancel.ThrowIfCancellationRequested();

            // Look up trophy folder
            if (!trophyFolderCache.TryGetValue(npcommid, out var trophyFolderPath))
            {
                _logger?.Debug($"[RPCS3] FetchGameDataAsync - Trophy folder not found for npcommid '{npcommid}' in cache");
                return Task.FromResult(BuildNoAchievementsData(game, providerName));
            }

            _logger?.Debug($"[RPCS3] FetchGameDataAsync - Trophy folder path: '{trophyFolderPath}'");

            var tropconfPath = Path.Combine(trophyFolderPath, "TROPCONF.SFM");
            var tropusrPath = Path.Combine(trophyFolderPath, "TROPUSR.DAT");

            _logger?.Debug($"[RPCS3] FetchGameDataAsync - TROPCONF.SFM path: '{tropconfPath}', Exists: {File.Exists(tropconfPath)}");
            _logger?.Debug($"[RPCS3] FetchGameDataAsync - TROPUSR.DAT path: '{tropusrPath}', Exists: {File.Exists(tropusrPath)}");

            if (!File.Exists(tropconfPath))
            {
                _logger?.Debug($"[RPCS3] FetchGameDataAsync - TROPCONF.SFM not found at '{tropconfPath}'");
                return Task.FromResult(BuildNoAchievementsData(game, providerName));
            }

            try
            {
                _logger?.Debug($"[RPCS3] FetchGameDataAsync - Parsing trophy definitions from '{tropconfPath}'");
                // Parse trophy definitions
                var trophies = Rpcs3TrophyParser.ParseTrophyDefinitions(tropconfPath, _logger);
                _logger?.Debug($"[RPCS3] FetchGameDataAsync - Parsed {trophies.Count} trophy definitions");

                // Parse unlock data
                if (File.Exists(tropusrPath))
                {
                    _logger?.Debug($"[RPCS3] FetchGameDataAsync - Parsing unlock data from '{tropusrPath}'");
                    Rpcs3TrophyParser.ParseTrophyUnlockData(tropusrPath, trophies, _logger);
                    var parsedUnlockedCount = trophies.Count(t => t.Unlocked);
                    _logger?.Debug($"[RPCS3] FetchGameDataAsync - {parsedUnlockedCount}/{trophies.Count} trophies unlocked");
                }
                else
                {
                    _logger?.Debug($"[RPCS3] FetchGameDataAsync - No TROPUSR.DAT, all trophies remain locked");
                }

                if (trophies.Count == 0)
                {
                    _logger?.Debug($"[RPCS3] FetchGameDataAsync - No trophies parsed, returning no achievements");
                    return Task.FromResult(BuildNoAchievementsData(game, providerName));
                }

                // Convert to achievements
                _logger?.Debug($"[RPCS3] FetchGameDataAsync - Converting {trophies.Count} trophies to achievements");
                var achievements = new List<AchievementDetail>();
                var unlockedCount = 0;

                foreach (var trophy in trophies)
                {
                    if (trophy.Unlocked)
                    {
                        unlockedCount++;
                    }

                    var iconPath = GetTrophyIconPath(trophyFolderPath, trophy.Id);
                    if (iconPath == null)
                    {
                        _logger?.Debug($"[RPCS3] FetchGameDataAsync - No icon found for trophy {trophy.Id} ('{trophy.Name}')");
                    }

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

                _logger?.Debug($"[RPCS3] FetchGameDataAsync - Created {achievements.Count} achievements, {unlockedCount} unlocked");

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
                _logger?.Error(ex, $"[RPCS3] FetchGameDataAsync - Failed to parse trophy data for '{game.Name}'");
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
            var rawInstallDir = game?.InstallDirectory;
            var gameDirectory = ExpandGamePath(game, rawInstallDir);

            _logger?.Debug($"[RPCS3] FindNpCommIdForGame - Game: '{game?.Name}'");
            _logger?.Debug($"[RPCS3] FindNpCommIdForGame - Raw InstallDirectory: '{rawInstallDir ?? "(null)"}'");
            _logger?.Debug($"[RPCS3] FindNpCommIdForGame - Expanded gameDirectory: '{gameDirectory ?? "(null)"}'");

            // Extract PS3 ID from the path and look it up in cache
            if (!string.IsNullOrWhiteSpace(gameDirectory))
            {
                var match = Ps3IdPattern.Match(gameDirectory);
                _logger?.Debug($"[RPCS3] FindNpCommIdForGame - PS3 ID pattern match: Success={match.Success}");

                if (match.Success)
                {
                    var ps3Id = match.Groups[1].Value.ToUpperInvariant();
                    _logger?.Debug($"[RPCS3] FindNpCommIdForGame - Extracted PS3 ID: '{ps3Id}'");
                    _logger?.Debug($"[RPCS3] FindNpCommIdForGame - Checking if '{ps3Id}' exists in cache (cache has {trophyFolderCache.Count} entries)");

                    if (trophyFolderCache.ContainsKey(ps3Id))
                    {
                        _logger?.Debug($"[RPCS3] FindNpCommIdForGame - Found matching npcommid '{ps3Id}' in cache");
                        return ps3Id;
                    }
                    else
                    {
                        _logger?.Debug($"[RPCS3] FindNpCommIdForGame - PS3 ID '{ps3Id}' not found in cache");
                        _logger?.Debug($"[RPCS3] FindNpCommIdForGame - Available cache keys: [{string.Join(", ", trophyFolderCache.Keys.Take(20))}{(trophyFolderCache.Count > 20 ? "..." : "")}]");
                    }
                }
            }
            else
            {
                _logger?.Debug($"[RPCS3] FindNpCommIdForGame - Game directory is null or empty");
            }

            _logger?.Debug($"[RPCS3] FindNpCommIdForGame - No npcommid found for game '{game?.Name}'");
            return null;
        }

        /// <summary>
        /// Expands path variables in game paths using Playnite's variable expansion.
        /// </summary>
        private string ExpandGamePath(Game game, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                _logger?.Debug($"[RPCS3] ExpandGamePath - Path is null or empty, returning as-is");
                return path;
            }

            // Use provider's expansion if available
            if (_provider != null)
            {
                var expanded = _provider.ExpandGamePath(game, path);
                _logger?.Debug($"[RPCS3] ExpandGamePath - Provider expansion: '{path}' -> '{expanded}'");
                return expanded;
            }

            // Fallback: use Playnite API directly if available
            try
            {
                var expanded = _playniteApi?.ExpandGameVariables(game, path) ?? path;
                _logger?.Debug($"[RPCS3] ExpandGamePath - Playnite API expansion: '{path}' -> '{expanded}'");
                return expanded;
            }
            catch (Exception ex)
            {
                _logger?.Debug($"[RPCS3] ExpandGamePath - Expansion failed: {ex.Message}, returning original path");
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
                _logger?.Debug($"[RPCS3] GetTrophyIconPath - Trophy folder path is null or empty");
                return null;
            }

            try
            {
                // Trophy icons follow TROP###.PNG format with zero-padded ID
                var iconFileName = $"TROP{trophyId.ToString().PadLeft(3, '0')}.PNG";
                var iconPath = Path.Combine(trophyFolderPath, iconFileName);

                _logger?.Debug($"[RPCS3] GetTrophyIconPath - Looking for icon at '{iconPath}'");

                if (File.Exists(iconPath))
                {
                    _logger?.Debug($"[RPCS3] GetTrophyIconPath - Found icon at '{iconPath}'");
                    return iconPath;
                }

                _logger?.Debug($"[RPCS3] GetTrophyIconPath - Icon not found at '{iconPath}'");
                return null;
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"[RPCS3] GetTrophyIconPath - Failed to get icon path for trophy {trophyId}");
                return null;
            }
        }
    }
}
