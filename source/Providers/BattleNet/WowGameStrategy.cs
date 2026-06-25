using System;
using System.Collections.Generic;
using System.Globalization;
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
        internal const string EnglishMetadataLocale = "en-US";

        private readonly BattleNetApiClient _client;
        private readonly BattleNetSessionManager _sessionManager;
        private readonly ILogger _logger;

        public WowGameStrategy(BattleNetApiClient client, BattleNetSessionManager sessionManager, ILogger logger)
        {
            _client = client;
            _sessionManager = sessionManager;
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

            if (string.IsNullOrEmpty(region) || string.IsNullOrEmpty(realmSlug) || string.IsNullOrEmpty(character))
            {
                _logger?.Warn($"[BattleNet/WoW] Region, realm, or character not configured. region={Presence(region)}, realmSlug={Presence(realmSlug)}, character={Presence(character)}");
                return null;
            }

            var wowLocale = BattleNetLocaleMapper.ToWowWebLocale(effectiveLocale);
            var allData = await _client.GetWowAllAchievementsAsync(region, realmSlug, character, wowLocale, ct);

            if (allData == null || allData.Count == 0)
            {
                _logger?.Warn($"[BattleNet/WoW] No public achievement category payloads returned. game={GameLabel(game)}");
                return null;
            }

            var achievements = ParseAchievements(allData);
            if (achievements.Count == 0)
            {
                _logger?.Warn($"[BattleNet/WoW] Public achievement payloads contained no parsed achievements. game={GameLabel(game)}");
                return null;
            }

            var mergeStats = await MergeOfficialAchievementDataAsync(
                achievements,
                settings,
                effectiveLocale,
                ct).ConfigureAwait(false);
            if (mergeStats.FetchedCharacters > 0)
            {
                _logger?.Info($"[BattleNet/WoW] Official achievement merge completed. characters={mergeStats.FetchedCharacters}, completions={mergeStats.CompletionCount}, updated={mergeStats.UpdatedCount}, added={mergeStats.AddedCount}, mode={mergeStats.Mode}");
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

        internal async Task<WowOfficialMergeStats> MergeOfficialAchievementDataAsync(
            List<AchievementDetail> achievements,
            BattleNetSettings settings,
            string locale,
            CancellationToken ct)
        {
            var stats = new WowOfficialMergeStats();
            if (achievements == null || settings == null)
            {
                return stats;
            }

            if (string.IsNullOrWhiteSpace(settings.WowRegion))
            {
                return stats;
            }

            var apiLocale = BattleNetLocaleMapper.ToApiLocale(locale);
            var region = settings.WowRegion;
            var bearerToken = default(string);
            var characters = new List<WowOfficialCharacterTarget>();

            if (settings.WowAggregateAccountCharacters && _sessionManager?.IsAuthenticated == true)
            {
                try
                {
                    bearerToken = await _sessionManager.GetAccessTokenAsync(ct).ConfigureAwait(false);
                    var accountProfile = await _client.GetWowAccountProfileAsync(region, bearerToken, apiLocale, ct).ConfigureAwait(false);
                    characters = ExtractAccountCharacters(region, accountProfile);
                    stats.Mode = "account";
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    _logger?.Warn(ex, "[BattleNet/WoW] Account character aggregation failed. Falling back to configured character if API credentials are available.");
                    bearerToken = null;
                    characters.Clear();
                }
            }

            if (characters.Count == 0)
            {
                var configured = BuildConfiguredCharacterTarget(settings);
                if (configured == null)
                {
                    return stats;
                }

                characters.Add(configured);
                stats.Mode = "configured-character";

                if (string.IsNullOrWhiteSpace(bearerToken))
                {
                    if (!BattleNetGameSupport.HasApiCredentials(settings))
                    {
                        _logger?.Debug("[BattleNet/WoW] Battle.net API credentials are not configured; skipping official completion merge.");
                        return stats;
                    }

                    try
                    {
                        bearerToken = await _client.GetClientCredentialsAccessTokenAsync(
                            region,
                            settings.BattleNetClientId,
                            settings.BattleNetClientSecret,
                            ct).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException))
                    {
                        _logger?.Warn(ex, "[BattleNet/WoW] Could not obtain Battle.net API token; skipping official completion merge.");
                        return stats;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(bearerToken))
            {
                return stats;
            }

            var byId = achievements
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.ApiName))
                .GroupBy(item => item.ApiName, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var definitionCache = new Dictionary<string, WowOfficialAchievementDefinition>(StringComparer.Ordinal);
            var mediaCache = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var character in DeduplicateCharacters(characters))
            {
                ct.ThrowIfCancellationRequested();

                WowCharacterAchievementsResponse response;
                try
                {
                    response = await _client.GetWowOfficialCharacterAchievementsAsync(
                        character.Region,
                        character.RealmSlug,
                        character.CharacterName,
                        apiLocale,
                        bearerToken,
                        ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    _logger?.Debug(ex, $"[BattleNet/WoW] Official character achievement fetch failed for {character.SourceLabel}.");
                    continue;
                }

                stats.FetchedCharacters++;
                foreach (var progress in response?.Achievements ?? Enumerable.Empty<WowOfficialAchievementProgress>())
                {
                    ct.ThrowIfCancellationRequested();

                    var id = progress?.AchievementId ?? 0;
                    if (id <= 0 || !progress.CompletedTimestamp.HasValue)
                    {
                        continue;
                    }

                    stats.CompletionCount++;
                    var key = id.ToString(CultureInfo.InvariantCulture);
                    if (!byId.TryGetValue(key, out var detail))
                    {
                        detail = await CreateMissingOfficialAchievementAsync(
                            progress,
                            apiLocale,
                            bearerToken,
                            definitionCache,
                            mediaCache,
                            ct).ConfigureAwait(false);
                        if (detail == null)
                        {
                            continue;
                        }

                        achievements.Add(detail);
                        byId[key] = detail;
                        stats.AddedCount++;
                    }

                    if (ApplyOfficialCompletion(detail, progress.CompletedTimestamp.Value))
                    {
                        stats.UpdatedCount++;
                    }
                }
            }

            return stats;
        }

        private async Task<AchievementDetail> CreateMissingOfficialAchievementAsync(
            WowOfficialAchievementProgress progress,
            string locale,
            string bearerToken,
            Dictionary<string, WowOfficialAchievementDefinition> definitionCache,
            Dictionary<string, string> mediaCache,
            CancellationToken ct)
        {
            var id = progress?.AchievementId ?? 0;
            if (id <= 0)
            {
                return null;
            }

            var definitionHref = progress.Achievement?.Key?.Href;
            var definition = default(WowOfficialAchievementDefinition);
            if (!string.IsNullOrWhiteSpace(definitionHref) &&
                !definitionCache.TryGetValue(definitionHref, out definition))
            {
                try
                {
                    definition = await _client.GetWowOfficialAchievementDefinitionAsync(
                        definitionHref,
                        locale,
                        bearerToken,
                        ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    _logger?.Debug(ex, $"[BattleNet/WoW] Failed to fetch achievement definition for id={id}.");
                }

                definitionCache[definitionHref] = definition;
            }

            var iconUrl = default(string);
            var mediaHref = definition?.Media?.Href;
            if (!string.IsNullOrWhiteSpace(mediaHref) &&
                !mediaCache.TryGetValue(mediaHref, out iconUrl))
            {
                try
                {
                    iconUrl = (await _client.GetWowOfficialAchievementMediaAsync(
                        mediaHref,
                        bearerToken,
                        ct).ConfigureAwait(false))?.GetIconUrl();
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    _logger?.Debug(ex, $"[BattleNet/WoW] Failed to fetch achievement media for id={id}.");
                }

                mediaCache[mediaHref] = iconUrl;
            }

            return new AchievementDetail
            {
                ApiName = id.ToString(CultureInfo.InvariantCulture),
                DisplayName = FirstNonEmpty(definition?.Name, progress.Achievement?.Name, id.ToString(CultureInfo.InvariantCulture)),
                Description = definition?.Description,
                UnlockedIconPath = iconUrl,
                Points = definition != null && definition.Points > 0 ? definition.Points : (int?)null,
                Category = FirstNonEmpty(definition?.Category?.Name, "Earned"),
                ProviderKey = "BattleNet",
                Unlocked = false
            };
        }

        private static bool ApplyOfficialCompletion(AchievementDetail detail, long completedTimestamp)
        {
            if (detail == null || completedTimestamp <= 0)
            {
                return false;
            }

            var unlockUtc = DateTimeOffset.FromUnixTimeMilliseconds(completedTimestamp).UtcDateTime;
            var changed = !detail.Unlocked ||
                !detail.UnlockTimeUtc.HasValue ||
                unlockUtc < detail.UnlockTimeUtc.Value;

            detail.Unlocked = true;
            if (!detail.UnlockTimeUtc.HasValue || unlockUtc < detail.UnlockTimeUtc.Value)
            {
                detail.UnlockTimeUtc = unlockUtc;
            }

            return changed;
        }

        private static WowOfficialCharacterTarget BuildConfiguredCharacterTarget(BattleNetSettings settings)
        {
            if (settings == null ||
                string.IsNullOrWhiteSpace(settings.WowRegion) ||
                string.IsNullOrWhiteSpace(settings.WowRealmSlug) ||
                string.IsNullOrWhiteSpace(settings.WowCharacter))
            {
                return null;
            }

            return new WowOfficialCharacterTarget
            {
                Region = settings.WowRegion,
                RealmSlug = settings.WowRealmSlug,
                CharacterName = settings.WowCharacter,
                SourceLabel = $"{settings.WowRegion}/{settings.WowRealmSlug}/{settings.WowCharacter}"
            };
        }

        private static List<WowOfficialCharacterTarget> ExtractAccountCharacters(
            string region,
            WowAccountProfileResponse accountProfile)
        {
            var characters = new List<WowOfficialCharacterTarget>();
            foreach (var account in accountProfile?.WowAccounts ?? Enumerable.Empty<WowAccountProfileAccount>())
            {
                foreach (var entry in account?.Characters ?? Enumerable.Empty<WowAccountProfileCharacterEntry>())
                {
                    var character = entry?.Character;
                    if (character == null ||
                        string.IsNullOrWhiteSpace(character.Name) ||
                        string.IsNullOrWhiteSpace(character.Realm?.Slug))
                    {
                        continue;
                    }

                    characters.Add(new WowOfficialCharacterTarget
                    {
                        Region = region,
                        RealmSlug = character.Realm.Slug,
                        CharacterName = character.Name,
                        SourceLabel = $"{region}/{character.Realm.Slug}/{character.Name}"
                    });
                }
            }

            return characters;
        }

        private static IEnumerable<WowOfficialCharacterTarget> DeduplicateCharacters(IEnumerable<WowOfficialCharacterTarget> characters)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var character in characters ?? Enumerable.Empty<WowOfficialCharacterTarget>())
            {
                if (character == null ||
                    string.IsNullOrWhiteSpace(character.Region) ||
                    string.IsNullOrWhiteSpace(character.RealmSlug) ||
                    string.IsNullOrWhiteSpace(character.CharacterName))
                {
                    continue;
                }

                var key = $"{character.Region}/{character.RealmSlug}/{character.CharacterName}";
                if (seen.Add(key))
                {
                    yield return character;
                }
            }
        }

        internal static bool RequiresEnglishMetadataProjection(string locale)
        {
            var wowLocale = BattleNetLocaleMapper.ToWowWebLocale(
                string.IsNullOrWhiteSpace(locale) ? EnglishMetadataLocale : locale);
            return !string.Equals(wowLocale, "en-us", StringComparison.OrdinalIgnoreCase);
        }

        internal static List<AchievementDetail> CreateEnglishMetadataProjection(
            IList<AchievementDetail> nativeAchievements,
            IList<AchievementDetail> englishAchievements)
        {
            var englishByApiName = BuildAchievementLookup(englishAchievements);
            var projection = new List<AchievementDetail>();

            foreach (var nativeAchievement in nativeAchievements ?? Enumerable.Empty<AchievementDetail>())
            {
                if (nativeAchievement == null)
                {
                    projection.Add(null);
                    continue;
                }

                var projected = CloneForMetadataProjection(nativeAchievement);
                var apiName = NormalizeApiName(nativeAchievement.ApiName);
                if (!string.IsNullOrWhiteSpace(apiName) &&
                    englishByApiName.TryGetValue(apiName, out var englishAchievement))
                {
                    if (!string.IsNullOrWhiteSpace(englishAchievement.DisplayName))
                    {
                        projected.DisplayName = englishAchievement.DisplayName;
                    }

                    if (!string.IsNullOrWhiteSpace(englishAchievement.Description))
                    {
                        projected.Description = englishAchievement.Description;
                    }

                    if (!string.IsNullOrWhiteSpace(englishAchievement.Category))
                    {
                        projected.Category = englishAchievement.Category;
                    }

                    if (englishAchievement.Points.HasValue)
                    {
                        projected.Points = englishAchievement.Points;
                    }
                }

                projection.Add(projected);
            }

            return projection;
        }

        internal static int ApplyProjectedRarity(
            IList<AchievementDetail> targetAchievements,
            IList<AchievementDetail> projectedAchievements)
        {
            var projectedByApiName = BuildAchievementLookup(projectedAchievements);
            var updated = 0;

            foreach (var targetAchievement in targetAchievements ?? Enumerable.Empty<AchievementDetail>())
            {
                var apiName = NormalizeApiName(targetAchievement?.ApiName);
                if (string.IsNullOrWhiteSpace(apiName) ||
                    !projectedByApiName.TryGetValue(apiName, out var projectedAchievement) ||
                    projectedAchievement?.GlobalPercentUnlocked == null)
                {
                    continue;
                }

                if (targetAchievement.GlobalPercentUnlocked == projectedAchievement.GlobalPercentUnlocked &&
                    targetAchievement.Rarity == projectedAchievement.Rarity)
                {
                    continue;
                }

                targetAchievement.GlobalPercentUnlocked = projectedAchievement.GlobalPercentUnlocked;
                targetAchievement.Rarity = projectedAchievement.Rarity;
                updated++;
            }

            return updated;
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

        private static AchievementDetail CloneForMetadataProjection(AchievementDetail source)
        {
            return new AchievementDetail
            {
                ApiName = source.ApiName,
                DisplayName = source.DisplayName,
                Description = source.Description,
                UnlockedIconPath = source.UnlockedIconPath,
                LockedIconPath = source.LockedIconPath,
                Points = source.Points,
                ScaledPoints = source.ScaledPoints,
                CategoryType = source.CategoryType,
                Category = source.Category,
                TrophyType = source.TrophyType,
                IsCapstone = source.IsCapstone,
                Hidden = source.Hidden,
                UnlockTimeUtc = source.UnlockTimeUtc,
                GlobalPercentUnlocked = source.GlobalPercentUnlocked,
                Rarity = source.Rarity,
                ProgressNum = source.ProgressNum,
                ProgressDenom = source.ProgressDenom,
                ProviderKey = source.ProviderKey,
                Unlocked = source.Unlocked
            };
        }

        private static Dictionary<string, AchievementDetail> BuildAchievementLookup(
            IEnumerable<AchievementDetail> achievements)
        {
            return (achievements ?? Enumerable.Empty<AchievementDetail>())
                .Where(achievement => !string.IsNullOrWhiteSpace(achievement?.ApiName))
                .GroupBy(achievement => NormalizeApiName(achievement.ApiName), StringComparer.OrdinalIgnoreCase)
                .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        }

        private static string NormalizeApiName(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
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

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        private sealed class WowOfficialCharacterTarget
        {
            public string Region { get; set; }
            public string RealmSlug { get; set; }
            public string CharacterName { get; set; }
            public string SourceLabel { get; set; }
        }

        internal sealed class WowOfficialMergeStats
        {
            public string Mode { get; set; } = "none";
            public int FetchedCharacters { get; set; }
            public int CompletionCount { get; set; }
            public int UpdatedCount { get; set; }
            public int AddedCount { get; set; }
        }
    }
}
