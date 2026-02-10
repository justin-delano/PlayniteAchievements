using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace PlayniteAchievements.Views.ThemeIntegration.Base
{
    /// <summary>
    /// Base class for controls that need to respond to settings changes.
    /// Provides common settings subscription and update debouncing logic.
    /// </summary>
    public abstract class SettingsAwareControlBase : AchievementThemeControlBase
    {
        private bool _updatePending;

        /// <summary>
        /// Initializes a new instance of the <see cref="SettingsAwareControlBase"/> class.
        /// </summary>
        protected SettingsAwareControlBase()
        {
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        /// <summary>
        /// Sets up the settings property changed handler for derived classes.
        /// Derived classes should call this in their constructor if they need to handle settings changes.
        /// </summary>
        protected void SetupSettingsHandler()
        {
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        /// <summary>
        /// Called when the control is loaded. Subscribes to settings property changes.
        /// </summary>
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (Plugin?.Settings != null)
            {
                Plugin.Settings.PropertyChanged -= Settings_PropertyChanged;
                Plugin.Settings.PropertyChanged += Settings_PropertyChanged;
            }

            UpdateFromSettings();
        }

        /// <summary>
        /// Called when the control is unloaded. Unsubscribes from settings property changes.
        /// </summary>
        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (Plugin?.Settings != null)
            {
                Plugin.Settings.PropertyChanged -= Settings_PropertyChanged;
            }
        }

        /// <summary>
        /// Handles settings property changed events with debouncing.
        /// Derived classes can override to provide custom handling.
        /// </summary>
        protected virtual void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e == null)
            {
                return;
            }

            if (ShouldHandlePropertyChange(e))
            {
                RequestUpdate();
            }
        }

        /// <summary>
        /// Determines whether a property change should trigger an update.
        /// Derived classes should override to specify which properties they care about.
        /// Default is false (no properties trigger updates).
        /// </summary>
        protected virtual bool ShouldHandlePropertyChange(PropertyChangedEventArgs e)
        {
            return false;
        }

        /// <summary>
        /// Requests an update from settings with debouncing to prevent excessive updates.
        /// </summary>
        protected void RequestUpdate()
        {
            if (_updatePending)
            {
                return;
            }

            _updatePending = true;
            Dispatcher?.BeginInvoke(new Action(() =>
            {
                _updatePending = false;
                UpdateFromSettings();
            }), DispatcherPriority.Background);
        }

        /// <summary>
        /// Updates the control from the current settings.
        /// Derived classes must implement this to provide their specific update logic.
        /// </summary>
        protected abstract void UpdateFromSettings();
    }
}
