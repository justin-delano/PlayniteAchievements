using System.Windows.Controls;

namespace PlayniteAchievements.Providers.Settings
{
    /// <summary>
    /// Base UserControl for provider settings views.
    /// </summary>
    public abstract class ProviderSettingsViewBase : UserControl, IProviderSettingsView
    {
        private IProviderSettings _settings;

        /// <inheritdoc />
        public abstract string ProviderKey { get; }

        /// <inheritdoc />
        public abstract string TabHeader { get; }

        /// <inheritdoc />
        public abstract string IconKey { get; }

        /// <summary>
        /// Gets the settings bound to this view.
        /// </summary>
        protected IProviderSettings Settings => _settings;

        /// <inheritdoc />
        public virtual void Initialize(IProviderSettings settings)
        {
            _settings = settings;
            DataContext = settings;
        }
    }
}
