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
    /// </summary>
    public sealed class FullscreenWindowService : IDisposable
    {
        private readonly IPlayniteAPI _api;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly Action<Guid?> _requestSingleGameThemeUpdate;
        private Dispatcher UiDispatcher => _api?.MainView?.UIDispatcher ?? Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        private Window _achievementsWindow;

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
            ShowAchievementsWindow(styleKey: "AchievementsWindow", preselectGameId: null);
        }

        /// <summary>
        /// Opens the achievement window for a specific game.
        /// </summary>
        public void OpenGameWindow(Guid gameId)
        {
            if (gameId == Guid.Empty)
            {
                return;
            }

            PreselectGame(gameId);
            ShowAchievementsWindow(styleKey: "GameAchievementsWindow", preselectGameId: gameId);
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

            PreselectGame(id.Value);
            ShowAchievementsWindow(styleKey: "GameAchievementsWindow", preselectGameId: id);
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
                    dispatcher.Invoke(() =>
                    {
                        if (_achievementsWindow != null && _achievementsWindow.IsVisible)
                        {
                            _achievementsWindow.Close();
                        }
                    }, DispatcherPriority.Send);
                }
            }
            catch
            {
                // Ignore errors when closing window
            }
        }

        private void PreselectGame(Guid gameId)
        {
            try { _api?.MainView?.SelectGame(gameId); } catch { }
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

        private void ShowAchievementsWindow(string styleKey, Guid? preselectGameId)
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
                    OpenOverlayWindowOnUiThread(styleKey, preselectGameId);
                }
                else
                {
                    dispatcher.Invoke(() => OpenOverlayWindowOnUiThread(styleKey, preselectGameId), DispatcherPriority.Send);
                }
            }
            catch
            {
                // Ignore errors when opening window
            }
        }

        private void OpenOverlayWindowOnUiThread(string styleKey, Guid? preselectGameId)
        {
            if (preselectGameId.HasValue)
            {
                PreselectGame(preselectGameId.Value);
            }

            try
            {
                if (_achievementsWindow != null && _achievementsWindow.IsVisible)
                {
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
            };

            window.Content = content;
            _achievementsWindow = window;

            window.ShowDialog();
        }
    }
}
