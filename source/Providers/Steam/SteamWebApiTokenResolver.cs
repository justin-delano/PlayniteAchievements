using System;
using System.Threading;
using System.Threading.Tasks;
using PlayniteAchievements.Models;
using PlayniteAchievements.Providers;
using Playnite.SDK;

namespace PlayniteAchievements.Providers.Steam
{
    internal sealed class SteamWebApiTokenResolver
    {
        private readonly ISessionManager _sessionManager;
        private readonly Func<CancellationToken, Task<string>> _resolveTokenAsync;
        private readonly ILogger _logger;

        public ISessionManager SessionManager => _sessionManager;

        public SteamWebApiTokenResolver(
            ISessionManager sessionManager,
            Func<CancellationToken, Task<string>> resolveTokenAsync,
            ILogger logger)
        {
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _resolveTokenAsync = resolveTokenAsync ?? throw new ArgumentNullException(nameof(resolveTokenAsync));
            _logger = logger;
        }

        public async Task<SteamWebApiTokenResolution> ResolveAsync(CancellationToken ct)
        {
            var probeResult = await _sessionManager.ProbeAuthStateAsync(ct).ConfigureAwait(false);
            var steamUserId = probeResult?.UserId?.Trim();
            if (probeResult?.IsSuccess != true || string.IsNullOrWhiteSpace(steamUserId))
            {
                return SteamWebApiTokenResolution.Fail(probeResult ?? AuthProbeResult.NotAuthenticated());
            }

            var token = await _resolveTokenAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(token))
            {
                _logger?.Warn("[SteamAch] Steam session is active, but the store web API token could not be resolved.");
                return SteamWebApiTokenResolution.Fail(AuthProbeResult.Create(
                    AuthOutcome.ProbeFailed,
                    "LOCPlayAch_Common_NotAuthenticated",
                    userId: steamUserId));
            }

            return SteamWebApiTokenResolution.Success(probeResult, token.Trim());
        }
    }

    internal sealed class SteamWebApiTokenResolution
    {
        public AuthProbeResult ProbeResult { get; private set; }

        public string Token { get; private set; }

        public string UserId => ProbeResult?.UserId?.Trim();

        public bool IsSuccess =>
            ProbeResult?.IsSuccess == true &&
            !string.IsNullOrWhiteSpace(UserId) &&
            !string.IsNullOrWhiteSpace(Token);

        private SteamWebApiTokenResolution()
        {
        }

        public static SteamWebApiTokenResolution Success(AuthProbeResult probeResult, string token)
        {
            return new SteamWebApiTokenResolution
            {
                ProbeResult = probeResult,
                Token = token?.Trim()
            };
        }

        public static SteamWebApiTokenResolution Fail(AuthProbeResult probeResult)
        {
            return new SteamWebApiTokenResolution
            {
                ProbeResult = probeResult,
                Token = null
            };
        }
    }
}
