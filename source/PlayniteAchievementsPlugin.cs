using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Models.Tagging;
using PlayniteAchievements.Models.ThemeIntegration;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.Providers;
using PlayniteAchievements.Services;
using PlayniteAchievements.Providers.Manual;
using PlayniteAchievements.Views;
using PlayniteAchievements.Views.Helpers;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using PlayniteAchievements.Common;
using PlayniteAchievements.Services.Images;
using PlayniteAchievements.Services.Friends;
using PlayniteAchievements.Services.Logging;
using PlayniteAchievements.Services.ThemeIntegration;
using PlayniteAchievements.Services.ThemeMigration;
using PlayniteAchievements.Services.Tagging;
using PlayniteAchievements.Services.UI;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Shell;
using System.Windows.Threading;
using LiveCharts;
using LiveCharts.Configurations;
using LiveCharts.Wpf;

namespace PlayniteAchievements
{
    public partial class PlayniteAchievementsPlugin : GenericPlugin
    {
        private readonly ILogger _logger;


        // Set Properties before constructor runs
        private static readonly GenericPluginProperties _pluginProperties = new GenericPluginProperties
        {
            HasSettings = true
        };

        private static readonly string[] ProviderDisplayOrder =
        {
            "Steam", "Epic", "GOG", "BattleNet", "EA", "Ubisoft", "PSN", "Xbox", "GooglePlay", "Apple", "FFXIV", "RetroAchievements", "RPCS3", "ShadPS4", "Xenia", "Manual", "Exophase", "Hoyoverse"
        };

        private static readonly string[] ProviderRefreshOrder =
        {
            "Manual", "FFXIV", "Exophase", "Steam", "Epic", "GOG", "BattleNet", "EA", "Hoyoverse", "RPCS3", "ShadPS4", "PSN", "Xenia", "Xbox", "RetroAchievements"
        };

        private readonly PlayniteAchievementsSettingsViewModel _settingsViewModel;
        private readonly RefreshRuntime _refreshService;
        private readonly AchievementOverridesService _achievementOverridesService;
        private readonly AchievementDataService _achievementDataService;
        private readonly ICacheManager _cacheManager;
        private readonly MemoryImageService _imageService;
        private readonly DiskImageService _diskImageService;
        private readonly ManagedCustomIconService _managedCustomIconService;
        private readonly NotificationPublisher _notifications;
        private readonly ProviderRegistry _providerRegistry;
        private readonly GameCustomDataStore _gameCustomDataStore;
        private readonly ManualSourceRegistry _manualSourceRegistry;
        private readonly SubscriptionCollection _eventSubscriptions = new SubscriptionCollection();

        private readonly BackgroundUpdater _backgroundUpdates;
        private readonly RefreshEntryPoint _refreshCoordinator;
        private bool _applicationStarted;

        // Top panel item
        private PlayniteAchievementsTopPanelItem _topPanelItem;

        // Theme integration
        private readonly FullscreenWindowService _fullscreenWindowService;
        private readonly ThemeIntegrationService _themeIntegrationService;
        private readonly ThemeControlRegistry _themeControlRegistry;
        private readonly AchievementResourceService _resourceService;
        private readonly PluginWindowService _windowService;
        private readonly AchievementHotkeyTargetResolver _achievementHotkeyTargetResolver;
        private readonly AchievementHotkeyService _achievementHotkeyService;
        private readonly FullscreenControllerNavigationService _fullscreenControllerNavigationService;
        private readonly ThemeAutoMigrationService _themeAutoMigrationService;

        // Tagging
        private readonly object _tagSyncGate = new object();
        private TagSyncService _tagSyncService;

        public override Guid Id { get; } =
            Guid.Parse("e6aad2c9-6e06-4d8d-ac55-ac3b252b5f7b");

