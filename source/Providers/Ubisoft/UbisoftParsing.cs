using Newtonsoft.Json;
using PlayniteAchievements.Models.Achievements;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace PlayniteAchievements.Providers.Ubisoft
{
    /// <summary>
    /// Pure parsing and mapping for Ubisoft: the stored web session, the entitlement list that maps a
    /// launcher product id to its ubiservices space id, and GraphQL achievement nodes.
    /// </summary>
    internal static class UbisoftParsing
    {
        /// <summary>
        /// Dates stay raw strings: ubiservices sends them without an offset marker, and Json.NET's
        /// default date handling would read them as local time.
        /// </summary>
        internal static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            DateParseHandling = DateParseHandling.None,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore
        };

        internal static T Deserialize<T>(string json) where T : class
        {
            return string.IsNullOrWhiteSpace(json) ? null : JsonConvert.DeserializeObject<T>(json, JsonSettings);
        }

        /// <summary>
        /// Reads a session record (connect.ubisoft.com's PRODloginData, or a /v3/profiles/sessions
        /// response). Returns null when the record lacks a ticket, session id, user id or expiry.
        /// </summary>
        internal static UbisoftSession ParseSession(string json)
        {
            UbisoftSessionRecord record;
            try
            {
                record = Deserialize<UbisoftSessionRecord>(json);
            }
            catch (JsonException)
            {
                return null;
            }

            return ToSession(record);
        }

        internal static UbisoftSession ToSession(UbisoftSessionRecord record)
        {
            if (record == null)
            {
                return null;
            }

            var userId = NullIfBlank(record.UserId) ?? NullIfBlank(record.ProfileId);
            var expires = ParseUtc(record.Expiration);
            if (string.IsNullOrWhiteSpace(record.Ticket) ||
                string.IsNullOrWhiteSpace(record.SessionId) ||
                userId == null ||
                !expires.HasValue)
            {
                return null;
            }

            return new UbisoftSession(
                record.Ticket.Trim(),
                record.SessionId.Trim(),
                userId,
                NullIfBlank(record.NameOnPlatform),
                expires.Value);
        }

        /// <summary>
        /// Parses a ubiservices timestamp as UTC. completionDate carries no offset marker but matches
        /// the launcher's local unlock record to the second when read as UTC; expiration carries a "Z".
        /// </summary>
        internal static DateTime? ParseUtc(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return DateTime.TryParse(
                value.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed)
                ? parsed
                : (DateTime?)null;
        }

        /// <summary>
        /// Product id to space id for every owned game. Addons, packages and bundles carry no space id
        /// and are skipped; the first game entry for a product wins.
        /// </summary>
        internal static Dictionary<long, string> IndexGameSpaces(IEnumerable<UbisoftEntitlement> entitlements)
        {
            var index = new Dictionary<long, string>();
            foreach (var entitlement in entitlements ?? Array.Empty<UbisoftEntitlement>())
            {
                var spaceId = NullIfBlank(entitlement?.SpaceId);
                if (spaceId == null ||
                    entitlement.ProductId <= 0 ||
                    !string.Equals(entitlement.Type, "game", StringComparison.OrdinalIgnoreCase) ||
                    index.ContainsKey(entitlement.ProductId))
                {
                    continue;
                }

                index[entitlement.ProductId] = spaceId;
            }

            return index;
        }

        /// <summary>
        /// A launcher product id ("7021"), as the Ubisoft Connect library plugin stores in Game.GameId.
        /// </summary>
        internal static long? ParseProductId(string value)
        {
            return long.TryParse((value ?? string.Empty).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var productId) &&
                   productId > 0
                ? productId
                : (long?)null;
        }

        /// <summary>
        /// Ubisoft's game title without trademark marks ("Prince of Persia™: The Lost Crown" becomes
        /// "Prince of Persia: The Lost Crown"), for searching other services by name. Null when blank.
        /// </summary>
        internal static string CleanTitle(string title)
        {
            var cleaned = NullIfBlank(title?.Replace("™", string.Empty).Replace("®", string.Empty).Replace("©", string.Empty));
            return cleaned == null ? null : string.Join(" ", cleaned.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }

        internal static List<AchievementDetail> MapAchievements(UbisoftGraphAchievementConnection connection)
        {
            var result = new List<AchievementDetail>();
            if (connection?.Nodes == null)
            {
                return result;
            }

            foreach (var node in connection.Nodes)
            {
                if (node == null || node.AchievementId <= 0)
                {
                    continue;
                }

                var meta = node.Viewer?.Meta;
                var isUnlocked = meta?.IsCompleted == true;

                result.Add(new AchievementDetail
                {
                    ApiName = node.AchievementId.ToString(CultureInfo.InvariantCulture),
                    DisplayName = NullIfBlank(node.Title),
                    Description = NullIfBlank(node.Description),
                    UnlockedIconPath = NullIfBlank(node.Icon),
                    UnlockTimeUtc = isUnlocked ? ParseUtc(meta.CompletionDate) : null,
                    Unlocked = isUnlocked
                });
            }

            return result;
        }

        private static string NullIfBlank(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
