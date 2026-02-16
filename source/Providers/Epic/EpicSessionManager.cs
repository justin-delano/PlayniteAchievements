using Newtonsoft.Json;
using PlayniteAchievements.Models;
using PlayniteAchievements.Common;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.Epic
{
    /// <summary>
    /// Manages Epic Games authentication by reusing credentials from installed library plugins.
    /// Supports Epic Games Library (official) and Legendary library plugins.
    /// Falls back to legacy persisted settings when no library plugin is available.
    /// </summary>
    public sealed class EpicSessionManager : IEpicSessionProvider
    {
        #region Constants

        private const string UrlAccountAuth = "https://account-public-service-prod03.ol.epicgames.com/account/api/oauth/token";
        private const string AuthEncodedString = "MzRhMDJjZjhmNDQxNGUyOWIxNTkyMTg3NmRhMzZmOWE6ZGFhZmJjY2M3Mzc3NDUwMzlkZmZlNTNkOTRmYzc2Y2Y=";

        private const int TokenExpiryBufferMinutes = 5;

        // Library plugin IDs
        private const string EpicLibraryPluginId = "00000002-DBD1-46C6-B5D0-B1BA559D10E4";
        private const string LegendaryPluginId = "EAD65C3B-2F8F-4E37-B4E6-B3DE6BE540C6";

        // Credential file names in StoresData (shared by both Epic Games Library and Legendary)
        private const string TokenFileName = "Epic_Tokens.dat";

        #endregion

        #region Fields

        private readonly IPlayniteAPI _api;
        private readonly ILogger _logger;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly SemaphoreSlim _tokenRefreshSemaphore = new SemaphoreSlim(1, 1);

        private string _accountId;
        private string _accessToken;
        private string _refreshToken;
        private string _tokenType;
        private DateTime _tokenExpiryUtc;
        private DateTime _refreshTokenExpiryUtc;

        private bool _useLibraryCredentials;
        private string _libraryPluginName;
        private bool _libraryTokenValidated;

        #endregion

        #region Constructor

        public EpicSessionManager(IPlayniteAPI api, ILogger logger, PlayniteAchievementsSettings settings)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _logger = logger;
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            // Try library credentials first
            _libraryPluginName = GetInstalledLibraryPluginName();
            if (_libraryPluginName != null)
            {
                var token = TryLoadLibraryToken();
                if (token != null && !string.IsNullOrWhiteSpace(token.Token))
                {
                    _useLibraryCredentials = true;
                    _libraryTokenValidated = false;
                    LoadFromStoreToken(token);
                    _logger?.Info($"[EpicAuth] Found credentials from {_libraryPluginName} library plugin. Will validate on first use.");
                    return;
                }
            }

            // Fall back to persisted settings (legacy mode)
            LoadFromPersistedSettings();
        }

        #endregion

        #region Public Properties

        public bool IsAuthenticated => HasValidAccessToken();

        public bool IsUsingLibraryCredentials => _libraryPluginName != null;

        public string LibraryPluginName => _libraryPluginName ?? "Epic";

        #endregion

        #region IEpicSessionProvider Implementation

        public string GetAccountId() => _accountId;

        public async Task<string> GetAccessTokenAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            // If using library credentials, validate the token with Epic API first
            if (_useLibraryCredentials && !_libraryTokenValidated && !string.IsNullOrWhiteSpace(_accessToken))
            {
                await _tokenRefreshSemaphore.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    // Double-check after acquiring lock
                    if (!_libraryTokenValidated && !string.IsNullOrWhiteSpace(_accessToken))
                    {
                        if (await TryValidateLibraryTokenAsync(ct).ConfigureAwait(false))
                        {
                            return _accessToken;
                        }
                        // Validation failed - fall through to refresh logic
                    }
                }
                finally
                {
                    _tokenRefreshSemaphore.Release();
                }
            }

            if (HasValidAccessToken())
            {
                return _accessToken;
            }

            if (!string.IsNullOrWhiteSpace(_refreshToken) && DateTime.UtcNow < _refreshTokenExpiryUtc)
            {
                await _tokenRefreshSemaphore.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    if (HasValidAccessToken())
                    {
                        return _accessToken;
                    }

                    await RenewTokensAsync(_refreshToken, ct).ConfigureAwait(false);
                    if (HasValidAccessToken())
                    {
                        return _accessToken;
                    }
                }
                finally
                {
                    _tokenRefreshSemaphore.Release();
                }
            }

            throw new EpicAuthRequiredException("Epic access token unavailable. Please authenticate.");
        }

        public async Task<bool> TryRefreshTokenAsync(CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(_refreshToken) || DateTime.UtcNow >= _refreshTokenExpiryUtc)
            {
                _logger?.Debug("[EpicAuth] Cannot refresh token - no valid refresh token available.");
                return false;
            }

            await _tokenRefreshSemaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (HasValidAccessToken())
                {
                    return true;
                }

                try
                {
                    await RenewTokensAsync(_refreshToken, ct).ConfigureAwait(false);
                    var success = HasValidAccessToken();
                    if (success)
                    {
                        _logger?.Info("[EpicAuth] Token refresh succeeded.");
                    }
                    else
                    {
                        _logger?.Warn("[EpicAuth] Token refresh completed but no valid access token.");
                    }
                    return success;
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, "[EpicAuth] Token refresh failed.");
                    return false;
                }
            }
            finally
            {
                _tokenRefreshSemaphore.Release();
            }
        }

        #endregion

        #region Authentication Methods

        public async Task PrimeAuthenticationStateAsync(CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var result = await ProbeAuthenticationAsync(ct).ConfigureAwait(false);
                _logger?.Debug($"[EpicAuth] Startup auth probe completed with outcome={result?.Outcome}.");
            }
            catch (OperationCanceledException)
            {
                _logger?.Debug("[EpicAuth] Startup auth probe cancelled.");
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "[EpicAuth] Startup auth probe failed.");
            }
        }

        public async Task<EpicAuthResult> ProbeAuthenticationAsync(CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                if (HasValidAccessToken())
                {
                    return EpicAuthResult.Create(
                        EpicAuthOutcome.AlreadyAuthenticated,
                        _libraryPluginName != null
                            ? "LOCPlayAch_Settings_Epic_LibraryDetected"
                            : "LOCPlayAch_Settings_EpicAuth_AlreadyAuthenticated",
                        _accountId,
                        windowOpened: false);
                }

                // Try to refresh with existing refresh token
                if (!string.IsNullOrWhiteSpace(_refreshToken) && DateTime.UtcNow < _refreshTokenExpiryUtc)
                {
                    try
                    {
                        await RenewTokensAsync(_refreshToken, ct).ConfigureAwait(false);
                    }
                    catch (Exception renewEx)
                    {
                        _logger?.Debug(renewEx, "[EpicAuth] Refresh token probe failed.");
                    }

                    if (HasValidAccessToken())
                    {
                        return EpicAuthResult.Create(
                            EpicAuthOutcome.AlreadyAuthenticated,
                            _libraryPluginName != null
                                ? "LOCPlayAch_Settings_Epic_LibraryDetected"
                                : "LOCPlayAch_Settings_EpicAuth_AlreadyAuthenticated",
                            _accountId,
                            windowOpened: false);
                    }
                }

                // No valid tokens available
                if (_libraryPluginName != null)
                {
                    // Library is detected but not logged in
                    return EpicAuthResult.Create(
                        EpicAuthOutcome.NotAuthenticated,
                        "LOCPlayAch_Settings_Epic_LibraryNotLoggedIn",
                        windowOpened: false);
                }

                return EpicAuthResult.Create(
                    EpicAuthOutcome.NotAuthenticated,
                    "LOCPlayAch_Settings_EpicAuth_NotAuthenticated",
                    windowOpened: false);
            }
            catch (OperationCanceledException)
            {
                return EpicAuthResult.Create(
                    EpicAuthOutcome.Cancelled,
                    "LOCPlayAch_Settings_EpicAuth_Cancelled",
                    windowOpened: false);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[EpicAuth] Probe failed with exception.");
                return EpicAuthResult.Create(
                    EpicAuthOutcome.ProbeFailed,
                    "LOCPlayAch_Settings_EpicAuth_ProbeFailed",
                    windowOpened: false);
            }
        }

        /// <summary>
        /// Clears the session state. In library mode, only clears in-memory state
        /// to preserve library plugin credentials.
        /// </summary>
        public void ClearSession()
        {
            // Only clear persisted settings in legacy mode
            if (!_useLibraryCredentials)
            {
                _settings.Persisted.EpicAccountId = null;
                _settings.Persisted.EpicAccessToken = null;
                _settings.Persisted.EpicRefreshToken = null;
                _settings.Persisted.EpicTokenType = null;
                _settings.Persisted.EpicTokenExpiryUtc = null;
                _settings.Persisted.EpicRefreshTokenExpiryUtc = null;
            }

            // Always clear in-memory state
            _accountId = null;
            _accessToken = null;
            _refreshToken = null;
            _tokenType = "bearer";
            _tokenExpiryUtc = DateTime.MinValue;
            _refreshTokenExpiryUtc = DateTime.MinValue;
            _libraryTokenValidated = false;

            _logger?.Info("[EpicAuth] Session cleared.");
        }

        /// <summary>
        /// Forces a refresh of credentials from the library plugin.
        /// Used when the user clicks "Refresh from Library" in settings.
        /// </summary>
        public void RefreshFromLibrary()
        {
            _libraryPluginName = GetInstalledLibraryPluginName();
            if (_libraryPluginName != null)
            {
                var token = TryLoadLibraryToken();
                if (token != null && !string.IsNullOrWhiteSpace(token.Token))
                {
                    _useLibraryCredentials = true;
                    _libraryTokenValidated = false;
                    LoadFromStoreToken(token);
                    _logger?.Info($"[EpicAuth] Refreshed credentials from {_libraryPluginName} library plugin.");
                    return;
                }
            }

            // No library credentials available
            _useLibraryCredentials = false;
            _libraryTokenValidated = false;
            _logger?.Info("[EpicAuth] No library credentials available for refresh.");
        }

        #endregion

        #region Library Plugin Detection

        /// <summary>
        /// Checks if an Epic library plugin is installed and returns its name.
        /// Uses Playnite API to detect installed plugins. Epic Games Library takes precedence.
        /// </summary>
        public string GetInstalledLibraryPluginName()
        {
            try
            {
                var plugins = _api?.Addons?.Plugins;
                if (plugins == null)
                {
                    _logger?.Debug("[EpicAuth] Could not access plugins collection.");
                    return null;
                }

                var epicId = Guid.Parse(EpicLibraryPluginId);
                var legendaryId = Guid.Parse(LegendaryPluginId);

                // Check for Epic Games Library first
                foreach (var plugin in plugins)
                {
                    if (plugin == null) continue;
                    if (plugin.Id == epicId)
                    {
                        _logger?.Debug("[EpicAuth] Found Epic Games Library plugin.");
                        return "Epic Games Library";
                    }
                }

                // Check for Legendary Library
                foreach (var plugin in plugins)
                {
                    if (plugin == null) continue;
                    if (plugin.Id == legendaryId)
                    {
                        _logger?.Debug("[EpicAuth] Found Legendary Library plugin.");
                        return "Legendary Library";
                    }
                }

                _logger?.Debug("[EpicAuth] No Epic library plugin found.");
                return null;
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[EpicAuth] Error checking for Epic library plugin.");
                return null;
            }
        }

        /// <summary>
        /// Checks if any Epic library plugin is installed.
        /// </summary>
        public bool IsLibraryPluginInstalled() => !string.IsNullOrEmpty(GetInstalledLibraryPluginName());

        private static string GetStoresDataPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Playnite", "ExtensionsData", "StoresData");
        }

        private static string GetExtensionsDataPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Playnite", "ExtensionsData");
        }

        #endregion

        #region Token Loading

        private EpicStoreToken TryLoadLibraryToken()
        {
            // Try official Epic Games Library plugin first (stores tokens in its own directory)
            var officialTokenPath = Path.Combine(GetExtensionsDataPath(), EpicLibraryPluginId, "tokens.json");
            if (File.Exists(officialTokenPath))
            {
                var token = TryDecryptToken(officialTokenPath);
                if (token != null)
                {
                    _logger?.Debug($"[EpicAuth] Loaded token from Epic Games Library plugin (official).");
                    return token;
                }
            }

            // Try Legendary Library plugin encrypted tokens
            var legendaryEncryptedPath = Path.Combine(GetExtensionsDataPath(), LegendaryPluginId, "tokens_encrypted.json");
            if (File.Exists(legendaryEncryptedPath))
            {
                var token = TryDecryptToken(legendaryEncryptedPath);
                if (token != null)
                {
                    _logger?.Debug($"[EpicAuth] Loaded encrypted token from Legendary Library plugin.");
                    return token;
                }
            }

            // Try Legendary unencrypted tokens (from legendary launcher config)
            // Check multiple possible locations: original legendary, Heroic variant, and env variable
            var legendaryToken = TryLoadLegendaryTokenFromConfig();
            if (legendaryToken != null)
            {
                _logger?.Debug($"[EpicAuth] Loaded token from Legendary config.");
                return legendaryToken;
            }

            // Fallback to playnite-plugincommon format (used by SuccessStory and other plugins)
            var pluginCommonTokenPath = Path.Combine(GetStoresDataPath(), TokenFileName);
            if (File.Exists(pluginCommonTokenPath))
            {
                var token = TryDecryptToken(pluginCommonTokenPath);
                if (token != null)
                {
                    _logger?.Debug($"[EpicAuth] Loaded token from {_libraryPluginName ?? "Epic"} library plugin (plugincommon).");
                    return token;
                }
            }

            _logger?.Debug($"[EpicAuth] No token files found.");
            return null;
        }

        /// <summary>
        /// Loads token from Legendary's unencrypted user.json format.
        /// </summary>
        private EpicStoreToken TryLoadLegendaryToken(string filePath)
        {
            try
            {
                var json = File.ReadAllText(filePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }

                // Legendary user.json has different structure - map it to EpicStoreToken
                var legendaryToken = JsonConvert.DeserializeObject<LegendaryUserToken>(json);
                if (legendaryToken == null || string.IsNullOrWhiteSpace(legendaryToken.access_token))
                {
                    return null;
                }

                return new EpicStoreToken
                {
                    AccountId = legendaryToken.account_id,
                    Token = legendaryToken.access_token,
                    RefreshToken = legendaryToken.refresh_token,
                    Type = legendaryToken.token_type,
                    ExpireAt = ParseLegendaryExpiry(legendaryToken.expires_at),
                    RefreshExpireAt = ParseLegendaryExpiry(legendaryToken.refresh_expires_at)
                };
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"[EpicAuth] Failed to load Legendary token from {filePath}.");
                return null;
            }
        }

        private static DateTime? ParseLegendaryExpiry(string expiresAt)
        {
            if (string.IsNullOrWhiteSpace(expiresAt))
            {
                return null;
            }

            if (DateTime.TryParse(expiresAt, out var dt))
            {
                return dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
            }

            return null;
        }

        /// <summary>
        /// Tries to load Legendary token from multiple possible config locations.
        /// Checks: original legendary, Heroic Games Launcher variant, and LEGENDARY_CONFIG_PATH env var.
        /// </summary>
        private EpicStoreToken TryLoadLegendaryTokenFromConfig()
        {
            // Build list of possible Legendary config paths
            var configPaths = new List<string>();

            // Check for LEGENDARY_CONFIG_PATH environment variable first
            var envConfigPath = Environment.GetEnvironmentVariable("LEGENDARY_CONFIG_PATH");
            if (!string.IsNullOrWhiteSpace(envConfigPath) && Directory.Exists(envConfigPath))
            {
                configPaths.Add(Path.Combine(envConfigPath, "user.json"));
            }

            // Original legendary config path
            var originalLegendaryPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "legendary", "user.json");
            configPaths.Add(originalLegendaryPath);

            // Heroic Games Launcher's legendary config path
            var heroicLegendaryPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "heroic", "legendaryConfig", "legendary", "user.json");
            configPaths.Add(heroicLegendaryPath);

            // Try each path, preferring the most recently modified one
            foreach (var configPath in configPaths)
            {
                if (File.Exists(configPath))
                {
                    var token = TryLoadLegendaryToken(configPath);
                    if (token != null)
                    {
                        _logger?.Debug($"[EpicAuth] Loaded Legendary token from {configPath}.");
                        return token;
                    }
                }
            }

            return null;
        }

        private EpicStoreToken TryDecryptToken(string filePath)
        {
            try
            {
                var password = WindowsIdentity.GetCurrent().User?.Value;
                if (string.IsNullOrEmpty(password))
                {
                    _logger?.Warn("[EpicAuth] Cannot decrypt token - no user identity available.");
                    return null;
                }

                var json = DecryptFromFile(filePath, Encoding.UTF8, password);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }

                return JsonConvert.DeserializeObject<EpicStoreToken>(json);
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"[EpicAuth] Failed to decrypt token from {filePath}.");
                return null;
            }
        }

        private void LoadFromStoreToken(EpicStoreToken token)
        {
            if (token == null)
            {
                return;
            }

            _accountId = token.AccountId?.Trim();
            _accessToken = token.Token;
            _refreshToken = token.RefreshToken;
            _tokenType = string.IsNullOrWhiteSpace(token.Type) ? "bearer" : token.Type.Trim();
            _tokenExpiryUtc = token.ExpireAt ?? DateTime.MinValue;
            _refreshTokenExpiryUtc = token.RefreshExpireAt ?? DateTime.MinValue;
        }

        private void LoadFromPersistedSettings()
        {
            _accountId = _settings.Persisted.EpicAccountId?.Trim();
            _accessToken = _settings.Persisted.EpicAccessToken;
            _refreshToken = _settings.Persisted.EpicRefreshToken;
            _tokenType = string.IsNullOrWhiteSpace(_settings.Persisted.EpicTokenType) ? "bearer" : _settings.Persisted.EpicTokenType.Trim();
            _tokenExpiryUtc = _settings.Persisted.EpicTokenExpiryUtc ?? DateTime.MinValue;
            _refreshTokenExpiryUtc = _settings.Persisted.EpicRefreshTokenExpiryUtc ?? DateTime.MinValue;
        }

        #endregion

        #region Token Renewal

        /// <summary>
        /// Validates the library token with Epic's API and refreshes if needed.
        /// Returns true if a valid access token is available after validation.
        /// </summary>
        private async Task<bool> TryValidateLibraryTokenAsync(CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(_accessToken) || string.IsNullOrWhiteSpace(_accountId))
            {
                _logger?.Debug("[EpicAuth] Cannot validate - missing access token or account ID.");
                return false;
            }

            // First, try to validate the current access token with the API
            var accountInfo = await GetAccountInfoAsync(_accountId, _accessToken, ct).ConfigureAwait(false);
            if (accountInfo != null)
            {
                _libraryTokenValidated = true;
                _logger?.Info($"[EpicAuth] Validated credentials from {_libraryPluginName} library plugin for account {accountInfo.DisplayName}.");
                return true;
            }

            _logger?.Debug("[EpicAuth] Access token validation failed, attempting refresh.");

            // Access token is invalid, try to refresh using refresh token
            if (!string.IsNullOrWhiteSpace(_refreshToken) && DateTime.UtcNow < _refreshTokenExpiryUtc)
            {
                try
                {
                    await RenewTokensAsync(_refreshToken, ct).ConfigureAwait(false);

                    // Validate the refreshed token
                    if (HasValidAccessToken() && !string.IsNullOrWhiteSpace(_accountId))
                    {
                        accountInfo = await GetAccountInfoAsync(_accountId, _accessToken, ct).ConfigureAwait(false);
                        if (accountInfo != null)
                        {
                            _libraryTokenValidated = true;
                            _logger?.Info($"[EpicAuth] Refreshed and validated credentials from {_libraryPluginName} library plugin.");
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Debug(ex, "[EpicAuth] Token refresh during validation failed.");
                }
            }

            _logger?.Warn($"[EpicAuth] Failed to validate credentials from {_libraryPluginName} library plugin.");
            return false;
        }

        /// <summary>
        /// Calls Epic's account API to validate an access token.
        /// Returns account info if the token is valid, null otherwise.
        /// </summary>
        private async Task<EpicAccountInfo> GetAccountInfoAsync(string accountId, string accessToken, CancellationToken ct)
        {
            const string accountInfoUrl = "https://account-public-service-prod03.ol.epicgames.com/account/api/public/account/{0}";

            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("Authorization", $"bearer {accessToken}");

                    var url = string.Format(accountInfoUrl, accountId);
                    _logger?.Debug($"[EpicAuth] Validating token with account API: {url}");

                    var response = await client.GetAsync(url, ct).ConfigureAwait(false);
                    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger?.Debug($"[EpicAuth] Account API returned status {response.StatusCode}: {content}");
                        return null;
                    }

                    var accountInfo = JsonConvert.DeserializeObject<EpicAccountInfo>(content);
                    return accountInfo;
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "[EpicAuth] Error calling account info API.");
                return null;
            }
        }

        private async Task RenewTokensAsync(string refreshToken, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                throw new EpicAuthRequiredException("Epic refresh token is missing.");
            }

            using (var httpClient = new HttpClient())
            {
                httpClient.DefaultRequestHeaders.Clear();
                httpClient.DefaultRequestHeaders.Add("Authorization", "basic " + AuthEncodedString);

                using (var content = new StringContent(
                    $"grant_type=refresh_token&refresh_token={refreshToken}&token_type=eg1"))
                {
                    content.Headers.Clear();
                    content.Headers.Add("Content-Type", "application/x-www-form-urlencoded");
                    var response = await httpClient.PostAsync(UrlAccountAuth, content, ct).ConfigureAwait(false);
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new EpicAuthRequiredException($"Epic token refresh failed with HTTP {(int)response.StatusCode}.");
                    }

                    ApplyTokenResponse(body);
                }
            }
        }

        private void ApplyTokenResponse(string responseJson)
        {
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                throw new EpicAuthRequiredException("Epic token endpoint returned an empty response.");
            }

            OauthResponse payload;
            try
            {
                payload = JsonConvert.DeserializeObject<OauthResponse>(responseJson);
            }
            catch (Exception ex)
            {
                throw new EpicAuthRequiredException("Epic token response could not be parsed: " + ex.Message);
            }

            if (payload == null || string.IsNullOrWhiteSpace(payload.access_token) || string.IsNullOrWhiteSpace(payload.account_id))
            {
                throw new EpicAuthRequiredException("Epic token response was missing required fields.");
            }

            _accountId = payload.account_id?.Trim();
            _accessToken = payload.access_token;
            _refreshToken = payload.refresh_token;
            _tokenType = string.IsNullOrWhiteSpace(payload.token_type) ? "bearer" : payload.token_type.Trim();
            _tokenExpiryUtc = NormalizeUtc(payload.expires_at, payload.expires_in);
            _refreshTokenExpiryUtc = NormalizeUtc(payload.refresh_expires_at, payload.refresh_expires_in);

            // Only save to persisted settings in legacy mode
            if (!_useLibraryCredentials)
            {
                SavePersistedTokenState();
            }
        }

        private static DateTime NormalizeUtc(DateTime? explicitUtc, int? expiresInSeconds)
        {
            if (explicitUtc.HasValue && explicitUtc.Value != DateTime.MinValue)
            {
                return explicitUtc.Value.Kind == DateTimeKind.Utc
                    ? explicitUtc.Value
                    : explicitUtc.Value.ToUniversalTime();
            }

            if (expiresInSeconds.HasValue && expiresInSeconds.Value > 0)
            {
                return DateTime.UtcNow.AddSeconds(expiresInSeconds.Value);
            }

            return DateTime.UtcNow.AddHours(1);
        }

        private void SavePersistedTokenState()
        {
            _settings.Persisted.EpicAccountId = _accountId;
            _settings.Persisted.EpicAccessToken = _accessToken;
            _settings.Persisted.EpicRefreshToken = _refreshToken;
            _settings.Persisted.EpicTokenType = _tokenType;
            _settings.Persisted.EpicTokenExpiryUtc = _tokenExpiryUtc == DateTime.MinValue ? (DateTime?)null : _tokenExpiryUtc;
            _settings.Persisted.EpicRefreshTokenExpiryUtc = _refreshTokenExpiryUtc == DateTime.MinValue ? (DateTime?)null : _refreshTokenExpiryUtc;
        }

        #endregion

        #region Helper Methods

        private bool HasValidAccessToken()
        {
            return !string.IsNullOrWhiteSpace(_accessToken) &&
                   DateTime.UtcNow < _tokenExpiryUtc.AddMinutes(-TokenExpiryBufferMinutes);
        }

        #endregion

        #region AES Decryption

        /// <summary>
        /// Decrypts a file encrypted with AES-256-CFB encryption.
        /// Matches the encryption format used by playnite-plugincommon.
        /// </summary>
        private static string DecryptFromFile(string inputFile, Encoding encoding, string password)
        {
            // Read entire file content into memory to avoid file locking
            byte[] encryptedData = File.ReadAllBytes(inputFile);

            var passwordBytes = Encoding.UTF8.GetBytes(password);
            var salt = new byte[32];

            // Extract salt from the beginning of the file
            Array.Copy(encryptedData, 0, salt, 0, salt.Length);

            using (var AES = new RijndaelManaged())
            {
                AES.KeySize = 256;
                AES.BlockSize = 128;
                AES.Padding = PaddingMode.PKCS7;
                AES.Mode = CipherMode.CFB;

                using (var key = new Rfc2898DeriveBytes(passwordBytes, salt, 1000))
                {
                    AES.Key = key.GetBytes(AES.KeySize / 8);
                    AES.IV = key.GetBytes(AES.BlockSize / 8);
                }

                // Create memory stream with encrypted data (excluding salt)
                using (var memoryStream = new MemoryStream(encryptedData, 32, encryptedData.Length - 32))
                using (var cs = new CryptoStream(memoryStream, AES.CreateDecryptor(), CryptoStreamMode.Read))
                using (var reader = new StreamReader(cs, encoding))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        #endregion

        #region Nested Classes

        private sealed class OauthResponse
        {
            [JsonProperty("account_id")]
            public string account_id { get; set; }

            [JsonProperty("token_type")]
            public string token_type { get; set; }

            [JsonProperty("access_token")]
            public string access_token { get; set; }

            [JsonProperty("expires_at")]
            public DateTime? expires_at { get; set; }

            [JsonProperty("expires_in")]
            public int? expires_in { get; set; }

            [JsonProperty("refresh_token")]
            public string refresh_token { get; set; }

            [JsonProperty("refresh_expires_at")]
            public DateTime? refresh_expires_at { get; set; }

            [JsonProperty("refresh_expires")]
            public int? refresh_expires_in { get; set; }
        }

        #endregion
    }
}
