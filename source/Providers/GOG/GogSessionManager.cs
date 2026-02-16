using PlayniteAchievements.Providers.GOG.Models;
using PlayniteAchievements.Models;
using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.IO;
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
        /// Gets the current access token, throwing if expired or missing.
        /// </summary>
        public string GetAccessToken()
        {
            if (string.IsNullOrEmpty(_accessToken) || DateTime.UtcNow >= _tokenExpiryUtc)
            {
                throw new AuthRequiredException("GOG authentication required. Please login via a GOG library plugin.");
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

            // Try loading token
            var tokenPath = Path.Combine(storesDataPath, TokenFileName);
            if (!TryLoadToken(tokenPath))
            {
                _logger?.Warn("[GogAuth] Failed to load GOG token from library.");
                _usingLibraryCredentials = false;
                _isSessionAuthenticated = false;
                return false;
            }

            _usingLibraryCredentials = true;
            _isSessionAuthenticated = !string.IsNullOrWhiteSpace(_userId);

            if (_isSessionAuthenticated)
            {
                _settings.Persisted.GogUserId = _userId;
                _logger?.Info($"[GogAuth] Successfully loaded credentials from {_libraryPluginName}. User: {_userId}");
            }

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
                // PlaynitePaths.ExtensionsDataPath is not directly accessible,
                // but we can construct the path from the application data directory
                var extensionsDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
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

                var tokenData = Serialization.FromJson<GogTokenData>(decryptedJson);
                if (tokenData == null || string.IsNullOrWhiteSpace(tokenData.Token))
                {
                    _logger?.Warn("[GogAuth] Failed to parse token data.");
                    return false;
                }

                _accessToken = tokenData.Token;

                // Try to load user info as well
                var storesDataPath = Path.GetDirectoryName(tokenPath);
                var userPath = Path.Combine(storesDataPath, UserFileName);
                TryLoadUserInfo(userPath, password);

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
                    return false;
                }

                var userData = Serialization.FromJson<GogUserData>(decryptedJson);
                if (userData != null && !string.IsNullOrWhiteSpace(userData.UserId))
                {
                    _userId = userData.UserId;
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "[GogAuth] Error loading user info.");
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
        /// Represents the token data stored by GOG library plugins.
        /// </summary>
        private class GogTokenData
        {
            public string Token { get; set; }
        }

        /// <summary>
        /// Represents the user data stored by GOG library plugins.
        /// </summary>
        private class GogUserData
        {
            public string UserId { get; set; }
            public string Username { get; set; }
        }

        #endregion
    }
}
