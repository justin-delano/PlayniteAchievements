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
        [JsonProperty("AccountId")]
        public string AccountId { get; set; }

        [JsonProperty("Type")]
        public string Type { get; set; }

        [JsonProperty("Token")]
        public string Token { get; set; }

        [JsonProperty("ExpireAt")]
        public DateTime? ExpireAt { get; set; }

        [JsonProperty("RefreshToken")]
        public string RefreshToken { get; set; }

        [JsonProperty("RefreshExpireAt")]
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
