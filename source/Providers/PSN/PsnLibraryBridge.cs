using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.PSN
{
    internal sealed class PsnLibraryBridge
    {
        private readonly IPlayniteAPI _api;
        private readonly ILogger _logger;

        // Folder under ...\Extensions\<folder> (GUID OR builtin name like "PlayStationLibrary_Builtin")
        private string _psnExtensionFolderName;

        // Main dll path we detected (PSNLibrary.dll)
        private string _psnLibraryDllPath;

        // Extension folder absolute path
        private string _psnExtensionFolderPath;

        private object _psnClientInstance;
        private Type _psnClientType;

        public PsnLibraryBridge(IPlayniteAPI api, ILogger logger)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _logger = logger;
        }

        public bool IsAvailable => !string.IsNullOrWhiteSpace(_psnLibraryDllPath);
        public string PsnExtensionId => _psnExtensionFolderName;

        public bool TryInitialize()
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

                // Find PSNLibrary.dll anywhere under Extensions
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

                // Resolve extension folder name: the directory directly under ...\Extensions\
                var dllDir = new DirectoryInfo(Path.GetDirectoryName(dll));
                var folderName = ResolveExtensionFolderNameFromDllPath(dllDir);
                if (string.IsNullOrWhiteSpace(folderName))
                {
                    _logger?.Warn($"[PSNAch] Could not resolve PSNLibrary extension folder name from: {dll}");
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

        public async Task<string> GetAccessTokenAsync(CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();

            if (!TryInitialize())
            {
                return null;
            }

            try
            {
                if (_psnClientInstance == null)
                {
                    // Load ALL dlls from the PSN extension folder, not only PSNLibrary.dll
                    var assemblies = LoadAssembliesFromFolder(_psnExtensionFolderPath);

                    _psnClientType = FindPsnClientType(assemblies);
                    if (_psnClientType == null)
                    {
                        _logger?.Warn("[PSNAch] PSNClient type not found in PSNLibrary extension DLLs.");
                        return null;
                    }

                    var dataPath = Path.Combine(_api.Paths.ExtensionsDataPath, _psnExtensionFolderName);
                    _logger?.Info($"[PSNAch] Using PSN dataPath='{dataPath}'");
                    _logger?.Info($"[PSNAch] Using PSNClient type='{_psnClientType.FullName}' (asm='{_psnClientType.Assembly.GetName().Name}')");

                    _psnClientInstance = CreateClientInstanceBestEffort(_psnClientType, dataPath);
                    if (_psnClientInstance == null)
                    {
                        _logger?.Warn("[PSNAch] Failed to create PSNClient instance (no compatible constructor).");
                        return null;
                    }
                }

                // IMPORTANT: await this (previous code was fire-and-forget)
                await InvokeIfExistsAsync(_psnClientInstance, _psnClientType, "CheckAuthentication").ConfigureAwait(false);

                // Builtin PSNLibrary stores token in private field: "mobileToken"
                var mobileTokenField = _psnClientType.GetField("mobileToken", BindingFlags.Instance | BindingFlags.NonPublic);
                var mobileTokenObj = mobileTokenField?.GetValue(_psnClientInstance);

                if (mobileTokenObj == null)
                {
                    _logger?.Warn("[PSNAch] mobileToken field is null. Not authenticated or token not acquired yet.");
                    return null;
                }

                // MobileTokens has public property: "access_token"
                var accessTokenProp = mobileTokenObj.GetType().GetProperty("access_token", BindingFlags.Instance | BindingFlags.Public);
                var accessToken = accessTokenProp?.GetValue(mobileTokenObj) as string;

                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    _logger?.Warn("[PSNAch] access_token empty. Not authenticated?");
                    return null;
                }

                _logger?.Info($"[PSNAch] PSN access token OK (len={accessToken.Length})");
                return accessToken;
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[PSNAch] Failed to get access token from PSNLibrary.");
                return null;
            }
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
                    // ignore individual load failures
                }
            }

            return list;
        }

        private Type FindPsnClientType(List<Assembly> assemblies)
        {
            // Return first type that contains PSNClient
            foreach (var asm in assemblies.Where(a => a != null))
            {
                foreach (var t in SafeGetTypes(asm))
                {
                    if (t == null) continue;

                    if (t.Name.Equals("PSNClient", StringComparison.OrdinalIgnoreCase) ||
                        t.FullName?.IndexOf("PSNClient", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return t;
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

            foreach (var c in ctors)
            {
                var sig = string.Join(", ", c.GetParameters().Select(p => $"{p.ParameterType.FullName} {p.Name}"));
                _logger?.Info($"[PSNAch] PSNClient ctor: ({sig})");
            }

            object TryInvoke(ConstructorInfo ctor, object[] args)
            {
                try { return ctor.Invoke(args); }
                catch (Exception ex)
                {
                    _logger?.Warn(ex, "[PSNAch] PSNClient ctor invoke failed.");
                    return null;
                }
            }

            foreach (var ctor in ctors)
            {
                var ps = ctor.GetParameters();

                // ✅ Real ctor for builtin PSNLibrary: (PSNLibrary.PSNLibrary psnLibrary)
                if (ps.Length == 1 && ps[0].ParameterType.FullName == "PSNLibrary.PSNLibrary")
                {
                    var psnLibraryInstance = FindLoadedPluginInstance(ps[0].ParameterType);
                    if (psnLibraryInstance == null)
                    {
                        _logger?.Warn("[PSNAch] Could not find loaded PSNLibrary.PSNLibrary instance in Playnite.");
                        return null;
                    }

                    return TryInvoke(ctor, new object[] { psnLibraryInstance });
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
                    _logger?.Warn("[PSNAch] PlayniteApi.Addons.Plugins not found/enumerable.");
                    return null;
                }

                foreach (var wrapper in plugins)
                {
                    if (wrapper == null) continue;

                    // Sometimes wrapper IS the plugin instance
                    if (expectedType.IsInstanceOfType(wrapper))
                    {
                        _logger?.Info($"[PSNAch] Found PSNLibrary instance directly on wrapper: {wrapper.GetType().FullName}");
                        return wrapper;
                    }

                    // Sometimes wrapper holds instance in a property
                    var instance =
                        GetProp(wrapper, "Plugin") ??
                        GetProp(wrapper, "Instance") ??
                        GetProp(wrapper, "PluginInstance") ??
                        GetProp(wrapper, "Value");

                    if (instance != null && expectedType.IsInstanceOfType(instance))
                    {
                        _logger?.Info($"[PSNAch] Found PSNLibrary instance: {instance.GetType().FullName}");
                        return instance;
                    }
                }

                _logger?.Warn($"[PSNAch] No loaded plugin instance matching '{expectedType.FullName}' found.");
                return null;
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[PSNAch] Failed while searching for loaded PSNLibrary plugin instance.");
                return null;
            }
        }

        private static object GetProp(object obj, string name)
        {
            if (obj == null) return null;
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

        private static async Task InvokeIfExistsAsync(object instance, Type type, string methodName)
        {
            try
            {
                var m = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
                if (m == null) return;

                var result = m.Invoke(instance, null);
                if (result is Task t)
                {
                    await t.ConfigureAwait(false);
                }
            }
            catch
            {
                // ignore
            }
        }
    }
}
