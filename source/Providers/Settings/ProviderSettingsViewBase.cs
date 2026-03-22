using System.Windows.Controls;

namespace PlayniteAchievements.Providers.Settings
{
    /// <summary>
    /// Base UserControl for provider settings views.
    /// </summary>
    public abstract class ProviderSettingsViewBase : UserControl
    {
        private IProviderSettings _settings;

        /// <summary>
        /// Gets the settings bound to this view.
        /// </summary>
        public IProviderSettings Settings => _settings;

        /// <inheritdoc />
        public virtual void Initialize(IProviderSettings settings)
        {
            _settings = settings;
            DataContext = this;
        }
    }
}
