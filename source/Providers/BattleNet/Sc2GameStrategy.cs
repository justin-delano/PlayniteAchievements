using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Providers.BattleNet.Models;
using PlayniteAchievements.Services.Logging;

namespace PlayniteAchievements.Providers.BattleNet
{
    internal sealed class Sc2GameStrategy
    {
        private readonly BattleNetApiClient _client;
        private readonly BattleNetSessionManager _session;
        private readonly ILogger _logger;

        public Sc2GameStrategy(BattleNetApiClient client, BattleNetSessionManager session, ILogger logger)
        {
            _client = client;
            _session = session;
            _logger = logger;
        }

        public bool MatchesGame(Game game)
        {
            return game?.Name != null &&
                game.Name.IndexOf("starcraft", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public async Task<GameAchievementData> FetchAchievementsAsync(Game game, string locale, CancellationToken ct)
        {
            var settings = ProviderRegistry.Settings<BattleNetSettings>();
            var regionId = settings.Sc2RegionId;
            var profileId = settings.Sc2ProfileId;

            if (profileId == 0)
            {
                profileId = await AutoDetectProfileAsync(regionId, ct);
                if (profileId == 0)
                {
                    _logger?.Warn("[BattleNet/SC2] Could not auto-detect SC2 profile.");
                    return CreateEmptyData(game, "StarCraft2");
                }

                settings.Sc2ProfileId = profileId;
                ProviderRegistry.Write(settings, persistToDisk: true);
            }

            var definitions = await _client.GetSc2AchievementDefinitionsAsync(locale, ct);
            var profile = await _client.GetSc2ProfileAsync(regionId, profileId, locale, ct);

            if (definitions == null || profile == null)
            {
                return CreateEmptyData(game, "StarCraft2");
            }

            var categoryLookup = definitions.Categories?.ToDictionary(c => c.Id, c => c.Name) ?? new Dictionary<string, string>();
            var parentLookup = definitions.Categories?.ToDictionary(c => c.Id, c => c.ParentCategoryId) ?? new Dictionary<string, string>();
            var earnedLookup = profile.EarnedAchievements?.ToDictionary(e => e.AchievementId) ?? new Dictionary<string, Sc2EarnedAchievement>();

            var achievements = new List<AchievementDetail>();

            foreach (var def in definitions.Achievements ?? Enumerable.Empty<Sc2AchievementDefinition>())
            {
                var detail = new AchievementDetail
                {
                    ApiName = def.Id,
                    DisplayName = def.Title,
                    Description = def.Description,
                    UnlockedIconPath = def.ImageUrl,
                    Points = def.Points > 0 ? def.Points : (int?)null,
                    Category = ResolveCategory(def.CategoryId, categoryLookup, parentLookup),
                    ProviderKey = "BattleNet"
                };

                if (earnedLookup.TryGetValue(def.Id, out var earned))
                {
                    detail.Unlocked = earned.IsComplete;
                    if (earned.IsComplete && int.TryParse(earned.CompletionDate, out var unixSeconds) && unixSeconds > 0)
                    {
                        detail.UnlockTimeUtc = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
                    }
                }
                else
                {
                    detail.Unlocked = false;
                }

                achievements.Add(detail);
            }

            return new GameAchievementData
            {
                ProviderKey = "BattleNet",
                GameName = game.Name,
                PlayniteGameId = game.Id,
                AppId = StableAppId("StarCraft2"),
                Achievements = achievements,
                LastUpdatedUtc = DateTime.UtcNow,
                HasAchievements = achievements.Count > 0
            };
        }

        private async Task<int> AutoDetectProfileAsync(int knownRegion, CancellationToken ct)
        {
            var settings = ProviderRegistry.Settings<BattleNetSettings>();
            if (!int.TryParse(settings.BattleNetUserId, out var userId) || userId == 0)
                return 0;

            if (knownRegion > 0 && knownRegion <= 3)
            {
                var profile = await _client.GetSc2ProfileAsync(knownRegion, userId, "en-US", ct);
                if (profile?.Summary != null && !string.IsNullOrEmpty(profile.Summary.DisplayName))
                    return userId;
            }

            for (int region = 1; region <= 3; region++)
            {
                if (region == knownRegion) continue;
                try
                {
                    var profile = await _client.GetSc2ProfileAsync(region, userId, "en-US", ct);
                    if (profile?.Summary != null && !string.IsNullOrEmpty(profile.Summary.DisplayName))
                    {
                        settings.Sc2RegionId = region;
                        return userId;
                    }
                }
                catch { }
            }

            return 0;
        }

        private static string ResolveCategory(string categoryId, Dictionary<string, string> names, Dictionary<string, string> parents)
        {
            if (string.IsNullOrEmpty(categoryId) || !names.TryGetValue(categoryId, out var name))
                return null;

            if (parents.TryGetValue(categoryId, out var parentId) &&
                !string.IsNullOrEmpty(parentId) &&
                names.TryGetValue(parentId, out var parentName))
            {
                return $"{parentName} - {name}";
            }

            return name;
        }

        private static GameAchievementData CreateEmptyData(Game game, string appId)
        {
            return new GameAchievementData
            {
                ProviderKey = "BattleNet",
                GameName = game.Name,
                PlayniteGameId = game.Id,
                AppId = StableAppId(appId),
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
