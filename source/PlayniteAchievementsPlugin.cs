using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.ThemeIntegration;
using PlayniteAchievements.Models.Achievement;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.Providers;
using PlayniteAchievements.Services;
using PlayniteAchievements.Providers.RetroAchievements;
using PlayniteAchievements.Providers.Steam;
using PlayniteAchievements.Views;
using PlayniteAchievements.Views.Helpers;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using PlayniteAchievements.Common;
using PlayniteAchievements.Services.Images;
using PlayniteAchievements.Services.ThemeIntegration;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Shell;
using System.Windows.Threading;
using LiveCharts.Wpf;

namespace PlayniteAchievements
{
    public class PlayniteAchievementsPlugin : GenericPlugin
    {
        private static readonly ILogger _logger = LogManager.GetLogger(nameof(PlayniteAchievementsPlugin));


        // Set Properties before constructor runs
        private static readonly GenericPluginProperties _pluginProperties = new GenericPluginProperties
        {
            HasSettings = true
        };

        private readonly PlayniteAchievementsSettingsViewModel _settingsViewModel;
        private readonly AchievementManager _achievementService;
        private readonly MemoryImageService _imageService;
        private readonly DiskImageService _diskImageService;
        private readonly NotificationPublisher _notifications;
        private readonly SteamHTTPClient _steamClient;
        private readonly SteamSessionManager _sessionManager;

        private readonly BackgroundUpdater _backgroundUpdates;

        // Theme integration
        private readonly ThemeIntegrationAdapter _themeAdapter;
        private readonly ThemeIntegrationUpdateService _themeUpdateService;
        private readonly FullscreenThemeIntegrationService _fullscreenThemeIntegration;

        // Control factories for theme integration
        private static readonly Dictionary<string, Func<Control>> SuccessStoryControlFactories = new Dictionary<string, Func<Control>>(StringComparer.OrdinalIgnoreCase)
        {
            { "PluginButton", () => new Views.ThemeIntegration.SuccessStory.SuccessStoryPluginButtonControl() },
            { "PluginProgressBar", () => new Views.ThemeIntegration.SuccessStory.SuccessStoryPluginProgressBarControl() },
            { "PluginCompactList", () => new Views.ThemeIntegration.SuccessStory.SuccessStoryPluginCompactListControl() },
            { "PluginCompactLocked", () => new Views.ThemeIntegration.SuccessStory.SuccessStoryPluginCompactLockedControl() },
            { "PluginCompactUnlocked", () => new Views.ThemeIntegration.SuccessStory.SuccessStoryPluginCompactUnlockedControl() },
            { "PluginChart", () => new Views.ThemeIntegration.SuccessStory.SuccessStoryPluginChartControl() },
            { "PluginUserStats", () => new Views.ThemeIntegration.SuccessStory.SuccessStoryPluginUserStatsControl() },
            { "PluginList", () => new Views.ThemeIntegration.SuccessStory.SuccessStoryPluginListControl() },
            { "PluginViewItem", () => new Views.ThemeIntegration.SuccessStory.SuccessStoryPluginViewItemControl() }
        };

        private static readonly Dictionary<string, Func<Control>> NativeControlFactories = new Dictionary<string, Func<Control>>(StringComparer.OrdinalIgnoreCase)
        {
            { "AchievementButton", () => new Views.ThemeIntegration.Native.AchievementButtonControl() },
            { "AchievementProgressBar", () => new Views.ThemeIntegration.Native.AchievementProgressBarControl() },
            { "AchievementCompactList", () => new Views.ThemeIntegration.Native.AchievementCompactListControl() },
            { "AchievementChart", () => new Views.ThemeIntegration.Native.AchievementChartControl() },
            { "AchievementStats", () => new Views.ThemeIntegration.Native.AchievementStatsControl() },
            { "AchievementList", () => new Views.ThemeIntegration.Native.AchievementListControl() }
        };

        public override Guid Id { get; } =
            Guid.Parse("e6aad2c9-6e06-4d8d-ac55-ac3b252b5f7b");

        public PlayniteAchievementsSettings Settings => _settingsViewModel.Settings;
        public AchievementManager AchievementService => _achievementService;
        public MemoryImageService ImageService => _imageService;
        public ThemeIntegrationAdapter ThemeIntegrationAdapter => _themeAdapter;
        public ThemeIntegrationUpdateService ThemeUpdateService => _themeUpdateService;
        public static PlayniteAchievementsPlugin Instance { get; private set; }

        /// <summary>
        /// Event raised when plugin settings are saved. Used to refresh UI components
        /// that display authentication status or other settings-dependent information.
        /// </summary>
        public static event EventHandler SettingsSaved;

