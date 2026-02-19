using Newtonsoft.Json;
using System.Collections.Generic;

namespace PlayniteAchievements.Providers.PSN.Models
{
    internal sealed class PsnTrophyTitleLookup
    {
        [JsonProperty("titles")]
        public List<PsnTitleContainer> Titles { get; set; }
    }

    internal sealed class PsnTitleContainer
    {
        [JsonProperty("trophyTitles")]
        public List<PsnTrophyTitleEntry> TrophyTitles { get; set; }
    }

    internal sealed class PsnTrophyTitleEntry
    {
        [JsonProperty("npCommunicationId")]
        public string NpCommunicationId { get; set; }
    }

    internal sealed class PsnTrophiesUserResponse
    {
        [JsonProperty("trophies")]
        public List<PsnUserTrophy> Trophies { get; set; }
    }

    internal sealed class PsnUserTrophy
    {
        [JsonProperty("trophyId")]
        public int TrophyId { get; set; }

        [JsonProperty("trophyEarnedRate")]
        public double? TrophyEarnedRate { get; set; }

        [JsonProperty("earned")]
        public bool Earned { get; set; }

        [JsonProperty("earnedDateTime")]
        public string EarnedDateTime { get; set; }
    }

    internal sealed class PsnTrophiesDetailResponse
    {
        [JsonProperty("trophies")]
        public List<PsnTrophyDetail> Trophies { get; set; }
    }

    internal sealed class PsnTrophyDetail
    {
        [JsonProperty("trophyId")]
        public int TrophyId { get; set; }

        [JsonProperty("trophyName")]
        public string TrophyName { get; set; }

        [JsonProperty("trophyDetail")]
        public string TrophyDetail { get; set; }

        [JsonProperty("trophyType")]
        public string TrophyType { get; set; }

        [JsonProperty("trophyIconUrl")]
        public string TrophyIconUrl { get; set; }

        [JsonProperty("hidden")]
        public bool Hidden { get; set; }
    }
}
