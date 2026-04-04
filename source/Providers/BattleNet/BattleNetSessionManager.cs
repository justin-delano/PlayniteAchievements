using System;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Data;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Providers.BattleNet.Models;

namespace PlayniteAchievements.Providers.BattleNet
{
    internal sealed class BattleNetSessionManager : ISessionManager
    {
        private const string UrlLogin = "https://starcraft2.com/login";
        private const string UrlAuthProbe = "https://starcraft2.blizzard.com/nav/authenticate";
        private static readonly TimeSpan InteractiveAuthTimeout = TimeSpan.FromMinutes(3);

        private int _authCheckInProgress;
        private (bool Success, string UserId) _authResult;
        private readonly IPlayniteAPI _api;
        private readonly ILogger _logger;

        public string ProviderKey => "BattleNet";

        public bool IsAuthenticated =>
            !string.IsNullOrWhiteSpace(ProviderRegistry.Settings<BattleNetSettings>().BattleNetUserId);

        public BattleNetSessionManager(IPlayniteAPI api, ILogger logger)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<AuthProbeResult> ProbeAuthStateAsync(CancellationToken ct)
        {
            using (PerfScope.Start(_logger, "BattleNet.ProbeAuthStateAsync", thresholdMs: 50))
            {
                try
                {
                    ct.ThrowIfCancellationRequested();

                    var user = await ProbeUserAsync(ct).ConfigureAwait(false);
                    if (user != null && user.Id != 0)
                    {
                        var userId = user.Id.ToString();
                        var settings = ProviderRegistry.Settings<BattleNetSettings>();
                        if (settings.BattleNetUserId != userId)
                        {
                            settings.BattleNetUserId = userId;
                            settings.BattleTag = user.Battletag;
                            ProviderRegistry.Write(settings, persistToDisk: true);
                        }

                        return AuthProbeResult.AlreadyAuthenticated(userId);
                    }

                    ClearSettings();
                    return AuthProbeResult.NotAuthenticated();
                }
                catch (OperationCanceledException)
                {
                    return AuthProbeResult.Cancelled();
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, "[BattleNetAuth] Probe failed with exception.");
                    return AuthProbeResult.ProbeFailed();
                }
            }
        }

