using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.ThemeIntegration;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.Providers;
using PlayniteAchievements.Services;
using PlayniteAchievements.Providers.RetroAchievements;
using PlayniteAchievements.Providers.Steam;
using PlayniteAchievements.Providers.GOG;
using PlayniteAchievements.Providers.Epic;
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
        private readonly AchievementManager _achievementManager;
        private readonly MemoryImageService _imageService;
        private readonly DiskImageService _diskImageService;
        private readonly NotificationPublisher _notifications;
        private readonly SteamSessionManager _steamSessionManager;
        private readonly GogSessionManager _gogSessionManager;
        private readonly EpicSessionManager _epicSessionManager;
        private readonly ProviderRegistry _providerRegistry;

        private readonly BackgroundUpdater _backgroundUpdates;

        // Theme integration
        private readonly ThemeIntegrationUpdateService _themeUpdateService;
        private readonly FullscreenWindowService _fullscreenWindowService;
        private readonly ThemeIntegrationService _themeIntegrationService;

        // Control factories for theme integration
        private static readonly Dictionary<string, Func<Control>> LegacyControlFactories = new Dictionary<string, Func<Control>>(StringComparer.OrdinalIgnoreCase)
        {
            { "PluginButton", () => new Views.ThemeIntegration.Legacy.PluginButtonControl() },
            { "PluginProgressBar", () => new Views.ThemeIntegration.Legacy.PluginProgressBarControl() },
            { "PluginCompactList", () => new Views.ThemeIntegration.Legacy.PluginCompactListControl() },
            { "PluginCompactLocked", () => new Views.ThemeIntegration.Legacy.PluginCompactLockedControl() },
            { "PluginCompactUnlocked", () => new Views.ThemeIntegration.Legacy.PluginCompactUnlockedControl() },
            { "PluginChart", () => new Views.ThemeIntegration.Legacy.PluginChartControl() },
            { "PluginUserStats", () => new Views.ThemeIntegration.Legacy.PluginUserStatsControl() },
            { "PluginList", () => new Views.ThemeIntegration.Legacy.PluginListControl() },
            { "PluginViewItem", () => new Views.ThemeIntegration.Legacy.PluginViewItemControl() }
        };

        private static readonly Dictionary<string, Func<Control>> DesktopControlFactories = new Dictionary<string, Func<Control>>(StringComparer.OrdinalIgnoreCase)
        {
            { "AchievementButton", () => new Views.ThemeIntegration.Desktop.AchievementButtonControl() },
            { "AchievementProgressBar", () => new Views.ThemeIntegration.Desktop.AchievementProgressBarControl() },
            { "AchievementCompactList", () => new Views.ThemeIntegration.Desktop.AchievementCompactListControl() },
            { "AchievementChart", () => new Views.ThemeIntegration.Desktop.AchievementChartControl() },
            { "AchievementStats", () => new Views.ThemeIntegration.Desktop.AchievementStatsControl() },
            { "AchievementList", () => new Views.ThemeIntegration.Desktop.AchievementListControl() }
        };

        public override Guid Id { get; } =
            Guid.Parse("e6aad2c9-6e06-4d8d-ac55-ac3b252b5f7b");

        public PlayniteAchievementsSettings Settings => _settingsViewModel.Settings;
        public ProviderRegistry ProviderRegistry => _providerRegistry;
        public AchievementManager AchievementManager => _achievementManager;
        public MemoryImageService ImageService => _imageService;
        public ThemeIntegrationService ThemeIntegrationService => _themeIntegrationService;
        public ThemeIntegrationUpdateService ThemeUpdateService => _themeUpdateService;
        public SteamSessionManager SteamSessionManager => _steamSessionManager;
        public EpicSessionManager EpicSessionManager => _epicSessionManager;
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
            return _achievementManager?.ExecuteScanAsync(Models.ScanModeType.Single.GetKey(), playniteGameId) ?? Task.CompletedTask;
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
            var panel = new WpfToolkit.Controls.VirtualizingWrapPanel();
            // NECESSARY DO NOT REMOVE


            // Configure rarity thresholds from settings
            RarityHelper.Configure(
                _settingsViewModel.Settings.Persisted.UltraRareThreshold,
                _settingsViewModel.Settings.Persisted.RareThreshold,
                _settingsViewModel.Settings.Persisted.UncommonThreshold);

            var settings = _settingsViewModel.Settings;
            var pluginUserDataPath = GetPluginUserDataPath();

            // Create shared Steam session manager for use by provider and settings UI
            _steamSessionManager = new SteamSessionManager(PlayniteApi, _logger, settings);
            _gogSessionManager = new GogSessionManager(PlayniteApi, _logger, settings);
            _epicSessionManager = new EpicSessionManager(PlayniteApi, _logger, settings);

            var providers = new List<IDataProvider>
            {
                new SteamDataProvider(
                    _logger,
                    settings,
                    PlayniteApi,
                    _steamSessionManager,
                    pluginUserDataPath),
                new GogDataProvider(
                    _logger,
                    settings,
                    PlayniteApi,
                    pluginUserDataPath,
                    _gogSessionManager),
                new EpicDataProvider(
                    _logger,
                    settings,
                    PlayniteApi,
                    _epicSessionManager),
                new RetroAchievementsDataProvider(
                    _logger,
                    settings,
                    PlayniteApi,
                    pluginUserDataPath)
            };

            _diskImageService = new DiskImageService(_logger, pluginUserDataPath);
            _imageService = new MemoryImageService(_logger, _diskImageService);

            // Create provider registry and sync from persisted settings
            _providerRegistry = new ProviderRegistry();
            _providerRegistry.SyncFromSettings(settings.Persisted);

            _achievementManager = new AchievementManager(api, settings, _logger, this, providers, _diskImageService, _providerRegistry);
            _notifications = new NotificationPublisher(api, settings, _logger);
            _backgroundUpdates = new BackgroundUpdater(_achievementManager, settings, _logger, _notifications, null);

            // Create theme integration services
            // Note: We need to create _themeIntegrationService before _themeUpdateService,
            // but _themeIntegrationService needs a callback to _themeUpdateService.
            // We resolve this by using a local variable for the callback.
            ThemeIntegrationUpdateService themeUpdateService = null;
            Action<Guid?> requestUpdate = (id) => themeUpdateService?.RequestUpdate(id);

            _fullscreenWindowService = new FullscreenWindowService(
                PlayniteApi,
                _settingsViewModel.Settings,
                requestUpdate);

            _themeIntegrationService = new ThemeIntegrationService(
                PlayniteApi,
                _achievementManager,
                _settingsViewModel.Settings,
                _fullscreenWindowService,
                requestUpdate,
                _logger);

            themeUpdateService = new ThemeIntegrationUpdateService(
                _themeIntegrationService,
                _achievementManager,
                _settingsViewModel.Settings,
                _logger,
                PlayniteApi?.MainView?.UIDispatcher ?? System.Windows.Application.Current.Dispatcher);
            _themeUpdateService = themeUpdateService;

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
                var control = new SettingsControl(_settingsViewModel, _logger, this, _steamSessionManager, _gogSessionManager, _epicSessionManager);
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
            if (args?.Games == null || args.Games.Count == 0)
            {
                yield break;
            }

            // Multiple games selected - offer "Scan Selected"
            if (args.Games.Count > 1)
            {
                var selectedGames = args.Games.Where(g => g != null).ToList();
                if (selectedGames.Count == 0)
                {
                    yield break;
                }

                yield return new GameMenuItem
                {
                    Description = ResourceProvider.GetString("LOCPlayAch_Menu_ScanSelected"),
                    MenuSection = "Playnite Achievements",
                    Action = (a) =>
                    {
                        ShowScanProgressControlAndRun(() => _achievementManager.ExecuteScanAsync(Models.ScanModeType.LibrarySelected.GetKey()));
                    }
                };

                yield return new GameMenuItem
                {
                    Description = ResourceProvider.GetString("LOCPlayAch_Menu_ClearData"),
                    MenuSection = "Playnite Achievements",
                    Action = (a) =>
                    {
                        ClearSelectedGamesData(selectedGames);
                    }
                };

                yield break;
            }

            // Single game selected
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
                    OpenSingleGameAchievementsView(game.Id);
                }
            };

            yield return new GameMenuItem
            {
                Description = ResourceProvider.GetString("LOCPlayAch_Menu_ScanGame"),
                MenuSection = "Playnite Achievements",
                Action = (a) =>
                {
                    ShowScanProgressControlAndRun(
                        () => _achievementManager.ExecuteScanAsync(Models.ScanModeType.Single.GetKey(), game.Id),
                        game.Id);
                }
            };

            yield return new GameMenuItem
            {
                Description = ResourceProvider.GetString("LOCPlayAch_Menu_ClearData"),
                MenuSection = "Playnite Achievements",
                Action = (a) =>
                {
                    ClearSingleGameData(game);
                }
            };
        }

        private void ClearSingleGameData(Game game)
        {
            if (game == null || game.Id == Guid.Empty)
            {
                return;
            }

            var result = PlayniteApi?.Dialogs?.ShowMessage(
                string.Format(ResourceProvider.GetString("LOCPlayAch_Menu_ClearData_ConfirmSingle"),game.Name),
                ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) ?? MessageBoxResult.None;

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                _achievementManager.RemoveGameCache(game.Id);
                PlayniteApi?.Dialogs?.ShowMessage(
                    string.Format(ResourceProvider.GetString("LOCPlayAch_Menu_ClearData_ConfirmSingle"), game.Name),
                    ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to clear cached data for game '{game.Name}' ({game.Id}).");
                PlayniteApi?.Dialogs?.ShowMessage(
                    string.Format(ResourceProvider.GetString("LOCPlayAch_Menu_ClearData_Failed"), ex.Message),
                    ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ClearSelectedGamesData(IEnumerable<Game> selectedGames)
        {
            var targets = selectedGames?
                .Where(g => g != null && g.Id != Guid.Empty)
                .GroupBy(g => g.Id)
                .Select(g => g.First())
                .ToList() ?? new List<Game>();

            if (targets.Count == 0)
            {
                return;
            }

            var result = PlayniteApi?.Dialogs?.ShowMessage(
                string.Format(ResourceProvider.GetString("LOCPlayAch_Menu_ClearData_ConfirmSelected"), targets.Count),
                ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) ?? MessageBoxResult.None;

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            var clearedCount = 0;
            foreach (var game in targets)
            {
                try
                {
                    _achievementManager.RemoveGameCache(game.Id);
                    clearedCount++;
                }
                catch (Exception ex)
                {
                    _logger?.Warn(ex, $"Failed to clear cached data for game '{game.Name}' ({game.Id}).");
                }
            }

            PlayniteApi?.Dialogs?.ShowMessage(
                string.Format(ResourceProvider.GetString("LOCPlayAch_Menu_ClearData_SuccessSelected"), clearedCount),
                ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            yield return new MainMenuItem
            {
                Description = ResourceProvider.GetString("LOCPlayAch_ScanMode_Quick"),
                MenuSection = "@Playnite Achievements",
                Action = (a) =>
                {
                    ShowScanProgressControlAndRun(() => _achievementManager.ExecuteScanAsync(Models.ScanModeType.Quick.GetKey()));
                }
            };

            yield return new MainMenuItem
            {
                Description = ResourceProvider.GetString("LOCPlayAch_ScanMode_Full"),
                MenuSection = "@Playnite Achievements",
                Action = (a) =>
                {
                    ShowScanProgressControlAndRun(() => _achievementManager.ExecuteScanAsync(Models.ScanModeType.Full.GetKey()));
                }
            };

            yield return new MainMenuItem
            {
                Description = ResourceProvider.GetString("LOCPlayAch_ScanMode_Installed"),
                MenuSection = "@Playnite Achievements",
                Action = (a) =>
                {
                    ShowScanProgressControlAndRun(() => _achievementManager.ExecuteScanAsync(Models.ScanModeType.Installed.GetKey()));
                }
            };

            yield return new MainMenuItem
            {
                Description = ResourceProvider.GetString("LOCPlayAch_ScanMode_Favorites"),
                MenuSection = "@Playnite Achievements",
                Action = (a) =>
                {
                    ShowScanProgressControlAndRun(() => _achievementManager.ExecuteScanAsync(Models.ScanModeType.Favorites.GetKey()));
                }
            };

            yield return new MainMenuItem
            {
                Description = ResourceProvider.GetString("LOCPlayAch_ScanMode_Selected"),
                MenuSection = "@Playnite Achievements",
                Action = (a) =>
                {
                    ShowScanProgressControlAndRun(() => _achievementManager.ExecuteScanAsync(Models.ScanModeType.LibrarySelected.GetKey()));
                }
            };

            yield return new MainMenuItem
            {
                Description = ResourceProvider.GetString("LOCPlayAch_ScanMode_Missing"),
                MenuSection = "@Playnite Achievements",
                Action = (a) =>
                {
                    ShowScanProgressControlAndRun(() => _achievementManager.ExecuteScanAsync(Models.ScanModeType.Missing.GetKey()));
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
                    return new SidebarHostControl(
                        () => new SidebarControl(PlayniteApi, _logger, _achievementManager, _settingsViewModel.Settings),
                        _logger,
                        PlayniteApi,
                        _achievementManager,
                        this);
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
            _ = _achievementManager.ExecuteScanAsync(Models.ScanModeType.Single.GetKey(), args.Game.Id);
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
            try { _fullscreenWindowService?.Dispose(); } catch { }
            try { _themeIntegrationService?.Dispose(); } catch { }
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
                        _themeIntegrationService?.NotifySelectionChanged(null);
                        _settingsViewModel.Settings.SelectedGame = null;
                        return;
                    }

                    _themeUpdateService.RequestUpdate(game.Id);
                    _themeIntegrationService?.NotifySelectionChanged(game.Id);
                    _settingsViewModel.Settings.SelectedGame = game;
                }
                else
                {
                    // Clear theme data when no game or multiple games selected
                    _themeUpdateService.RequestUpdate(null);
                    _themeIntegrationService?.NotifySelectionChanged(null);
                    _settingsViewModel.Settings.SelectedGame = null;
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
                    _fullscreenWindowService?.CloseOverlayWindowIfOpen();
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

        private void ShowScanProgressControlAndRun(Func<Task> scanTask, Guid? singleGameScanId = null)
        {
            try
            {
                // Validate authentication before showing progress window
                if (!_achievementManager.ValidateCanStartScan())
                {
                    return;
                }

                EnsureWpfFallbackResources();

                var progressWindow = new ScanProgressControl(
                    _achievementManager,
                    _logger,
                    singleGameScanId,
                    OpenSingleGameAchievementsView);

                var windowOptions = new WindowOptions
                {
                    ShowMinimizeButton = false,
                    ShowMaximizeButton = false,
                    ShowCloseButton = true,
                    CanBeResizable = false,
                    Width = 400,
                    Height = 280
                };

                var window = PlayniteUiProvider.CreateExtensionWindow(
                    progressWindow.WindowTitle,
                    progressWindow,
                    windowOptions
                );

                try
                {
                    if (window.Owner == null)
                    {
                        window.Owner = PlayniteApi?.Dialogs?.GetCurrentAppWindow();
                    }
                }
                catch { }

                // Wire up close button from UserControl
                progressWindow.RequestClose += (s, ev) => window.Close();

                window.Closed += (s, ev) => { };

                var isFullscreen = false;
                try
                {
                    isFullscreen = PlayniteApi?.ApplicationInfo?.Mode == ApplicationMode.Fullscreen;
                }
                catch { }

                // Start the scan task after setting up window
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

                if (isFullscreen)
                {
                    window.Show();
                    try
                    {
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
                _logger.Error(ex, "Failed to show scan progress window");
            }
        }

        public override Control GetGameViewControl(GetGameViewControlArgs args)
        {
            // SuccessStory-compatible controls (legacy naming; properties are always populated).
            if (LegacyControlFactories.TryGetValue(args.Name, out var successStoryFactory))
            {
                return successStoryFactory();
            }

            // Desktop PlayniteAchievements controls (always available)
            if (DesktopControlFactories.TryGetValue(args.Name, out var desktopFactory))
            {
                return desktopFactory();
            }

            return null;
        }

        // === End Theme Integration ===

        /// <summary>
        /// Opens the per-game achievements view window for the specified game.
        /// Public for access from theme integration controls.
        /// </summary>
        public void OpenSingleGameAchievementsView(Guid gameId)
        {
            try
            {
                EnsureWpfFallbackResources();

                var view = new SingleGameControl(
                    gameId,
                    _achievementManager,
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
                    app.Dispatcher.BeginInvoke((Action)Ensure, DispatcherPriority.Background);
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
            if (e == null)
            {
                return;
            }

            var addedItems = e.AddedItems;
            if (addedItems != null)
            {
                var addedGameIds = new List<Guid>();
                foreach (var game in addedItems)
                {
                    if (game == null)
                    {
                        continue;
                    }

                    addedGameIds.Add(game.Id);
                }

                if (addedGameIds.Count > 0)
                {
                    _ = TriggerNewGamesScanAsync(addedGameIds);
                }
            }

            var removedItems = e.RemovedItems;
            if (removedItems != null)
            {
                foreach (var game in removedItems)
                {
                    if (game == null)
                    {
                        continue;
                    }

                    _ = TriggerRemovedGameCleanupAsync(game);
                }
            }
        }

        private Task TriggerNewGamesScanAsync(List<Guid> gameIds)
        {
            return Task.Run(async () =>
            {
                try
                {
                    var validGameIds = gameIds?
                        .Where(id => id != Guid.Empty)
                        .Distinct()
                        .ToList() ?? new List<Guid>();

                    if (validGameIds.Count == 0)
                    {
                        return;
                    }

                    _logger.Info($"Detected {validGameIds.Count} new game(s); starting batched scan.");
                    await _achievementManager.ExecuteScanForGamesAsync(validGameIds).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed batched auto-scan for newly added games.");
                }
            });
        }

        private Task TriggerRemovedGameCleanupAsync(Game game)
        {
            return Task.Run(() =>
            {
                try
                {
                    _logger.Info($"Detected removed game '{game?.Name}' ({game?.GameId}); removing cached achievements and icons.");
                    _achievementManager.RemoveGameCache(game.Id);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Failed cleanup for removed game '{game?.Name}' ({game?.GameId}).");
                }
            });
        }
    }
}
