using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Playnite.SDK.Models;
using PlayniteAchievements.Models;
using Playnite.SDK;
using PlayniteAchievements.Services;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.StartPage;
using PlayniteAchievements.Services.Showcase;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.ViewModels.Items;
using PlayniteAchievements.ViewModels.StartPage;
using PlayniteAchievements.Views.Helpers;
using PlayniteAchievements.Views.StartPage;
using PlayniteAchievements.Views.Showcase;
using StartPage.SDK;

namespace PlayniteAchievements
{
    public partial class PlayniteAchievementsPlugin : IStartPageExtension
    {
        private readonly Dictionary<string, IDisposable> _startPageViewModels =
            new Dictionary<string, IDisposable>(StringComparer.Ordinal);
        private StartPageDataCoordinator _startPageDataCoordinator;

        public StartPageExtensionArgs GetAvailableStartPageViews()
        {
            EnsureAchievementResourcesLoaded();

            return new StartPageExtensionArgs
            {
                ExtensionName = L("LOCPlayAch_Title_PluginName"),
                Views = StartPageViewCatalog.Views
                    .Select(view => new StartPageViewArgsBase
                    {
                        ViewId = view.ViewId,
                        Name = L(view.NameKey, view.ViewId),
                        Description = string.Empty,
                        HasSettings = view.HasSettings,
                        AllowMultipleInstances = view.AllowMultipleInstances
                    }).ToList()
            };
        }

        public object GetStartPageView(string viewId, Guid instanceId)
        {
            if (!StartPageViewCatalog.TryGetDefinition(viewId, out var definition))
            {
                return null;
            }

            EnsureAchievementResourcesLoaded();

            var sharedSettings = definition.ShowcaseWidgetKind.HasValue
                ? GetOrCreateStartPageWidgetSettings(
                    viewId,
                    instanceId,
                    definition.ShowcaseWidgetKind.Value)
                : null;
            var viewModel = CreateStartPageViewModel(definition, sharedSettings);
            if (viewModel == null)
            {
                return null;
            }

            var view = CreateStartPageView(definition);
            Common.FormattingCulture.Apply(view);
            view.DataContext = viewModel;

            var key = GetStartPageInstanceKey(viewId, instanceId);
            DisposeStartPageViewModel(key);
            _startPageViewModels[key] = viewModel;
            if (viewModel is IStartPageControl startPageControl)
            {
                startPageControl.OnStartPageOpened();
            }

            return view;
        }

        public Control GetStartPageViewSettings(string viewId, Guid instanceId)
        {
            if (!StartPageViewCatalog.TryGetDefinition(viewId, out var definition) ||
                !definition.HasSettings ||
                !definition.ShowcaseWidgetKind.HasValue)
            {
                return null;
            }

            var settings = GetOrCreateStartPageWidgetSettings(
                viewId,
                instanceId,
                definition.ShowcaseWidgetKind.Value);
            return new ShowcaseWidgetOptionsControl(
                settings,
                PersistSettingsForUi);
        }

        public void OnViewRemoved(string viewId, Guid instanceId)
        {
            DisposeStartPageViewModel(GetStartPageInstanceKey(viewId, instanceId));
            var persisted = Settings?.Persisted;
            if (persisted?.Showcase?.StartPageInstances?.Remove(GetStartPageInstanceKey(viewId, instanceId)) == true)
            {
                ShowcaseGridSurfaces.PruneOrphaned(persisted.GridOptions, persisted.Showcase);
                PersistSettingsForUi();
                ShowcaseConfigurationEvents.RaiseChanged();
            }
        }

        internal ContextMenu BuildStartPageRowContextMenu(
            object data,
            FrameworkElement resourceOwner,
            Action onChanged)
        {
            var menu = BuildStartPageBaseRowContextMenu(data, resourceOwner);
            AchievementRowOptionsMenuBuilder.AppendAchievementOptions(
                menu,
                data,
                resourceOwner,
                onChanged);

            return menu.Items.Count > 0 ? menu : null;
        }

