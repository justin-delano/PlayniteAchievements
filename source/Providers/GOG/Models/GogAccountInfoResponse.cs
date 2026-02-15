using System.Runtime.Serialization;

namespace PlayniteAchievements.Providers.GOG.Models
{
    /// <summary>
    /// Response from GOG account info API.
    /// Used to validate authentication and extract access token.
    /// API: https://menu.gog.com/v1/account/basic
    /// Based on playnite-plugincommon/CommonPluginsStores/Gog/Models/AccountBasicResponse.cs
    /// </summary>
    [DataContract]
    internal sealed class GogAccountInfoResponse
    {
        [DataMember(Name = "isLoggedIn")]
        public bool IsLoggedIn { get; set; }

        [DataMember(Name = "userId")]
        public string UserId { get; set; }

        /// <summary>
        /// Access token for GOG API calls.
        /// </summary>
        [DataMember(Name = "accessToken")]
        public string AccessToken { get; set; }

        [DataMember(Name = "access_token")]
        public string AccessTokenLegacy { get; set; }

        /// <summary>
        /// Token expiration time in seconds from now.
        /// </summary>
        [DataMember(Name = "accessTokenExpires")]
        public long AccessTokenExpires { get; set; }

        [DataMember(Name = "access_token_expires")]
        public long AccessTokenExpiresLegacy { get; set; }

        [IgnoreDataMember]
        public string ResolvedAccessToken =>
            !string.IsNullOrWhiteSpace(AccessToken) ? AccessToken : AccessTokenLegacy;

        [IgnoreDataMember]
        public long ResolvedAccessTokenExpires =>
            AccessTokenExpires > 0 ? AccessTokenExpires : AccessTokenExpiresLegacy;
    }
}
