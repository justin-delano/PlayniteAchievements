using System;
using System.Collections.Generic;

namespace PlayniteAchievements.Providers.Xbox.Models
{
    /// <summary>
    /// Microsoft Live OAuth token response.
    /// </summary>
    public class LiveTokens
    {
        public string access_token { get; set; }
        public string refresh_token { get; set; }
        public int expires_in { get; set; }
        public string token_type { get; set; }
        public string user_id { get; set; }
        public DateTime CreationDate { get; set; }
    }

    /// <summary>
    /// Request body for Xbox Live authentication.
    /// </summary>
    public class XboxAuthRequest
    {
        public XboxAuthProperties Properties { get; set; } = new XboxAuthProperties();
    }

    public class XboxAuthProperties
    {
        public string AuthMethod { get; set; } = "RPS";
        public string SiteName { get; set; } = "user.auth.xboxlive.com";
        public string RpsTicket { get; set; }
    }

    /// <summary>
    /// Response from Xbox Live authentication.
    /// </summary>
    public class XboxAuthResponse
    {
        public string IssueInstant { get; set; }
        public string NotAfter { get; set; }
        public string Token { get; set; }
        public XboxDisplayClaims DisplayClaims { get; set; }
    }

    /// <summary>
    /// Request body for XSTS authorization.
    /// </summary>
    public class XstsRequest
    {
        public XstsProperties Properties { get; set; } = new XstsProperties();
    }

    public class XstsProperties
    {
        public string SandboxId { get; set; } = "RETAIL";
        public List<string> UserTokens { get; set; } = new List<string>();
    }

    /// <summary>
    /// Response from XSTS authorization.
    /// </summary>
    public class XstsResponse
    {
        public string IssueInstant { get; set; }
        public string NotAfter { get; set; }
        public string Token { get; set; }
        public XboxDisplayClaims DisplayClaims { get; set; }
        public XstsError XErr { get; set; }
    }

    public class XstsError
    {
        public long XErr { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// Display claims from Xbox authentication containing user info.
    /// </summary>
    public class XboxDisplayClaims
    {
        public List<XboxUserInfo> xui { get; set; }
    }

    public class XboxUserInfo
    {
        public string uhs { get; set; }
        public string xid { get; set; }
        public string gtg { get; set; }
    }

    /// <summary>
    /// Stored authorization data for Xbox Live API calls.
    /// </summary>
    public class AuthorizationData
    {
        public string IssueInstant { get; set; }
        public string NotAfter { get; set; }
        public string Token { get; set; }
        public XboxDisplayClaims DisplayClaims { get; set; }
    }
}
