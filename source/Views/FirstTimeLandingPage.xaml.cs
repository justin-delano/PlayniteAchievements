using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services;
using Playnite.SDK;

namespace PlayniteAchievements.Views
{
    /// <summary>
    /// Landing page shown to users on first plugin open to guide them through initial setup.
    /// Shows different content based on whether they have configured auth and have cached data.
    /// </summary>
    public partial class FirstTimeLandingPage : IDisposable, INotifyPropertyChanged
    {
        private readonly ILogger _logger;
        private readonly AchievementManager _achievementManager;
        private readonly PlayniteAchievementsPlugin _plugin;
        private PlayniteAchievementsSettings _settings;
        private readonly IPlayniteAPI _api;

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        /// <summary>
        /// View model for provider authentication status display.
        /// </summary>
        public class ProviderStatus : ObservableObject
        {
            private bool _isAuthenticated;

            public string Name { get; set; }

            public bool IsAuthenticated
            {
                get => _isAuthenticated;
                set
                {
                    _isAuthenticated = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusIcon));
                    OnPropertyChanged(nameof(StatusSubtitle));
                    OnPropertyChanged(nameof(StatusBadgeText));
                }
            }

            public string StatusIcon => IsAuthenticated ? "\uE73E" : "\uE711"; // Checkmark / Cancel

            /// <summary>
            /// Gets the localized subtitle text based on authentication status.
            /// </summary>
            public string StatusSubtitle => IsAuthenticated
                ? ResourceProvider.GetString("LOCPlayAch_Landing_Status_ReadyToScan")
                : ResourceProvider.GetString("LOCPlayAch_Landing_Status_ConfigureInSettings");

