using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using Playnite.SDK.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Providers.BattleNet.Models;

namespace PlayniteAchievements.Providers.BattleNet
{
    internal sealed class WowGameStrategy
    {
        private readonly BattleNetApiClient _client;
        private readonly BattleNetSessionManager _session;
        private readonly ILogger _logger;

        public WowGameStrategy(BattleNetApiClient client, BattleNetSessionManager session, ILogger logger)
        {
            _client = client;
            _session = session;
            _logger = logger;
        }

        public bool MatchesGame(Game game)
        {
            return BattleNetGameSupport.IsWowGame(game);
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
                return null;
            }

            var wowLocale = BattleNetLocaleMapper.ToWowWebLocale(effectiveLocale);
            _logger?.Debug($"[BattleNet/WoW] Normalized locale for WoW request. input={effectiveLocale}, normalized={wowLocale}");
            var allData = await _client.GetWowAllAchievementsAsync(region, realmSlug, character, wowLocale, ct);
            _logger?.Debug($"[BattleNet/WoW] Received WoW achievement category payloads. count={allData?.Count ?? 0}");

            var achievements = ParseAchievements(allData);
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

        internal static List<AchievementDetail> ParseAchievements(IEnumerable<WowAchievementsData> allData)
        {
            var achievements = new List<AchievementDetail>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var categoryData in allData ?? Enumerable.Empty<WowAchievementsData>())
            {
                if (categoryData == null)
                {
                    continue;
                }

                var category = categoryData.Name ?? categoryData.Category;
                var subcategories = ReadSubcategories(categoryData.Subcategories);
                if (subcategories.Count > 0)
                {
                    foreach (var subcategory in subcategories)
                    {
                        AddAchievements(achievements, seen, subcategory?.Achievements, category);
                    }
                }
                else
                {
                    AddAchievements(achievements, seen, categoryData.AchievementsList, category);
                }
            }

            return achievements;
        }

        private static void AddAchievements(
            List<AchievementDetail> target,
            HashSet<string> seen,
            IEnumerable<WowAchievement> source,
            string category)
        {
            foreach (var achievement in source ?? Enumerable.Empty<WowAchievement>())
            {
                if (achievement == null || achievement.Id == 0 || !seen.Add(achievement.Id.ToString()))
                {
                    continue;
                }

                var detail = new AchievementDetail
                {
                    ApiName = achievement.Id.ToString(),
                    DisplayName = achievement.Name,
                    Description = achievement.Description,
                    UnlockedIconPath = achievement.Icon?.Url,
                    Points = achievement.Point > 0 ? achievement.Point : (int?)null,
                    Category = category,
                    ProviderKey = "BattleNet",
                    Unlocked = achievement.Time.HasValue && achievement.Time.Value != default
                };

                if (detail.Unlocked)
                {
                    var time = achievement.Time.Value;
                    detail.UnlockTimeUtc = time.Kind == DateTimeKind.Utc
                        ? time
                        : time.Kind == DateTimeKind.Local
                            ? time.ToUniversalTime()
                            : DateTime.SpecifyKind(time, DateTimeKind.Utc);
                }

                target.Add(detail);
            }
        }

        internal static List<AchievementDetail> ParseProfileAchievements(WowProfileAchievementsResponse profile)
        {
            var achievements = new List<AchievementDetail>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var item in profile?.Achievements ?? Enumerable.Empty<WowProfileAchievement>())
            {
                var id = item.Achievement?.Id > 0 ? item.Achievement.Id : item.Id;
                if (id == 0 || !seen.Add(id.ToString()))
                {
                    continue;
                }

                var completed = item.CompletedTimestamp.GetValueOrDefault() > 0;
                var detail = new AchievementDetail
                {
                    ApiName = id.ToString(),
                    DisplayName = item.Achievement?.Name,
                    ProviderKey = "BattleNet",
                    Unlocked = completed
                };

                if (completed)
                {
                    detail.UnlockTimeUtc = DateTimeOffset.FromUnixTimeMilliseconds(item.CompletedTimestamp.Value).UtcDateTime;
                }

                achievements.Add(detail);
            }

            return achievements;
        }

        private static List<WowSubcategory> ReadSubcategories(object value)
        {
            try
            {
                if (value is JObject keyed)
                {
                    return keyed.Properties()
                        .Select(property => property.Value?.ToObject<WowSubcategory>())
                        .Where(item => item != null)
                        .ToList();
                }

                if (value is JArray array)
                {
                    return array
                        .Select(token => token?.ToObject<WowSubcategory>())
                        .Where(item => item != null)
                        .ToList();
                }

                return (value as IEnumerable<WowSubcategory>)?
                    .Where(item => item != null)
                    .ToList() ?? new List<WowSubcategory>();
            }
            catch
            {
                return new List<WowSubcategory>();
            }
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
