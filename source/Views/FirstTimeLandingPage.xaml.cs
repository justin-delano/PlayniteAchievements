using System;
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
    public partial class FirstTimeLandingPage : IDisposable
    {
        private readonly ILogger _logger;
        private readonly AchievementManager _achievementManager;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly IPlayniteAPI _api;

        /// <summary>
        /// Event raised when setup is complete and the sidebar should be shown.
        /// </summary>
        public event EventHandler SetupComplete;

        public FirstTimeLandingPage(
            IPlayniteAPI api,
            ILogger logger,
            AchievementManager achievementManager,
            PlayniteAchievementsSettings settings)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _achievementManager = achievementManager ?? throw new ArgumentNullException(nameof(achievementManager));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            InitializeComponent();

            DataContext = this;
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