        public async Task<AuthProbeResult> AuthenticateInteractiveAsync(
            bool forceInteractive,
            CancellationToken ct,
            IProgress<AuthProgressStep> progress = null)
        {
            var windowOpened = false;

            try
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(AuthProgressStep.CheckingExistingSession);

                _logger?.Info("[BattleNetAuth] Starting interactive authentication.");

                if (!forceInteractive)
                {
                    var existingResult = await ProbeAuthStateAsync(ct).ConfigureAwait(false);
                    if (existingResult.IsSuccess)
                    {
                        _logger?.Info("[BattleNetAuth] Already authenticated.");
                        progress?.Report(AuthProgressStep.Completed);
                        return existingResult;
                    }
                }
                else
                {
                    ClearSession();
                }

                _logger?.Info("[BattleNetAuth] Opening login dialog.");
                progress?.Report(AuthProgressStep.OpeningLoginWindow);

                var loginTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

                _ = _api.MainView.UIDispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var result = LoginInteractively();
                        loginTcs.TrySetResult(result ?? "");
                    }
                    catch (Exception ex)
                    {
                        loginTcs.TrySetException(ex);
                    }
                }));
                windowOpened = true;

                progress?.Report(AuthProgressStep.WaitingForUserLogin);
                var completed = await Task.WhenAny(
                    loginTcs.Task,
                    Task.Delay(InteractiveAuthTimeout, ct)).ConfigureAwait(false);

                if (completed != loginTcs.Task)
                {
                    _logger?.Warn("[BattleNetAuth] Interactive login timed out.");
                    progress?.Report(AuthProgressStep.Failed);
                    return AuthProbeResult.TimedOut(windowOpened);
                }

                var extractedId = await loginTcs.Task.ConfigureAwait(false);

                progress?.Report(AuthProgressStep.VerifyingSession);
                if (string.IsNullOrWhiteSpace(extractedId))
                {
                    var probeResult = await ProbeAuthStateAsync(ct).ConfigureAwait(false);
                    if (probeResult.IsSuccess)
                    {
                        extractedId = probeResult.UserId;
                    }
                }

                if (string.IsNullOrWhiteSpace(extractedId))
                {
                    _logger?.Warn("[BattleNetAuth] Interactive login failed or was cancelled.");
                    progress?.Report(AuthProgressStep.Failed);
                    return AuthProbeResult.Cancelled(windowOpened);
                }

                _logger?.Info("[BattleNetAuth] Interactive login succeeded.");
                progress?.Report(AuthProgressStep.Completed);
                return AuthProbeResult.Authenticated(extractedId, windowOpened: windowOpened);
            }
            catch (OperationCanceledException)
            {
                _logger?.Info("[BattleNetAuth] Authentication was cancelled or timed out.");
                progress?.Report(AuthProgressStep.Failed);
                return AuthProbeResult.TimedOut(windowOpened);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[BattleNetAuth] Authentication failed with exception.");
                progress?.Report(AuthProgressStep.Failed);
                return AuthProbeResult.Failed(windowOpened);
            }
        }

        public void ClearSession()
        {
            _logger?.Info("[BattleNetAuth] Clearing session.");
            _authResult = (false, null);
            ClearSettings();

            try
            {
                _api.MainView.UIDispatcher.Invoke(() =>
                {
                    using (var view = _api.WebViews.CreateOffscreenView())
                    {
                        view.DeleteDomainCookies(".blizzard.com");
                        view.DeleteDomainCookies(".battle.net");
                        view.DeleteDomainCookies(".starcraft2.com");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "[BattleNetAuth] Failed to clear Battle.net cookies from CEF.");
            }
        }

        private async Task<BattleNetUser> ProbeUserAsync(CancellationToken ct)
        {
            var dispatchOp = _api.MainView.UIDispatcher.InvokeAsync(async () =>
            {
                using (var view = _api.WebViews.CreateOffscreenView())
                {
                    await view.NavigateAndWaitAsync(UrlAuthProbe, timeoutMs: 8000);
                    var text = await view.GetPageTextAsync();
                    if (!string.IsNullOrWhiteSpace(text) &&
                        Serialization.TryFromJson(text, out BattleNetUser user) &&
                        user.Id != 0)
                    {
                        return user;
                    }
                }
                return null;
            });

            var task = await dispatchOp.Task.ConfigureAwait(false);
            return await task.ConfigureAwait(false);
        }

        private string LoginInteractively()
        {
            _authResult = (false, null);
            IWebView view = null;

            try
            {
                view = _api.WebViews.CreateView(580, 700);
                view.DeleteDomainCookies(".blizzard.com");
                view.DeleteDomainCookies(".battle.net");
                view.DeleteDomainCookies(".starcraft2.com");

                view.LoadingChanged += CloseWhenAuthenticated;
                view.Navigate(UrlLogin);
                view.OpenDialog();

                return _authResult.Success ? _authResult.UserId : null;
            }
            finally
            {
                if (view != null)
                {
                    view.LoadingChanged -= CloseWhenAuthenticated;
                    view.Dispose();
                }
            }
        }

        private async void CloseWhenAuthenticated(object sender, WebViewLoadingChangedEventArgs e)
        {
            try
            {
                if (e.IsLoading)
                    return;

                var address = ((IWebView)sender).GetCurrentAddress();

                if (IsLoginPageUrl(address))
                    return;

                if (Interlocked.CompareExchange(ref _authCheckInProgress, 1, 0) != 0)
                    return;

                _logger?.Debug($"[BattleNetAuth] Navigation to: {address}");

                var user = await WaitForAuthenticatedUserAsync(CancellationToken.None).ConfigureAwait(false);
                if (user != null && user.Id != 0)
                {
                    var userId = user.Id.ToString();
                    var settings = ProviderRegistry.Settings<BattleNetSettings>();
                    settings.BattleNetUserId = userId;
                    settings.BattleTag = user.Battletag;
                    ProviderRegistry.Write(settings, persistToDisk: true);

                    _authResult = (true, userId);
                    _logger?.Info($"[BattleNetAuth] Authenticated as user: {userId}");
                    _ = _api.MainView.UIDispatcher.BeginInvoke(new Action(() =>
                    {
                        try { ((IWebView)sender).Close(); }
                        catch (Exception closeEx)
                        { _logger?.Debug(closeEx, "[BattleNetAuth] Failed to close login dialog."); }
                    }));
                }
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "[BattleNetAuth] Failed to check authentication status");
            }
            finally
            {
                Interlocked.Exchange(ref _authCheckInProgress, 0);
            }
        }

        private async Task<BattleNetUser> WaitForAuthenticatedUserAsync(CancellationToken ct)
        {
            const int attempts = 8;
            const int delayMs = 500;

            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var user = await ProbeUserAsync(ct).ConfigureAwait(false);
                    if (user != null && user.Id != 0)
                    {
                        return user;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Debug(ex, "[BattleNetAuth] Waiting for authenticated user failed, retrying.");
                }

                if (attempt < attempts)
                {
                    await Task.Delay(delayMs, ct).ConfigureAwait(false);
                }
            }

            return null;
        }

        private void ClearSettings()
        {
            var settings = ProviderRegistry.Settings<BattleNetSettings>();
            if (!string.IsNullOrWhiteSpace(settings.BattleNetUserId))
            {
                settings.BattleNetUserId = null;
                settings.BattleTag = null;
                ProviderRegistry.Write(settings, persistToDisk: true);
            }
        }

        private static bool IsLoginPageUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            return url.IndexOf("/login", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
