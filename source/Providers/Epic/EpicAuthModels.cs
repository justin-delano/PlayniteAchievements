using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace PlayniteAchievements.Providers.Epic
{
    /// <summary>
    /// Token model matching playnite-plugincommon's StoreToken structure.
    /// Used to deserialize encrypted tokens from Epic/Legendary library plugins.
    /// </summary>
    internal sealed class EpicStoreToken
    {
        [JsonProperty("account_id")]
        public string AccountId { get; set; }

        [JsonProperty("token_type")]
        public string Type { get; set; }

        [JsonProperty("access_token")]
        public string Token { get; set; }

        [JsonProperty("expires_at")]
        public DateTime? ExpireAt { get; set; }

        [JsonProperty("refresh_token")]
        public string RefreshToken { get; set; }

        [JsonProperty("refresh_expires_at")]
        public DateTime? RefreshExpireAt { get; set; }
    }

    public enum EpicAuthOutcome
    {
        Authenticated,
        AlreadyAuthenticated,
        NotAuthenticated,
        Cancelled,
        TimedOut,
        Failed,
        ProbeFailed
    }

    public enum EpicAuthProgressStep
    {
        CheckingExistingSession,
        OpeningLoginWindow,
        WaitingForUserLogin,
        VerifyingSession,
        Completed,
        Failed
    }

    public enum EpicAuthFlow
    {
        AutoFallback,
        EmbeddedWindow,
        SystemBrowser
    }

    public sealed class EpicAuthResult
    {
        public EpicAuthOutcome Outcome { get; set; }

        public string MessageKey { get; set; }

        public string AccountId { get; set; }

        public bool WindowOpened { get; set; }

        public bool IsSuccess =>
            Outcome == EpicAuthOutcome.Authenticated ||
            Outcome == EpicAuthOutcome.AlreadyAuthenticated;

        public static EpicAuthResult Create(
            EpicAuthOutcome outcome,
            string messageKey,
            string accountId = null,
            bool windowOpened = false)
        {
            return new EpicAuthResult
            {
                Outcome = outcome,
                MessageKey = messageKey,
                AccountId = accountId,
                WindowOpened = windowOpened
            };
        }
    }

    public interface IEpicSessionProvider
    {
        string GetAccountId();

        Task<string> GetAccessTokenAsync(CancellationToken ct);

        Task<bool> TryRefreshTokenAsync(CancellationToken ct);
    }

    public class EpicAuthRequiredException : Exception
    {
        public EpicAuthRequiredException(string message) : base(message) { }
    }

    /// <summary>
    /// Token model matching Legendary launcher's user.json format.
    /// Legendary stores tokens unencrypted in ~/.config/legendary/user.json
    /// </summary>
    internal sealed class LegendaryUserToken
    {
        [JsonProperty("account_id")]
        public string account_id { get; set; }

        [JsonProperty("token_type")]
        public string token_type { get; set; }

        [JsonProperty("access_token")]
        public string access_token { get; set; }

        [JsonProperty("expires_at")]
        public string expires_at { get; set; }

        [JsonProperty("refresh_token")]
        public string refresh_token { get; set; }

        [JsonProperty("refresh_expires_at")]
        public string refresh_expires_at { get; set; }

        [JsonProperty("expires_in")]
        public int? expires_in { get; set; }

        [JsonProperty("refresh_expires_in")]
        public int? refresh_expires_in { get; set; }
    }

    /// <summary>
    /// Response model for Epic account info API.
    /// Used to validate access tokens by querying account details.
    /// </summary>
    internal sealed class EpicAccountInfo
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("displayName")]
        public string DisplayName { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("email")]
        public string Email { get; set; }
    }
}
