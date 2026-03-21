using System.ComponentModel;

namespace PlayniteAchievements.Providers.Settings
{
    /// <summary>
    /// Base interface for provider-specific settings.
    /// Providers implement this to define their own settings class.
    /// </summary>
    public interface IProviderSettings : INotifyPropertyChanged
    {
        /// <summary>
        /// Provider key these settings belong to.
        /// </summary>
        string ProviderKey { get; }

        /// <summary>
        /// Whether the provider is enabled.
        /// </summary>
        bool IsEnabled { get; set; }

        /// <summary>
        /// Creates a deep clone of the settings.
        /// </summary>
        IProviderSettings Clone();

        /// <summary>
        /// Copies values from another settings instance.
        /// </summary>
        /// <param name="source">Source settings to copy from.</param>
        void CopyFrom(IProviderSettings source);
    }
}