        /// <summary>
        /// Raises the SettingsSaved event to notify listeners that settings have changed.
        /// </summary>
        public static void NotifySettingsSaved() => SettingsSaved?.Invoke(null, EventArgs.Empty);

        // Public bridge method for external helpers/themes that used to target SuccessStory via reflection.
        // AnikiHelper (PlayniteAchievements-based) will call this when available.
        public Task RequestSingleGameScanAsync(Guid playniteGameId)
        {
            return _achievementService?.ExecuteScanAsync(Models.ScanModeKeys.Single, playniteGameId) ?? Task.CompletedTask;
        }

        public PlayniteAchievementsPlugin(IPlayniteAPI api) : base(api)
        {
            Properties = _pluginProperties;

            Instance = this;
            _logger.Info("PlayniteAchievementsPlugin initializing...");
            _settingsViewModel = new PlayniteAchievementsSettingsViewModel(this);

            EnsureWpfFallbackResources();


            // NECESSARY TO MAKE SURE CHARTS WORK
            var Circle = LiveCharts.Wpf.DefaultGeometries.Circle;
            // NECESSARY DO NOT REMOVE

            // Configure rarity thresholds from settings
            RarityHelper.Configure(
                _settingsViewModel.Settings.Persisted.UltraRareThreshold,
                _settingsViewModel.Settings.Persisted.RareThreshold,
                _settingsViewModel.Settings.Persisted.UncommonThreshold);

            _sessionManager = new SteamSessionManager(PlayniteApi, _logger, GetPluginUserDataPath(), _settingsViewModel.Settings);
            _steamClient = new SteamHTTPClient(PlayniteApi, _logger, _sessionManager);

            var providers = new List<IDataProvider>
            {
                new SteamDataProvider(
                    _logger,
                    _settingsViewModel.Settings,
                    _steamClient,
                    _sessionManager,
                    new SteamAPIClient(_steamClient.ApiHttpClient, _logger),
                    PlayniteApi),

                new RetroAchievementsDataProvider(
                    _logger,
                    _settingsViewModel.Settings,
                    GetPluginUserDataPath())
            };

            _diskImageService = new DiskImageService(_logger, GetPluginUserDataPath());
            _imageService = new MemoryImageService(_logger, _diskImageService);
            _achievementService = new AchievementManager(api, _settingsViewModel.Settings, _logger, this, providers, _diskImageService);
            _notifications = new NotificationPublisher(api, _settingsViewModel.Settings, _logger);
            _backgroundUpdates = new BackgroundUpdater(_achievementService, _settingsViewModel.Settings, _logger, _notifications, null);
            _themeAdapter = new ThemeIntegrationAdapter(_settingsViewModel.Settings);
            _themeUpdateService = new ThemeIntegrationUpdateService(
                _themeAdapter,
                _achievementService,
                _settingsViewModel.Settings,
                _logger,
                PlayniteApi?.MainView?.UIDispatcher ?? System.Windows.Application.Current.Dispatcher);
            _fullscreenThemeIntegration = new FullscreenThemeIntegrationService(
                PlayniteApi,
                _achievementService,
                _settingsViewModel.Settings,
                OpenPerGameAchievementsView,
                _themeUpdateService.RequestUpdate,
                _logger);

            // Listen for new games entering the database to auto-scan .
            PlayniteApi?.Database?.Games?.ItemCollectionChanged += Games_ItemCollectionChanged;

            // Theme integration - settings support
            // SettingsRoot = "Settings" tells Playnite to access the Settings property on the ViewModel
            // Theme bindings like {PluginSettings Plugin=PlayniteAchievements, Path=HasAchievements}
            // will resolve to ViewModel.Settings.HasAchievements
            AddSettingsSupport(new AddSettingsSupportArgs
            {
                SourceName = "PlayniteAchievements",
                SettingsRoot = "Settings"
            });

            // Custom elements integration
            AddCustomElementSupport(new AddCustomElementSupportArgs
            {
                ElementList = new List<string>
                {
                    // SuccessStory-compatible controls (legacy naming; properties are also exposed via native keys)
                    "PluginButton",
                    "PluginProgressBar",
                    "PluginCompactList",
                    "PluginCompactLocked",
                    "PluginCompactUnlocked",
                    "PluginChart",
                    "PluginUserStats",
                    "PluginList",
                    "PluginViewItem",

                    // Native PlayniteAchievements controls (always available)
                    "AchievementButton",
                    "AchievementProgressBar",
                    "AchievementCompactList",
                    "AchievementChart",
                    "AchievementStats",
                    "AchievementList"
                },
                SourceName = "PlayniteAchievements"
            });

            _logger.Info("PlayniteAchievementsPlugin initialized.");
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            _logger.Info($"GetSettings called, firstRunSettings={firstRunSettings}");
            return _settingsViewModel;
        }

