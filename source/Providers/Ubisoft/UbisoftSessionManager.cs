using Playnite.SDK;
using Playnite.SDK.Events;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.Ubisoft
{
    /// <summary>
    /// Web-login authentication for Ubisoft Connect. The user signs in at connect.ubisoft.com in a
    /// WebView; the site keeps the session in its local storage (PRODloginData) and is the only place
    /// the credential lives. Each probe and refresh reads it from there through an offscreen view, and
    /// only the account id and name are persisted.
    ///
    /// The site's session lasts three hours and the site never renews it silently: once expired, its
    /// login page offers a one-click "continue" that spends the remember-me ticket it also keeps in
    /// local storage. When the stored session has expired, this manager presses that button in an
    /// offscreen view, so the site performs and records its own renewal.
    /// </summary>
    public sealed class UbisoftSessionManager : ISessionManager
    {
        private const string LogPrefix = "[UbisoftAuth]";

        /// <summary>
        /// A connect.ubisoft.com page that does not redirect away, so the view stays on the origin that
        /// owns the session's local storage.
        /// </summary>
        internal const string SessionPageUrl = "https://connect.ubisoft.com/sdk.html";

        internal const string LoginUrl =
            "https://connect.ubisoft.com/login?appId=" + UbisoftApiClient.ClientAppId +
            "&genomeId=954e66a0-be1b-4aa0-9690-fb75201e4e9e&lang=en-US" +
            "&nextUrl=https%3A%2F%2Fconnect.ubisoft.com%2Fsdk.html";

        private const string ReadSessionScript =
            "(function(){try{return window.localStorage.getItem('PRODloginData')||'';}catch(e){return '';}})()";

        private const string HasRememberMeScript =
            "(function(){try{return window.localStorage.getItem('PRODrememberMe')?'yes':'';}catch(e){return '';}})()";

        /// <summary>
        /// Presses the remembered-account page's only button. Refuses when a password field is showing
        /// (the full sign-in form) or when the page does not have exactly one visible button, so it
        /// never submits anything but the one-click resume.
        /// </summary>
        private const string ContinueScript =
            "(function(){try{" +
            "if(document.querySelector('input[type=password]')&&document.querySelector('input[type=password]').offsetParent!==null)return 'form';" +
            "var b=Array.prototype.filter.call(document.querySelectorAll('button'),function(e){return e.offsetParent!==null&&!e.disabled;});" +
            "if(b.length!==1)return 'buttons:'+b.length;b[0].click();return 'clicked';}catch(e){return '';}})()";

        private const string ClearSessionScript =
            "(function(){try{['PRODloginData','PRODrememberMe','PRODlastProfile','reduxPersist:AuthenticationStore']" +
            ".forEach(function(k){window.localStorage.removeItem(k);});return 'ok';}catch(e){return '';}})()";

        private static readonly TimeSpan InteractiveAuthTimeout = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan SessionMargin = TimeSpan.FromMinutes(2);

        private readonly IPlayniteAPI _api;
        private readonly ILogger _logger;
        private readonly UbisoftApiClient _apiClient;
        private readonly SemaphoreSlim _sessionGate = new SemaphoreSlim(1, 1);

        private UbisoftSession _clientSession;
        private UbisoftSession _loginSession;
        private int _loginCheckInProgress;

        internal UbisoftSessionManager(IPlayniteAPI api, ILogger logger, UbisoftApiClient apiClient)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _logger = logger;
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        }

        public string ProviderKey => "Ubisoft";

        public bool IsAuthenticated => ProviderRegistry.Settings<UbisoftSettings>().HasUserId;

        /// <summary>
        /// A session usable by <see cref="UbisoftApiClient"/>, or null when the user is signed out or
        /// the site's session could not be resumed. The client-application ticket is held in memory
        /// until shortly before it expires.
        /// </summary>
        internal async Task<UbisoftSession> GetSessionAsync(CancellationToken ct)
        {
            await _sessionGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_clientSession != null && _clientSession.IsUsable(DateTime.UtcNow, SessionMargin))
                {
                    return _clientSession;
                }

                _clientSession = null;
                var webSession = await ReadOrResumeWebSessionAsync(ct).ConfigureAwait(false);
                if (webSession == null)
                {
                    return null;
                }

                _clientSession = await _apiClient.CreateClientSessionAsync(webSession, ct).ConfigureAwait(false);
                return _clientSession;
            }
            finally
            {
                _sessionGate.Release();
            }
        }

        /// <summary>Drops the in-memory session so the next call re-reads the browser.</summary>
        internal void InvalidateSession()
        {
            _clientSession = null;
        }

        public async Task<AuthProbeResult> ProbeAuthStateAsync(CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                var session = await GetSessionAsync(ct).ConfigureAwait(false);
                if (session == null)
                {
                    return AuthProbeResult.NotAuthenticated();
                }

                PersistIdentity(session);
                return AuthProbeResult.AlreadyAuthenticated(session.NameOnPlatform ?? session.UserId);
            }
            catch (OperationCanceledException)
            {
                return AuthProbeResult.Cancelled();
            }
            catch (UbisoftAuthException ex)
            {
                _logger?.Info($"{LogPrefix} Stored session was rejected: {ex.Message}");
                InvalidateSession();
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
                else
                {
                    // A forced sign-in starts from a clean slate, so the site shows its sign-in form
                    // rather than resuming whoever signed in last.
                    await ClearSessionAsync(ct).ConfigureAwait(false);
                }

                progress?.Report(AuthProgressStep.OpeningLoginWindow);
                _loginSession = null;

                var loginTcs = new TaskCompletionSource<UbisoftSession>(TaskCreationOptions.RunContinuationsAsynchronously);
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

                var webSession = await loginTcs.Task.ConfigureAwait(false);
                progress?.Report(AuthProgressStep.VerifyingSession);

                if (webSession == null)
                {
                    // The window may have been closed by hand after a successful sign-in, so the
                    // browser is the authority rather than the dialog's outcome.
                    webSession = await ReadWebSessionAsync(ct).ConfigureAwait(false);
                }

                if (webSession == null)
                {
                    _logger?.Warn($"{LogPrefix} Interactive login was cancelled or did not produce a session.");
                    progress?.Report(AuthProgressStep.Failed);
                    return AuthProbeResult.Cancelled(windowOpened);
                }

                InvalidateSession();
                PersistIdentity(webSession);
                progress?.Report(AuthProgressStep.Completed);
                return AuthProbeResult.Authenticated(webSession.NameOnPlatform ?? webSession.UserId, windowOpened: windowOpened);
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

        /// <summary>
        /// Clears the persisted identity and the in-memory session, and starts dropping the site's
        /// stored session. Runs on the UI thread with no way to await, so the local-storage wipe is
        /// started rather than waited for; callers that can await use <see cref="ClearSessionAsync"/>.
        /// </summary>
        public void ClearSession()
        {
            ClearLocalState();
            _ = ClearStoredSessionAsync(CancellationToken.None);
        }

        /// <summary>
        /// A full sign-out: the persisted identity, the in-memory session and the site's stored
        /// session. The site keeps its session only in local storage, so its cookies stay.
        /// </summary>
        public async Task ClearSessionAsync(CancellationToken ct)
        {
            ClearLocalState();
            await ClearStoredSessionAsync(ct).ConfigureAwait(false);
        }

        private void ClearLocalState()
        {
            _logger?.Info($"{LogPrefix} Clearing session.");
            _clientSession = null;
            _loginSession = null;

            var settings = ProviderRegistry.Settings<UbisoftSettings>();
            settings.UserId = null;
            settings.NameOnPlatform = null;
            ProviderRegistry.Write(settings, persistToDisk: true);
        }

        private async Task ClearStoredSessionAsync(CancellationToken ct)
        {
            try
            {
                await _api.WithOffscreenViewAsync(async view =>
                {
                    // Local storage is origin scoped, so the page has to be loaded to touch it.
                    await view.NavigateAndWaitAsync(SessionPageUrl, timeoutMs: 10000);
                    return await view.EvaluateScriptAsync(ClearSessionScript);
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"{LogPrefix} Failed to clear the site's stored session.");
            }
        }

        private void PersistIdentity(UbisoftSession session)
        {
            var settings = ProviderRegistry.Settings<UbisoftSettings>();
            var name = session.NameOnPlatform ?? settings.NameOnPlatform;
            if (string.Equals(settings.UserId, session.UserId, StringComparison.Ordinal) &&
                string.Equals(settings.NameOnPlatform, name, StringComparison.Ordinal))
            {
                return;
            }

            settings.UserId = session.UserId;
            settings.NameOnPlatform = name;
            ProviderRegistry.Write(settings, persistToDisk: true);
        }

        /// <summary>The site's stored session, or null when it is absent or expired.</summary>
        private Task<UbisoftSession> ReadWebSessionAsync(CancellationToken ct)
        {
            return _api.WithOffscreenViewAsync(async view =>
            {
                await view.NavigateAndWaitAsync(SessionPageUrl);
                return await ReadSessionFromViewAsync(view, ct);
            });
        }

        /// <summary>
        /// The site's stored session; when it has expired and the site remembers the account, the
        /// remembered-account page's continue button is pressed and the renewed session read back.
        /// </summary>
        private Task<UbisoftSession> ReadOrResumeWebSessionAsync(CancellationToken ct)
        {
            return _api.WithOffscreenViewAsync(async view =>
            {
                await view.NavigateAndWaitAsync(SessionPageUrl);
                var session = await ReadSessionFromViewAsync(view, ct);
                if (session != null)
                {
                    return session;
                }

                if (!await HasRememberedAccountAsync(view))
                {
                    return null;
                }

                _logger?.Info($"{LogPrefix} Stored session expired; resuming the remembered account.");
                await view.NavigateAndWaitAsync(LoginUrl);

                var clicked = await AsyncPoll.UntilAsync(
                    async token => (await EvaluateStringAsync(view, ContinueScript)) ?? string.Empty,
                    result => result == "clicked" || result == "form",
                    maxAttempts: 20,
                    delayMs: 500,
                    ct);
                if (clicked != "clicked")
                {
                    _logger?.Warn($"{LogPrefix} Could not resume the remembered account ({(string.IsNullOrEmpty(clicked) ? "no page" : clicked)}).");
                    return null;
                }

                return await AsyncPoll.UntilAsync(
                    token => ReadSessionFromViewAsync(view, token),
                    resumed => resumed != null,
                    maxAttempts: 20,
                    delayMs: 500,
                    ct);
            });
        }

        private async Task<bool> HasRememberedAccountAsync(IWebView view)
        {
            return !string.IsNullOrEmpty(await EvaluateStringAsync(view, HasRememberMeScript));
        }

        private async Task<UbisoftSession> ReadSessionFromViewAsync(IWebView view, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var session = UbisoftParsing.ParseSession(await EvaluateStringAsync(view, ReadSessionScript));
            return session != null && session.IsUsable(DateTime.UtcNow, SessionMargin) ? session : null;
        }

        private async Task<string> EvaluateStringAsync(IWebView view, string script)
        {
            try
            {
                var evaluated = await view.EvaluateScriptAsync(script);
                return evaluated?.Success == true ? evaluated.Result as string : null;
            }
            catch (Exception ex)
            {
                // Evaluating mid-navigation fails; callers treat that as "not ready yet".
                _logger?.Debug(ex, $"{LogPrefix} Script evaluation failed.");
                return null;
            }
        }

        /// <summary>
        /// Opens the login dialog on the UI thread and blocks until a session is stored or the dialog
        /// is closed.
        /// </summary>
        private UbisoftSession LoginInteractively()
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
                return _loginSession;
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
            if (e.IsLoading || _loginSession != null)
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
                // The site writes the session after its post-login redirect settles.
                var session = await AsyncPoll.UntilAsync(
                    token => ReadSessionFromViewAsync(view, token),
                    found => found != null,
                    maxAttempts: 8,
                    delayMs: 500,
                    CancellationToken.None).ConfigureAwait(false);

                if (session == null)
                {
                    return;
                }

                _loginSession = session;

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
            catch (Exception ex)
            {
                _logger?.Warn(ex, $"{LogPrefix} Failed to check login state.");
            }
            finally
            {
                Interlocked.Exchange(ref _loginCheckInProgress, 0);
            }
        }
    }
}
