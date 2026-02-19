using Playnite.SDK;
using PlayniteAchievements.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.PSN
{
    public sealed class PsnSessionManager
    {
        private static readonly TimeSpan InteractiveAuthTimeout = TimeSpan.FromMinutes(3);
        private static readonly TimeSpan CachedTokenLifetime = TimeSpan.FromMinutes(45);

        private readonly IPlayniteAPI _api;
        private readonly ILogger _logger;
        private readonly object _clientLock = new object();
        private readonly SemaphoreSlim _tokenSemaphore = new SemaphoreSlim(1, 1);

        private string _psnExtensionFolderName;
        private string _psnLibraryDllPath;
        private string _psnExtensionFolderPath;

        private object _psnClientInstance;
        private Type _psnClientType;

        private string _accessToken;
        private DateTime _tokenAcquiredUtc = DateTime.MinValue;
        private bool _isSessionAuthenticated;

        public PsnSessionManager(IPlayniteAPI api, ILogger logger)
        {
            if (api == null) throw new ArgumentNullException(nameof(api));

            _api = api;
            _logger = logger;
            TryInitialize();
        }

        public bool IsAuthenticated => _isSessionAuthenticated;

        public async Task PrimeAuthenticationStateAsync(CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var result = await ProbeAuthenticationAsync(ct).ConfigureAwait(false);
                _logger?.Debug($"[PSNAch] Startup auth probe completed with outcome={result?.Outcome}.");
            }
            catch (OperationCanceledException)
            {
                _logger?.Debug("[PSNAch] Startup auth probe cancelled.");
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "[PSNAch] Startup auth probe failed.");
            }
        }

        public async Task<string> GetAccessTokenAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var token = await TryAcquireTokenAsync(ct, forceRefresh: false).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(token))
            {
                return token;
            }

            throw new PsnAuthRequiredException("PlayStation authentication required. Please login.");
        }

        public async Task<PsnAuthResult> ProbeAuthenticationAsync(CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                if (!TryInitialize())
                {
                    SetCachedToken(null);
                    return PsnAuthResult.Create(
                        PsnAuthOutcome.LibraryMissing,
                        "LOCPlayAch_Settings_PsnAuth_LibraryMissing",
                        windowOpened: false);
                }

                var token = await TryAcquireTokenAsync(ct, forceRefresh: false).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(token))
                {
                    return PsnAuthResult.Create(
                        PsnAuthOutcome.AlreadyAuthenticated,
                        "LOCPlayAch_Settings_PsnAuth_AlreadyAuthenticated",
                        windowOpened: false);
                }

                return PsnAuthResult.Create(
                    PsnAuthOutcome.NotAuthenticated,
                    "LOCPlayAch_Settings_PsnAuth_NotAuthenticated",
                    windowOpened: false);
            }
            catch (OperationCanceledException)
            {
                return PsnAuthResult.Create(
                    PsnAuthOutcome.Cancelled,
                    "LOCPlayAch_Settings_PsnAuth_Cancelled",
                    windowOpened: false);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[PSNAch] Probe failed with exception.");
                return PsnAuthResult.Create(
                    PsnAuthOutcome.ProbeFailed,
                    "LOCPlayAch_Settings_PsnAuth_ProbeFailed",
                    windowOpened: false);
            }
        }

        public async Task<PsnAuthResult> AuthenticateInteractiveAsync(
            bool forceInteractive,
            CancellationToken ct,
            IProgress<PsnAuthProgressStep> progress = null)
        {
            var windowOpened = false;

            try
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(PsnAuthProgressStep.CheckingExistingSession);

                if (!TryInitialize())
                {
                    SetCachedToken(null);
                    progress?.Report(PsnAuthProgressStep.Failed);
                    return PsnAuthResult.Create(
                        PsnAuthOutcome.LibraryMissing,
                        "LOCPlayAch_Settings_PsnAuth_LibraryMissing",
                        windowOpened: false);
                }

                if (!forceInteractive)
                {
                    var existingToken = await TryAcquireTokenAsync(ct, forceRefresh: false).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(existingToken))
                    {
                        progress?.Report(PsnAuthProgressStep.Completed);
                        return PsnAuthResult.Create(
                            PsnAuthOutcome.AlreadyAuthenticated,
                            "LOCPlayAch_Settings_PsnAuth_AlreadyAuthenticated",
                            windowOpened: false);
                    }
                }

                progress?.Report(PsnAuthProgressStep.OpeningLoginWindow);
                windowOpened = await TryTriggerAuthenticationAsync(ct).ConfigureAwait(false);

                if (!windowOpened)
                {
                    progress?.Report(PsnAuthProgressStep.Failed);
                    return PsnAuthResult.Create(
                        PsnAuthOutcome.Failed,
                        "LOCPlayAch_Settings_PsnAuth_WindowNotOpened",
                        windowOpened: false);
                }

                progress?.Report(PsnAuthProgressStep.WaitingForUserLogin);
                var deadlineUtc = DateTime.UtcNow.Add(InteractiveAuthTimeout);

                while (DateTime.UtcNow < deadlineUtc)
                {
                    ct.ThrowIfCancellationRequested();

                    var token = await TryAcquireTokenAsync(ct, forceRefresh: true).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        progress?.Report(PsnAuthProgressStep.Completed);
                        return PsnAuthResult.Create(
                            PsnAuthOutcome.Authenticated,
                            "LOCPlayAch_Settings_PsnAuth_Verified",
                            windowOpened: true);
                    }

                    await Task.Delay(1000, ct).ConfigureAwait(false);
                }

                progress?.Report(PsnAuthProgressStep.Failed);
                return PsnAuthResult.Create(
                    PsnAuthOutcome.TimedOut,
                    "LOCPlayAch_Settings_PsnAuth_TimedOut",
                    windowOpened: true);
            }
            catch (OperationCanceledException)
            {
                return PsnAuthResult.Create(
                    PsnAuthOutcome.Cancelled,
                    "LOCPlayAch_Settings_PsnAuth_Cancelled",
                    windowOpened: windowOpened);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[PSNAch] Interactive auth failed.");
                progress?.Report(PsnAuthProgressStep.Failed);
                return PsnAuthResult.Create(
                    PsnAuthOutcome.Failed,
                    "LOCPlayAch_Settings_PsnAuth_Failed",
                    windowOpened: windowOpened);
            }
        }

        public void ClearSession()
        {
            try
            {
                TryClearAuthentication();
                ResetClientState();
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "[PSNAch] Failed to clear PSN auth state.");
            }

            SetCachedToken(null);
        }

        private async Task<string> TryAcquireTokenAsync(CancellationToken ct, bool forceRefresh)
        {
            if (!forceRefresh && HasFreshCachedToken())
            {
                return _accessToken;
            }

            await _tokenSemaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!forceRefresh && HasFreshCachedToken())
                {
                    return _accessToken;
                }

                var token = await GetBridgeAccessTokenAsync(ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(token))
                {
                    SetCachedToken(null);
                    return null;
                }

                SetCachedToken(token);
                return token;
            }
            finally
            {
                _tokenSemaphore.Release();
            }
        }

        private bool HasFreshCachedToken()
        {
            return !string.IsNullOrWhiteSpace(_accessToken) &&
                   (DateTime.UtcNow - _tokenAcquiredUtc) < CachedTokenLifetime;
        }

        private void SetCachedToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                _accessToken = null;
                _tokenAcquiredUtc = DateTime.MinValue;
                _isSessionAuthenticated = false;
                return;
            }

            _accessToken = token;
            _tokenAcquiredUtc = DateTime.UtcNow;
            _isSessionAuthenticated = true;
        }

        private bool TryInitialize()
        {
            using (PerfScope.Start(_logger, "PSN.TryInitialize", thresholdMs: 50))
            {
                if (!string.IsNullOrWhiteSpace(_psnLibraryDllPath) &&
                    !string.IsNullOrWhiteSpace(_psnExtensionFolderName) &&
                    !string.IsNullOrWhiteSpace(_psnExtensionFolderPath))
                {
                    return true;
                }

                try
                {
                    var extensionsRoot = Path.Combine(_api.Paths.ConfigurationPath, "Extensions");
                    if (!Directory.Exists(extensionsRoot))
                    {
                        _logger?.Warn($"[PSNAch] Extensions folder not found: {extensionsRoot}");
                        return false;
                    }

                    var dll = Directory.EnumerateFiles(extensionsRoot, "*.dll", SearchOption.AllDirectories)
                        .FirstOrDefault(p =>
                            string.Equals(Path.GetFileName(p), "PSNLibrary.dll", StringComparison.OrdinalIgnoreCase) ||
                            p.IndexOf("PSNLibrary", StringComparison.OrdinalIgnoreCase) >= 0);

                    if (string.IsNullOrWhiteSpace(dll))
                    {
                        _logger?.Info("[PSNAch] PSNLibrary not found (no PSNLibrary*.dll).");
                        return false;
                    }

                    _psnLibraryDllPath = dll;

                    var dllDir = new DirectoryInfo(Path.GetDirectoryName(dll));
                    var folderName = ResolveExtensionFolderNameFromDllPath(dllDir);
                    if (string.IsNullOrWhiteSpace(folderName))
                    {
                        _logger?.Warn($"[PSNAch] Could not resolve PSNLibrary extension folder from: {dll}");
                        return false;
                    }

                    _psnExtensionFolderName = folderName;
                    _psnExtensionFolderPath = Path.Combine(extensionsRoot, _psnExtensionFolderName);

                    _logger?.Info($"[PSNAch] PSNLibrary detected. ExtensionFolder='{_psnExtensionFolderName}' (dll='{dll}')");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, "[PSNAch] Failed to locate PSNLibrary.");
                    return false;
                }
            }
        }

        private async Task<string> GetBridgeAccessTokenAsync(CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();

            if (!TryInitialize())
            {
                return null;
            }

            if (!EnsureClientInitialized())
            {
                return null;
            }

            try
            {
                await InvokeIfExistsAsync(_psnClientInstance, _psnClientType, "CheckAuthentication", cancel).ConfigureAwait(false);

                var token = TryReadAccessToken(_psnClientInstance, _psnClientType);
                if (string.IsNullOrWhiteSpace(token))
                {
                    _logger?.Warn("[PSNAch] access_token empty. Not authenticated?");
                    return null;
                }

                _logger?.Debug($"[PSNAch] PSN access token available (len={token.Length}).");
                return token;
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[PSNAch] Failed to get access token from PSNLibrary.");
                return null;
            }
        }

        private async Task<bool> TryTriggerAuthenticationAsync(CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();

            if (!TryInitialize())
            {
                return false;
            }

            if (!EnsureClientInitialized())
            {
                return TryOpenSettings();
            }

            var authMethodNames = new[]
            {
                "AuthenticateInteractiveAsync",
                "AuthenticateAsync",
                "Authenticate",
                "LoginInteractiveAsync",
                "LoginAsync",
                "Login",
                "OpenLoginWindow",
                "OpenLoginDialog",
                "StartAuthenticationAsync",
                "StartAuthentication"
            };

            foreach (var methodName in authMethodNames)
            {
                if (await InvokeIfExistsAsync(_psnClientInstance, _psnClientType, methodName, cancel).ConfigureAwait(false))
                {
                    _logger?.Info($"[PSNAch] Invoked PSN auth method '{methodName}'.");
                    return true;
                }
            }

            _logger?.Info("[PSNAch] No interactive auth method found on PSNClient, trying to open PSNLibrary settings.");
            return TryOpenSettings();
        }

        private bool TryClearAuthentication()
        {
            if (!TryInitialize())
            {
                return false;
            }

            if (!EnsureClientInitialized())
            {
                return false;
            }

            var clearMethodNames = new[]
            {
                "ClearAuthentication",
                "ClearSession",
                "Logout",
                "SignOut",
                "ResetAuthentication"
            };

            foreach (var methodName in clearMethodNames)
            {
                if (InvokeIfExistsSync(_psnClientInstance, _psnClientType, methodName))
                {
                    _logger?.Info($"[PSNAch] Invoked PSN clear auth method '{methodName}'.");
                    return true;
                }
            }

            return false;
        }

        private void ResetClientState()
        {
            lock (_clientLock)
            {
                _psnClientInstance = null;
                _psnClientType = null;
            }
        }

        private bool TryOpenSettings()
        {
            try
            {
                if (Guid.TryParse(_psnExtensionFolderName, out var extensionGuid))
                {
                    _api.MainView.OpenPluginSettings(extensionGuid);
                    return true;
                }

                var addons = GetProp(_api, "Addons");
                var plugins = GetProp(addons, "Plugins") as System.Collections.IEnumerable;
                if (plugins == null)
                {
                    return false;
                }

                foreach (var wrapper in plugins)
                {
                    if (wrapper == null)
                    {
                        continue;
                    }

                    var wrapperTypeName = wrapper.GetType().FullName ?? string.Empty;
                    var wrapperName = (GetProp(wrapper, "Name") as string) ?? string.Empty;
                    var instance =
                        GetProp(wrapper, "Plugin") ??
                        GetProp(wrapper, "Instance") ??
                        GetProp(wrapper, "PluginInstance") ??
                        GetProp(wrapper, "Value");

                    var instanceTypeName = instance?.GetType().FullName ?? string.Empty;
                    var instanceName = (GetProp(instance, "Name") as string) ?? string.Empty;

                    if (!LooksLikePsnPlugin(wrapperName, wrapperTypeName, instanceName, instanceTypeName))
                    {
                        continue;
                    }

                    var idObj =
                        GetProp(wrapper, "Id") ??
                        GetProp(wrapper, "AddonId") ??
                        GetProp(wrapper, "PluginId") ??
                        GetProp(instance, "Id");

                    if (!TryExtractGuid(idObj, out var pluginId))
                    {
                        continue;
                    }

                    _api.MainView.OpenPluginSettings(pluginId);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "[PSNAch] Failed to open PSNLibrary settings.");
            }

            return false;
        }

        private bool EnsureClientInitialized()
        {
            if (_psnClientInstance != null && _psnClientType != null)
            {
                return true;
            }

            lock (_clientLock)
            {
                if (_psnClientInstance != null && _psnClientType != null)
                {
                    return true;
                }

                var assemblies = LoadAssembliesFromFolder(_psnExtensionFolderPath);
                _psnClientType = FindPsnClientType(assemblies);
                if (_psnClientType == null)
                {
                    _logger?.Warn("[PSNAch] PSNClient type not found in PSNLibrary extension DLLs.");
                    return false;
                }

                var dataPath = Path.Combine(_api.Paths.ExtensionsDataPath, _psnExtensionFolderName ?? string.Empty);
                _psnClientInstance = CreateClientInstanceBestEffort(_psnClientType, dataPath);
                if (_psnClientInstance == null)
                {
                    _logger?.Warn("[PSNAch] Failed to create PSNClient instance.");
                    return false;
                }

                return true;
            }
        }

        private static string ResolveExtensionFolderNameFromDllPath(DirectoryInfo dllDir)
        {
            var current = dllDir;
            while (current != null && current.Parent != null)
            {
                if (string.Equals(current.Parent.Name, "Extensions", StringComparison.OrdinalIgnoreCase))
                {
                    return current.Name;
                }

                current = current.Parent;
            }

            return null;
        }

        private static List<Assembly> LoadAssembliesFromFolder(string folder)
        {
            var list = new List<Assembly>();
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                return list;
            }

            foreach (var dll in Directory.EnumerateFiles(folder, "*.dll", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    list.Add(Assembly.LoadFrom(dll));
                }
                catch
                {
                    // Ignore individual load failures.
                }
            }

            return list;
        }

        private static Type FindPsnClientType(List<Assembly> assemblies)
        {
            foreach (var asm in assemblies.Where(a => a != null))
            {
                foreach (var type in SafeGetTypes(asm))
                {
                    if (type == null)
                    {
                        continue;
                    }

                    if (type.Name.Equals("PSNClient", StringComparison.OrdinalIgnoreCase) ||
                        (type.FullName?.IndexOf("PSNClient", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0)
                    {
                        return type;
                    }
                }
            }

            return null;
        }

        private static IEnumerable<Type> SafeGetTypes(Assembly asm)
        {
            try
            {
                return asm.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types?.Where(x => x != null) ?? Enumerable.Empty<Type>();
            }
            catch
            {
                return Enumerable.Empty<Type>();
            }
        }

        private object CreateClientInstanceBestEffort(Type clientType, string dataPath)
        {
            var ctors = clientType.GetConstructors(BindingFlags.Instance | BindingFlags.Public);

            object TryInvoke(ConstructorInfo ctor, object[] args)
            {
                try
                {
                    return ctor.Invoke(args);
                }
                catch (Exception ex)
                {
                    _logger?.Debug(ex, "[PSNAch] PSNClient ctor invoke failed.");
                    return null;
                }
            }

            foreach (var ctor in ctors)
            {
                var ps = ctor.GetParameters();

                if (ps.Length == 1 && ps[0].ParameterType.FullName == "PSNLibrary.PSNLibrary")
                {
                    var psnLibraryInstance = FindLoadedPluginInstance(ps[0].ParameterType);
                    if (psnLibraryInstance == null)
                    {
                        return null;
                    }

                    return TryInvoke(ctor, new[] { psnLibraryInstance });
                }

                if (ps.Length == 1 && ps[0].ParameterType == typeof(string))
                {
                    return TryInvoke(ctor, new object[] { dataPath });
                }

                if (ps.Length == 0)
                {
                    return TryInvoke(ctor, null);
                }
            }

            return null;
        }

        private object FindLoadedPluginInstance(Type expectedType)
        {
            try
            {
                var addons = GetProp(_api, "Addons");
                var plugins = GetProp(addons, "Plugins") as System.Collections.IEnumerable;
                if (plugins == null)
                {
                    return null;
                }

                foreach (var wrapper in plugins)
                {
                    if (wrapper == null)
                    {
                        continue;
                    }

                    if (expectedType.IsInstanceOfType(wrapper))
                    {
                        return wrapper;
                    }

                    var instance =
                        GetProp(wrapper, "Plugin") ??
                        GetProp(wrapper, "Instance") ??
                        GetProp(wrapper, "PluginInstance") ??
                        GetProp(wrapper, "Value");

                    if (instance != null && expectedType.IsInstanceOfType(instance))
                    {
                        return instance;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[PSNAch] Failed while searching for loaded PSNLibrary instance.");
            }

            return null;
        }

        private static string TryReadAccessToken(object clientInstance, Type clientType)
        {
            if (clientInstance == null || clientType == null)
            {
                return null;
            }

            try
            {
                var mobileTokenField = clientType.GetField("mobileToken", BindingFlags.Instance | BindingFlags.NonPublic);
                var mobileToken = mobileTokenField?.GetValue(clientInstance);

                if (mobileToken != null)
                {
                    var token = TryReadStringProperty(mobileToken, "access_token") ??
                                TryReadStringProperty(mobileToken, "AccessToken");
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        return token;
                    }
                }

                return TryReadStringProperty(clientInstance, "AccessToken") ??
                       TryReadStringProperty(clientInstance, "access_token") ??
                       TryReadStringProperty(clientInstance, "Token");
            }
            catch
            {
                return null;
            }
        }

        private static string TryReadStringProperty(object obj, string propertyName)
        {
            if (obj == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return null;
            }

            try
            {
                var prop = obj.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return prop?.GetValue(obj) as string;
            }
            catch
            {
                return null;
            }
        }

        private async Task<bool> InvokeIfExistsAsync(object instance, Type type, string methodName, CancellationToken ct)
        {
            try
            {
                var methods = type
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .Where(m => string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var method in methods)
                {
                    var parameters = method.GetParameters();
                    object[] args;
                    if (parameters.Length == 0)
                    {
                        args = null;
                    }
                    else if (parameters.Length == 1 && parameters[0].ParameterType == typeof(CancellationToken))
                    {
                        args = new object[] { ct };
                    }
                    else if (parameters.Length == 1 && parameters[0].ParameterType == typeof(bool))
                    {
                        args = new object[] { true };
                    }
                    else
                    {
                        continue;
                    }

                    var result = method.Invoke(instance, args);
                    if (result is Task taskResult)
                    {
                        await taskResult.ConfigureAwait(false);
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"[PSNAch] Failed invoking method '{methodName}'.");
            }

            return false;
        }

        private bool InvokeIfExistsSync(object instance, Type type, string methodName)
        {
            try
            {
                var method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
                if (method == null)
                {
                    return false;
                }

                method.Invoke(instance, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool LooksLikePsnPlugin(string wrapperName, string wrapperTypeName, string instanceName, string instanceTypeName)
        {
            var combined = string.Join("|", new[]
            {
                wrapperName ?? string.Empty,
                wrapperTypeName ?? string.Empty,
                instanceName ?? string.Empty,
                instanceTypeName ?? string.Empty
            });

            return combined.IndexOf("PSNLibrary", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   combined.IndexOf("PlayStation", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryExtractGuid(object value, out Guid guid)
        {
            if (value is Guid g)
            {
                guid = g;
                return true;
            }

            if (value is string s && Guid.TryParse(s, out var parsed))
            {
                guid = parsed;
                return true;
            }

            guid = Guid.Empty;
            return false;
        }

        private static object GetProp(object obj, string name)
        {
            if (obj == null)
            {
                return null;
            }

            try
            {
                var p = obj.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                return p?.GetValue(obj);
            }
            catch
            {
                return null;
            }
        }
    }
}
