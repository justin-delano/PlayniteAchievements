using Playnite.SDK;
using Playnite.SDK.Models;
using PlayniteAchievements.Models;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;

namespace PlayniteAchievements.Services.ThemeIntegration
{
    /// <summary>
    /// Service for managing fullscreen overlay achievement windows.
    /// Handles window creation, display, and closing for fullscreen mode.
    /// Follows SuccessStoryFullscreenHelper pattern for opening windows,
    /// but restores original selection when window closes.
    /// </summary>
    public sealed class FullscreenWindowService : IDisposable
    {
        private readonly IPlayniteAPI _api;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly Action<Guid?> _requestSingleGameThemeUpdate;
        private Dispatcher UiDispatcher => _api?.MainView?.UIDispatcher ?? Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        private Window _achievementsWindow;
        private Guid? _originalSelectedGameId;

        public FullscreenWindowService(
            IPlayniteAPI api,
            PlayniteAchievementsSettings settings,
            Action<Guid?> requestSingleGameThemeUpdate)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _requestSingleGameThemeUpdate = requestSingleGameThemeUpdate ?? throw new ArgumentNullException(nameof(requestSingleGameThemeUpdate));
        }

        public void Dispose()
        {
            CloseOverlayWindowIfOpen();
        }

        /// <summary>
        /// Opens the all-games achievement overview window.
        /// </summary>
        public void OpenOverviewWindow()
        {
            _originalSelectedGameId = GetSingleSelectedGameId();
            ShowAchievementsWindow(styleKey: "AchievementsWindow", selectGameId: null);
        }

        /// <summary>
        /// Opens the achievement window for a specific game.
        /// Changes Playnite's selection so theme bindings resolve to this game.
        /// </summary>
        public void OpenGameWindow(Guid gameId)
        {
            if (gameId == Guid.Empty)
            {
                return;
            }

            // Change Playnite's selection so theme bindings ({PluginSettings}, {Binding SelectedGame...})
            // resolve to this game's data. This is the SuccessStoryFullscreenHelper pattern.
            SelectGame(gameId);
            ShowAchievementsWindow(styleKey: "GameAchievementsWindow", selectGameId: null);
        }

        /// <summary>
        /// Opens the achievement window for the currently selected game.
        /// </summary>
        public void OpenSelectedGameWindow()
        {
            var id = GetSingleSelectedGameId();
            if (!id.HasValue)
            {
                return;
            }

            _originalSelectedGameId = id.Value;
            SelectGame(id.Value);
            ShowAchievementsWindow(styleKey: "GameAchievementsWindow", selectGameId: null);
        }

        /// <summary>
        /// Closes the overlay window if it's currently open.
        /// </summary>
        public void CloseOverlayWindowIfOpen()
        {
            try
            {
                var dispatcher = UiDispatcher;
                if (dispatcher == null || dispatcher.CheckAccess())
                {
                    if (_achievementsWindow != null && _achievementsWindow.IsVisible)
                    {
                        _achievementsWindow.Close();
                    }
                }
                else
                {
                    dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_achievementsWindow != null && _achievementsWindow.IsVisible)
                        {
                            _achievementsWindow.Close();
                        }
                    }), DispatcherPriority.Background);
                }
            }
            catch
            {
                // Ignore errors when closing window
            }
        }

        private void SelectGame(Guid gameId)
        {
            // Change Playnite's main selection - this triggers OnGameSelected which
            // updates SelectedGame and theme data for the new selection
            try { _api?.MainView?.SelectGame(gameId); } catch { }
            // Request theme data update for the selected game
            try { _requestSingleGameThemeUpdate(gameId); } catch { }
        }

        private Guid? GetSingleSelectedGameId()
        {
            try
            {
                var selected = _api?.MainView?.SelectedGames?
                    .Where(g => g != null)
                    .Take(2)
                    .ToList();

                if (selected == null || selected.Count != 1)
                {
                    return null;
                }

                return selected[0].Id;
            }
            catch
            {
                return null;
            }
        }

        private void ShowAchievementsWindow(string styleKey, Guid? selectGameId)
        {
            if (string.IsNullOrWhiteSpace(styleKey))
            {
                return;
            }

            try
            {
                var dispatcher = UiDispatcher;
                if (dispatcher == null)
                {
                    return;
                }

                if (dispatcher.CheckAccess())
                {
                    OpenOverlayWindowOnUiThread(styleKey, selectGameId);
                }
                else
                {
                    dispatcher.BeginInvoke(new Action(() => OpenOverlayWindowOnUiThread(styleKey, selectGameId)), DispatcherPriority.Background);
                }
            }
            catch
            {
                // Ignore errors when opening window
            }
        }

        private void OpenOverlayWindowOnUiThread(string styleKey, Guid? selectGameId)
        {
            if (selectGameId.HasValue)
            {
                SelectGame(selectGameId.Value);
            }

            try
            {
                if (_achievementsWindow != null && _achievementsWindow.IsVisible)
                {
                    // Clear original selection tracking before closing so the Closed
                    // handler doesn't restore to old selection when transitioning windows
                    _originalSelectedGameId = null;
                    _achievementsWindow.Close();
                }
            }
            catch
            {
            }

            // Match SuccessStoryFullscreenHelper behavior as closely as possible (Aniki ReMake expects this).
            var window = _api.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMinimizeButton = false
            });

            var parent = _api.Dialogs.GetCurrentAppWindow();
            if (parent != null)
            {
                window.Owner = parent;
            }
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            window.Title = "Achievements";

            window.Height = parent != null && parent.Height > 0 ? parent.Height : SystemParameters.PrimaryScreenHeight;
            window.Width = parent != null && parent.Width > 0 ? parent.Width : SystemParameters.PrimaryScreenWidth;

            var xamlString = $@"
                <Viewbox Stretch=""Uniform""
                        xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
                        xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
                        xmlns:pbeh=""clr-namespace:Playnite.Behaviors;assembly=Playnite"">
                    <Grid Width=""1920"" Height=""1080"">
                        <ContentControl x:Name=""AchievementsWindow""
                                        Focusable=""False""
                                        Style=""{{DynamicResource {styleKey}}}"" />
                    </Grid>
                </Viewbox>";

            var content = (FrameworkElement)XamlReader.Parse(xamlString);
            content.DataContext = _settings;

            window.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    try { window.Close(); } catch { }
                    e.Handled = true;
                }
            };

            window.Closed += (_, __) =>
            {
                if (ReferenceEquals(_achievementsWindow, window))
                {
                    _achievementsWindow = null;
                }

                // Restore original selection when window closes
                if (_originalSelectedGameId.HasValue)
                {
                    try { _api?.MainView?.SelectGame(_originalSelectedGameId.Value); } catch { }
                    try { _requestSingleGameThemeUpdate(_originalSelectedGameId); } catch { }
                    _originalSelectedGameId = null;
                }
            };

            window.Content = content;
            _achievementsWindow = window;

            window.ShowDialog();
        }
    }
}
