using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PlayniteAchievements.Providers.Settings
{
    /// <summary>
    /// Base class for provider-specific settings with common functionality.
    /// </summary>
    public abstract class ProviderSettingsBase : IProviderSettings
    {
        private bool _isEnabled = true;

        /// <inheritdoc />
        public abstract string ProviderKey { get; }

        /// <inheritdoc />
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetValue(ref _isEnabled, value);
        }

        /// <inheritdoc />
        public abstract IProviderSettings Clone();

        /// <inheritdoc />
        public abstract void CopyFrom(IProviderSettings source);

        /// <summary>
        /// Sets the property value and raises PropertyChanged if the value changed.
        /// </summary>
        protected bool SetValue<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        /// <summary>
        /// Raises the PropertyChanged event.
        /// </summary>
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <inheritdoc />
        public event PropertyChangedEventHandler PropertyChanged;
    }
}
