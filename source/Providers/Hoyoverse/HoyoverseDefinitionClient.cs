using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using PlayniteAchievements.Models.Achievements;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.Hoyoverse
{
    internal interface IHoyoverseDefinitionClient
    {
        Task<IReadOnlyList<AchievementDetail>> GetDefinitionsAsync(
            HoyoverseGameKind kind,
            string globalLanguage,
            CancellationToken cancel);
    }

    internal sealed class HoyoverseDefinitionClient : IHoyoverseDefinitionClient, IDisposable
    {
        private static readonly TimeSpan DefinitionTtl = TimeSpan.FromHours(24);
        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;
        private readonly ILogger _logger;
        private readonly string _cacheRoot;

        public HoyoverseDefinitionClient(HttpClient httpClient, ILogger logger, string pluginUserDataPath)
        {
            _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            _ownsHttpClient = httpClient == null;
            _logger = logger;
            _cacheRoot = Path.Combine(pluginUserDataPath ?? string.Empty, "hoyoverse", "definitions");
        }

        public async Task<IReadOnlyList<AchievementDetail>> GetDefinitionsAsync(
            HoyoverseGameKind kind,
            string globalLanguage,
            CancellationToken cancel)
        {
            try
            {
                switch (kind)
                {
                    case HoyoverseGameKind.GenshinImpact:
                        return await GetGenshinDefinitionsAsync(globalLanguage, cancel).ConfigureAwait(false);
                    case HoyoverseGameKind.HonkaiStarRail:
                        return await GetHonkaiStarRailDefinitionsAsync(globalLanguage, cancel).ConfigureAwait(false);
                    case HoyoverseGameKind.ZenlessZoneZero:
                        return await GetZenlessZoneZeroDefinitionsAsync(globalLanguage, cancel).ConfigureAwait(false);
                    default:
                        return Array.Empty<AchievementDetail>();
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, $"[HoYoverse] Failed to load achievement definitions for {kind}.");
                return Array.Empty<AchievementDetail>();
            }
        }

        private async Task<IReadOnlyList<AchievementDetail>> GetGenshinDefinitionsAsync(
            string globalLanguage,
            CancellationToken cancel)
        {
            var language = MapGlobalLanguageToPaimonLocale(globalLanguage);
            var url = $"https://raw.githubusercontent.com/MadeBaruna/paimon-moe/main/src/data/achievement/{language}.json";
            var json = await GetStringWithCacheAsync($"genshin-{language}.json", url, cancel).ConfigureAwait(false);
            return ParseGenshinDefinitions(json);
        }

        private async Task<IReadOnlyList<AchievementDetail>> GetHonkaiStarRailDefinitionsAsync(
            string globalLanguage,
            CancellationToken cancel)
        {
            var textMapLanguage = MapGlobalLanguageToStarRailTextMap(globalLanguage);
            var textMapUrl = $"https://raw.githubusercontent.com/DimbreathBot/TurnBasedGameData/refs/heads/main/TextMap/TextMap{textMapLanguage}.json";
            var achievementUrl = "https://raw.githubusercontent.com/DimbreathBot/TurnBasedGameData/refs/heads/main/ExcelOutput/AchievementData.json";
            var seriesUrl = "https://raw.githubusercontent.com/DimbreathBot/TurnBasedGameData/refs/heads/main/ExcelOutput/AchievementSeries.json";

            var achievements = await GetStringWithCacheAsync("hsr-achievement-data.json", achievementUrl, cancel).ConfigureAwait(false);
            var series = await GetStringWithCacheAsync("hsr-achievement-series.json", seriesUrl, cancel).ConfigureAwait(false);
            var textMap = await GetStringWithCacheAsync($"hsr-textmap-{textMapLanguage}.json", textMapUrl, cancel).ConfigureAwait(false);

            string englishTextMap = null;
            if (!string.Equals(textMapLanguage, "EN", StringComparison.OrdinalIgnoreCase))
            {
                var englishUrl = "https://raw.githubusercontent.com/DimbreathBot/TurnBasedGameData/refs/heads/main/TextMap/TextMapEN.json";
                englishTextMap = await GetStringWithCacheAsync("hsr-textmap-EN.json", englishUrl, cancel).ConfigureAwait(false);
            }

            return ParseHonkaiStarRailDefinitions(achievements, series, textMap, englishTextMap);
        }

        private async Task<IReadOnlyList<AchievementDetail>> GetZenlessZoneZeroDefinitionsAsync(
            string globalLanguage,
            CancellationToken cancel)
        {
            var locale = MapGlobalLanguageToZzzLocale(globalLanguage);
            var html = await GetStringWithCacheAsync("zzz-index.html", "https://zzz.seelie.me/", cancel).ConfigureAwait(false);
            var indexAsset = Regex.Match(html ?? string.Empty, @"/assets/index-[^""']+\.js", RegexOptions.IgnoreCase).Value;
            if (string.IsNullOrWhiteSpace(indexAsset))
            {
                throw new InvalidDataException("Could not find zzz.seelie.me index asset.");
            }

            var indexUrl = "https://zzz.seelie.me" + indexAsset;
            var indexJs = await GetStringWithCacheAsync("zzz-index.js", indexUrl, cancel).ConfigureAwait(false);
            var localeAsset = FindZzzAchievementAsset(indexJs, locale) ?? FindZzzAchievementAsset(indexJs, "en");
            if (string.IsNullOrWhiteSpace(localeAsset))
            {
                throw new InvalidDataException("Could not find zzz.seelie.me achievement locale asset.");
            }

            var url = "https://zzz.seelie.me" + localeAsset;
            var js = await GetStringWithCacheAsync($"zzz-achievements-{locale}.js", url, cancel).ConfigureAwait(false);
            return ParseZenlessZoneZeroDefinitions(js);
        }

        private async Task<string> GetStringWithCacheAsync(string cacheFileName, string url, CancellationToken cancel)
        {
            Directory.CreateDirectory(_cacheRoot);
            var cachePath = Path.Combine(_cacheRoot, SanitizeFileName(cacheFileName));

            if (File.Exists(cachePath))
            {
                var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath);
                if (age <= DefinitionTtl)
                {
                    return File.ReadAllText(cachePath);
                }
            }

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.TryAddWithoutValidation("User-Agent", "PlayniteAchievements/HoYoverse");
                    using (var response = await _httpClient.SendAsync(request, cancel).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        File.WriteAllText(cachePath, content);
                        return content;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (File.Exists(cachePath))
                {
                    _logger?.Warn(ex, $"[HoYoverse] Using stale definition cache for '{cacheFileName}'.");
                    return File.ReadAllText(cachePath);
                }

                throw;
            }
        }

        internal static List<AchievementDetail> ParseGenshinDefinitions(string json)
        {
            var root = ParseJsonToken(json);
            var achievements = new List<AchievementDetail>();
            if (root == null)
            {
                return achievements;
            }

            foreach (var category in EnumerateCategoryObjects(root))
            {
                var categoryName = ReadString(category.Object["name"]) ??
                                   ReadString(category.Object["title"]) ??
                                   category.Name;
                var token = category.Object["achievements"] ??
                            category.Object["items"] ??
                            category.Object["data"] ??
                            category.Object;

                foreach (var obj in FindAchievementObjects(token, "id", "name"))
                {
                    var id = ReadString(obj["id"]);
                    var name = ReadString(obj["name"]);
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    var points = ReadInt(obj["reward"]) ?? ReadInt(obj["points"]) ?? 5;
                    achievements.Add(CreateDefinition(
                        id,
                        name,
                        ReadString(obj["desc"]) ?? ReadString(obj["description"]),
                        categoryName,
                        points,
                        ReadString(obj["icon"]) ?? ReadString(obj["iconPath"])));
                }
            }

            return DeduplicateDefinitions(achievements);
        }

        internal static List<AchievementDetail> ParseHonkaiStarRailDefinitions(
            string achievementDataJson,
            string achievementSeriesJson,
            string textMapJson,
            string englishTextMapJson = null)
        {
            var achievementRoot = ParseJsonToken(achievementDataJson);
            var seriesRoot = ParseJsonToken(achievementSeriesJson);
            var textMap = JObject.Parse(string.IsNullOrWhiteSpace(textMapJson) ? "{}" : textMapJson);
            var englishTextMap = string.IsNullOrWhiteSpace(englishTextMapJson)
                ? textMap
                : JObject.Parse(englishTextMapJson);
            var seriesById = BuildHsrSeriesMap(seriesRoot, textMap, englishTextMap);
            var definitions = new List<AchievementDetail>();

            foreach (var obj in EnumerateObjects(achievementRoot))
            {
                var id = ReadString(obj["AchievementID"]) ?? ReadString(obj["ID"]) ?? ReadString(obj["id"]);
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                var titleHash = obj["AchievementTitle"]?["Hash"] ?? obj["Title"]?["Hash"] ?? obj["Title"];
                var descHash = obj["AchievementDesc"]?["Hash"] ?? obj["Desc"]?["Hash"] ?? obj["Desc"];
                var name = ResolveText(textMap, englishTextMap, titleHash);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var description = ResolveText(textMap, englishTextMap, descHash);
                description = ApplyHsrParameters(description, obj["ParamList"]);
                var seriesId = ReadString(obj["SeriesID"]) ?? ReadString(obj["Series"]);
                seriesById.TryGetValue(seriesId ?? string.Empty, out var category);

                var points = ReadHsrPoints(obj["Rarity"]);
                definitions.Add(CreateDefinition(
                    id,
                    CleanText(name),
                    CleanText(description),
                    category,
                    points,
                    ReadString(obj["IconPath"])));
            }

            return DeduplicateDefinitions(definitions);
        }

        internal static List<AchievementDetail> ParseZenlessZoneZeroDefinitions(string jsOrJson)
        {
            var normalized = NormalizeZzzPayload(jsOrJson);
            var root = ParseJsonToken(normalized);
            var definitions = new List<AchievementDetail>();
            if (root == null)
            {
                return definitions;
            }

            foreach (var category in EnumerateCategoryObjects(root))
            {
                var categoryName = ReadString(category.Object["n"]) ??
                                   ReadString(category.Object["name"]) ??
                                   category.Name;
                var achievementToken = category.Object["a"] ??
                                       category.Object["achievements"] ??
                                       category.Object["items"] ??
                                       category.Object;

                foreach (var obj in FindAchievementObjects(achievementToken, "id", "n", "name"))
                {
                    var id = ReadString(obj["id"]);
                    var name = ReadString(obj["n"]) ?? ReadString(obj["name"]);
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    var points = ReadInt(obj["r"]) ?? ReadInt(obj["reward"]) ?? 5;
                    definitions.Add(CreateDefinition(
                        id,
                        CleanText(name),
                        CleanText(ReadString(obj["d"]) ?? ReadString(obj["desc"]) ?? ReadString(obj["description"])),
                        CleanText(categoryName),
                        points,
                        ReadString(obj["icon"]) ?? ReadString(obj["i"])));
                }
            }

            return DeduplicateDefinitions(definitions);
        }

        internal static string MapGlobalLanguageToPaimonLocale(string globalLanguage)
        {
            switch (NormalizeLanguage(globalLanguage))
            {
                case "chinese":
                case "schinese":
                case "simplified chinese":
                    return "chs";
                case "tchinese":
                case "traditional chinese":
                    return "cht";
                case "japanese":
                    return "ja";
                case "koreana":
                case "korean":
                    return "ko";
                case "french":
                    return "fr";
                case "german":
                    return "de";
                case "spanish":
                case "latam":
                    return "es";
                case "russian":
                    return "ru";
                case "thai":
                    return "th";
                case "vietnamese":
                    return "vi";
                case "indonesian":
                    return "id";
                case "portuguese":
                case "brazilian":
                    return "pt";
                default:
                    return "en";
            }
        }

        internal static string MapGlobalLanguageToStarRailTextMap(string globalLanguage)
        {
            switch (NormalizeLanguage(globalLanguage))
            {
                case "chinese":
                case "schinese":
                case "simplified chinese":
                    return "CHS";
                case "tchinese":
                case "traditional chinese":
                    return "CHT";
                case "japanese":
                    return "JP";
                case "koreana":
                case "korean":
                    return "KR";
                case "french":
                    return "FR";
                case "german":
                    return "DE";
                case "spanish":
                case "latam":
                    return "ES";
                case "russian":
                    return "RU";
                case "thai":
                    return "TH";
                case "vietnamese":
                    return "VI";
                case "indonesian":
                    return "ID";
                case "portuguese":
                case "brazilian":
                    return "PT";
                default:
                    return "EN";
            }
        }

        internal static string MapGlobalLanguageToZzzLocale(string globalLanguage)
        {
            switch (NormalizeLanguage(globalLanguage))
            {
                case "chinese":
                case "schinese":
                case "simplified chinese":
                    return "zh";
                case "tchinese":
                case "traditional chinese":
                    return "zh-tw";
                case "japanese":
                    return "ja";
                case "koreana":
                case "korean":
                    return "ko";
                case "french":
                    return "fr";
                case "german":
                    return "de";
                case "spanish":
                case "latam":
                    return "es";
                case "russian":
                    return "ru";
                case "thai":
                    return "th";
                case "vietnamese":
                    return "vi";
                case "indonesian":
                    return "id";
                case "portuguese":
                case "brazilian":
                    return "pt";
                default:
                    return "en";
            }
        }

        private static string FindZzzAchievementAsset(string indexJs, string locale)
        {
            var escapedLocale = Regex.Escape(locale ?? "en");
            var match = Regex.Match(
                indexJs ?? string.Empty,
                $@"/assets/locale/achievements-{escapedLocale}-[^""']+\.js",
                RegexOptions.IgnoreCase);
            return match.Success ? match.Value : null;
        }

        private static IEnumerable<(string Name, JObject Object)> EnumerateCategoryObjects(JToken root)
        {
            if (root is JObject rootObject)
            {
                foreach (var property in rootObject.Properties())
                {
                    if (property.Value is JObject categoryObject)
                    {
                        yield return (property.Name, categoryObject);
                    }
                    else if (property.Value is JArray categoryArray)
                    {
                        foreach (var child in categoryArray.OfType<JObject>())
                        {
                            yield return (property.Name, child);
                        }
                    }
                }
            }
            else if (root is JArray rootArray)
            {
                foreach (var child in rootArray.OfType<JObject>())
                {
                    yield return (ReadString(child["name"]) ?? ReadString(child["n"]) ?? string.Empty, child);
                }
            }
        }

        private static Dictionary<string, string> BuildHsrSeriesMap(JToken seriesRoot, JObject textMap, JObject englishTextMap)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var obj in EnumerateObjects(seriesRoot))
            {
                var id = ReadString(obj["SeriesID"]) ?? ReadString(obj["ID"]) ?? ReadString(obj["id"]);
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                var titleHash = obj["SeriesTitle"]?["Hash"] ??
                                obj["AchievementSeriesTitle"]?["Hash"] ??
                                obj["Title"]?["Hash"] ??
                                obj["Title"];
                var title = ResolveText(textMap, englishTextMap, titleHash);
                if (!string.IsNullOrWhiteSpace(title))
                {
                    map[id] = CleanText(title);
                }
            }

            return map;
        }

        private static IEnumerable<JObject> FindAchievementObjects(JToken token, params string[] requiredKeys)
        {
            if (token == null)
            {
                yield break;
            }

            if (token is JObject obj)
            {
                if (requiredKeys.Any(key => obj[key] != null) &&
                    (obj["id"] != null || obj["AchievementID"] != null || obj["ID"] != null))
                {
                    yield return obj;
                }

                foreach (var child in obj.Properties())
                {
                    foreach (var match in FindAchievementObjects(child.Value, requiredKeys))
                    {
                        yield return match;
                    }
                }
            }
            else if (token is JArray array)
            {
                foreach (var child in array)
                {
                    foreach (var match in FindAchievementObjects(child, requiredKeys))
                    {
                        yield return match;
                    }
                }
            }
        }

        private static IEnumerable<JObject> EnumerateObjects(JToken token)
        {
            if (token == null)
            {
                yield break;
            }

            if (token is JObject obj)
            {
                yield return obj;
                foreach (var property in obj.Properties())
                {
                    foreach (var child in EnumerateObjects(property.Value))
                    {
                        yield return child;
                    }
                }
            }
            else if (token is JArray array)
            {
                foreach (var child in array)
                {
                    foreach (var childObject in EnumerateObjects(child))
                    {
                        yield return childObject;
                    }
                }
            }
        }

        private static AchievementDetail CreateDefinition(
            string id,
            string name,
            string description,
            string category,
            int points,
            string iconPath)
        {
            var cleanName = CleanText(name);
            return new AchievementDetail
            {
                ApiName = id?.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(cleanName) ? id?.Trim() : cleanName,
                Description = CleanText(description),
                Category = CleanText(category),
                Points = points,
                Rarity = GetRarityFromReward(points),
                UnlockedIconPath = iconPath,
                LockedIconPath = iconPath,
                GlobalPercentUnlocked = null,
                Hidden = false,
                Unlocked = false,
                UnlockTimeUtc = null
            };
        }

        private static List<AchievementDetail> DeduplicateDefinitions(IEnumerable<AchievementDetail> source)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<AchievementDetail>();
            foreach (var detail in source ?? Enumerable.Empty<AchievementDetail>())
            {
                if (detail == null || string.IsNullOrWhiteSpace(detail.ApiName) || !seen.Add(detail.ApiName))
                {
                    continue;
                }

                result.Add(detail);
            }

            return result;
        }

        private static RarityTier GetRarityFromReward(int points)
        {
            if (points >= 20)
            {
                return RarityTier.Rare;
            }

            if (points >= 10)
            {
                return RarityTier.Uncommon;
            }

            return RarityTier.Common;
        }

        private static int ReadHsrPoints(JToken rarityToken)
        {
            var rarity = ReadString(rarityToken);
            if (string.IsNullOrWhiteSpace(rarity))
            {
                return 5;
            }

            switch (rarity.Trim().ToLowerInvariant())
            {
                case "high":
                case "rare":
                case "20":
                    return 20;
                case "mid":
                case "medium":
                case "10":
                    return 10;
                default:
                    return 5;
            }
        }

        private static string ResolveText(JObject textMap, JObject englishTextMap, JToken hashToken)
        {
            var hash = ReadString(hashToken);
            if (string.IsNullOrWhiteSpace(hash))
            {
                return null;
            }

            var value = ReadString(textMap?[hash]);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return ReadString(englishTextMap?[hash]);
        }

        private static string ApplyHsrParameters(string text, JToken paramList)
        {
            if (string.IsNullOrWhiteSpace(text) || !(paramList is JArray parameters) || parameters.Count == 0)
            {
                return text;
            }

            var result = text;
            for (var i = 0; i < parameters.Count; i++)
            {
                var value = ReadString(parameters[i]?["Value"]) ??
                            ReadString(parameters[i]?["value"]) ??
                            ReadString(parameters[i]);
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                var placeholder = "#" + (i + 1);
                result = Regex.Replace(result, Regex.Escape(placeholder) + @"(?:\[[^\]]+\])?", value);
            }

            return result;
        }

        private static string CleanText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            var text = value.Replace("\\n", " ").Replace("\n", " ").Replace("\r", " ");
            text = Regex.Replace(text, @"<[^>]+>", string.Empty);
            text = Regex.Replace(text, @"\{[A-Z]+#([^}]*)\}", "$1");
            text = Regex.Replace(text, @"\s+", " ");
            return text.Trim();
        }

        private static string NormalizeZzzPayload(string jsOrJson)
        {
            var text = (jsOrJson ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return "{}";
            }

            var objectStart = text.IndexOf('{');
            var exportStart = text.IndexOf(";export", StringComparison.Ordinal);
            if (objectStart > 0)
            {
                text = text.Substring(objectStart);
            }

            if (exportStart > objectStart && exportStart >= 0)
            {
                var length = exportStart - objectStart;
                if (length > 0 && length <= text.Length)
                {
                    text = text.Substring(0, length);
                }
            }

            text = Regex.Replace(text, @"(?<=[{,])\s*([A-Za-z_$][A-Za-z0-9_$]*)\s*:", "\"$1\":");
            text = Regex.Replace(text, @"(?<=[{,])\s*(\d+)\s*:", "\"$1\":");
            text = Regex.Replace(text, @"'([^'\\]*(?:\\.[^'\\]*)*)'", match =>
                JsonConvert.ToString(Regex.Unescape(match.Groups[1].Value)));
            text = Regex.Replace(text, @"`([^`\\]*(?:\\.[^`\\]*)*)`", match =>
                JsonConvert.ToString(Regex.Unescape(match.Groups[1].Value)));
            text = Regex.Replace(text, @"\b(\d+)e(\d+)\b", match =>
            {
                if (!double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    return match.Value;
                }

                return ((long)number).ToString(CultureInfo.InvariantCulture);
            });

            return text;
        }

        private static JToken ParseJsonToken(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return JToken.Parse(json);
        }

        private static string ReadString(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
            {
                return null;
            }

            if (token.Type == JTokenType.String)
            {
                return token.Value<string>();
            }

            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float || token.Type == JTokenType.Boolean)
            {
                return Convert.ToString(((JValue)token).Value, CultureInfo.InvariantCulture);
            }

            return token.ToString(Formatting.None);
        }

        private static int? ReadInt(JToken token)
        {
            if (token == null)
            {
                return null;
            }

            if (token.Type == JTokenType.Integer)
            {
                return token.Value<int>();
            }

            if (int.TryParse(ReadString(token), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }

            return null;
        }

        private static string NormalizeLanguage(string globalLanguage)
        {
            return (globalLanguage ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static string SanitizeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = (value ?? "cache").Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
            return new string(chars);
        }

        public void Dispose()
        {
            if (_ownsHttpClient)
            {
                _httpClient.Dispose();
            }
        }
    }
}
