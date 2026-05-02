using Newtonsoft.Json.Linq;
using Playnite.SDK;
using PlayniteAchievements.Models.Achievements;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace PlayniteAchievements.Providers.Hoyoverse
{
    internal static class HoyoverseExportParser
    {
        public static HashSet<string> ReadUnlockedIds(
            HoyoverseGameKind kind,
            string exportPath,
            IReadOnlyList<AchievementDetail> definitions,
            ILogger logger)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(exportPath))
            {
                return result;
            }

            if (!File.Exists(exportPath))
            {
                logger?.Warn($"[HoYoverse] Export file does not exist: {exportPath}");
                return result;
            }

            try
            {
                var text = File.ReadAllText(exportPath);
                if (IsStarRailStationExport(exportPath, text))
                {
                    return kind == HoyoverseGameKind.HonkaiStarRail
                        ? ParseStarRailStationDat(text, definitions)
                        : result;
                }

                var root = JToken.Parse(text);
                AddPaimonMoeUnlockedIds(kind, root, result);
                AddSeelieUnlockedIds(root, result);
                AddStarDbUnlockedIds(kind, root, result);
                AddRngMoeUnlockedIds(kind, root, result);
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, $"[HoYoverse] Failed to parse export file '{exportPath}'. Achievements will remain locked.");
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            return result;
        }

        private static bool IsStarRailStationExport(string path, string text)
        {
            return string.Equals(Path.GetExtension(path), ".dat", StringComparison.OrdinalIgnoreCase) ||
                   (text ?? string.Empty).StartsWith("srs", StringComparison.Ordinal);
        }

        private static void AddPaimonMoeUnlockedIds(
            HoyoverseGameKind kind,
            JToken root,
            ISet<string> result)
        {
            if (kind != HoyoverseGameKind.GenshinImpact)
            {
                return;
            }

            var achievementRoot = root?["achievement"];
            if (achievementRoot == null)
            {
                return;
            }

            AddTruthyPropertyNames(achievementRoot, result);
        }

        private static void AddSeelieUnlockedIds(JToken root, ISet<string> result)
        {
            var achievements = root?["achievements"] as JObject;
            if (achievements == null)
            {
                return;
            }

            foreach (var property in achievements.Properties())
            {
                if (IsTruthy(property.Value) || IsTruthy(property.Value?["done"]))
                {
                    result.Add(property.Name);
                }
            }
        }

        private static void AddStarDbUnlockedIds(
            HoyoverseGameKind kind,
            JToken root,
            ISet<string> result)
        {
            var gameKey = GetStarDbGameKey(kind);
            if (string.IsNullOrWhiteSpace(gameKey))
            {
                return;
            }

            var achievements = root?["user"]?[gameKey]?["achievements"];
            if (achievements == null)
            {
                return;
            }

            if (achievements is JArray array)
            {
                foreach (var item in array)
                {
                    var id = ReadId(item);
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        result.Add(id);
                    }
                }
            }
            else if (achievements is JObject obj)
            {
                foreach (var property in obj.Properties())
                {
                    if (IsTruthy(property.Value) || IsTruthy(property.Value?["done"]) || IsTruthy(property.Value?["unlocked"]))
                    {
                        result.Add(property.Name);
                    }
                }
            }
        }

        private static void AddRngMoeUnlockedIds(
            HoyoverseGameKind kind,
            JToken root,
            ISet<string> result)
        {
            if (kind != HoyoverseGameKind.ZenlessZoneZero)
            {
                return;
            }

            var profiles = root?["data"]?["profiles"];
            if (profiles == null)
            {
                return;
            }

            foreach (var profile in EnumerateProfileObjects(profiles))
            {
                var enabled = profile?["stores"]?["2"]?["enabled"] as JObject;
                if (enabled == null)
                {
                    continue;
                }

                foreach (var property in enabled.Properties())
                {
                    if (IsTruthy(property.Value))
                    {
                        result.Add(property.Name);
                    }
                }
            }
        }

        private static HashSet<string> ParseStarRailStationDat(
            string content,
            IReadOnlyList<AchievementDetail> definitions)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var compressed = (content ?? string.Empty).StartsWith("srs", StringComparison.Ordinal)
                ? content.Substring(3)
                : content;
            var json = HoyoverseLzStringUtf16Codec.DecompressFromUtf16(compressed);
            if (string.IsNullOrWhiteSpace(json))
            {
                return result;
            }

            var root = JToken.Parse(json);
            var completeState = root?["data"]?["stores"]?["1_achieve"]?["completeState"];
            if (completeState == null)
            {
                return result;
            }

            var officialIds = new HashSet<string>(
                (definitions ?? Array.Empty<AchievementDetail>())
                    .Where(detail => !string.IsNullOrWhiteSpace(detail?.ApiName))
                    .Select(detail => detail.ApiName),
                StringComparer.OrdinalIgnoreCase);
            var definitionsByTitle = (definitions ?? Array.Empty<AchievementDetail>())
                .Where(detail => !string.IsNullOrWhiteSpace(detail?.DisplayName) && !string.IsNullOrWhiteSpace(detail.ApiName))
                .GroupBy(detail => NormalizeTitle(detail.DisplayName), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().ApiName, StringComparer.OrdinalIgnoreCase);

            foreach (var stationId in EnumerateTruthyPropertyNames(completeState))
            {
                if (officialIds.Contains(stationId))
                {
                    result.Add(stationId);
                    continue;
                }

                if (StarRailStationAchievementMap.TryGetOfficialTitle(stationId, out var englishTitle) &&
                    definitionsByTitle.TryGetValue(NormalizeTitle(englishTitle), out var officialId))
                {
                    result.Add(officialId);
                }
            }

            return result;
        }

        private static IEnumerable<JObject> EnumerateProfileObjects(JToken profiles)
        {
            if (profiles is JObject obj)
            {
                foreach (var property in obj.Properties())
                {
                    if (property.Value is JObject profile)
                    {
                        yield return profile;
                    }
                }
            }
            else if (profiles is JArray array)
            {
                foreach (var profile in array.OfType<JObject>())
                {
                    yield return profile;
                }
            }
        }

        private static void AddTruthyPropertyNames(JToken token, ISet<string> result)
        {
            foreach (var id in EnumerateTruthyPropertyNames(token))
            {
                result.Add(id);
            }
        }

        private static IEnumerable<string> EnumerateTruthyPropertyNames(JToken token)
        {
            if (token is JObject obj)
            {
                foreach (var property in obj.Properties())
                {
                    if (IsTruthy(property.Value))
                    {
                        yield return property.Name;
                    }

                    foreach (var nested in EnumerateTruthyPropertyNames(property.Value))
                    {
                        yield return nested;
                    }
                }
            }
            else if (token is JArray array)
            {
                foreach (var child in array)
                {
                    foreach (var nested in EnumerateTruthyPropertyNames(child))
                    {
                        yield return nested;
                    }
                }
            }
        }

        private static bool IsTruthy(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
            {
                return false;
            }

            if (token.Type == JTokenType.Boolean)
            {
                return token.Value<bool>();
            }

            if (token.Type == JTokenType.Integer)
            {
                return token.Value<int>() != 0;
            }

            var value = token.Type == JTokenType.String ? token.Value<string>() : token.ToString();
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "done", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadId(JToken item)
        {
            if (item == null)
            {
                return null;
            }

            if (item.Type == JTokenType.String || item.Type == JTokenType.Integer)
            {
                return Convert.ToString(((JValue)item).Value, CultureInfo.InvariantCulture);
            }

            return item["id"]?.ToString() ??
                   item["achievementId"]?.ToString() ??
                   item["achievement_id"]?.ToString();
        }

        private static string GetStarDbGameKey(HoyoverseGameKind kind)
        {
            switch (kind)
            {
                case HoyoverseGameKind.GenshinImpact:
                    return "gi";
                case HoyoverseGameKind.HonkaiStarRail:
                    return "hsr";
                case HoyoverseGameKind.ZenlessZoneZero:
                    return "zzz";
                default:
                    return null;
            }
        }

        private static string NormalizeTitle(string title)
        {
            return (title ?? string.Empty).Trim().ToUpperInvariant();
        }
    }

    internal static class StarRailStationAchievementMap
    {
        private static readonly Dictionary<string, string> StationIdToEnglishTitle =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["705481"] = "Guess Who I Am",
                ["705482"] = "The Sorrows of Young Arlan",
                ["705483"] = "The Mandela Effect",
                ["705484"] = "The Fourth Little Mole",
                ["705485"] = "The Banality of Evil",
                ["705486"] = "Sensory Socialization",
                ["705487"] = "Farewell, Comet Hunter",
                ["705488"] = "The Memories We Share",
                ["705489"] = "Winds at Your Back, World at Your Side",
                ["705490"] = "Half the Wizard of Oz"
            };

        public static bool TryGetOfficialTitle(string stationId, out string title)
        {
            return StationIdToEnglishTitle.TryGetValue(stationId ?? string.Empty, out title);
        }
    }
}