        private IDisposable CreateStartPageViewModel(
            StartPageViewDefinition definition,
            ShowcaseWidgetInstanceSettings sharedSettings)
        {
            var widgetKind = definition.WidgetKind;
            if (definition.ShowcaseWidgetKind.HasValue)
            {
                return new StartPageShowcaseWidgetViewModel(
                    sharedSettings,
                    GetStartPageDataCoordinator(),
                    Settings,
                    _logger);
            }

            switch (widgetKind)
            {
                case StartPageWidgetKind.CollectionScoreCard:
                case StartPageWidgetKind.PrestigeScoreCard:
                    return new StartPageScoreCardWidgetViewModel(widgetKind, GetStartPageDataCoordinator(), Settings, _logger);
                default:
                    return null;
            }
        }

        private static Control CreateStartPageView(StartPageViewDefinition definition)
        {
            if (definition.ShowcaseWidgetKind.HasValue)
            {
                return new StartPageShowcaseWidgetView();
            }

            var widgetKind = definition.WidgetKind;
            switch (widgetKind)
            {
                case StartPageWidgetKind.CollectionScoreCard:
                case StartPageWidgetKind.PrestigeScoreCard:
                    return new StartPageScoreCardWidgetView();
                default:
                    return null;
            }
        }

        private StartPageDataCoordinator GetStartPageDataCoordinator()
        {
            if (_startPageDataCoordinator == null)
            {
                _startPageDataCoordinator = new StartPageDataCoordinator(
                    _achievementDataService,
                    _libraryProjectionService,
                    Providers,
                    PlayniteApi,
                    _logger,
                    Settings,
                    () => FriendCacheManager?.LoadCurrentUserIdentities());
            }

            return _startPageDataCoordinator;
        }

        private void InvalidateStartPageData()
        {
            _startPageDataCoordinator?.Invalidate();
        }

        internal void InvalidateStartPageDataForUi()
        {
            InvalidateStartPageData();
        }

        private void DisposeStartPageViews()
        {
            foreach (var viewModel in _startPageViewModels.Values.ToList())
            {
                try
                {
                    viewModel?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger?.Debug(ex, "Failed to dispose StartPage widget view model.");
                }
            }

            _startPageViewModels.Clear();

            try
            {
                _startPageDataCoordinator?.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Failed to dispose StartPage data coordinator.");
            }

            _startPageDataCoordinator = null;
        }

        private void DisposeStartPageViewModel(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (!_startPageViewModels.TryGetValue(key, out var viewModel))
            {
                return;
            }

            _startPageViewModels.Remove(key);
            try
            {
                viewModel?.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Failed to dispose StartPage widget view model.");
            }
        }

        private ContextMenu BuildStartPageBaseRowContextMenu(object data, FrameworkElement resourceOwner)
        {
            var menu = new ContextMenu();
            if (!TryGetStartPageGameId(data, out var gameId))
            {
                return menu;
            }

            if (data is GameSummaryItem)
            {
                AddStartPageGameRowMenuItems(
                    menu,
                    gameId,
                    resourceOwner,
                    includeShowcasePin: !(data is FriendGameSummaryItem));
                return menu;
            }

            if (data is FriendAchievementDisplayItem)
            {
                if (Settings.Persisted.EnableFriendsFeatures)
                {
                    menu.Items.Add(CreateStartPageMenuItem(resourceOwner, "LOCPlayAch_Menu_ViewFriendsAchievements",
                        () => OpenViewFriendsAchievementsWindow(gameId)));
                }
                menu.Items.Add(CreateStartPageMenuItem(resourceOwner, "LOCPlayAch_Menu_ViewAchievements",
                    () => OpenViewAchievementsWindow(gameId)));
                menu.Items.Add(CreateStartPageOpenMenu(resourceOwner, gameId));
                return menu;
            }

            if (data is AchievementDisplayItem || data is RecentAchievementItem)
            {
                menu.Items.Add(CreateStartPageMenuItem(resourceOwner, "LOCPlayAch_Menu_ViewAchievements",
                    () => OpenViewAchievementsWindow(gameId)));
                menu.Items.Add(CreateStartPageOpenMenu(resourceOwner, gameId));
            }

            return menu;
        }

