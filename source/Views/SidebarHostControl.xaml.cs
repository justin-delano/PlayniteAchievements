using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Services;
using Playnite.SDK;

namespace PlayniteAchievements.Views
{
    /// <summary>
    /// Lightweight host that returns immediately from the sidebar Opened callback and defers
    /// creation of the heavy SidebarControl until after Playnite has a chance to paint.
    /// </summary>
    public partial class SidebarHostControl : UserControl
    {
        private readonly Func<UserControl> _createView;
        private readonly ILogger _logger;
        private readonly bool _enableDiagnostics;

        private SidebarControl _sidebar;
        private bool _createScheduled;

        public SidebarHostControl(Func<UserControl> createView, ILogger logger, bool enableDiagnostics)
        {
            _createView = createView ?? throw new ArgumentNullException(nameof(createView));
            _logger = logger;
            _enableDiagnostics = enableDiagnostics;

            InitializeComponent();

            Loaded += SidebarHostControl_Loaded;
            Unloaded += SidebarHostControl_Unloaded;
        }

        private void SidebarHostControl_Loaded(object sender, RoutedEventArgs e)
        {
            EnsureSidebarCreated();
            _sidebar?.Activate();
        }

        private void SidebarHostControl_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _sidebar?.Deactivate();
                _sidebar?.Dispose();
            }
            catch
            {
                // no-op
            }
            finally
            {
                _sidebar = null;
                _createScheduled = false;
            }
        }

        private void EnsureSidebarCreated()
        {
            if (_sidebar != null || _createScheduled)
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

                using (PerfTrace.Measure("SidebarHost.CreateSidebarControl", _logger, _enableDiagnostics))
                {
                    try
                    {
                        var control = _createView() as SidebarControl;
                        if (control == null)
                        {
                            throw new InvalidOperationException("SidebarHostControl factory did not return SidebarControl.");
                        }

                        _sidebar = control;
                        PART_Content.Content = control;
                        PART_Loading.Visibility = Visibility.Collapsed;
                        _sidebar.Activate();
                    }
                    catch (Exception ex)
                    {
                        _logger?.Error(ex, "Failed to create sidebar control.");
                        PART_Loading.Visibility = Visibility.Visible;
                    }
                }
            }), DispatcherPriority.Background);
        }
    }
}
