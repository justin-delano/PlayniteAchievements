using System;
using System.Collections.Generic;
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

            if (string.IsNullOrEmpty(region) || string.IsNullOrEmpty(realmSlug) || string.IsNullOrEmpty(character))
            {
                _logger?.Warn("[BattleNet/WoW] Region, realm, or character not configured.");
                return CreateEmptyData(game);
            }

            var wowLocale = NormalizeLocale(locale);
            var allData = await _client.GetWowAllAchievementsAsync(region, realmSlug, character, wowLocale, ct);

            var achievements = new List<AchievementDetail>();

            foreach (var categoryData in allData)
            {
                if (categoryData.Subcategories == null) continue;

                var subcategoriesJson = Serialization.ToJson(categoryData.Subcategories);
                List<WowSubcategory> subcategories;
                try
                {
                    subcategories = Serialization.FromJson<List<WowSubcategory>>(subcategoriesJson);
                }
                catch
                {
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

            return new GameAchievementData
            {
                ProviderKey = "BattleNet",
                GameName = game.Name,
                PlayniteGameId = game.Id,
                AppId = StableAppId("WoW"),
                Achievements = achievements,
                LastUpdatedUtc = DateTime.UtcNow,
                HasAchievements = achievements.Count > 0
            };
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
    }
}
