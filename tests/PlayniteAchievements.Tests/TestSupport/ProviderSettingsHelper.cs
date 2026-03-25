using Newtonsoft.Json.Linq;
using PlayniteAchievements.Models.Settings;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace PlayniteAchievements.Providers.Settings
{
    public static class ProviderSettingsHelper
    {
        [ThreadStatic]
        private static PersistedSettings _currentSettings;
        private static readonly ConditionalWeakTable<PersistedSettings, Dictionary<string, ProviderSettingsBase>> SettingsCache =
            new ConditionalWeakTable<PersistedSettings, Dictionary<string, ProviderSettingsBase>>();

        public static void Bind(PersistedSettings settings)
        {
            _currentSettings = settings;
            if (_currentSettings?.ProviderSettings == null)
            {
                _currentSettings.ProviderSettings = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public static T Load<T>(PersistedSettings settings, string providerKey)
            where T : ProviderSettingsBase, new()
        {
            var resolvedSettings = settings ?? _currentSettings;
            var resolvedKey = string.IsNullOrWhiteSpace(providerKey) ? new T().ProviderKey : providerKey;
            if (resolvedSettings == null)
            {
                return new T();
            }

            var cache = SettingsCache.GetOrCreateValue(resolvedSettings);
            if (cache.TryGetValue(resolvedKey, out var cached) && cached is T cachedTyped)
            {
                return cachedTyped;
            }

            var instance = new T();

            if (resolvedSettings.ProviderSettings != null &&
                resolvedSettings.ProviderSettings.TryGetValue(resolvedKey, out var json) &&
                json != null)
            {
                instance.DeserializeFromJson(json.ToString());
            }

            cache[resolvedKey] = instance;
            return instance;
        }

        public static T LoadCurrent<T>()
            where T : ProviderSettingsBase, new()
        {
            return Load<T>(_currentSettings, new T().ProviderKey);
        }

        public static void SaveCurrent(ProviderSettingsBase settings)
        {
            Save(_currentSettings, settings);
        }

        public static void Save(PersistedSettings persistedSettings, ProviderSettingsBase settings)
        {
            var resolvedSettings = persistedSettings ?? _currentSettings;
            if (resolvedSettings == null || settings == null)
            {
                return;
            }

            if (resolvedSettings.ProviderSettings == null)
            {
                resolvedSettings.ProviderSettings = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            }

            var cache = SettingsCache.GetOrCreateValue(resolvedSettings);
            cache[settings.ProviderKey] = settings;
            resolvedSettings.ProviderSettings[settings.ProviderKey] =
                JObject.Parse(settings.SerializeToJson() ?? "{}");
        }
    }
}
