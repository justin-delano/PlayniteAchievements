using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace PlayniteAchievements.Providers.ShadPS4
{
    internal sealed class ShadPS4Scanner
    {
        private readonly ILogger _logger;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly ShadPS4DataProvider _provider;

        // PS4's RTC epoch is January 1, 2008 00:00:00 UTC
        private static readonly DateTime Ps4Epoch = new DateTime(2008, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // The consistent difference we need to subtract (ShadPS4-specific offset)
        private const int YearOffset = 2007;

        // Default rarity estimates by trophy type (no Exophase in initial implementation)
        private const double PlatinumRarity = 5.0;
        private const double GoldRarity = 15.0;
        private const double SilverRarity = 30.0;
        private const double BronzeRarity = 60.0;

        public ShadPS4Scanner(ILogger logger, PlayniteAchievementsSettings settings, ShadPS4DataProvider provider = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _provider = provider;
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
            Dictionary<string, string> titleCache;
            if (_provider != null)
            {
                titleCache = _provider.GetOrBuildTitleCache();
            }
            else
            {
                titleCache = await BuildTitleIdCacheAsync(cancel).ConfigureAwait(false);
            }

            if (titleCache == null || titleCache.Count == 0)
            {
                _logger?.Warn("[ShadPS4] No games found in ShadPS4 user/game_data folder.");
                return new RebuildPayload { Summary = summary };
            }

            _logger?.Info($"[ShadPS4] Using title cache with {titleCache.Count} games.");

            var providerName = GetProviderName();

            for (var i = 0; i < gamesToRefresh.Count; i++)
            {
                cancel.ThrowIfCancellationRequested();
                var game = gamesToRefresh[i];
                report(new ProviderRefreshUpdate { CurrentGameName = game?.Name });

                try
                {
                    var data = await FetchGameDataAsync(game, titleCache, providerName, cancel).ConfigureAwait(false);
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
                    _logger?.Debug(ex, $"[ShadPS4] Failed to scan {game?.Name}");
                }
            }

            report(new ProviderRefreshUpdate { CurrentGameName = null });
            return new RebuildPayload { Summary = summary };
        }

        private async Task<Dictionary<string, string>> BuildTitleIdCacheAsync(CancellationToken cancel)
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
                _logger?.Warn($"[ShadPS4] user/game_data folder not found at {gameDataPath}");
                return cache;
            }

            try
            {
                var titleDirectories = Directory.GetDirectories(gameDataPath);
                foreach (var titleDir in titleDirectories)
                {
                    cancel.ThrowIfCancellationRequested();

                    var titleId = Path.GetFileName(titleDir);
                    if (string.IsNullOrWhiteSpace(titleId))
                    {
                        continue;
                    }

                    // Look for TROP.XML
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
                                var normalizedName = ShadPS4DataProvider.NormalizeGameName(titleName);
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
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[ShadPS4] Failed to enumerate title directories.");
            }

            return await Task.FromResult(cache).ConfigureAwait(false);
        }

        private Task<GameAchievementData> FetchGameDataAsync(
            Game game,
            Dictionary<string, string> titleCache,
            string providerName,
            CancellationToken cancel)
        {
            if (game == null)
            {
                return Task.FromResult<GameAchievementData>(null);
            }

            var gameName = game.Name;
            if (string.IsNullOrWhiteSpace(gameName))
            {
                return Task.FromResult(BuildNoAchievementsData(game, providerName));
            }

            var normalizedGameName = ShadPS4DataProvider.NormalizeGameName(gameName);
            if (string.IsNullOrWhiteSpace(normalizedGameName))
            {
                return Task.FromResult(BuildNoAchievementsData(game, providerName));
            }

            // Look up title ID in cache
            if (!titleCache.TryGetValue(normalizedGameName, out var titleId))
            {
                _logger?.Debug($"[ShadPS4] No title ID found for game '{gameName}' (normalized: '{normalizedGameName}')");
                return Task.FromResult(BuildNoAchievementsData(game, providerName));
            }

            cancel.ThrowIfCancellationRequested();

            var installFolder = _settings?.Persisted?.ShadPS4InstallationFolder;
            var xmlPath = Path.Combine(installFolder, "user", "game_data", titleId, "trophyfiles", "trophy00", "Xml", "TROP.XML");
            var iconsFolder = Path.Combine(installFolder, "user", "game_data", titleId, "trophyfiles", "trophy00", "Icons");

            if (!File.Exists(xmlPath))
            {
                _logger?.Debug($"[ShadPS4] TROP.XML not found at {xmlPath}");
                return Task.FromResult(BuildNoAchievementsData(game, providerName));
            }

            try
            {
                var doc = XDocument.Load(xmlPath);
                var achievements = new List<AchievementDetail>();
                var unlockedCount = 0;

                foreach (var trophyElement in doc.Descendants("trophy"))
                {
                    cancel.ThrowIfCancellationRequested();

                    var trophyId = trophyElement.Attribute("id")?.Value;
                    var trophyType = trophyElement.Attribute("ttype")?.Value;
                    var isHidden = trophyElement.Attribute("hidden")?.Value == "yes";
                    var name = trophyElement.Element("name")?.Value?.Trim();
                    var description = trophyElement.Element("detail")?.Value?.Trim();

                    // Check if trophy is unlocked
                    var isUnlocked = trophyElement.Attribute("unlockstate") != null;
                    DateTime? unlockTime = null;

                    if (isUnlocked)
                    {
                        unlockedCount++;
                        var timestamp = trophyElement.Attribute("timestamp")?.Value;
                        unlockTime = ConvertPs4Timestamp(timestamp);
                    }

                    // Calculate rarity based on trophy type
                    double rarity = GetRarityByTrophyType(trophyType);
                    string trophyTypeNormalized = NormalizeTrophyType(trophyType);

                    // Build icon path
                    var iconPath = GetTrophyIconPath(iconsFolder, trophyId);

                    achievements.Add(new AchievementDetail
                    {
                        ApiName = trophyId,
                        DisplayName = name,
                        Description = description,
                        UnlockedIconPath = iconPath,
                        LockedIconPath = iconPath,
                        Hidden = isHidden,
                        Unlocked = isUnlocked,
                        UnlockTimeUtc = unlockTime,
                        GlobalPercentUnlocked = rarity,
                        TrophyType = trophyTypeNormalized
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
                _logger?.Error(ex, $"[ShadPS4] Failed to parse TROP.XML for {game.Name}");
                return Task.FromResult(BuildNoAchievementsData(game, providerName));
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
            var value = ResourceProvider.GetString("LOCPlayAch_Provider_ShadPS4");
            return string.IsNullOrWhiteSpace(value) ? "ShadPS4" : value;
        }

        /// <summary>
        /// Converts a PS4 timestamp to UTC DateTime.
        /// PS4 epoch: 2008-01-01 00:00:00 UTC
        /// Format: Microseconds since epoch
        /// Correction: Subtract 2007 years from result (ShadPS4-specific offset)
        /// </summary>
        private DateTime? ConvertPs4Timestamp(string timestamp)
        {
            if (string.IsNullOrEmpty(timestamp))
            {
                return null;
            }

            try
            {
                var tickValue = ulong.Parse(timestamp);

                // Divide by 1000 to get milliseconds instead of microseconds
                var milliseconds = (long)(tickValue / 1000);

                // Add milliseconds to PS4 epoch
                var utcTime = Ps4Epoch.AddMilliseconds(milliseconds);

                // Adjust the year by subtracting the offset
                if (utcTime.Year > YearOffset)
                {
                    return new DateTime(
                        utcTime.Year - YearOffset,
                        utcTime.Month,
                        utcTime.Day,
                        utcTime.Hour,
                        utcTime.Minute,
                        utcTime.Second,
                        utcTime.Millisecond,
                        DateTimeKind.Utc);
                }

                return utcTime;
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"[ShadPS4] Failed to convert timestamp: {timestamp}");
                return null;
            }
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

        private string GetTrophyIconPath(string iconsFolder, string trophyId)
        {
            if (string.IsNullOrWhiteSpace(trophyId))
            {
                return null;
            }

            try
            {
                // Trophy icons follow TROP###.PNG format with zero-padded ID
                var iconFileName = $"TROP{trophyId.PadLeft(3, '0')}.PNG";
                var iconPath = Path.Combine(iconsFolder, iconFileName);

                if (File.Exists(iconPath))
                {
                    // Return absolute file path - DiskImageService can handle local file:// paths
                    return iconPath;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"[ShadPS4] Failed to get icon path for trophy {trophyId}");
                return null;
            }
        }
    }
}