        public PlayniteAchievementsSettings Settings => _settingsViewModel.Settings;
        public ProviderRegistry ProviderRegistry => _providerRegistry;
        public GameCustomDataStore GameCustomDataStore => _gameCustomDataStore;
        public IReadOnlyList<IDataProvider> Providers => _refreshService?.Providers;
        public RefreshRuntime RefreshRuntime => _refreshService;
        public AchievementOverridesService AchievementOverridesService => _achievementOverridesService;
        public AchievementDataService AchievementDataService => _achievementDataService;
        public MemoryImageService ImageService => _imageService;
        public DiskImageService DiskImageService => _diskImageService;
        public ManagedCustomIconService ManagedCustomIconService => _managedCustomIconService;
        public ThemeIntegrationService ThemeIntegrationService => _themeIntegrationService;
        public ThemeIntegrationService ThemeUpdateService => _themeIntegrationService;
        public TagSyncService TagSyncService => _tagSyncService;
        internal RefreshEntryPoint RefreshEntryPoint => _refreshCoordinator;
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

        private void TryWarmCustomDataCache()
        {
            if (_gameCustomDataStore == null)
            {
                return;
            }

            try
            {
                using (PerfScope.StartStartup(_logger, "PluginCtor.CustomDataWarmup", thresholdMs: 50))
                {
                    var rows = _gameCustomDataStore.LoadAll();
                    _logger?.Debug($"Preloaded {rows?.Count ?? 0} game custom-data rows.");
                }
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "Failed to warm game custom-data cache during plugin startup.");
            }
        }

        public void PersistSettingsForUi()
        {
            try
            {
                _providerRegistry?.PersistAllProviderSettings(false);
                SavePluginSettings(_settingsViewModel.Settings);
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "Failed to persist plugin settings.");
            }