        private void AddStartPageGameRowMenuItems(
            ContextMenu menu,
            Guid gameId,
            FrameworkElement resourceOwner,
            bool includeShowcasePin)
        {
            menu.Items.Add(CreateStartPageMenuItem(resourceOwner, "LOCPlayAch_Menu_ViewAchievements",
                () => OpenViewAchievementsWindow(gameId)));

            if (!IsRefreshInProgress())
            {
                menu.Items.Add(CreateStartPageMenuItem(resourceOwner, "LOCPlayAch_Menu_RefreshGame",
                    () => _ = StartMenuRefreshAsync(
                        new RefreshRequest
                        {
                            Mode = RefreshModeType.Single,
                            SingleGameId = gameId,
                            SurfaceUserNotices = true
                        },
                        gameId)));
            }

            menu.Items.Add(CreateStartPageOpenMenu(resourceOwner, gameId));
            menu.Items.Add(CreateStartPageMenuItem(resourceOwner, "LOCPlayAch_Menu_ManageAchievements",
                () => OpenManageAchievementsView(gameId)));

            if (includeShowcasePin)
            {
                ShowcasePinMenuBuilder.AppendGameMenu(menu, resourceOwner, gameId);
            }

            menu.Items.Add(new Separator());

            var game = PlayniteApi?.Database?.Games?.Get(gameId);
            if (game != null)
            {
                menu.Items.Add(CreateStartPageMenuItem(resourceOwner, "LOCPlayAch_Menu_ClearData",
                    () => ClearSingleGameData(game)));

                var excludedFromSummaries = IsGameExcludedFromSummaries(gameId);
                menu.Items.Add(CreateStartPageMenuItem(
                    resourceOwner,
                    excludedFromSummaries
                        ? "LOCPlayAch_Common_Action_IncludeInSummaries"
                        : "LOCPlayAch_Common_Action_ExcludeFromSummaries",
                    () => ToggleExcludedFromSummaries(new[] { game })));

                var excludedFromRefreshes = IsGameExcluded(gameId);
                menu.Items.Add(CreateStartPageMenuItem(
                    resourceOwner,
                    excludedFromRefreshes
                        ? "LOCPlayAch_Menu_IncludeInRefreshes"
                        : "LOCPlayAch_Menu_ExcludeFromRefreshes",
                    () => ToggleExcludedFromRefreshes(
                        new[] { game },
                        clearDataWhenExcluding: false,
                        confirmWhenClearingData: false)));

                menu.Items.Add(CreateStartPageMenuItem(
                    resourceOwner,
                    excludedFromRefreshes
                        ? "LOCPlayAch_Menu_IncludeInRefreshesAndRefresh"
                        : "LOCPlayAch_Menu_ExcludeFromRefreshesAndClearData",
                    () => ToggleExcludedFromRefreshesAndRefresh(new[] { game })));
            }
        }

        private MenuItem CreateStartPageOpenMenu(FrameworkElement resourceOwner, Guid gameId)
        {
            return GameRowContextMenuBuilder.CreateOpenMenu(
                resourceOwner,
                gameId,
                () => OpenStartPageGameInLibrary(gameId),
                PlayniteApi,
                _logger);
        }

        private static MenuItem CreateStartPageMenuItem(
            FrameworkElement resourceOwner,
            string resourceKey,
            Action onClick)
        {
            var item = new MenuItem
            {
                Header = resourceOwner?.TryFindResource(resourceKey) as string
                         ?? ResourceProvider.GetString(resourceKey)
                         ?? resourceKey
            };
            item.Click += (_, __) => onClick?.Invoke();
            return item;
        }

        private void OpenStartPageGameInLibrary(Guid gameId)
        {
            if (gameId == Guid.Empty)
            {
                return;
            }

            try
            {
                PlayniteUiProvider.RestoreMainView();
                PlayniteApi?.MainView?.SelectGame(gameId);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to open game in Playnite library: {gameId}");
            }
        }

