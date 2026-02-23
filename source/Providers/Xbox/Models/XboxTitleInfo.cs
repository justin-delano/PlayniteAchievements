using System;
using System.Collections.Generic;

namespace PlayniteAchievements.Providers.Xbox.Models
{
    /// <summary>
    /// Request body for Title Hub batch API.
    /// Used to resolve PFN (Package Family Name) to titleId.
    /// </summary>
    public class TitleHubRequest
    {
        public List<string> pfns { get; set; } = new List<string>();
        public List<string> windowsPhoneProductIds { get; set; } = new List<string>();
    }

    /// <summary>
    /// Response from Title Hub API containing title information.
    /// </summary>
    public class TitleHistoryResponse
    {
        public List<XboxTitle> titles { get; set; }
    }

    /// <summary>
    /// Title information from Xbox Live.
    /// </summary>
    public class XboxTitle
    {
        public string titleId { get; set; }
        public string pfn { get; set; }
        public string name { get; set; }
        public string type { get; set; }
        public List<string> devices { get; set; }
        public XboxTitleDetail detail { get; set; }
    }

    public class XboxTitleDetail
    {
        public string productId { get; set; }
        public string description { get; set; }
        public string publisher { get; set; }
        public string developer { get; set; }
    }

    /// <summary>
    /// Request for user profile settings.
    /// Used to verify authentication is still valid.
    /// </summary>
    public class ProfileRequest
    {
        public List<string> settings { get; set; }
        public List<ulong> userIds { get; set; }
    }
}