        public override UserControl GetSettingsView(bool firstRunView)
        {
            try
            {
                _logger.Info($"GetSettingsView called, firstRunView={firstRunView}");
                var control = new SettingsControl(_settingsViewModel, _logger, _steamClient, _sessionManager, this);
                _logger.Info("GetSettingsView succeeded");
                return control;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "GetSettingsView failed");
                throw;
            }
        }

        // === Menus ===

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            // Show per-game view/scan for any single game. Provider selection is handled by AchievementManager.
            if (args?.Games == null || args.Games.Count != 1)
            {
                yield break;
            }

            var game = args.Games[0];
            if (game == null)
            {
                yield break;
            }

            yield return new GameMenuItem
            {
                Description = ResourceProvider.GetString("LOCPlayAch_Menu_ViewAchievements"),
                MenuSection = "Playnite Achievements",
                Action = (a) =>
                {
                    OpenPerGameAchievementsView(game.Id);
                }
            };

            // yield return new GameMenuItem
            // {
            //     Description = ResourceProvider.GetString("LOCPlayAch_Menu_ScanGame"),
            //     MenuSection = "Playnite Achievements",
            //     Action = (a) =>
            //     {
            //         // Prevent single game scan during full/quick scan
            //         if (_achievementService.IsRebuilding)
            //         {
            //             return;
            //         }
            //         _ = _achievementService.StartManagedSingleGameScanAsync(game.Id);
            //     }
            // };

            // yield return new GameMenuItem
            // {
            //     Description = ResourceProvider.GetString("LOCPlayAch_Menu_ParityTest_Native"),
            //     MenuSection = "Playnite Achievements",
            //     Action = (a) =>
            //     {
            //         OpenParityTestView(game.Id, ParityTestMode.Native);
            //     }
            // };

            // yield return new GameMenuItem
            // {
            //     Description = ResourceProvider.GetString("LOCPlayAch_Menu_ParityTest_Compatibility"),
            //     MenuSection = "Playnite Achievements",
            //     Action = (a) =>
            //     {
            //         OpenParityTestView(game.Id, ParityTestMode.Compatibility);
            //     }
            // };
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            yield return new MainMenuItem
            {
                Description = ResourceProvider.GetString("LOCPlayAch_ScanMode_Quick"),
                MenuSection = "@Playnite Achievements",
                Action = (a) =>
                {
                    ShowScanProgressWindowAndRun(() => _achievementService.ExecuteScanAsync(Models.ScanModeKeys.Quick));
                }
            };

            yield return new MainMenuItem
            {
                Description = ResourceProvider.GetString("LOCPlayAch_ScanMode_Full"),
                MenuSection = "@Playnite Achievements",
                Action = (a) =>
                {
                    ShowScanProgressWindowAndRun(() => _achievementService.ExecuteScanAsync(Models.ScanModeKeys.Full));
                }
            };

            yield return new MainMenuItem
            {
                Description = ResourceProvider.GetString("LOCPlayAch_ScanMode_Installed"),
                MenuSection = "@Playnite Achievements",
                Action = (a) =>
                {
                    ShowScanProgressWindowAndRun(() => _achievementService.ExecuteScanAsync(Models.ScanModeKeys.Installed));
                }
            };

            yield return new MainMenuItem
            {
                Description = ResourceProvider.GetString("LOCPlayAch_ScanMode_Favorites"),
                MenuSection = "@Playnite Achievements",
                Action = (a) =>
                {
                    ShowScanProgressWindowAndRun(() => _achievementService.ExecuteScanAsync(Models.ScanModeKeys.Favorites));
                }
            };

