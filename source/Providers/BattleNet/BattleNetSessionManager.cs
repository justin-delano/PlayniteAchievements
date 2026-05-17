using System;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Events;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Providers.BattleNet.Models;

namespace PlayniteAchievements.Providers.BattleNet
{
    public sealed class BattleNetSessionManager : ISessionManager
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
                    var currentSettings = ProviderRegistry.Settings<BattleNetSettings>();
                    _logger?.Debug($"[BattleNetAuth] Starting auth probe. currentUserId={MaskId(currentSettings.BattleNetUserId)}");

                    var user = await ProbeUserAsync(ct).ConfigureAwait(false);
                    if (user != null && user.Id != 0)
                    {
                        var userId = user.Id.ToString();
                        var settings = ProviderRegistry.Settings<BattleNetSettings>();
                        if (settings.BattleNetUserId != userId)
                        {
                            _logger?.Info($"[BattleNetAuth] Auth probe found a different authenticated user. oldUserId={MaskId(settings.BattleNetUserId)}, newUserId={MaskId(userId)}, battleTag={Presence(user.Battletag)}");
                            settings.BattleNetUserId = userId;
                            settings.BattleTag = user.Battletag;
                            ProviderRegistry.Write(settings, persistToDisk: true);
                        }
                        else
                        {
                            _logger?.Debug($"[BattleNetAuth] Auth probe confirmed existing user. userId={MaskId(userId)}");
                        }

                        return AuthProbeResult.AlreadyAuthenticated(userId);
                    }

