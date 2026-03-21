using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;

namespace PlayniteAchievements.Providers.Settings
{
    /// <summary>
    /// Extension methods for accessing provider settings through the registry.
    /// </summary>
    public static class ProviderSettingsExtensions
    {
        /// <summary>
        /// Gets provider settings from the registry.
        /// </summary>
        /// <typeparam name="T">The provider settings type.</typeparam>
        /// <param name="settings">The settings instance (used for type resolution).</param>
        /// <returns>The cached provider settings.</returns>
        public static T ProviderSettings<T>(this PlayniteAchievementsSettings settings) where T : ProviderSettingsBase, new()
        {
            return ProviderRegistry.Instance?.Settings<T>() ?? new T();
        }

        /// <summary>
        /// Gets provider settings from the registry.
        /// </summary>
        /// <typeparam name="T">The provider settings type.</typeparam>
        /// <param name="settings">The persisted settings instance (used for type resolution).</param>
        /// <returns>The cached provider settings.</returns>
        public static T ProviderSettings<T>(this PersistedSettings settings) where T : ProviderSettingsBase, new()
        {
            return ProviderRegistry.Instance?.Settings<T>() ?? new T();
        }

        /// <summary>
        /// Saves provider settings through the registry.
        /// </summary>
        /// <typeparam name="T">The provider settings type.</typeparam>
        /// <param name="settings">The settings instance (used for type resolution).</param>
        /// <param name="providerSettings">The provider settings to save.</param>
        public static void SaveProviderSettings<T>(this PlayniteAchievementsSettings settings, T providerSettings) where T : ProviderSettingsBase
        {
            ProviderRegistry.Instance?.Save(providerSettings);
        }

        /// <summary>
        /// Saves provider settings through the registry.
        /// </summary>
        /// <typeparam name="T">The provider settings type.</typeparam>
        /// <param name="settings">The persisted settings instance (used for type resolution).</param>
        /// <param name="providerSettings">The provider settings to save.</param>
        public static void SaveProviderSettings<T>(this PersistedSettings settings, T providerSettings) where T : ProviderSettingsBase
        {
            ProviderRegistry.Instance?.Save(providerSettings);
        }
    }
}
