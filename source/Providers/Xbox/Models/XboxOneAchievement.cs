using System;
using System.Collections.Generic;

namespace PlayniteAchievements.Providers.Xbox.Models
{
    /// <summary>
    /// Achievement model for Xbox One/Series X|S games.
    /// Uses contract version 2 of the achievements API.
    /// </summary>
    public class XboxOneAchievement
    {
        public string id { get; set; }
        public string serviceConfigId { get; set; }
        public string name { get; set; }
        public List<TitleAssociation> titleAssociations { get; set; }
        public string progressState { get; set; }
        public XboxOneProgression progression { get; set; }
        public List<XboxOneMediaAsset> mediaAssets { get; set; }
        public List<string> platforms { get; set; }
        public bool isSecret { get; set; }
        public string description { get; set; }
        public string lockedDescription { get; set; }
        public string productId { get; set; }
        public string achievementType { get; set; }
        public string participationType { get; set; }
        public List<XboxOneReward> rewards { get; set; }
    }

    public class TitleAssociation
    {
        public string name { get; set; }
        public int id { get; set; }
    }

    public class XboxOneProgression
    {
        public List<XboxOneRequirement> requirements { get; set; }
        public DateTime timeUnlocked { get; set; }
    }

    public class XboxOneRequirement
    {
        public string id { get; set; }
        public string current { get; set; }
        public string target { get; set; }
        public string operationType { get; set; }
        public string valueType { get; set; }
        public string ruleParticipationType { get; set; }
    }

    public class XboxOneMediaAsset
    {
        public string name { get; set; }
        public string type { get; set; }
        public string url { get; set; }
    }

    public class XboxOneReward
    {
        public string name { get; set; }
        public string description { get; set; }
        public string value { get; set; }
        public string type { get; set; }
        public object mediaAsset { get; set; }
        public string valueType { get; set; }
    }

    /// <summary>
    /// Response from Xbox One achievements API.
    /// </summary>
    public class XboxOneAchievementResponse
    {
        public List<XboxOneAchievement> achievements { get; set; }
        public XboxPagingInfo pagingInfo { get; set; }
    }

    public class XboxPagingInfo
    {
        public string continuationToken { get; set; }
        public int totalRecords { get; set; }
    }
}
