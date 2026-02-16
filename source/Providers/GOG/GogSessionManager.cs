using PlayniteAchievements.Providers.GOG.Models;
using PlayniteAchievements.Models;
using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.GOG
{
    /// <summary>
    /// Manages GOG authentication by reading credentials from installed GOG library plugins.
    /// Supports both official GOG Library and GOG OSS Library plugins.
    /// Users must install and authenticate via a GOG library plugin to use GOG features.
    /// </summary>
    public sealed class GogSessionManager : IGogTokenProvider
    {
        #region Constants

        // Official GOG Library Plugin ID
        private const string OfficialGogLibraryPluginId = "AEBE8B7C-6DC3-4A66-AF31-E7375C6B5E9E";

        // GOG OSS Library Plugin ID
        private const string GogOssLibraryPluginId = "03689811-3F33-4DFB-A121-2EE168FB9A5C";

        // Credential file names in StoresData
        private const string TokenFileName = "GOG_Token.dat";
        private const string CookiesFileName = "GOG_Cookies.dat";
        private const string UserFileName = "GOG_User.dat";

        #endregion

        #region Fields

        private readonly IPlayniteAPI _api;
        private readonly ILogger _logger;
        private readonly PlayniteAchievementsSettings _settings;

        private string _accessToken;
        private string _userId;
        private DateTime _tokenExpiryUtc = DateTime.MinValue;
        private bool _isSessionAuthenticated;
        private bool _usingLibraryCredentials;
        private string _libraryPluginName;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the current access token, throwing if missing.
        /// Note: Expiry checking is disabled when using library credentials since the library handles refresh.
        /// </summary>
        public string GetAccessToken()
        {
            if (string.IsNullOrEmpty(_accessToken))
            {
                throw new AuthRequiredException("GOG authentication required. Please login via a GOG library plugin.");
            }

            // When using library credentials, skip expiry check - the library plugin handles token refresh
            // Only check expiry if we have an actual expiry time set (not DateTime.MinValue)
            if (_tokenExpiryUtc != DateTime.MinValue && DateTime.UtcNow >= _tokenExpiryUtc)
            {
                throw new AuthRequiredException("GOG authentication token expired. Please refresh from library.");
            }

            return _accessToken;
        }

        /// <summary>
        /// Gets the current user ID.
        /// </summary>
        public string GetUserId() => _userId;

        /// <summary>
        /// Checks if currently authenticated with valid credentials.
        /// </summary>
        public bool IsAuthenticated => _isSessionAuthenticated;

        /// <summary>
        /// Gets whether credentials were loaded from a GOG library plugin.
        /// </summary>
        public bool UsingLibraryCredentials => _usingLibraryCredentials;

        /// <summary>
        /// Gets the name of the detected library plugin, or null if none detected.
        /// </summary>
        public string LibraryPluginName => _libraryPluginName;

        #endregion

        #region Constructor

        public GogSessionManager(IPlayniteAPI api, ILogger logger, PlayniteAchievementsSettings settings)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _logger = logger;
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            var persistedUserId = _settings?.Persisted?.GogUserId;
            if (!string.IsNullOrWhiteSpace(persistedUserId))
            {
                _userId = persistedUserId.Trim();
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Checks if a GOG library plugin is installed.
        /// </summary>
        public bool IsGogLibraryInstalled()
        {
            return !string.IsNullOrEmpty(GetInstalledLibraryPluginName());
        }

        /// <summary>
        /// Gets the name of the installed GOG library plugin, or null if none.
        /// Official GOG Library takes precedence over GOG OSS.
        /// </summary>
        public string GetInstalledLibraryPluginName()
        {
            try
            {
                var plugins = _api?.Addons?.Plugins;
                if (plugins == null)
                {
                    _logger?.Debug("[GogAuth] Could not access plugins collection.");
                    return null;
                }

                var officialId = Guid.Parse(OfficialGogLibraryPluginId);
                var ossId = Guid.Parse(GogOssLibraryPluginId);

                // Check for official GOG Library first
                foreach (var plugin in plugins)
                {
                    if (plugin == null) continue;
                    if (plugin.Id == officialId)
                    {
                        _logger?.Debug("[GogAuth] Found official GOG Library plugin.");
                        return "GOG Library";
                    }
                }

                // Check for GOG OSS Library
                foreach (var plugin in plugins)
                {
                    if (plugin == null) continue;
                    if (plugin.Id == ossId)
                    {
                        _logger?.Debug("[GogAuth] Found GOG OSS Library plugin.");
                        return "GOG OSS Library";
                    }
                }

                _logger?.Debug("[GogAuth] No GOG library plugin found.");
                return null;
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[GogAuth] Error checking for GOG library plugin.");
                return null;
            }
        }

        /// <summary>
        /// Attempts to load credentials from the GOG library plugin storage.
        /// </summary>
        public bool TryLoadLibraryCredentials()
        {
            _libraryPluginName = GetInstalledLibraryPluginName();

            if (string.IsNullOrEmpty(_libraryPluginName))
            {
                _logger?.Info("[GogAuth] No GOG library plugin installed.");
                _usingLibraryCredentials = false;
                _isSessionAuthenticated = false;
                return false;
            }

            var storesDataPath = GetStoresDataPath();
            if (string.IsNullOrEmpty(storesDataPath) || !Directory.Exists(storesDataPath))
            {
                _logger?.Warn($"[GogAuth] StoresData directory not found: {storesDataPath}");
                _usingLibraryCredentials = false;
                _isSessionAuthenticated = false;
                return false;
            }

            _logger?.Info($"[GogAuth] Loading credentials from {storesDataPath}");

            var password = GetEncryptionPassword();
            if (string.IsNullOrEmpty(password))
            {
                _logger?.Warn("[GogAuth] Could not get encryption password.");
                _usingLibraryCredentials = false;
                _isSessionAuthenticated = false;
                return false;
            }

            // Load cookies from GOG_Cookies.dat
            var cookiesPath = Path.Combine(storesDataPath, CookiesFileName);
            var cookies = TryLoadCookies(cookiesPath, password);
            if (cookies == null || cookies.Count == 0)
            {
                _logger?.Warn("[GogAuth] Failed to load GOG cookies from library.");
                _usingLibraryCredentials = false;
                _isSessionAuthenticated = false;
                return false;
            }

            _logger?.Info($"[GogAuth] Loaded {cookies.Count} cookies from library.");

            // Use cookies to get real access token from account API
            var accountInfo = GetAccountInfoAsync(cookies).GetAwaiter().GetResult();
            if (accountInfo == null || !accountInfo.IsLoggedIn)
            {
                _logger?.Warn("[GogAuth] Account info API returned not logged in.");
                _usingLibraryCredentials = false;
                _isSessionAuthenticated = false;
                return false;
            }

            if (string.IsNullOrWhiteSpace(accountInfo.AccessToken))
            {
                _logger?.Warn("[GogAuth] Account info returned but access token is missing.");
                _usingLibraryCredentials = false;
                _isSessionAuthenticated = false;
                return false;
            }

            // Store the real access token from the API
            _accessToken = accountInfo.AccessToken;
            _userId = accountInfo.UserId;

            // Calculate token expiry
            if (accountInfo.AccessTokenExpires > 0)
            {
                _tokenExpiryUtc = DateTime.UtcNow.AddSeconds(accountInfo.AccessTokenExpires);
            }

            _usingLibraryCredentials = true;
            _isSessionAuthenticated = true;

            if (!string.IsNullOrWhiteSpace(_userId))
            {
                _settings.Persisted.GogUserId = _userId;
            }

            _logger?.Info($"[GogAuth] Successfully authenticated via {_libraryPluginName}. UserId: {_userId}");

            return _isSessionAuthenticated;
        }

        /// <summary>
        /// Probes authentication state by loading library credentials.
        /// </summary>
        public Task<GogAuthResult> ProbeAuthenticationAsync(CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                if (TryLoadLibraryCredentials())
                {
                    return Task.FromResult(GogAuthResult.Create(
                        GogAuthOutcome.AlreadyAuthenticated,
                        "LOCPlayAch_Settings_Gog_LibraryDetected",
                        _userId,
                        _tokenExpiryUtc,
                        windowOpened: false));
                }

                var libraryName = GetInstalledLibraryPluginName();
                if (!string.IsNullOrEmpty(libraryName))
                {
                    // Library installed but not logged in
                    return Task.FromResult(GogAuthResult.Create(
                        GogAuthOutcome.NotAuthenticated,
                        "LOCPlayAch_Settings_Gog_LibraryNotLoggedIn",
                        windowOpened: false));
                }

                // No library installed
                return Task.FromResult(GogAuthResult.Create(
                    GogAuthOutcome.NotAuthenticated,
                    "LOCPlayAch_Settings_Gog_NoLibrary",
                    windowOpened: false));
            }
            catch (OperationCanceledException)
            {
                return Task.FromResult(GogAuthResult.Create(
                    GogAuthOutcome.Cancelled,
                    "LOCPlayAch_Settings_GogAuth_Cancelled",
                    windowOpened: false));
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[GogAuth] Probe failed with exception.");
                return Task.FromResult(GogAuthResult.Create(
                    GogAuthOutcome.ProbeFailed,
                    "LOCPlayAch_Settings_GogAuth_ProbeFailed",
                    windowOpened: false));
            }
        }

        /// <summary>
        /// Runs a best-effort background probe to hydrate authentication state.
        /// </summary>
        public async Task PrimeAuthenticationStateAsync(CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var result = await ProbeAuthenticationAsync(ct).ConfigureAwait(false);
                _logger?.Debug($"[GogAuth] Startup auth probe completed with outcome={result?.Outcome}.");
            }
            catch (OperationCanceledException)
            {
                _logger?.Debug("[GogAuth] Startup auth probe cancelled.");
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "[GogAuth] Startup auth probe failed.");
            }
        }

        /// <summary>
        /// Refreshes credentials from the library plugin storage.
        /// </summary>
        public void RefreshFromLibrary()
        {
            _logger?.Info("[GogAuth] Refreshing credentials from library.");
            TryLoadLibraryCredentials();
        }

        /// <summary>
        /// Clears the in-memory session state (does not affect library plugin credentials).
        /// </summary>
        public void ClearSession()
        {
            _logger?.Info("[GogAuth] Clearing session.");
            _accessToken = null;
            _userId = null;
            _tokenExpiryUtc = DateTime.MinValue;
            _isSessionAuthenticated = false;
            _usingLibraryCredentials = false;
            _settings.Persisted.GogUserId = null;
        }

        #endregion

        #region Private Methods

        private string GetStoresDataPath()
        {
            try
            {
                // Playnite stores data in %APPDATA%\Playnite\ExtensionsData (not LocalApplicationData)
                var extensionsDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Playnite",
                    "ExtensionsData");

                return Path.Combine(extensionsDataPath, "StoresData");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[GogAuth] Failed to get StoresData path.");
                return null;
            }
        }

        private bool TryLoadToken(string tokenPath)
        {
            if (!File.Exists(tokenPath))
            {
                _logger?.Debug($"[GogAuth] Token file not found: {tokenPath}");
                return false;
            }

            try
            {
                var password = GetEncryptionPassword();
                if (string.IsNullOrEmpty(password))
                {
                    _logger?.Warn("[GogAuth] Could not get encryption password.");
                    return false;
                }

                var decryptedJson = DecryptFromFile(tokenPath, Encoding.UTF8, password);
                if (string.IsNullOrWhiteSpace(decryptedJson))
                {
                    _logger?.Warn("[GogAuth] Decrypted token is empty.");
                    return false;
                }

                _logger?.Debug($"[GogAuth] Token file content (first 100 chars): {decryptedJson.Substring(0, Math.Min(100, decryptedJson.Length))}");

                var tokenData = Serialization.FromJson<GogTokenData>(decryptedJson);
                if (tokenData == null)
                {
                    _logger?.Warn("[GogAuth] Failed to parse token data.");
                    return false;
                }

                // Token field contains the access token
                _accessToken = tokenData.Token;

                // AccountId field contains the user ID (if present in token)
                if (!string.IsNullOrWhiteSpace(tokenData.AccountId))
                {
                    _userId = tokenData.AccountId;
                }

                if (string.IsNullOrWhiteSpace(_accessToken))
                {
                    _logger?.Warn("[GogAuth] Token value is empty.");
                    return false;
                }

                // If we don't have user ID from token, try loading from user file
                if (string.IsNullOrWhiteSpace(_userId))
                {
                    var storesDataPath = Path.GetDirectoryName(tokenPath);
                    var userPath = Path.Combine(storesDataPath, UserFileName);
                    TryLoadUserInfo(userPath, password);
                }

                return true;
            }
            catch (CryptographicException ex)
            {
                _logger?.Error(ex, "[GogAuth] Cryptographic error decrypting token.");
                return false;
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[GogAuth] Error loading token.");
                return false;
            }
        }

        private bool TryLoadUserInfo(string userPath, string password)
        {
            if (!File.Exists(userPath))
            {
                _logger?.Debug($"[GogAuth] User file not found: {userPath}");
                return false;
            }

            try
            {
                var decryptedJson = DecryptFromFile(userPath, Encoding.UTF8, password);
                if (string.IsNullOrWhiteSpace(decryptedJson))
                {
                    _logger?.Debug("[GogAuth] User file content is empty after decryption.");
                    return false;
                }

                _logger?.Debug($"[GogAuth] User file content (first 200 chars): {decryptedJson.Substring(0, Math.Min(200, decryptedJson.Length))}");

                // The User file contains AccountInfos structure from CommonPluginsStores
                var userData = Serialization.FromJson<GogAccountInfos>(decryptedJson);
                if (userData != null && !string.IsNullOrWhiteSpace(userData.UserId))
                {
                    _userId = userData.UserId;
                    _logger?.Info($"[GogAuth] Loaded user ID from User file: {_userId}");
                    return true;
                }

                _logger?.Warn("[GogAuth] User data parsed but UserId is missing.");
                return false;
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[GogAuth] Error loading user info.");
                return false;
            }
        }

        private static string GetEncryptionPassword()
        {
            try
            {
                return WindowsIdentity.GetCurrent()?.User?.Value;
            }
            catch
            {
                return null;
            }
        }

        private List<GogCookie> TryLoadCookies(string cookiesPath, string password)
        {
            if (!File.Exists(cookiesPath))
            {
                _logger?.Debug($"[GogAuth] Cookies file not found: {cookiesPath}");
                return null;
            }

            try
            {
                var decryptedJson = DecryptFromFile(cookiesPath, Encoding.UTF8, password);
                if (string.IsNullOrWhiteSpace(decryptedJson))
                {
                    _logger?.Warn("[GogAuth] Decrypted cookies are empty.");
                    return null;
                }

                var cookies = Serialization.FromJson<List<GogCookie>>(decryptedJson);
                if (cookies == null || cookies.Count == 0)
                {
                    _logger?.Warn("[GogAuth] Failed to parse cookies JSON or list is empty.");
                    return null;
                }

                // Filter to GOG domain cookies and remove expired ones
                var validCookies = cookies
                    .Where(c => c.Domain != null && c.Domain.IndexOf("gog.com", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Where(c => c.Expires == null || c.Expires > DateTime.Now)
                    .ToList();

                _logger?.Debug($"[GogAuth] Found {validCookies.Count} valid GOG cookies (filtered from {cookies.Count} total).");
                return validCookies;
            }
            catch (CryptographicException ex)
            {
                _logger?.Error(ex, "[GogAuth] Cryptographic error decrypting cookies.");
                return null;
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[GogAuth] Error loading cookies.");
                return null;
            }
        }

        private async Task<GogAccountBasicResponse> GetAccountInfoAsync(List<GogCookie> cookies)
        {
            const string accountInfoUrl = "https://menu.gog.com/v1/account/basic";

            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                    // Build cookie header
                    var cookieHeader = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
                    client.DefaultRequestHeaders.Add("Cookie", cookieHeader);

                    _logger?.Debug($"[GogAuth] Calling account info API: {accountInfoUrl}");

                    var response = await client.GetAsync(accountInfoUrl).ConfigureAwait(false);
                    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger?.Warn($"[GogAuth] Account info API returned status {response.StatusCode}: {content}");
                        return null;
                    }

                    _logger?.Debug($"[GogAuth] Account info response (first 200 chars): {content.Substring(0, Math.Min(200, content.Length))}");

                    var accountInfo = Serialization.FromJson<GogAccountBasicResponse>(content);
                    return accountInfo;
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[GogAuth] Error calling account info API.");
                return null;
            }
        }

        #endregion

        #region Encryption (ported from playnite-plugincommon)

        /// <summary>
        /// Decrypts content from a file using AES encryption.
        /// Ported from CommonPlayniteShared.Common.Encryption.
        /// </summary>
        private static string DecryptFromFile(string inputFile, Encoding encoding, string password)
        {
            byte[] encryptedData = File.ReadAllBytes(inputFile);
            var passwordBytes = Encoding.UTF8.GetBytes(password);
            var salt = new byte[32];

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

                using (var memoryStream = new MemoryStream(encryptedData, 32, encryptedData.Length - 32))
                using (var cs = new CryptoStream(memoryStream, AES.CreateDecryptor(), CryptoStreamMode.Read))
                using (var reader = new StreamReader(cs, encoding))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        #endregion

        #region Nested Types

        /// <summary>
        /// Represents a cookie stored by GOG library plugins.
        /// Matches HttpCookie structure from Playnite SDK.
        /// </summary>
        private class GogCookie
        {
            public string Name { get; set; }
            public string Value { get; set; }
            public string Domain { get; set; }
            public string Path { get; set; }
            public DateTime? Expires { get; set; }
            public bool Secure { get; set; }
            public bool HttpOnly { get; set; }
        }

        /// <summary>
        /// Represents the account info response from menu.gog.com.
        /// Contains the real access token needed for gameplay API.
        /// </summary>
        private class GogAccountBasicResponse
        {
            [SerializationPropertyName("isLoggedIn")]
            public bool IsLoggedIn { get; set; }

            [SerializationPropertyName("userId")]
            public string UserId { get; set; }

            [SerializationPropertyName("accessToken")]
            public string AccessToken { get; set; }

            [SerializationPropertyName("accessTokenExpires")]
            public int AccessTokenExpires { get; set; }

            [SerializationPropertyName("clientId")]
            public string ClientId { get; set; }

            [SerializationPropertyName("username")]
            public string Username { get; set; }
        }

        /// <summary>
        /// Represents the token data stored by GOG library plugins.
        /// Matches StoreToken structure from playnite-plugincommon.
        /// </summary>
        private class GogTokenData
        {
            public string AccountId { get; set; }
            public string Type { get; set; }
            public string Token { get; set; }
            public DateTime? ExpireAt { get; set; }
            public string RefreshToken { get; set; }
            public DateTime? RefreshExpireAt { get; set; }
        }

        /// <summary>
        /// Represents the user account info stored by GOG library plugins.
        /// Matches AccountInfos structure from CommonPluginsStores.
        /// </summary>
        private class GogAccountInfos
        {
            public string UserId { get; set; }
            public string ClientId { get; set; }
            public string Pseudo { get; set; }
            public string Avatar { get; set; }
            public string Link { get; set; }
            public bool IsCurrent { get; set; }
        }

        #endregion
    }
}
