using PlayniteAchievements.Models;
using PlayniteAchievements.Providers;
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers
{
    public interface IDataProvider
    {
        string ProviderKey { get; }
        bool IsAuthenticated { get; }
        ISessionManager AuthSession { get; }
    }
}

namespace PlayniteAchievements.Providers.Epic
{
    public class EpicSessionManager
    {
        public string AccessToken { get; set; }
        public string AccountId { get; set; }
        public bool TryRefreshTokenResult { get; set; }

        public Task<string> GetAccessTokenAsync(CancellationToken ct)
        {
            return Task.FromResult(AccessToken);
        }

        public string GetAccountId()
        {
            return AccountId;
        }

        public Task<bool> TryRefreshTokenAsync(CancellationToken ct)
        {
            return Task.FromResult(TryRefreshTokenResult);
        }
    }
}

namespace PlayniteAchievements.Providers.GOG
{
    public class GogSessionManager
    {
        public string AccessToken { get; set; }

        public string GetAccessToken()
        {
            return AccessToken;
        }
    }
}

namespace PlayniteAchievements.Providers.Exophase
{
    public class ExophaseSessionManager : ISessionManager
    {
        public string ProviderKey => "Exophase";

        public bool IsAuthenticated { get; set; }

        public AuthProbeResult ProbeResult { get; set; } = AuthProbeResult.NotAuthenticated();

        public int ProbeCallCount { get; private set; }

        public int LoadCookiesCallCount { get; private set; }

        public Action<CookieContainer> CookieLoader { get; set; }

        public Task<AuthProbeResult> ProbeAuthStateAsync(CancellationToken ct)
        {
            ProbeCallCount++;
            return Task.FromResult(ProbeResult);
        }

        public Task<AuthProbeResult> AuthenticateInteractiveAsync(
            bool forceInteractive,
            CancellationToken ct,
            IProgress<AuthProgressStep> progress = null)
        {
            return Task.FromResult(ProbeResult);
        }

        public void ClearSession()
        {
        }

        public void LoadCefCookiesIntoJar(CookieContainer cookieJar)
        {
            LoadCookiesCallCount++;
            CookieLoader?.Invoke(cookieJar);
        }
    }
}
