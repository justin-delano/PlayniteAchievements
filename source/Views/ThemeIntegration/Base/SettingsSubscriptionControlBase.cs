using System;
using System.ComponentModel;
using System.Windows;

namespace PlayniteAchievements.Views.ThemeIntegration.Base
{
    /// <summary>
    /// Base class for controls that need simple settings subscription without debouncing.
    /// Provides basic Loaded/Unloaded handling for settings property changes.
    /// </summary>
    public abstract class SettingsSubscriptionControlBase : SuccessStoryThemeControlBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SettingsSubscriptionControlBase"/> class.
        /// </summary>
        protected SettingsSubscriptionControlBase()
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

            OnSettingsLoaded();
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

            OnSettingsUnloaded();
        }

        /// <summary>
        /// Handles settings property changed events.
        /// Derived classes can override to provide custom handling.
        /// </summary>
        protected virtual void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
        }

        /// <summary>
        /// Called when settings are loaded (control is loaded).
        /// Derived classes can override to perform initial refresh.
        /// </summary>
        protected virtual void OnSettingsLoaded()
        {
        }

        /// <summary>
        /// Called when settings are unloaded (control is unloaded).
        /// Derived classes can override to perform cleanup.
        /// </summary>
        protected virtual void OnSettingsUnloaded()
        {
        }
    }
}
