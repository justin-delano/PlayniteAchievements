using Playnite.SDK;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK.Data;
using PlayniteAchievements.Providers.Xbox.Models;

namespace PlayniteAchievements.Providers.Xbox
{
    /// <summary>
    /// Manages Xbox Live authentication via Microsoft Live OAuth.
    /// Handles token storage, refresh, and session validation.
    /// </summary>
    public sealed class XboxSessionManager
    {
        private static readonly TimeSpan InteractiveAuthTimeout = TimeSpan.FromMinutes(3);
        private static readonly TimeSpan CachedTokenLifetime = TimeSpan.FromMinutes(45);

        private const string ClientId = "38cd2fa8-66fd-4760-afb2-405eb65d5b0c";
        private const string RedirectUri = "https://login.live.com/oauth20_desktop.srf";
        private const string Scope = "Xboxlive.signin Xboxlive.offline_access";

        private const string LoginUrlFormat = @"https://login.live.com/oauth20_authorize.srf?client_id={0}&response_type=code&approval_prompt=auto&scope={1}&redirect_uri={2}";

        private const string TokenUrl = "https://login.live.com/oauth20_token.srf";
        private const string XboxAuthUrl = "https://user.auth.xboxlive.com/user/authenticate";
        private const string XstsAuthUrl = "https://xsts.auth.xboxlive.com/xsts/authorize";
        private const string ProfileUrl = "https://profile.xboxlive.com/users/batch/profile/settings";

        private readonly IPlayniteAPI _api;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _tokenSemaphore = new SemaphoreSlim(1, 1);
        private readonly string _liveTokensPath;
        private readonly string _xstsTokensPath;

        private AuthorizationData _cachedAuthData;
        private DateTime _tokenAcquiredUtc = DateTime.MinValue;
        private bool _isSessionAuthenticated;

        public XboxSessionManager(IPlayniteAPI api, ILogger logger, PersistedSettings settings)
        {
            if (api == null) throw new ArgumentNullException(nameof(api));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            _api = api;
            _logger = logger;
            var basePath = Path.Combine(api.Paths.ExtensionsDataPath, "PlayniteAchievements");
            _liveTokensPath = Path.Combine(basePath, "xbox_live.json");
            _xstsTokensPath = Path.Combine(basePath, "xbox_xsts.json");
        }

        public bool IsAuthenticated => _isSessionAuthenticated;