            NotifySettingsSaved();
        }

        // Public bridge method for external helpers/themes that used to target SuccessStory via reflection.
        // AnikiHelper (PlayniteAchievements-based) will call this when available.
        public Task RequestSingleGameRefreshAsync(Guid playniteGameId)
        {
            return _refreshCoordinator?.ExecuteAsync(new RefreshRequest
            {
                Mode = RefreshModeType.Single,
                SingleGameId = playniteGameId
            }) ?? Task.CompletedTask;
        }

        // Invoked by AchievementHotkeyService on F5. Refreshes the plugin view shown in the
        // active/topmost window (the single-game View Achievements window, or the Overview as a
        // standalone window or open sidebar view) regardless of which element holds focus.
        // Returns true when a plugin view handled it, so the service can suppress Playnite's own
        // F5 library update.
        private bool TryRefreshActivePluginView()
        {
            var window = Application.Current?.Windows
                .OfType<Window>()
                .FirstOrDefault(w => w.IsActive)
                ?? Application.Current?.MainWindow;
            if (window == null)
            {
                return false;
            }

            // View Achievements is always its own window; the Overview is either its own window
            // or hosted inside Playnite's main window as the sidebar view.
            var singleGame = VisualTreeHelpers.FindVisualChild<ViewAchievementsControl>(window);
            if (singleGame != null && singleGame.IsVisible)
            {
                singleGame.TriggerHotkeyRefresh();
                return true;
            }

            var overview = VisualTreeHelpers.FindVisualChild<OverviewControl>(window);
            if (overview != null && overview.IsVisible)
            {
                overview.TriggerHotkeyRefresh();
                return true;
            }

            return false;
        }

        public PlayniteAchievementsPlugin(IPlayniteAPI api) : base(api)
        {
            // Initialize logging system first
            PluginLogger.Initialize(GetPluginUserDataPath());
            _logger = PluginLogger.GetLogger(nameof(PlayniteAchievementsPlugin));
            _themeControlRegistry = new ThemeControlRegistry();
            _resourceService = new AchievementResourceService(_logger);

            using (PerfScope.StartStartup(_logger, "PluginCtor.Total", thresholdMs: 50))
            {
                Properties = _pluginProperties;

                Instance = this;
                _logger.Info("PlayniteAchievementsPlugin initializing...");

                // Phase 1: Load settings and chart plumbing used by theme controls.
                using (PerfScope.StartStartup(_logger, "PluginCtor.SettingsLoad", thresholdMs: 50))
                {
                    _settingsViewModel = new PlayniteAchievementsSettingsViewModel(this);
                }

                // NECESSARY TO MAKE SURE CHARTS WORK
                var Circle = LiveCharts.Wpf.DefaultGeometries.Circle;
                var panel = new WpfToolkit.Controls.VirtualizingWrapPanel();
                // NECESSARY DO NOT REMOVE

                // Configure LiveCharts mapper for PieSliceChartData so the tooltip can bind to custom data
                var pieSliceMapper = Mappers.Pie<PieSliceChartData>()
                    .Value(data => data.ChartValue);
                Charting.For<PieSliceChartData>(pieSliceMapper);

                var settings = _settingsViewModel.Settings;
                var pluginUserDataPath = GetPluginUserDataPath();
                _manualSourceRegistry = new ManualSourceRegistry(_logger, settings, PlayniteApi, pluginUserDataPath);

                // Create provider registry
                _providerRegistry = new ProviderRegistry(settings, ProviderDisplayOrder, _logger, _manualSourceRegistry);
                _providerRegistry.SyncFromSettings(settings.Persisted);
                _gameCustomDataStore = _settingsViewModel.GameCustomDataStore;
                _gameCustomDataStore.AttachRuntimeSettings(settings);
                TryWarmCustomDataCache();

                List<IDataProvider> providers;
                using (PerfScope.StartStartup(_logger, "PluginCtor.ProviderCreation", thresholdMs: 50))
                {
                    providers = _providerRegistry.CreateProviders(settings, PlayniteApi, pluginUserDataPath);
                }

                // Phase 3: Wire core services, refresh pipeline, and tagging.
                using (PerfScope.StartStartup(_logger, "PluginCtor.RefreshServiceCreation", thresholdMs: 50))
                {
                    _diskImageService = new DiskImageService(_logger, pluginUserDataPath);
                    _managedCustomIconService = new ManagedCustomIconService(_diskImageService, _logger);
                    _imageService = new MemoryImageService(_logger, _diskImageService);
                    _gameCustomDataStore.AttachManagedCustomIconService(_managedCustomIconService);

                    _refreshService = new RefreshRuntime(api, settings, _logger, this, providers, _diskImageService, _managedCustomIconService, _providerRegistry, ProviderRefreshOrder, onRefreshCompleted: payload => HandleRefreshAuthNotifications(payload));
                    _cacheManager = _refreshService.Cache;
                    _cacheManager.CacheInvalidated += (_, __) =>
                    {
                        try { _imageService?.Clear(); } catch { }
                        InvalidateStartPageData();
                    };
                    _achievementOverridesService = new AchievementOverridesService(
                        _gameCustomDataStore,
                        _cacheManager,
                        _logger);
                    _achievementDataService = new AchievementDataService(
                        _cacheManager,
                        PlayniteApi,
                        _settingsViewModel.Settings,
                        _logger,
                        _gameCustomDataStore);
                    _gameCustomDataStore.AttachAchievementDataService(_achievementDataService);
                    _notifications = new NotificationPublisher(api, settings, _logger);
                    _refreshCoordinator = new RefreshEntryPoint(
                        _refreshService,
                        _logger,
                        runWithProgressWindow: ShowRefreshProgressControlAndRun);
                    _backgroundUpdates = new BackgroundUpdater(_refreshCoordinator, _refreshService, _cacheManager, settings, _logger, _notifications, null);

                    // Create tag sync service
                    _tagSyncService = new TagSyncService(
                        PlayniteApi,
                        _logger,
                        settings.Persisted);
                    _tagSyncService.InitializeAndSubscribeTaggingSettings();

                    _fullscreenControllerNavigationService = new FullscreenControllerNavigationService(
                        PlayniteApi,
                        _logger);

                    _windowService = new PluginWindowService(
                        PlayniteApi,
                        _logger,
                        _refreshService,
                        _refreshCoordinator,
                        _cacheManager,
                        PersistSettingsForUi,
                        _achievementOverridesService,
                        _achievementDataService,
                        _gameCustomDataStore,
                        _settingsViewModel.Settings,
                        _manualSourceRegistry,
                        () => _resourceService.EnsureAchievementResourcesLoaded(_settingsViewModel.Settings),
                        _fullscreenControllerNavigationService);

                    _achievementHotkeyTargetResolver = new AchievementHotkeyTargetResolver(PlayniteApi, _logger);
                    _achievementHotkeyService = new AchievementHotkeyService(
                        PlayniteApi,
                        _settingsViewModel.Settings,
                        _achievementHotkeyTargetResolver,
                        _logger,
                        gameId => _windowService.ToggleViewAchievementsWindowFromHotkey(gameId),
                        gameId => _windowService.ToggleManageAchievementsViewFromHotkey(gameId),
                        ToggleOverviewWindowFromHotkey,
                        TryRefreshActivePluginView);

                    _themeAutoMigrationService = new ThemeAutoMigrationService(
                        _logger,
                        PlayniteApi,
                        _settingsViewModel.Settings,
                        () => SavePluginSettings(_settingsViewModel.Settings),
                        themeName => _notifications?.ShowThemeAutoMigrated(themeName));

                    SubscribePluginEventHandlers();
                }

                // Phase 4: Connect theme runtime services and custom controls.
                using (PerfScope.StartStartup(_logger, "PluginCtor.ThemeServicesWiring", thresholdMs: 50))
                {
                    Action<Guid?> requestUpdate = (id) => _themeIntegrationService?.RequestUpdate(id);

                    _fullscreenWindowService = new FullscreenWindowService(
                        PlayniteApi,
                        _settingsViewModel.Settings,
                        requestUpdate);

                    _themeIntegrationService = new ThemeIntegrationService(
                        PlayniteApi,
                        _refreshService,
                        _achievementDataService,
                        _refreshCoordinator,
                        _settingsViewModel.Settings,
                        _fullscreenWindowService,
                        _logger,
                        _windowService.RunRefreshWithGlobalProgressAsync,
                        gameId => _windowService.OpenManageAchievementsView(gameId, ManageAchievementsTab.Overview));

                    SubscribeDatabaseEventHandlers();

                    AddSettingsSupport(new AddSettingsSupportArgs
                    {
                        SourceName = "PlayniteAchievements",
                        SettingsRoot = "Settings"
                    });

                    AddCustomElementSupport(new AddCustomElementSupportArgs
                    {
                        ElementList = _themeControlRegistry.GetSupportedElementNames(),
                        SourceName = "PlayniteAchievements"
                    });
                }

                // Initialize top panel item for popout window
                _topPanelItem = new PlayniteAchievementsTopPanelItem(
                    OpenOverviewWindow);

                _logger.Info("PlayniteAchievementsPlugin initialized.");
            }
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
                var control = new SettingsControl(
                    _settingsViewModel,
                    _logger,
                    this,
                    _providerRegistry,
                    (owner, currentValue) => _windowService?.PickColor(owner, currentValue));
                _logger.Info("GetSettingsView succeeded");
                return control;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "GetSettingsView failed");
                throw;
            }
        }

        // === Overview ===

        public override IEnumerable<SidebarItem> GetSidebarItems()
        {
            yield return new SidebarItem
            {
                Title = ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                Type = SiderbarItemType.View,
                Icon = GetOverviewIcon(),
                Opened = () =>
                {
                    return new OverviewHostControl(
                        () => new OverviewControl(PlayniteApi, _logger, _refreshService, _cacheManager, PersistSettingsForUi, _achievementOverridesService, _achievementDataService, _gameCustomDataStore, _refreshCoordinator, _settingsViewModel.Settings, OverviewLaunchContext.Sidebar),
                        _logger,
                        PlayniteApi,
                        _refreshService,
                        this);
                }
            };

            yield return new SidebarItem
            {
                Title = ResourceProvider.GetString("LOCPlayAch_Menu_OpenFriendsOverview"),
                Type = SiderbarItemType.View,
                Icon = GetOverviewIcon(),
                Opened = () =>
                {
                    EnsureAchievementResourcesLoaded();
                    return new FriendsOverviewControl(
                        _logger,
                        _cacheManager as IFriendCacheManager,
                        _refreshCoordinator,
                        _refreshService,
                        _settingsViewModel.Settings,
                        PersistSettingsForUi,
                        OverviewLaunchContext.Sidebar,
                        PlayniteApi,
                        _cacheManager,
                        _achievementOverridesService);
                }
            };
        }

        private TextBlock GetOverviewIcon()
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

        // === Top Panel ===

        public override IEnumerable<TopPanelItem> GetTopPanelItems()
        {
            if (_settingsViewModel?.Settings?.Persisted?.ShowTopMenuBarButton == true)
            {
                yield return _topPanelItem;
            }
        }

        public override void OnGameStarted(OnGameStartedEventArgs args)
        {
            try
            {
                _achievementHotkeyTargetResolver?.NotifyGameStarted(args?.Game);
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Failed to track started game for achievement hotkeys.");
            }
        }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            try
            {
                _achievementHotkeyTargetResolver?.NotifyGameStopped(args?.Game);
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Failed to track stopped game for achievement hotkeys.");
            }

            _logger.Info($"Game stopped: {args.Game.Name}. Triggering refresh.");
            _ = _refreshCoordinator.ExecuteAsync(new RefreshRequest
            {
                Mode = RefreshModeType.Single,
                SingleGameId = args.Game.Id
            });
        }

        // === Lifecycle ===

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            using (PerfScope.StartStartup(_logger, "OnApplicationStarted", thresholdMs: 50))
            {
                _applicationStarted = true;

                var dispatcher = PlayniteApi?.MainView?.UIDispatcher
                    ?? System.Windows.Application.Current?.Dispatcher;

                if (dispatcher != null)
                {
                    try
                    {
                        var selectedGames = PlayniteApi?.MainView?.SelectedGames?
                            .Where(g => g != null)
                            .Take(2)
                            .ToList();

                        if (selectedGames?.Count == 1)
                        {
                            var game = selectedGames[0];
                            _logger.Debug($"Populating initial theme data for selected game: {game.Name}");
                            _themeIntegrationService?.PopulateSingleGameDataSync(game.Id);
                            _settingsViewModel.Settings.SetSelectedGame(game);
                            _themeIntegrationService?.RequestUpdate(game.Id);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Failed to populate initial theme data.");
                    }
                }
                else
                {
                    _logger.Warn("Could not obtain UI dispatcher; theme integration updates disabled");
                }

                try
                {
                    EnsureAchievementResourcesLoaded();
                }
                catch
                {
                    // ignore
                }

                _achievementHotkeyService?.Start();

                // Auto-migrate themes that have been updated since the last migration.
                _themeAutoMigrationService?.ScheduleAutoMigration();

                RestartBackgroundUpdater();
            }
        }

        private void PersistedSettings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e == null)
            {
                return;
            }

            if (e.PropertyName == nameof(PersistedSettings.EnablePeriodicUpdates) ||
                e.PropertyName == nameof(PersistedSettings.PeriodicUpdateHours))
            {
                RestartBackgroundUpdater();
            }

            if (e.PropertyName == nameof(PersistedSettings.UseUniformRarityBadges) ||
                e.PropertyName == nameof(PersistedSettings.RarityColors))
            {
                RarityAppearanceHelper.ApplyBadgeApplicationResources(
                    _settingsViewModel?.Settings?.Persisted);
            }

            if (e.PropertyName == nameof(PersistedSettings.EnableAchievementHotkeys) ||
                e.PropertyName == nameof(PersistedSettings.EnableGlobalAchievementHotkeys) ||
                e.PropertyName == nameof(PersistedSettings.ViewAchievementsHotkey) ||
                e.PropertyName == nameof(PersistedSettings.ManageAchievementsHotkey) ||
                e.PropertyName == nameof(PersistedSettings.OverviewHotkey))
            {
                _achievementHotkeyService?.RefreshConfiguration();
            }

            InvalidateStartPageData();
            _tagSyncService?.HandlePersistedSettingsPropertyChanged(e);
        }

        private void RestartBackgroundUpdater()
        {
            try
            {
                _backgroundUpdates?.Stop();
                if (_applicationStarted)
                {
                    _backgroundUpdates?.Start();
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to restart background updater.");
            }
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            _logger.Info("OnApplicationStopped called.");
            _applicationStarted = false;
            // Stop startup init if still running
            try
            {
                _eventSubscriptions.DisposeAll();

            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Error during application shutdown cleanup.");
            }

            _backgroundUpdates.Stop();

            try { _achievementHotkeyService?.Dispose(); } catch (Exception ex) { _logger?.Debug(ex, "Failed to dispose achievementHotkeyService"); }
            try { _windowService?.Dispose(); } catch (Exception ex) { _logger?.Debug(ex, "Failed to dispose windowService"); }
            try { _imageService?.Dispose(); } catch (Exception ex) { _logger?.Debug(ex, "Failed to dispose imageService"); }
            try { _diskImageService?.Dispose(); } catch (Exception ex) { _logger?.Debug(ex, "Failed to dispose diskImageService"); }
            try { _manualSourceRegistry?.Dispose(); } catch (Exception ex) { _logger?.Debug(ex, "Failed to dispose manualSourceRegistry"); }
            try { _fullscreenControllerNavigationService?.Dispose(); } catch (Exception ex) { _logger?.Debug(ex, "Failed to dispose fullscreenControllerNavigationService"); }
            try { _fullscreenWindowService?.Dispose(); } catch (Exception ex) { _logger?.Debug(ex, "Failed to dispose fullscreenWindowService"); }
            try { _themeIntegrationService?.Dispose(); } catch (Exception ex) { _logger?.Debug(ex, "Failed to dispose themeIntegrationService"); }
            DisposeStartPageViews();

            // Shutdown logging system
            try { PluginLogger.Shutdown(); } catch (Exception ex) { System.Diagnostics.Trace.TraceError($"Failed to shutdown logger: {ex}"); }
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
                        _themeIntegrationService?.RequestUpdate(null);
                        _themeIntegrationService?.NotifySelectionChanged(null);
                        _themeIntegrationService?.ClearSingleGameThemeProperties();
                        _settingsViewModel.Settings.SetSelectedGame(null);
                        return;
                    }

                    _themeIntegrationService?.NotifySelectionChanged(game.Id);
                    // Populate cached single-game data immediately, then let the async pass reconcile if needed.
                    _themeIntegrationService?.PopulateSingleGameDataSync(game.Id);
                    _settingsViewModel.Settings.SetSelectedGame(game);
                    _themeIntegrationService?.RequestUpdate(game.Id);
                }
                else
                {
                    // Clear theme data when no game or multiple games selected
                    _themeIntegrationService?.RequestUpdate(null);
                    _themeIntegrationService?.NotifySelectionChanged(null);
                    _themeIntegrationService?.ClearSingleGameThemeProperties();
                    _settingsViewModel.Settings.SetSelectedGame(null);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in OnGameSelected");
            }
        }

        public override void OnControllerButtonStateChanged(OnControllerButtonStateChangedArgs args)
        {
            try
            {
                if (_fullscreenControllerNavigationService?.TryHandleControllerButtonStateChanged(args) == true)
                {
                    return;
                }

                base.OnControllerButtonStateChanged(args);

                if (args?.Button == ControllerInput.B && args.State == ControllerInputState.Pressed)
                {
                    _fullscreenWindowService?.HandleControllerBackPressed();
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Failed to handle controller button state change for fullscreen overlay.");
            }
        }

        internal void RequestThemeUpdate(Game gameContext)
        {
            _themeIntegrationService?.RequestUpdate(gameContext?.Id);
        }

        private void SubscribeDatabaseEventHandlers()
        {
            // Listen for game database changes to auto-refresh new entries and clean up removed games.
            PlayniteApi?.Database?.Games?.ItemCollectionChanged += Games_ItemCollectionChanged;
            _eventSubscriptions.Add(() => PlayniteApi?.Database?.Games?.ItemCollectionChanged -= Games_ItemCollectionChanged);
        }

        private void SubscribePluginEventHandlers()
        {
            _refreshService.GameRefreshed += OnAchievementGameRefreshed;
            _eventSubscriptions.Add(() => _refreshService.GameRefreshed -= OnAchievementGameRefreshed);

            try
            {
                if (_gameCustomDataStore != null)
                {
                    _gameCustomDataStore.CustomDataChanged += GameCustomDataStore_CustomDataChanged;
                    _eventSubscriptions.Add(() => _gameCustomDataStore.CustomDataChanged -= GameCustomDataStore_CustomDataChanged);
                }

                var persisted = _settingsViewModel?.Settings?.Persisted;
                if (persisted != null)
                {
                    persisted.PropertyChanged += PersistedSettings_PropertyChanged;
                    _eventSubscriptions.Add(() => persisted.PropertyChanged -= PersistedSettings_PropertyChanged);
                }

                _eventSubscriptions.Add(() => _tagSyncService?.DetachTaggingSettingsSubscription());
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Failed to subscribe to persisted settings changes.");
            }
        }

        private void GameCustomDataStore_CustomDataChanged(object sender, GameCustomDataChangedEventArgs e)
        {
            if (e?.PlayniteGameId == Guid.Empty)
            {
                return;
            }

            var persisted = _settingsViewModel?.Settings?.Persisted;
            if (_tagSyncService != null && persisted?.TaggingSettings?.EnableTagging == true)
            {
                QueueTagSync(e.PlayniteGameId);
            }

            try
            {
                _themeIntegrationService?.NotifyCustomDataChanged(e.PlayniteGameId);
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"Failed to refresh theme state after custom-data change for gameId={e.PlayniteGameId}.");
            }

            InvalidateStartPageData();
        }

        private void QueueTagSync(Guid gameId)
        {
            if (gameId == Guid.Empty)
            {
                return;
            }

            var tagSyncService = _tagSyncService;
            if (tagSyncService == null)
            {
                return;
            }

            _ = Task.Run(() =>
            {
                try
                {
                    lock (_tagSyncGate)
                    {
                        tagSyncService.SyncTagsForGames(new List<Guid> { gameId });
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Debug(ex, $"Failed to sync tags after custom-data change for gameId={gameId}.");
                }
            });
        }

        private void OnAchievementGameRefreshed(Guid gameId)
        {
            var persisted = _settingsViewModel?.Settings?.Persisted;
            if (_tagSyncService != null && persisted?.TaggingSettings?.EnableTagging == true)
            {
                _tagSyncService.SyncTagsForGames(new List<Guid> { gameId });
            }

            InvalidateStartPageData();
        }

        private void HandleRefreshAuthNotifications(RebuildPayload payload)
        {
            if (payload == null)
                return;

            if (payload.FailedProviderKeys?.Count > 0)
            {
                _notifications?.ShowProviderAuthFailed(payload.FailedProviderKeys);
            }
            else if (!payload.AuthRequired)
            {
                _notifications?.ClearAllProviderAuthNotifications();
            }
        }

        public override Control GetGameViewControl(GetGameViewControlArgs args)
        {
            EnsureAchievementResourcesLoaded();
            return _themeControlRegistry.TryCreate(args.Name, out var control) ? control : null;
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
                    _ = TriggerNewGamesRefreshAsync(addedGameIds);
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

        private Task TriggerNewGamesRefreshAsync(List<Guid> gameIds)
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

                    _logger.Info($"Detected {validGameIds.Count} new game(s); starting batched refresh.");
                    await _refreshCoordinator.ExecuteAsync(new RefreshRequest { GameIds = validGameIds }).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed batched auto-refresh for newly added games.");
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
                    _cacheManager.RemoveGameCache(game.Id);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Failed cleanup for removed game '{game?.Name}' ({game?.GameId}).");
                }
            });
        }
    }
}





