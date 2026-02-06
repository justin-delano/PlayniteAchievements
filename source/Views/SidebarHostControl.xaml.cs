using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services;
using Playnite.SDK;

namespace PlayniteAchievements.Views
{
    /// <summary>
    /// Lightweight host that returns immediately from the sidebar Opened callback and defers
    /// creation of the heavy SidebarControl until after Playnite has a chance to paint.
    /// Also handles showing the first-time landing page when appropriate.
    /// </summary>
    public partial class SidebarHostControl : UserControl
    {
        private readonly Func<UserControl> _createView;
        private readonly ILogger _logger;
        private readonly bool _enableDiagnostics;
        private readonly IPlayniteAPI _api;
        private readonly AchievementManager _achievementManager;
        private readonly PlayniteAchievementsSettings _settings;

        private SidebarControl _sidebar;
        private FirstTimeLandingPage _landingPage;
        private bool _createScheduled;

        public SidebarHostControl(
            Func<UserControl> createView,
            ILogger logger,
            bool enableDiagnostics,
            IPlayniteAPI api,
            AchievementManager achievementManager,
            PlayniteAchievementsSettings settings)
        {
            _createView = createView ?? throw new ArgumentNullException(nameof(createView));
            _logger = logger;
            _enableDiagnostics = enableDiagnostics;
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _achievementManager = achievementManager ?? throw new ArgumentNullException(nameof(achievementManager));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            InitializeComponent();

            Loaded += SidebarHostControl_Loaded;
            Unloaded += SidebarHostControl_Unloaded;
        }

        private void SidebarHostControl_Loaded(object sender, RoutedEventArgs e)
        {
            EnsureContentCreated();
            _sidebar?.Activate();
        }

        private void SidebarHostControl_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _sidebar?.Deactivate();
                _sidebar?.Dispose();
                _landingPage?.Dispose();
            }
            catch
            {
                // no-op
            }
            finally
            {
                _sidebar = null;
                _landingPage = null;
                _createScheduled = false;
            }
        }

        private void EnsureContentCreated()
        {
            if ((_sidebar != null || _landingPage != null) || _createScheduled)
            {
                return;
            }

            _createScheduled = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!IsLoaded)
                {
                    _createScheduled = false;
                    return;
                }

                using (PerfTrace.Measure("SidebarHost.CreateContent", _logger, _enableDiagnostics))
                {
                    try
                    {
                        // Check if first-time setup is needed
                        if (!_settings.Persisted.FirstTimeSetupCompleted)
                        {
                            CreateLandingPage();
                        }
                        else
                        {
                            CreateSidebar();
                        }

                        PART_Loading.Visibility = Visibility.Collapsed;
                    }
                    catch (Exception ex)
                    {
                        _logger?.Error(ex, "Failed to create sidebar content.");
                        PART_Loading.Visibility = Visibility.Visible;
                    }
                }
            }), DispatcherPriority.Background);
        }

        private void CreateLandingPage()
        {
            _logger.Info("Showing first-time landing page.");

            _landingPage = new FirstTimeLandingPage(
                _api,
                _logger,
                _achievementManager,
                _settings);

            _landingPage.SetupComplete += LandingPage_SetupComplete;

            PART_Content.Content = _landingPage;
        }

        private void CreateSidebar()
        {
            _logger.Info("Creating sidebar control.");

            var control = _createView() as SidebarControl;
            if (control == null)
            {
                throw new InvalidOperationException("SidebarHostControl factory did not return SidebarControl.");
            }

            _sidebar = control;
            PART_Content.Content = _sidebar;
            _sidebar.Activate();
        }

        private void LandingPage_SetupComplete(object sender, EventArgs e)
        {
            _logger.Info("First-time setup complete, transitioning to sidebar.");

            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    _landingPage?.Dispose();
                    _landingPage = null;

                    CreateSidebar();
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, "Failed to transition from landing page to sidebar.");
                }
            }), DispatcherPriority.Background);
        }
    }
}