        private static bool TryGetStartPageGameId(object data, out Guid gameId)
        {
            switch (data)
            {
                case GameSummaryItem game when game.PlayniteGameId.HasValue:
                    gameId = game.PlayniteGameId.Value;
                    return true;
                case AchievementDisplayItem achievement when achievement.PlayniteGameId.HasValue:
                    gameId = achievement.PlayniteGameId.Value;
                    return true;
                case RecentAchievementItem recent when recent.PlayniteGameId.HasValue:
                    gameId = recent.PlayniteGameId.Value;
                    return true;
                case Guid id when id != Guid.Empty:
                    gameId = id;
                    return true;
                default:
                    gameId = Guid.Empty;
                    return false;
            }
        }

        private static string GetStartPageInstanceKey(string viewId, Guid instanceId)
        {
            return $"{viewId}:{instanceId:N}";
        }

        private ShowcaseWidgetInstanceSettings GetOrCreateStartPageWidgetSettings(
            string viewId,
            Guid instanceId,
            ShowcaseWidgetKind kind)
        {
            var showcase = Settings.Persisted.Showcase;
            var key = GetStartPageInstanceKey(viewId, instanceId);
            if (showcase.StartPageInstances.TryGetValue(key, out var existing) &&
                existing != null &&
                existing.Kind == kind)
            {
                return existing;
            }

            var settings = ShowcaseWidgetSettingsFactory.CreateDefault(
                kind,
                instanceId.ToString("N"));

            if (kind == ShowcaseWidgetKind.PinnedAchievements ||
                kind == ShowcaseWidgetKind.IconMosaic)
            {
                ShowcaseWidgetOptions.SetPinCollectionId(
                    settings,
                    showcase.DefaultAchievementPinCollectionId);
            }
            else if (kind == ShowcaseWidgetKind.FavoriteGames ||
                     kind == ShowcaseWidgetKind.GameMosaic)
            {
                ShowcaseWidgetOptions.SetPinCollectionId(
                    settings,
                    showcase.DefaultGamePinCollectionId);
            }

            // The four pie views share the showcase Pie kind; each seeds the distribution
            // its view id has always shown.
            if (kind == ShowcaseWidgetKind.Pie)
            {
                ShowcaseWidgetOptions.SetPieMode(settings, ResolvePieModeForView(viewId));
            }

            // Migration: StartPage-hosted grid widgets used to share the fixed StartPage
            // surfaces edited on the Display tab. Seed each new per-instance surface from
            // the matching fixed surface so already-placed widgets keep their configured
            // display options and column layout.
            var catalog = Settings.Persisted.GridOptions;
            var surfaceKey = ShowcaseGridSurfaces.ResolveWidgetSurface(kind, settings.InstanceId);
            if (kind == ShowcaseWidgetKind.RecentAchievements)
            {
                catalog.SeedAchievementFrom(surfaceKey, GridOptionKeys.Achievement.StartPageRecent);
            }
            else if (kind == ShowcaseWidgetKind.GameSummaries)
            {
                catalog.SeedGameSummariesFrom(surfaceKey, GridOptionKeys.GameSummaries.StartPage);
            }

            showcase.StartPageInstances[key] = settings;
            PersistSettingsForUi();
            return settings;
        }

        private static ShowcasePieMode ResolvePieModeForView(string viewId)
        {
            switch (viewId)
            {
                case StartPageViewCatalog.ProviderPieViewId:
                    return ShowcasePieMode.Provider;
                case StartPageViewCatalog.RarityPieViewId:
                    return ShowcasePieMode.Rarity;
                case StartPageViewCatalog.TrophyPieViewId:
                    return ShowcasePieMode.Trophy;
                default:
                    return ShowcasePieMode.CompletedGames;
            }
        }

        private static string L(string key)
        {
            return ResourceProvider.GetString(key);
        }

        private static string L(string key, string fallback)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return fallback;
            }

            var value = ResourceProvider.GetString(key);
            return string.IsNullOrWhiteSpace(value) || string.Equals(value, key, StringComparison.Ordinal)
                ? fallback
                : value;
        }
    }
}
