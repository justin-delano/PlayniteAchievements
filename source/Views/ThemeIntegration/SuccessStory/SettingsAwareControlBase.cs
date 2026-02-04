// --SUCCESSSTORY--
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using PlayniteAchievements.Views.ThemeIntegration.Base;

namespace PlayniteAchievements.Views.ThemeIntegration.SuccessStory
{
    /// <summary>
    /// Base class for SuccessStory controls that need to respond to settings changes.
    /// Provides common pattern for debounced settings updates with thread-safe dispatching.
    /// </summary>
    public abstract class SettingsAwareControlBase : SuccessStoryThemeControlBase
    {
        private bool _updatePending;
        private readonly object _updateLock = new object();
        private readonly HashSet<string> _watchedProperties = new HashSet<string>(StringComparer.Ordinal);
        private bool _handlersRegistered;

        /// <summary>
        /// Initializes a new instance of the <see cref="SettingsAwareControlBase"/> class.
        /// </summary>
        protected SettingsAwareControlBase()
        {
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        /// <summary>
        /// Sets up the settings change handler for the specified property names.
        /// </summary>
        /// <param name="propertyNames">The property names to watch for changes.</param>
        protected void SetupSettingsHandler(params string[] propertyNames)
        {
            if (propertyNames == null)
            {
                return;
            }

            foreach (var propertyName in propertyNames)
            {
                if (!string.IsNullOrEmpty(propertyName))
                {
                    _watchedProperties.Add(propertyName);
                }
            }
        }

        /// <summary>
        /// Adds a property name to the list of watched properties.
        /// </summary>
        /// <param name="propertyName">The property name to watch.</param>
        protected void WatchProperty(string propertyName)
        {
            if (!string.IsNullOrEmpty(propertyName))
            {
                _watchedProperties.Add(propertyName);
            }
        }

        /// <summary>
        /// Called when the control is loaded. Registers settings change handlers.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        protected virtual void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (Plugin?.Settings != null && !_handlersRegistered)
            {
                Plugin.Settings.PropertyChanged += Settings_PropertyChanged;
                _handlersRegistered = true;
            }

            // Allow derived classes to perform additional initialization
            OnSettingsInitialized();
        }

        /// <summary>
        /// Called when the control is unloaded. Unregisters settings change handlers.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        protected virtual void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (Plugin?.Settings != null && _handlersRegistered)
            {
                Plugin.Settings.PropertyChanged -= Settings_PropertyChanged;
                _handlersRegistered = false;
            }
        }

        /// <summary>
        /// Called after settings change handlers have been registered.
        /// Derived classes can override this to perform initialization that requires settings to be available.
        /// </summary>
        protected virtual void OnSettingsInitialized()
        {
            // Default implementation does nothing
        }

        /// <summary>
        /// Handles settings property change events with debounced update dispatching.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The property changed event arguments.</param>
        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e == null)
            {
                return;
            }

            if (!_watchedProperties.Contains(e.PropertyName))
            {
                return;
            }

            lock (_updateLock)
            {
                if (_updatePending)
                {
                    return;
                }

                _updatePending = true;
            }

            Dispatcher?.BeginInvoke(new Action(() =>
            {
                lock (_updateLock)
                {
                    if (_updatePending)
                    {
                        _updatePending = false;
                        UpdateFromSettings();
                    }
                }
            }), DispatcherPriority.Background);
        }

        /// <summary>
        /// Updates the control from current settings.
        /// Derived classes must implement this to refresh their UI when watched settings change.
        /// </summary>
        protected abstract void UpdateFromSettings();
    }
}
// --END SUCCESSSTORY--