            /// <summary>
            /// Gets the localized badge text based on authentication status.
            /// </summary>
            public string StatusBadgeText => IsAuthenticated
                ? ResourceProvider.GetString("LOCPlayAch_Landing_Status_BadgeReady")
                : ResourceProvider.GetString("LOCPlayAch_Landing_Status_BadgeSetup");
        }

        private readonly ObservableCollection<ProviderStatus> _providers;

        public ObservableCollection<ProviderStatus> Providers => _providers;

        /// <summary>
        /// Gets the Playnite application icon path for use in the UI.
        /// Returns the Playnite executable path which can be used as an Image source.
        /// </summary>
        public string PlayniteIconPath
        {
            get
            {
                try
                {
                    var playnitePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(playnitePath) && System.IO.File.Exists(playnitePath))
                    {
                        return playnitePath;
                    }
                }
                catch
                {
                    // Ignore errors
                }
                return null;
            }
        }

        /// <summary>
        /// Event raised when setup is complete and the sidebar should be shown.
        /// </summary>
        public event EventHandler SetupComplete;

        public FirstTimeLandingPage(
            IPlayniteAPI api,
            ILogger logger,
            AchievementManager achievementManager,
            PlayniteAchievementsSettings settings,
            PlayniteAchievementsPlugin plugin)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _achievementManager = achievementManager ?? throw new ArgumentNullException(nameof(achievementManager));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));

            _providers = new ObservableCollection<ProviderStatus>();

            InitializeComponent();

            DataContext = this;

            RefreshProviderStatuses();
        }

        /// <summary>
        /// Refreshes the authentication status for all providers.
        /// Called when settings are updated to reflect credential changes.
        /// Also triggers a full state refresh to update panel visibility.
        /// </summary>
        public void RefreshProviderStatuses()
        {
            // Reload settings from disk to get the latest persisted values
            ReloadSettings();

            var providers = _achievementManager.GetProviders();
            _providers.Clear();

            foreach (var provider in providers)
            {
                var status = new ProviderStatus
                {
                    Name = provider.ProviderName,
                    IsAuthenticated = provider.IsAuthenticated
                };
                _providers.Add(status);
            }

            // Raise PropertyChanged for all state-dependent properties to refresh UI
            OnPropertyChanged(nameof(HasAnyProviderAuth));
            OnPropertyChanged(nameof(CurrentState));
            OnPropertyChanged(nameof(ShowNoAuthPanel));
            OnPropertyChanged(nameof(ShowNeedsScanPanel));
            OnPropertyChanged(nameof(ShowHasDataPanel));
        }

        /// <summary>
        /// Reloads the settings from disk to ensure we have the latest persisted values.
        /// This is called when settings are saved externally (e.g., via the settings dialog).
        /// </summary>
        private void ReloadSettings()
        {
            try
            {
                var reloaded = _plugin.LoadPluginSettings<PlayniteAchievementsSettings>();
                if (reloaded != null)
                {
                    // Preserve the plugin reference for ISettings methods
                    reloaded._plugin = _plugin;
                    _settings = reloaded;
                    _logger.Info("Landing page settings reloaded from disk.");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to reload settings in landing page.");
            }
        }

        /// <summary>
        /// Gets whether any provider authentication is configured.
        /// Delegates to AchievementManager to check if any provider is authenticated.
        /// </summary>
        public bool HasAnyProviderAuth => _achievementManager.HasAnyAuthenticatedProvider();

        /// <summary>
        /// Gets the settings for checking if setup is complete.
        /// </summary>
        private PlayniteAchievementsSettings CurrentSettings => _settings._plugin?.Settings ?? _settings;

        /// <summary>
        /// Gets whether cached achievement data exists.
        /// </summary>
        public bool HasCachedData
        {
            get
            {
                try
                {
                    var cachedIds = _achievementManager.Cache.GetCachedGameIds();
                    return cachedIds != null && cachedIds.Count > 0;
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, "Failed to check for cached data.");
                    return false;
                }
            }
        }

        /// <summary>
        /// Gets the current landing state based on auth and cache status.
        /// </summary>
        public LandingState CurrentState
        {
            get
            {
                if (!HasAnyProviderAuth)
                {
                    return LandingState.NoAuth;
                }
                else if (!HasCachedData)
                {
                    return LandingState.NeedsScan;
                }
                else
                {
                    return LandingState.HasData;
                }
            }
        }

        /// <summary>
        /// Gets whether to show the No Auth panel.
        /// </summary>
        public bool ShowNoAuthPanel => CurrentState == LandingState.NoAuth;

        /// <summary>
        /// Gets whether to show the Needs Scan panel.
        /// </summary>
        public bool ShowNeedsScanPanel => CurrentState == LandingState.NeedsScan;

        /// <summary>
        /// Gets whether to show the Has Data panel.
        /// </summary>
        public bool ShowHasDataPanel => CurrentState == LandingState.HasData;

        /// <summary>
        /// Command to begin the first scan.
        /// </summary>
        public ICommand BeginScanCommand => new RelayCommand(() =>
        {
            try
            {
                _logger.Info("User clicked Begin Scan from first-time landing page.");

                MarkSetupComplete();

                _ = _achievementManager.StartManagedRebuildAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to begin scan from first-time landing page.");
                _api?.Notifications?.Add(new NotificationMessage(
                    "PlayAch_FirstTimeScanError",
                    $"Failed to start scan: {ex.Message}",
                    NotificationType.Error));
            }
        });

        /// <summary>
        /// Command to continue to the sidebar without scanning.
        /// </summary>
        public ICommand ContinueCommand => new RelayCommand(() =>
        {
            try
            {
                _logger.Info("User clicked Continue from first-time landing page.");

                MarkSetupComplete();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to continue from first-time landing page.");
            }
        });

        /// <summary>
        /// Command to open the plugin settings window.
        /// </summary>
        public ICommand OpenPluginSettingsCommand => new RelayCommand(() =>
        {
            try
            {
                _logger.Info("User clicked Open Plugin Settings from first-time landing page.");

                var pluginId = _settings._plugin?.Id;
                if (pluginId.HasValue)
                {
                    _api.MainView.OpenPluginSettings(pluginId.Value);
                }
                else
                {
                    _logger.Warn("Cannot open plugin settings: plugin ID is not available.");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to open plugin settings from first-time landing page.");
            }
        });

        private void MarkSetupComplete()
        {
            _settings.Persisted.FirstTimeSetupCompleted = true;

            // Save the settings to persist FirstTimeSetupCompleted
            _settings._plugin?.SavePluginSettings(_settings);

            SetupComplete?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            SetupComplete = null;
        }

        /// <summary>
        /// Simple command implementation for landing page actions.
        /// </summary>
        private class RelayCommand : ICommand
        {
            private readonly Action _execute;

            public RelayCommand(Action execute)
            {
                _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            }

#pragma warning disable CS0067 // Event is never raised (CanExecute always returns true)
            public event EventHandler CanExecuteChanged;
#pragma warning restore CS0067

            public bool CanExecute(object parameter) => true;

            public void Execute(object parameter) => _execute();
        }
    }

    /// <summary>
    /// States for the first-time landing page.
    /// </summary>
    public enum LandingState
    {
        /// <summary>
        /// No provider authentication is configured.
        /// Shows settings navigation diagram.
        /// </summary>
        NoAuth,

        /// <summary>
        /// Authentication is configured but no cached data exists.
        /// Shows "Begin Scan" button.
        /// </summary>
        NeedsScan,

        /// <summary>
        /// Authentication is configured and cached data exists.
        /// Shows "Continue" button for existing users.
        /// </summary>
        HasData
    }
}