        /// <summary>
        /// Primes the authentication state on startup.
        /// </summary>
        public async Task PrimeAuthenticationStateAsync(CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var result = await ProbeAuthenticationAsync(ct).ConfigureAwait(false);
                _logger?.Debug($"[XboxAch] Startup auth probe completed with outcome={result?.Outcome}.");
            }
            catch (OperationCanceledException)
            {
                _logger?.Debug("[XboxAch] Startup auth probe cancelled.");
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "[XboxAch] Startup auth probe failed.");
            }
        }

        /// <summary>
        /// Gets the authorization data needed for API calls.
        /// </summary>
        public async Task<AuthorizationData> GetAuthorizationAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var authData = await TryAcquireAuthorizationAsync(ct, forceRefresh: false).ConfigureAwait(false);
            if (authData != null)
            {
                return authData;
            }

            throw new XboxAuthRequiredException("Xbox authentication required. Please login.");
        }

        /// <summary>
        /// Probes current authentication state without triggering a login.
        /// </summary>
        public async Task<XboxAuthResult> ProbeAuthenticationAsync(CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                var authData = await TryAcquireAuthorizationAsync(ct, forceRefresh: false).ConfigureAwait(false);
                if (authData != null)
                {
                    return XboxAuthResult.Create(
                        XboxAuthOutcome.AlreadyAuthenticated,
                        "LOCPlayAch_Settings_XboxAuth_AlreadyAuthenticated",
                        windowOpened: false);
                }

                return XboxAuthResult.Create(
                    XboxAuthOutcome.NotAuthenticated,
                    "LOCPlayAch_Settings_XboxAuth_NotAuthenticated",
                    windowOpened: false);
            }
            catch (OperationCanceledException)
            {
                return XboxAuthResult.Create(
                    XboxAuthOutcome.Cancelled,
                    "LOCPlayAch_Settings_XboxAuth_Cancelled",
                    windowOpened: false);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[XboxAch] Probe failed with exception.");
                return XboxAuthResult.Create(
                    XboxAuthOutcome.ProbeFailed,
                    "LOCPlayAch_Settings_XboxAuth_ProbeFailed",
                    windowOpened: false);
            }
        }

        /// <summary>
        /// Performs interactive authentication via WebView.
        /// </summary>
        public async Task<XboxAuthResult> AuthenticateInteractiveAsync(
            bool forceInteractive,
            CancellationToken ct,
            IProgress<XboxAuthProgressStep> progress = null)
        {
            var windowOpened = false;

            try
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(XboxAuthProgressStep.CheckingExistingSession);

                if (!forceInteractive)
                {
                    var existingAuth = await TryAcquireAuthorizationAsync(ct, forceRefresh: false).ConfigureAwait(false);
                    if (existingAuth != null)
                    {
                        progress?.Report(XboxAuthProgressStep.Completed);
                        return XboxAuthResult.Create(
                            XboxAuthOutcome.AlreadyAuthenticated,
                            "LOCPlayAch_Settings_XboxAuth_AlreadyAuthenticated",
                            windowOpened: false);
                    }
                }

                progress?.Report(XboxAuthProgressStep.OpeningLoginWindow);
                windowOpened = await LoginAsync(ct).ConfigureAwait(false);

                if (!windowOpened)
                {
                    progress?.Report(XboxAuthProgressStep.Failed);
                    return XboxAuthResult.Create(
                        XboxAuthOutcome.Failed,
                        "LOCPlayAch_Settings_XboxAuth_WindowNotOpened",
                        windowOpened: false);
                }

                progress?.Report(XboxAuthProgressStep.WaitingForUserLogin);
                var deadlineUtc = DateTime.UtcNow.Add(InteractiveAuthTimeout);

                while (DateTime.UtcNow < deadlineUtc)
                {
                    ct.ThrowIfCancellationRequested();

                    var authData = await TryAcquireAuthorizationAsync(ct, forceRefresh: true).ConfigureAwait(false);
                    if (authData != null)
                    {
                        progress?.Report(XboxAuthProgressStep.Completed);
                        return XboxAuthResult.Create(
                            XboxAuthOutcome.Authenticated,
                            "LOCPlayAch_Settings_XboxAuth_Verified",
                            windowOpened: true);
                    }

                    await Task.Delay(1000, ct).ConfigureAwait(false);
                }

                progress?.Report(XboxAuthProgressStep.Failed);
                return XboxAuthResult.Create(
                    XboxAuthOutcome.TimedOut,
                    "LOCPlayAch_Settings_XboxAuth_TimedOut",
                    windowOpened: true);
            }
            catch (OperationCanceledException)
            {
                return XboxAuthResult.Create(
                    XboxAuthOutcome.Cancelled,
                    "LOCPlayAch_Settings_XboxAuth_Cancelled",
                    windowOpened: windowOpened);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[XboxAch] Interactive auth failed.");
                progress?.Report(XboxAuthProgressStep.Failed);
                return XboxAuthResult.Create(
                    XboxAuthOutcome.Failed,
                    "LOCPlayAch_Settings_XboxAuth_Failed",
                    windowOpened: windowOpened);
            }
        }

        /// <summary>
        /// Clears the stored session data.
        /// </summary>
        public void ClearSession()
        {
            try
            {
                ClearAuthentication();
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "[XboxAch] Failed to clear Xbox auth state.");
            }

            SetCachedAuthorization(null);
        }

        private async Task<bool> LoginAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var callbackUrl = string.Empty;

            try
            {
                // Delete existing token files
                if (File.Exists(_liveTokensPath))
                {
                    File.Delete(_liveTokensPath);
                }

                if (File.Exists(_xstsTokensPath))
                {
                    File.Delete(_xstsTokensPath);
                }

                var loginUrl = string.Format(LoginUrlFormat, ClientId, Uri.EscapeDataString(Scope), Uri.EscapeDataString(RedirectUri));

                var webViewSettings = new WebViewSettings
                {
                    WindowHeight = 560,
                    WindowWidth = 490
                };

                using (var view = _api.WebViews.CreateView(webViewSettings))
                {
                    view.LoadingChanged += (s, e) =>
                    {
                        var url = view.GetCurrentAddress();
                        if (url.Contains("code="))
                        {
                            callbackUrl = url;
                            view.Close();
                        }
                    };

                    // Clear existing cookies
                    view.DeleteDomainCookies(".live.com");
                    view.DeleteDomainCookies(".login.live.com");
                    view.DeleteDomainCookies("live.com");
                    view.DeleteDomainCookies("login.live.com");
                    view.DeleteDomainCookies(".xboxlive.com");
                    view.DeleteDomainCookies(".xbox.com");
                    view.DeleteDomainCookies(".microsoft.com");
                    view.Navigate(loginUrl);
                    view.OpenDialog();
                }

                if (string.IsNullOrEmpty(callbackUrl))
                {
                    return false;
                }

                // Extract authorization code from callback URL
                var rediUri = new Uri(callbackUrl);
                var authorizationCode = GetQueryParam(rediUri.Query, "code");

                if (string.IsNullOrEmpty(authorizationCode))
                {
                    _logger?.Warn("[XboxAch] No authorization code in callback URL.");
                    return false;
                }

                // Exchange authorization code for tokens
                var tokenResponse = await RequestOAuthTokenAsync(authorizationCode).ConfigureAwait(false);

                var liveTokens = new LiveTokens
                {
                    access_token = tokenResponse.access_token,
                    refresh_token = tokenResponse.refresh_token,
                    expires_in = tokenResponse.expires_in,
                    token_type = tokenResponse.token_type,
                    user_id = tokenResponse.user_id,
                    CreationDate = DateTime.Now
                };

                // Save tokens encrypted
                SaveLiveTokens(liveTokens);

                // Authenticate with Xbox Live
                await AuthenticateXboxLiveAsync(liveTokens.access_token).ConfigureAwait(false);

                return true;
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[XboxAch] Login failed.");
                return false;
            }
        }

        private async Task<LiveTokens> RequestOAuthTokenAsync(string authorizationCode)
        {
            var requestData = BuildFormData(new Dictionary<string, string>
            {
                { "grant_type", "authorization_code" },
                { "code", authorizationCode }
            });
            return await ExecuteTokenRequestAsync(requestData).ConfigureAwait(false);
        }

        private async Task<LiveTokens> RefreshOAuthTokenAsync(string refreshToken)
        {
            var requestData = BuildFormData(new Dictionary<string, string>
            {
                { "grant_type", "refresh_token" },
                { "refresh_token", refreshToken }
            });
            return await ExecuteTokenRequestAsync(requestData).ConfigureAwait(false);
        }

        private async Task<LiveTokens> ExecuteTokenRequestAsync(string formData)
        {
            using (var client = new HttpClient())
            {
                var response = await client.PostAsync(
                    TokenUrl,
                    new StringContent(formData, Encoding.ASCII, "application/x-www-form-urlencoded")).ConfigureAwait(false);

                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return Serialization.FromJson<LiveTokens>(content);
            }
        }

        private string BuildFormData(Dictionary<string, string> data)
        {
            var allData = new Dictionary<string, string>(data)
            {
                { "scope", Scope },
                { "client_id", ClientId },
                { "redirect_uri", RedirectUri }
            };

            var parts = new List<string>();
            foreach (var kvp in allData)
            {
                parts.Add($"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}");
            }
            return string.Join("&", parts);
        }

        private async Task AuthenticateXboxLiveAsync(string accessToken)
        {
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("x-xbl-contract-version", "1");

                // Authenticate with Xbox Live
                var authRequest = new XboxAuthRequest
                {
                    Properties = { RpsTicket = $"d={accessToken}" }
                };

                var authContent = new StringContent(
                    Serialization.ToJson(authRequest),
                    Encoding.UTF8,
                    "application/json");

                var authResponse = await client.PostAsync(XboxAuthUrl, authContent).ConfigureAwait(false);
                var authResponseContent = await authResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!authResponse.IsSuccessStatusCode)
                {
                    _logger?.Error($"[XboxAch] Xbox auth failed: {authResponse.StatusCode} - {authResponseContent}");
                    throw new Exception("Xbox Live authentication failed.");
                }

                var authTokens = Serialization.FromJson<AuthorizationData>(authResponseContent);

                // Authorize with XSTS
                var xstsRequest = new XstsRequest
                {
                    Properties = { UserTokens = new List<string> { authTokens.Token } }
                };

                var xstsContent = new StringContent(
                    Serialization.ToJson(xstsRequest),
                    Encoding.UTF8,
                    "application/json");

                var xstsResponse = await client.PostAsync(XstsAuthUrl, xstsContent).ConfigureAwait(false);
                var xstsResponseContent = await xstsResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!xstsResponse.IsSuccessStatusCode)
                {
                    // Check for XSTS error
                    var xstsError = Serialization.FromJson<XstsResponse>(xstsResponseContent);
                    var errorCode = xstsError?.XErr?.XErr ?? 0;
                    var errorMessage = xstsError?.XErr?.Message ?? "Unknown error";

                    _logger?.Error($"[XboxAch] XSTS authorization failed: {errorCode} - {errorMessage}");

                    if (errorCode == 2148916233)
                    {
                        throw new Exception("The account doesn't have an Xbox account.");
                    }
                    else if (errorCode == 2148916235)
                    {
                        throw new Exception("Xbox Live is not available in your region.");
                    }
                    else if (errorCode == 2148916236 || errorCode == 2148916237)
                    {
                        throw new Exception("Adult verification required. Please login to Xbox.com to verify your account.");
                    }
                    else if (errorCode == 2148916238)
                    {
                        throw new Exception("The account belongs to a minor without Xbox profile.");
                    }

                    throw new Exception($"XSTS authorization failed: {errorMessage}");
                }

                // Save XSTS tokens encrypted
                var currentUser = WindowsIdentity.GetCurrent().User?.Value;
                if (!string.IsNullOrEmpty(currentUser))
                {
                    Encryption.EncryptToFile(
                        _xstsTokensPath,
                        xstsResponseContent,
                        Encoding.UTF8,
                        currentUser);
                }
            }
        }

        private async Task<bool> CheckAuthenticationAsync()
        {
            try
            {
                var authData = LoadXstsTokens();
                if (authData == null)
                {
                    return false;
                }

                // Verify session is still valid
                using (var client = new HttpClient())
                {
                    SetAuthenticationHeaders(client.DefaultRequestHeaders, authData);

                    var profileRequest = new ProfileRequest
                    {
                        settings = new List<string> { "GameDisplayName" },
                        userIds = new List<ulong> { ulong.Parse(authData.DisplayClaims.xui[0].xid) }
                    };

                    var response = await client.PostAsync(
                        ProfileUrl,
                        new StringContent(Serialization.ToJson(profileRequest), Encoding.UTF8, "application/json")).ConfigureAwait(false);

                    return response.IsSuccessStatusCode;
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "[XboxAch] Check authentication failed.");
                return false;
            }
        }

        private async Task<AuthorizationData> TryAcquireAuthorizationAsync(CancellationToken ct, bool forceRefresh)
        {
            if (!forceRefresh && HasFreshCachedAuthorization())
            {
                return _cachedAuthData;
            }

            await _tokenSemaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!forceRefresh && HasFreshCachedAuthorization())
                {
                    return _cachedAuthData;
                }

                // Try loading existing tokens
                var authData = LoadXstsTokens();
                if (authData == null)
                {
                    SetCachedAuthorization(null);
                    return null;
                }

                // Check if session is valid
                var isValid = await CheckAuthenticationAsync().ConfigureAwait(false);
                if (!isValid)
                {
                    // Try refreshing tokens
                    var liveTokens = LoadLiveTokens();
                    if (liveTokens != null && !string.IsNullOrEmpty(liveTokens.refresh_token))
                    {
                        try
                        {
                            var refreshedTokens = await RefreshOAuthTokenAsync(liveTokens.refresh_token).ConfigureAwait(false);
                            liveTokens.access_token = refreshedTokens.access_token;
                            liveTokens.refresh_token = refreshedTokens.refresh_token;
                            SaveLiveTokens(liveTokens);

                            await AuthenticateXboxLiveAsync(liveTokens.access_token).ConfigureAwait(false);
                            authData = LoadXstsTokens();
                        }
                        catch (Exception ex)
                        {
                            _logger?.Debug(ex, "[XboxAch] Token refresh failed.");
                        }
                    }

                    isValid = await CheckAuthenticationAsync().ConfigureAwait(false);
                    if (!isValid)
                    {
                        SetCachedAuthorization(null);
                        return null;
                    }
                }

                SetCachedAuthorization(authData);
                return authData;
            }
            finally
            {
                _tokenSemaphore.Release();
            }
        }

        private void ClearAuthentication()
        {
            try
            {
                if (File.Exists(_liveTokensPath))
                {
                    File.Delete(_liveTokensPath);
                }
                if (File.Exists(_xstsTokensPath))
                {
                    File.Delete(_xstsTokensPath);
                }
                _logger?.Info("[XboxAch] Authentication cleared.");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[XboxAch] Failed to clear authentication.");
            }
        }

        private void SaveLiveTokens(LiveTokens tokens)
        {
            try
            {
                var directory = Path.GetDirectoryName(_liveTokensPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var currentUser = WindowsIdentity.GetCurrent().User?.Value;
                if (!string.IsNullOrEmpty(currentUser))
                {
                    Encryption.EncryptToFile(
                        _liveTokensPath,
                        Serialization.ToJson(tokens),
                        Encoding.UTF8,
                        currentUser);
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[XboxAch] Failed to save Live tokens.");
            }
        }

        private LiveTokens LoadLiveTokens()
        {
            try
            {
                if (!File.Exists(_liveTokensPath))
                {
                    return null;
                }

                var currentUser = WindowsIdentity.GetCurrent().User?.Value;
                if (string.IsNullOrEmpty(currentUser))
                {
                    return null;
                }

                var json = Encryption.DecryptFromFile(_liveTokensPath, Encoding.UTF8, currentUser);
                return Serialization.FromJson<LiveTokens>(json);
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "[XboxAch] Failed to load Live tokens.");
                return null;
            }
        }

        private AuthorizationData LoadXstsTokens()
        {
            try
            {
                if (!File.Exists(_xstsTokensPath))
                {
                    return null;
                }

                var currentUser = WindowsIdentity.GetCurrent().User?.Value;
                if (string.IsNullOrEmpty(currentUser))
                {
                    return null;
                }

                var json = Encryption.DecryptFromFile(_xstsTokensPath, Encoding.UTF8, currentUser);
                return Serialization.FromJson<AuthorizationData>(json);
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "[XboxAch] Failed to load XSTS tokens.");
                return null;
            }
        }

        private bool HasFreshCachedAuthorization()
        {
            return _cachedAuthData != null &&
                   (DateTime.UtcNow - _tokenAcquiredUtc) < CachedTokenLifetime;
        }

        private void SetCachedAuthorization(AuthorizationData authData)
        {
            if (authData == null)
            {
                _cachedAuthData = null;
                _tokenAcquiredUtc = DateTime.MinValue;
                _isSessionAuthenticated = false;
                return;
            }

            _cachedAuthData = authData;
            _tokenAcquiredUtc = DateTime.UtcNow;
            _isSessionAuthenticated = true;
        }

        /// <summary>
        /// Extracts a query parameter value from a URL query string.
        /// </summary>
        private static string GetQueryParam(string query, string key)
        {
            if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            // Remove leading '?' if present
            if (query.StartsWith("?"))
            {
                query = query.Substring(1);
            }

            var pairs = query.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in pairs)
            {
                var keyValue = pair.Split(new[] { '=' }, 2);
                if (keyValue.Length == 2 && string.Equals(keyValue[0], key, StringComparison.OrdinalIgnoreCase))
                {
                    return Uri.UnescapeDataString(keyValue[1]);
                }
            }

            return null;
        }

        /// <summary>
        /// Sets authentication headers for an HTTP request.
        /// </summary>
        public static void SetAuthenticationHeaders(
            System.Net.Http.Headers.HttpRequestHeaders headers,
            AuthorizationData auth,
            string contractVersion = "2",
            string acceptLanguage = "en-US")
        {
            headers.Add("x-xbl-contract-version", contractVersion);
            headers.Add("Authorization", $"XBL3.0 x={auth.DisplayClaims.xui[0].uhs};{auth.Token}");
            headers.Add("Accept-Language", acceptLanguage);
        }
    }
}
