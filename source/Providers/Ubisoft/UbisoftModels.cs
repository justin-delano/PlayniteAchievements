using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace PlayniteAchievements.Providers.Ubisoft
{
    /// <summary>
    /// A ubiservices session: the ticket and session id every API call carries, who it belongs to, and
    /// when the ticket stops being accepted. Built from connect.ubisoft.com's stored login data or from
    /// a /v3/profiles/sessions response; the ticket is a live credential and is never logged or persisted.
    /// </summary>
    internal sealed class UbisoftSession
    {
        public UbisoftSession(string ticket, string sessionId, string userId, string nameOnPlatform, DateTime expiresUtc)
        {
            Ticket = ticket;
            SessionId = sessionId;
            UserId = userId;
            NameOnPlatform = nameOnPlatform;
            ExpiresUtc = expiresUtc;
        }

        public string Ticket { get; }

        public string SessionId { get; }

        public string UserId { get; }

        public string NameOnPlatform { get; }

        public DateTime ExpiresUtc { get; }

        /// <summary>True when the ticket is still good for at least <paramref name="margin"/>.</summary>
        public bool IsUsable(DateTime nowUtc, TimeSpan margin)
        {
            return !string.IsNullOrWhiteSpace(Ticket) &&
                   !string.IsNullOrWhiteSpace(SessionId) &&
                   !string.IsNullOrWhiteSpace(UserId) &&
                   ExpiresUtc > nowUtc + margin;
        }
    }

    /// <summary>
    /// The session record shape shared by connect.ubisoft.com's PRODloginData and the
    /// /v3/profiles/sessions response. Dates stay strings so they are parsed explicitly as UTC.
    /// </summary>
    internal sealed class UbisoftSessionRecord
    {
        [JsonProperty("ticket")]
        public string Ticket { get; set; }

        [JsonProperty("sessionId")]
        public string SessionId { get; set; }

        [JsonProperty("userId")]
        public string UserId { get; set; }

        [JsonProperty("profileId")]
        public string ProfileId { get; set; }

        [JsonProperty("nameOnPlatform")]
        public string NameOnPlatform { get; set; }

        [JsonProperty("expiration")]
        public string Expiration { get; set; }
    }

    internal sealed class UbisoftEntitlementsResponse
    {
        [JsonProperty("entitlements")]
        public List<UbisoftEntitlement> Entitlements { get; set; }
    }

    /// <summary>
    /// One owned product. Only "game" entries carry a spaceId; addons, packages and bundles leave it empty.
    /// </summary>
    internal sealed class UbisoftEntitlement
    {
        [JsonProperty("productId")]
        public long ProductId { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("spaceId")]
        public string SpaceId { get; set; }

        [JsonProperty("availability")]
        public string Availability { get; set; }
    }

    internal sealed class UbisoftGraphResponse
    {
        [JsonProperty("data")]
        public UbisoftGraphData Data { get; set; }

        [JsonProperty("errors")]
        public List<UbisoftGraphError> Errors { get; set; }
    }

    internal sealed class UbisoftGraphError
    {
        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("extensions")]
        public UbisoftGraphErrorExtensions Extensions { get; set; }
    }

    internal sealed class UbisoftGraphErrorExtensions
    {
        [JsonProperty("code")]
        public string Code { get; set; }
    }

    internal sealed class UbisoftGraphData
    {
        [JsonProperty("game")]
        public UbisoftGraphGame Game { get; set; }
    }

    internal sealed class UbisoftGraphGame
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("viewer")]
        public UbisoftGraphGameViewer Viewer { get; set; }
    }

    internal sealed class UbisoftGraphGameViewer
    {
        [JsonProperty("meta")]
        public UbisoftGraphGameViewerMeta Meta { get; set; }
    }

    internal sealed class UbisoftGraphGameViewerMeta
    {
        [JsonProperty("achievements")]
        public UbisoftGraphAchievementConnection Achievements { get; set; }
    }

    internal sealed class UbisoftGraphAchievementConnection
    {
        [JsonProperty("totalCount")]
        public int TotalCount { get; set; }

        [JsonProperty("completedCount")]
        public int CompletedCount { get; set; }

        [JsonProperty("nodes")]
        public List<UbisoftGraphAchievement> Nodes { get; set; }
    }

    internal sealed class UbisoftGraphAchievement
    {
        [JsonProperty("achievementId")]
        public long AchievementId { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("icon")]
        public string Icon { get; set; }

        [JsonProperty("viewer")]
        public UbisoftGraphAchievementViewer Viewer { get; set; }
    }

    internal sealed class UbisoftGraphAchievementViewer
    {
        [JsonProperty("meta")]
        public UbisoftGraphAchievementViewerMeta Meta { get; set; }
    }

    internal sealed class UbisoftGraphAchievementViewerMeta
    {
        [JsonProperty("isCompleted")]
        public bool IsCompleted { get; set; }

        [JsonProperty("completionDate")]
        public string CompletionDate { get; set; }
    }
}
