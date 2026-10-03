using Playnite.SDK;
using Playnite.SDK.Events;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.Meta
{
    /// <summary>
    /// Web-login authentication for Meta. The user signs in through auth.meta.com in a WebView; on the
    /// redirect back to secure.oculus.com the site sets the oc_www_at cookie, which is the graph.oculus.com
    /// access token. The token stays in Playnite's shared browser cookie store and is read from there for
    /// every probe and refresh; only the resolved user id and alias are persisted.
    /// </summary>
    public sealed class MetaSessionManager : ISessionManager
    {
        internal const string TokenCookieName = "oc_www_at";
        internal const string CookieSiteDomain = "oculus.com";
        private const string LoginUrl = "https://secure.oculus.com/my/profile/";
        private const string LogPrefix = "[MetaAuth]";
        private static readonly string[] CookieDomains =
        {
            "oculus.com", ".oculus.com", "secure.oculus.com", ".secure.oculus.com",
            "meta.com", ".meta.com", "auth.meta.com"
        };
        private static readonly TimeSpan InteractiveAuthTimeout = TimeSpan.FromMinutes(5);

        private readonly IPlayniteAPI _api;
        private readonly ILogger _logger;
        private readonly MetaApiClient _apiClient;
        private readonly OffscreenViewLeaseSource _offscreenViews;

        private MetaViewer _loginViewer;
        private int _loginCheckInProgress;

        public string ProviderKey => "Meta";

        public bool IsAuthenticated => ProviderRegistry.Settings<MetaSettings>().HasUserId;

        public string UserId => ProviderRegistry.Settings<MetaSettings>().UserId;

        internal MetaSessionManager(IPlayniteAPI api, ILogger logger, MetaApiClient apiClient)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _logger = logger;
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _offscreenViews = new OffscreenViewLeaseSource(_api, _logger);
        }

        /// <summary>
        /// The current access token from the shared browser store, or null when the user is signed out.
        /// </summary>
        internal async Task<string> GetAccessTokenAsync(CancellationToken ct)
        {
            var cookies = await CefCookieReader.ReadAsync(_api, _offscreenViews, CookieSiteDomain, _logger, LogPrefix, ct)
                .ConfigureAwait(false);
            return SelectToken(cookies);
        }

        public async Task<AuthProbeResult> ProbeAuthStateAsync(CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                var token = await GetAccessTokenAsync(ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(token))
                {
                    return AuthProbeResult.NotAuthenticated();
                }

                var viewer = await _apiClient.GetViewerAsync(token, ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(viewer?.Id))
                {
                    return AuthProbeResult.NotAuthenticated();
                }

                PersistViewer(viewer);
                return AuthProbeResult.AlreadyAuthenticated(viewer.Alias ?? viewer.Id);
            }
            catch (OperationCanceledException)
            {
                return AuthProbeResult.Cancelled();
            }
            catch (MetaAuthException ex)
            {
                _logger?.Info($"{LogPrefix} Stored token was rejected: {ex.Message}");
                return AuthProbeResult.NotAuthenticated();
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"{LogPrefix} Auth probe failed.");
                return AuthProbeResult.ProbeFailed();
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

                if (!forceInteractive)
                {
                    var existing = await ProbeAuthStateAsync(ct).ConfigureAwait(false);
                    if (existing.IsSuccess)
                    {
                        progress?.Report(AuthProgressStep.Completed);
                        return existing;
                    }
                }

                progress?.Report(AuthProgressStep.OpeningLoginWindow);
                _loginViewer = null;

                var loginTcs = new TaskCompletionSource<MetaViewer>(TaskCreationOptions.RunContinuationsAsynchronously);
                _ = _api.MainView.UIDispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        loginTcs.TrySetResult(LoginInteractively());
                    }
                    catch (Exception ex)
                    {
                        loginTcs.TrySetException(ex);
                    }
                }));
                windowOpened = true;

                progress?.Report(AuthProgressStep.WaitingForUserLogin);
                var completed = await Task.WhenAny(loginTcs.Task, Task.Delay(InteractiveAuthTimeout, ct)).ConfigureAwait(false);
                if (completed != loginTcs.Task)
                {
                    _logger?.Warn($"{LogPrefix} Interactive login timed out.");
                    progress?.Report(AuthProgressStep.Failed);
                    return AuthProbeResult.TimedOut(windowOpened);
                }

                var viewer = await loginTcs.Task.ConfigureAwait(false);
                progress?.Report(AuthProgressStep.VerifyingSession);

                if (string.IsNullOrWhiteSpace(viewer?.Id))
                {
                    _logger?.Warn($"{LogPrefix} Interactive login was cancelled or did not produce a token.");
                    progress?.Report(AuthProgressStep.Failed);
                    return AuthProbeResult.Cancelled(windowOpened);
                }

                PersistViewer(viewer);
                progress?.Report(AuthProgressStep.Completed);
                return AuthProbeResult.Authenticated(viewer.Alias ?? viewer.Id, windowOpened: windowOpened);
            }
            catch (OperationCanceledException)
            {
                progress?.Report(AuthProgressStep.Failed);
                return AuthProbeResult.TimedOut(windowOpened);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"{LogPrefix} Authentication failed.");
                progress?.Report(AuthProgressStep.Failed);
                return AuthProbeResult.Failed(windowOpened);
            }
        }

        public void ClearSession()
        {
            _logger?.Info($"{LogPrefix} Clearing session.");
            _loginViewer = null;

            var settings = ProviderRegistry.Settings<MetaSettings>();
            settings.UserId = null;
            settings.Alias = null;
            ProviderRegistry.Write(settings, persistToDisk: true);

            _api.DeleteDomainCookies(_logger, LogPrefix, CookieDomains);
        }

        private void PersistViewer(MetaViewer viewer)
        {
            var settings = ProviderRegistry.Settings<MetaSettings>();
            settings.UserId = viewer.Id.Trim();
            settings.Alias = viewer.Alias?.Trim();
            ProviderRegistry.Write(settings, persistToDisk: true);
        }

        internal static string SelectToken(IEnumerable<HttpCookie> cookies)
        {
            return (cookies ?? Enumerable.Empty<HttpCookie>())
                .Where(c => c != null && string.Equals(c.Name, TokenCookieName, StringComparison.Ordinal))
                .Select(c => c.Value?.Trim())
                .FirstOrDefault(v => !string.IsNullOrEmpty(v));
        }

        /// <summary>
        /// Opens the login dialog on the UI thread and blocks until the token cookie is captured and
        /// verified, or the dialog is closed.
        /// </summary>
        private MetaViewer LoginInteractively()
        {
            IWebView view = null;
            try
            {
                view = _api.WebViews.CreateView(new WebViewSettings
                {
                    WindowWidth = 600,
                    WindowHeight = 760
                });

                view.LoadingChanged += CloseWhenLoggedIn;
                view.Navigate(LoginUrl);
                view.OpenDialog();
                return _loginViewer;
            }
            finally
            {
                if (view != null)
                {
                    view.LoadingChanged -= CloseWhenLoggedIn;
                    view.Dispose();
                }
            }
        }

        private async void CloseWhenLoggedIn(object sender, WebViewLoadingChangedEventArgs e)
        {
            if (e.IsLoading || _loginViewer != null)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _loginCheckInProgress, 1, 0) != 0)
            {
                return;
            }

            var view = (IWebView)sender;
            try
            {
                var token = await ReadTokenFromViewAsync(view).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(token))
                {
                    return;
                }

                var viewer = await _apiClient.GetViewerAsync(token, CancellationToken.None).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(viewer?.Id))
                {
                    return;
                }

                _loginViewer = viewer;

                // Closing the modal view re-entrantly from inside LoadingChanged can wedge the dialog.
                _ = _api.MainView.UIDispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        view.Close();
                    }
                    catch (Exception closeEx)
                    {
                        _logger?.Debug(closeEx, $"{LogPrefix} Failed to close login dialog.");
                    }
                }));
            }
            catch (MetaAuthException)
            {
                // A stale cookie from an earlier session; keep waiting for the user to finish logging in.
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, $"{LogPrefix} Failed to check login state.");
            }
            finally
            {
                Interlocked.Exchange(ref _loginCheckInProgress, 0);
            }
        }

        private async Task<string> ReadTokenFromViewAsync(IWebView view)
        {
            var operation = _api.MainView.UIDispatcher.InvokeAsync(() =>
            {
                var address = view.GetCurrentAddress();
                if (string.IsNullOrWhiteSpace(address) ||
                    address.IndexOf("secure.oculus.com", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return null;
                }

                return SelectToken(CefCookieReader.Filter(view.GetCookies(), CookieSiteDomain));
            });

            return await operation.Task.ConfigureAwait(false);
        }
    }
}
