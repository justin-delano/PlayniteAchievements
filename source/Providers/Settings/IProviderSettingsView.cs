namespace PlayniteAchievements.Providers.Settings
{
    /// <summary>
    /// Interface for provider settings UI components.
    /// </summary>
    public interface IProviderSettingsView
    {
        /// <summary>
        /// Gets the provider key this view manages.
        /// </summary>
        string ProviderKey { get; }

        /// <summary>
        /// Gets the tab header text (localized).
        /// </summary>
        string TabHeader { get; }

        /// <summary>
        /// Gets the icon resource key for the tab.
        /// </summary>
        string IconKey { get; }

        /// <summary>
        /// Initializes the view with provider settings.
        /// </summary>
        /// <param name="settings">The settings to bind to the view.</param>
        void Initialize(IProviderSettings settings);
    }
}