            yield return new MainMenuItem
            {
                Description = ResourceProvider.GetString("LOCPlayAch_ScanMode_Selected"),
                MenuSection = "@Playnite Achievements",
                Action = (a) =>
                {
                    ShowScanProgressWindowAndRun(() => _achievementService.ExecuteScanAsync(Models.ScanModeKeys.LibrarySelected));
                }
            };
        }

        // === Sidebar ===

        public override IEnumerable<SidebarItem> GetSidebarItems()
        {
            yield return new SidebarItem
            {
                Title = ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                Type = SiderbarItemType.View,
                Icon = GetSidebarIcon(),
                Opened = () =>
                {
                    using (PlayniteAchievements.Common.PerfTrace.Measure(
                        "SidebarItem.Opened",
                        _logger,
                        _settingsViewModel?.Settings?.Persisted?.EnableDiagnostics == true))
                    {
                        return new SidebarHostControl(
                            () => new SidebarControl(PlayniteApi, _logger, _achievementService, _settingsViewModel.Settings),
                            _logger,
                            _settingsViewModel?.Settings?.Persisted?.EnableDiagnostics == true,
                            PlayniteApi,
                            _achievementService,
                            this);
                    }
                }
            };
        }

        private TextBlock GetSidebarIcon()
        {
            var tb = new TextBlock
            {
                Text = char.ConvertFromUtf32(0xedd7), // ico-font: trophy
                FontSize = 18
            };

            var font = ResourceProvider.GetResource("FontIcoFont") as FontFamily;
            tb.FontFamily = font ?? new FontFamily("Segoe UI Symbol");
            // tb.SetResourceReference(TextBlock.ForegroundProperty, "GlyphBrush");

            return tb;
        }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            _logger.Info($"Game stopped: {args.Game.Name}. Triggering achievement scan.");
            _ = _achievementService.ExecuteScanAsync(Models.ScanModeKeys.Single, args.Game.Id);
        }

        // === Lifecycle ===

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            try
            {
                EnsureWpfFallbackResources();
            }
            catch
            {
                // ignore
            }
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            _logger.Info("OnApplicationStopped called.");
            // Stop startup init if still running
            try
            {


                PlayniteApi?.Database?.Games?.ItemCollectionChanged -= Games_ItemCollectionChanged;

            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Error during application shutdown cleanup.");
            }

            _backgroundUpdates.Stop();

            try { _imageService?.Dispose(); } catch { }
            try { _diskImageService?.Dispose(); } catch { }
            try { _themeUpdateService?.Dispose(); } catch { }
            try { _fullscreenThemeIntegration?.Dispose(); } catch { }
        }

        // === Theme Integration ===

        public override void OnGameSelected(OnGameSelectedEventArgs args)
        {
            try
            {
                if (args.NewValue?.Count == 1)
                {
                    var game = args.NewValue[0];
                    if (game == null)
                    {
                        _themeUpdateService.RequestUpdate(null);
                        _fullscreenThemeIntegration?.NotifySelectionChanged(null);
                        return;
                    }

                    _themeUpdateService.RequestUpdate(game.Id);
                    _fullscreenThemeIntegration?.NotifySelectionChanged(game.Id);
                }
                else
                {
                    // Clear theme data when no game or multiple games selected
                    _themeUpdateService.RequestUpdate(null);
                    _fullscreenThemeIntegration?.NotifySelectionChanged(null);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in OnGameSelected");
            }
        }

        public override void OnControllerButtonStateChanged(OnControllerButtonStateChangedArgs args)
        {
            base.OnControllerButtonStateChanged(args);

            try
            {
                if (args?.Button == ControllerInput.B && args.State == ControllerInputState.Pressed)
                {
                    _fullscreenThemeIntegration?.CloseOverlayWindowIfOpen();
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Failed to handle controller button state change for fullscreen overlay.");
            }
        }

        internal void RequestThemeUpdate(Game gameContext)
        {
            _themeUpdateService?.RequestUpdate(gameContext?.Id);
        }

        private void ShowScanProgressWindowAndRun(Func<Task> scanTask)
        {
            try
            {
                EnsureWpfFallbackResources();

                var progressWindow = new ScanProgressWindow(_achievementService, _logger);

                progressWindow.Owner = PlayniteApi?.Dialogs?.GetCurrentAppWindow();

                progressWindow.Show();

                Task.Run(async () =>
                {
                    try
                    {
                        await scanTask().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Scan task failed");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to show scan progress window");
            }
        }

        public override Control GetGameViewControl(GetGameViewControlArgs args)
        {
            // SuccessStory-compatible controls (legacy naming; properties are always populated).
            if (SuccessStoryControlFactories.TryGetValue(args.Name, out var successStoryFactory))
            {
                return successStoryFactory();
            }

            // Native PlayniteAchievements controls (always available)
            if (NativeControlFactories.TryGetValue(args.Name, out var nativeFactory))
            {
                return nativeFactory();
            }

            return null;
        }

        // === End Theme Integration ===

        private void OpenPerGameAchievementsView(Guid gameId)
        {
            try
            {
                EnsureWpfFallbackResources();

                var view = new PerGameControl(
                    gameId,
                    _achievementService,
                    PlayniteApi,
                    _logger,
                    _settingsViewModel.Settings);

                var windowOptions = new WindowOptions
                {
                    ShowMinimizeButton = true,
                    ShowMaximizeButton = true,
                    ShowCloseButton = true,
                    CanBeResizable = true,
                    Width = 800,
                    Height = 700
                };

                var window = PlayniteUiProvider.CreateExtensionWindow(
                    view.WindowTitle,
                    view,
                    windowOptions
                );

                window.MinWidth = 450;
                window.MinHeight = 500;
                try
                {
                    if (window.Owner == null)
                    {
                        window.Owner = PlayniteApi?.Dialogs?.GetCurrentAppWindow();
                    }
                }
                catch { }
                window.Closed += (s, ev) => view.Cleanup();

                var isFullscreen = false;
                try
                {
                    isFullscreen = PlayniteApi?.ApplicationInfo?.Mode == ApplicationMode.Fullscreen;
                }
                catch { }

                // In fullscreen mode, modal dialogs can effectively lock the UI if they fail to surface above the theme.
                // Prefer a non-modal window.
                if (isFullscreen)
                {
                    window.Show();
                    try
                    {
                        // Force focus/foreground in fullscreen themes.
                        window.Topmost = true;
                        window.Activate();
                        window.Topmost = false;
                    }
                    catch { }
                }
                else
                {
                    window.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to open per-game achievements view for gameId={gameId}");
                PlayniteApi?.Dialogs?.ShowErrorMessage(
                    $"Failed to open achievements view: {ex.Message}",
                    "Playnite Achievements");
            }
        }

        private void EnsureWpfFallbackResources()
        {
            try
            {
                var app = Application.Current;
                if (app == null)
                {
                    return;
                }

                void Ensure()
                {
                    if (!app.Resources.Contains("BaseTextBlockStyle"))
                    {
                        app.Resources["BaseTextBlockStyle"] = new Style(typeof(TextBlock));
                    }
                }

                if (app.Dispatcher.CheckAccess())
                {
                    Ensure();
                }
                else
                {
                    app.Dispatcher.Invoke((Action)Ensure, DispatcherPriority.Send);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to ensure WPF fallback resources.");
            }
        }

        private enum ParityTestMode
        {
            Native,
            Compatibility
        }

        private void OpenParityTestView(Guid gameId, ParityTestMode mode)
        {
            try
            {
                var game = PlayniteApi?.Database?.Games?.Get(gameId);
                if (game == null)
                {
                    PlayniteApi?.Dialogs?.ShowErrorMessage(
                        ResourceProvider.GetString("LOCPlayAch_Text_UnknownGame"),
                        ResourceProvider.GetString("LOCPlayAch_Title_PluginName"));
                    return;
                }

                UserControl view;
                string title;

                if (mode == ParityTestMode.Native)
                {
                    view = new Views.ParityTests.NativeParityTestView(game);
                    title = "PlayniteAchievements UI Parity Test (Native)";
                }
                else
                {
                    view = new Views.ParityTests.CompatibilityParityTestView(game);
                    title = "PlayniteAchievements UI Parity Test (Compatibility)";
                }

                var windowOptions = new WindowOptions
                {
                    ShowMinimizeButton = true,
                    ShowMaximizeButton = true,
                    ShowCloseButton = true,
                    CanBeResizable = true,
                    Width = 980,
                    Height = 780
                };

                var window = PlayniteUiProvider.CreateExtensionWindow(title, view, windowOptions);
                window.MinWidth = 700;
                window.MinHeight = 500;

                window.ShowDialog();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to open parity test view for gameId={gameId}, mode={mode}");
                PlayniteApi?.Dialogs?.ShowErrorMessage(
                    $"Failed to open parity test view: {ex.Message}",
                    ResourceProvider.GetString("LOCPlayAch_Title_PluginName"));
            }
        }

        // === Game selection wiring ===

        private void Games_ItemCollectionChanged(object sender, ItemCollectionChangedEventArgs<Game> e)
        {
            _logger.Info("Games_ItemCollectionChanged triggered.");
            if (e?.AddedItems == null || e.AddedItems.Count == 0)
            {
                return;
            }

            foreach (var game in e.AddedItems)
            {
                if (game == null)
                {
                    continue;
                }

                // Fire and forget; StartManagedSingleGameScanAsync already manages progress/state.
                _ = TriggerNewGameScanAsync(game);
            }
        }

        private Task TriggerNewGameScanAsync(Game game)
        {
            return Task.Run(async () =>
            {
                try
                {
                    _logger.Info($"Detected new game '{game?.Name}' ({game?.GameId}); starting single-game scan.");
                    await _achievementService.ExecuteScanAsync(Models.ScanModeKeys.Single, game.Id).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Failed auto-scan for new game '{game?.Name}' ({game?.GameId}).");
                }
            });
        }
    }
}
