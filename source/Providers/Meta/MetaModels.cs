using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace PlayniteAchievements.Providers.Meta
{
    // Wire models for graph.oculus.com. Field names are those observed in live responses; see
    // docs/notes/meta-quest/api.md for the request shapes.

    internal sealed class MetaViewer
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("alias")]
        public string Alias { get; set; }
    }

    internal sealed class MetaGraphError
    {
        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("code")]
        public int Code { get; set; }
    }

    internal sealed class MetaRestErrorEnvelope
    {
        [JsonProperty("error")]
        public MetaGraphError Error { get; set; }
    }

    internal sealed class MetaDefinitionList
    {
        [JsonProperty("data")]
        public List<MetaDefinition> Data { get; set; }
    }

    /// <summary>
    /// One entry of /{appId}/achievement_definitions. The Rift and Quest app ids of a cross-buy title
    /// return the same definition ids.
    /// </summary>
    internal sealed class MetaDefinition
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("api_name")]
        public string ApiName { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("is_secret")]
        public bool IsSecret { get; set; }

        [JsonProperty("unlocked_image_uri")]
        public string UnlockedImageUri { get; set; }

        [JsonProperty("locked_image_uri")]
        public string LockedImageUri { get; set; }
    }

    internal sealed class MetaFeedResponse
    {
        [JsonProperty("data")]
        public MetaFeedData Data { get; set; }

        [JsonProperty("errors")]
        public List<MetaGraphError> Errors { get; set; }
    }

    internal sealed class MetaFeedData
    {
        [JsonProperty("user")]
        public MetaFeedUser User { get; set; }
    }

    internal sealed class MetaFeedUser
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("profile_info")]
        public MetaFeedProfileInfo ProfileInfo { get; set; }
    }

    internal sealed class MetaFeedProfileInfo
    {
        [JsonProperty("modules")]
        public List<MetaFeedModule> Modules { get; set; }
    }

    internal sealed class MetaFeedModule
    {
        [JsonProperty("__typename")]
        public string TypeName { get; set; }

        [JsonProperty("achievements")]
        public MetaFeedConnection Achievements { get; set; }
    }

    internal sealed class MetaFeedConnection
    {
        [JsonProperty("edges")]
        public List<MetaFeedEdge> Edges { get; set; }

        [JsonProperty("page_info")]
        public MetaPageInfo PageInfo { get; set; }
    }

    internal sealed class MetaPageInfo
    {
        [JsonProperty("end_cursor")]
        public string EndCursor { get; set; }

        [JsonProperty("has_next_page")]
        public bool HasNextPage { get; set; }
    }

    internal sealed class MetaFeedEdge
    {
        [JsonProperty("node")]
        public MetaFeedNode Node { get; set; }
    }

    internal sealed class MetaFeedNode
    {
        [JsonProperty("is_unlocked")]
        public bool IsUnlocked { get; set; }

        [JsonProperty("unlock_date_description")]
        public string UnlockDateDescription { get; set; }

        [JsonProperty("definition")]
        public MetaFeedDefinition Definition { get; set; }
    }

    internal sealed class MetaFeedDefinition
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("unlock_count_description_short")]
        public string UnlockCountDescription { get; set; }
    }

    /// <summary>
    /// A parsed unlock from the feed, keyed by achievement definition id.
    /// </summary>
    internal sealed class MetaUnlock
    {
        public string DefinitionId { get; set; }

        /// <summary>Local midnight of the unlock day, as UTC; the feed carries no time of day.</summary>
        public DateTime? UnlockDateUtc { get; set; }

        /// <summary>Global unlock count parsed from "2.3M players unlocked", or null.</summary>
        public long? GlobalUnlockCount { get; set; }
    }
}
