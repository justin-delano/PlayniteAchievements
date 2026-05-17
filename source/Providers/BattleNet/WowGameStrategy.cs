using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Providers.BattleNet.Models;

namespace PlayniteAchievements.Providers.BattleNet
{
    internal sealed class WowGameStrategy
    {
        private readonly BattleNetApiClient _client;
        private readonly ILogger _logger;

        public WowGameStrategy(BattleNetApiClient client, ILogger logger)
        {
            _client = client;
            _logger = logger;
        }

        public bool MatchesGame(Game game)
        {
            if (game?.Name == null) return false;
            var name = game.Name;
            return name.IndexOf("warcraft", StringComparison.OrdinalIgnoreCase) >= 0
                || name.Equals("wow", StringComparison.OrdinalIgnoreCase)
                || name.Equals("world of warcraft", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<GameAchievementData> FetchAchievementsAsync(Game game, string locale, CancellationToken ct)
        {
            var settings = ProviderRegistry.Settings<BattleNetSettings>();
            var region = settings.WowRegion;
            var realmSlug = settings.WowRealmSlug;
            var character = settings.WowCharacter;
            var effectiveLocale = string.IsNullOrWhiteSpace(locale) ? "en-US" : locale;

            _logger?.Info($"[BattleNet/WoW] Fetch started. game={GameLabel(game)}, locale={effectiveLocale}, region={region ?? "<none>"}, realmSlug={realmSlug ?? "<none>"}, character={Presence(character)}");

            if (string.IsNullOrEmpty(region) || string.IsNullOrEmpty(realmSlug) || string.IsNullOrEmpty(character))
            {
                _logger?.Warn($"[BattleNet/WoW] Region, realm, or character not configured. region={Presence(region)}, realmSlug={Presence(realmSlug)}, character={Presence(character)}");
                return CreateEmptyData(game);
            }

            var wowLocale = NormalizeLocale(effectiveLocale);
            _logger?.Debug($"[BattleNet/WoW] Normalized locale for WoW request. input={effectiveLocale}, normalized={wowLocale}");
            var allData = await _client.GetWowAllAchievementsAsync(region, realmSlug, character, wowLocale, ct);
            _logger?.Debug($"[BattleNet/WoW] Received WoW achievement category payloads. count={allData?.Count ?? 0}");

            var achievements = new List<AchievementDetail>();

            foreach (var categoryData in allData ?? new List<WowAchievementsData>())
            {
                if (categoryData.Subcategories == null) continue;

                var subcategoriesJson = Serialization.ToJson(categoryData.Subcategories);
                List<WowSubcategory> subcategories;
                try
                {
                    subcategories = Serialization.FromJson<List<WowSubcategory>>(subcategoriesJson);
                }
                catch (Exception ex)
                {
                    _logger?.Debug(ex, $"[BattleNet/WoW] Failed to parse WoW achievement subcategories. category={categoryData.Name ?? categoryData.Category ?? "<unknown>"}");
                    continue;
                }

                if (subcategories == null) continue;

                foreach (var sub in subcategories)
                {
                    if (sub.Achievements == null) continue;

                    foreach (var ach in sub.Achievements)
                    {
                        var detail = new AchievementDetail
                        {
                            ApiName = ach.Id.ToString(),
                            DisplayName = ach.Name,
                            Description = ach.Description,
                            UnlockedIconPath = ach.Icon?.Url,
                            Points = ach.Point > 0 ? ach.Point : (int?)null,
                            Category = categoryData.Name ?? categoryData.Category,
                            ProviderKey = "BattleNet"
                        };

                        if (ach.Time.HasValue && ach.Time.Value != default)
                        {
                            var time = ach.Time.Value;
                            detail.UnlockTimeUtc = time.Kind == DateTimeKind.Utc
                                ? time
                                : time.Kind == DateTimeKind.Local
                                    ? time.ToUniversalTime()
                                    : DateTime.SpecifyKind(time, DateTimeKind.Utc);
                            detail.Unlocked = true;
                        }
                        else
                        {
                            detail.Unlocked = false;
                        }

                        achievements.Add(detail);
                    }
                }
            }

            var data = new GameAchievementData
            {
                ProviderKey = "BattleNet",
                GameName = game.Name,
                PlayniteGameId = game.Id,
                AppId = StableAppId("WoW"),
                Achievements = achievements,
                LastUpdatedUtc = DateTime.UtcNow,
                HasAchievements = achievements.Count > 0
            };

            _logger?.Info($"[BattleNet/WoW] Fetch completed. game={GameLabel(game)}, achievements={achievements.Count}, unlocked={achievements.Count(a => a.Unlocked)}");
            return data;
        }

        private static string NormalizeLocale(string locale)
        {
            if (string.IsNullOrEmpty(locale)) return "en-us";
            return locale.Replace("-", "-").ToLowerInvariant();
        }

        private static GameAchievementData CreateEmptyData(Game game)
        {
            return new GameAchievementData
            {
                ProviderKey = "BattleNet",
                GameName = game.Name,
                PlayniteGameId = game.Id,
                AppId = StableAppId("WoW"),
                LastUpdatedUtc = DateTime.UtcNow,
                HasAchievements = false
            };
        }

        private static int StableAppId(string id)
        {
            int hash = 0;
            foreach (char c in id)
            {
                hash = (hash << 5) - hash + c;
            }
            return Math.Abs(hash);
        }

        private static string Presence(string value) => string.IsNullOrWhiteSpace(value) ? "missing" : "set";

        private static string GameLabel(Game game)
        {
            if (game == null)
            {
                return "<null>";
            }

            return $"{game.Name ?? "<unnamed>"} ({game.Id})";
        }
    }
}