                    _logger?.Info("[BattleNetAuth] Auth probe did not find an authenticated user.");
                    ClearSettings();
                    return AuthProbeResult.NotAuthenticated();
                }
                catch (OperationCanceledException)
                {
                    _logger?.Info("[BattleNetAuth] Auth probe cancelled.");
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

                _logger?.Info($"[BattleNetAuth] Starting interactive authentication. forceInteractive={Bool(forceInteractive)}, currentUserId={MaskId(ProviderRegistry.Settings<BattleNetSettings>().BattleNetUserId)}");

                if (!forceInteractive)
                {
                    var existingResult = await ProbeAuthStateAsync(ct).ConfigureAwait(false);
                    if (existingResult.IsSuccess)
                    {
                        _logger?.Info($"[BattleNetAuth] Existing session is authenticated. userId={MaskId(existingResult.UserId)}");
                        progress?.Report(AuthProgressStep.Completed);
                        return existingResult;
                    }
                }
                else
                {
                    _logger?.Debug("[BattleNetAuth] Force interactive requested; clearing existing session first.");
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
                _logger?.Debug($"[BattleNetAuth] Login dialog closed. extractedUserId={MaskId(extractedId)}");

                progress?.Report(AuthProgressStep.VerifyingSession);
                if (string.IsNullOrWhiteSpace(extractedId))
                {
                    _logger?.Debug("[BattleNetAuth] Login dialog did not return a user id; probing session.");
                    var probeResult = await ProbeAuthStateAsync(ct).ConfigureAwait(false);
                    if (probeResult.IsSuccess)
                    {
                        extractedId = probeResult.UserId;
                        _logger?.Debug($"[BattleNetAuth] Session probe after dialog succeeded. userId={MaskId(extractedId)}");
                    }
                }

                if (string.IsNullOrWhiteSpace(extractedId))
                {
                    _logger?.Warn("[BattleNetAuth] Interactive login failed or was cancelled.");
                    progress?.Report(AuthProgressStep.Failed);
                    return AuthProbeResult.Cancelled(windowOpened);
                }

                _logger?.Info($"[BattleNetAuth] Interactive login succeeded. userId={MaskId(extractedId)}");
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
            _logger?.Info($"[BattleNetAuth] Clearing session. currentUserId={MaskId(ProviderRegistry.Settings<BattleNetSettings>().BattleNetUserId)}");
            _authResult = (false, null);
            ClearSettings();

            try
            {
                _api.MainView.UIDispatcher.Invoke(() =>
                {
                    using (var view = _api.WebViews.CreateOffscreenView())
                    {
                        _logger?.Debug("[BattleNetAuth] Deleting Battle.net related CEF cookies.");
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
            _logger?.Debug($"[BattleNetAuth] Navigating offscreen auth probe. url={UrlHostAndPath(UrlAuthProbe)}");
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
                        _logger?.Debug($"[BattleNetAuth] Offscreen auth probe returned user. userId={MaskId(user.Id.ToString())}, battleTag={Presence(user.Battletag)}");
                        return user;
                    }
                }
                _logger?.Debug("[BattleNetAuth] Offscreen auth probe returned no authenticated user.");
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
                _logger?.Debug("[BattleNetAuth] Creating interactive login web view.");
                view = _api.WebViews.CreateView(580, 700);
                view.DeleteDomainCookies(".blizzard.com");
                view.DeleteDomainCookies(".battle.net");
                view.DeleteDomainCookies(".starcraft2.com");

                view.LoadingChanged += CloseWhenAuthenticated;
                _logger?.Debug($"[BattleNetAuth] Navigating login web view. url={UrlHostAndPath(UrlLogin)}");
                view.Navigate(UrlLogin);
                view.OpenDialog();

                _logger?.Debug($"[BattleNetAuth] Interactive login web view closed. success={Bool(_authResult.Success)}, userId={MaskId(_authResult.UserId)}");
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
                {
                    _logger?.Debug("[BattleNetAuth] Skipping auth check because another navigation auth check is in progress.");
                    return;
                }

                _logger?.Debug($"[BattleNetAuth] Navigation completed. url={UrlHostAndPath(address)}");

                var user = await WaitForAuthenticatedUserAsync(CancellationToken.None).ConfigureAwait(false);
                if (user != null && user.Id != 0)
                {
                    var userId = user.Id.ToString();
                    var settings = ProviderRegistry.Settings<BattleNetSettings>();
                    settings.BattleNetUserId = userId;
                    settings.BattleTag = user.Battletag;
                    ProviderRegistry.Write(settings, persistToDisk: true);

                    _authResult = (true, userId);
                    _logger?.Info($"[BattleNetAuth] Authenticated from navigation. userId={MaskId(userId)}, battleTag={Presence(user.Battletag)}");
                    _ = _api.MainView.UIDispatcher.BeginInvoke(new Action(() =>
                    {
                        try { ((IWebView)sender).Close(); }
                        catch (Exception closeEx)
                        { _logger?.Debug(closeEx, "[BattleNetAuth] Failed to close login dialog."); }
                    }));
                }
                else
                {
                    _logger?.Debug("[BattleNetAuth] Navigation auth check did not find an authenticated user.");
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
                _logger?.Debug($"[BattleNetAuth] Waiting for authenticated user. attempt={attempt}/{attempts}");

                try
                {
                    var user = await ProbeUserAsync(ct).ConfigureAwait(false);
                    if (user != null && user.Id != 0)
                    {
                        _logger?.Debug($"[BattleNetAuth] Authenticated user detected while waiting. attempt={attempt}, userId={MaskId(user.Id.ToString())}");
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

            _logger?.Debug("[BattleNetAuth] Timed out waiting for authenticated user after login navigation.");
            return null;
        }

        private void ClearSettings()
        {
            var settings = ProviderRegistry.Settings<BattleNetSettings>();
            if (!string.IsNullOrWhiteSpace(settings.BattleNetUserId))
            {
                _logger?.Info($"[BattleNetAuth] Clearing persisted auth settings. userId={MaskId(settings.BattleNetUserId)}");
                settings.BattleNetUserId = null;
                settings.BattleTag = null;
                ProviderRegistry.Write(settings, persistToDisk: true);
            }
            else
            {
                _logger?.Debug("[BattleNetAuth] Persisted auth settings already empty.");
            }
        }

        private static bool IsLoginPageUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            return url.IndexOf("/login", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string Bool(bool value) => value ? "true" : "false";

        private static string MaskId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "<empty>";
            }

            var trimmed = value.Trim();
            if (trimmed.Length <= 4)
            {
                return "****";
            }

            return $"{new string('*', Math.Min(8, trimmed.Length - 4))}{trimmed.Substring(trimmed.Length - 4)}";
        }

        private static string Presence(string value) => string.IsNullOrWhiteSpace(value) ? "missing" : "set";

        private static string UrlHostAndPath(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return "<empty>";
            }

            try
            {
                var uri = new Uri(url);
                return uri.GetLeftPart(UriPartial.Path);
            }
            catch
            {
                return "<invalid-url>";
            }
        }
    }
}
