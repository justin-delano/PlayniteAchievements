using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Playnite.SDK;
using PlayniteAchievements.Services;
using PlayniteAchievements.ViewModels;

namespace PlayniteAchievements.Views.Helpers
{
    /// <summary>
    /// Builds the per-game right-click context menu shared by the Overview window and the
    /// View Achievements window. Parameterized by the host's commands and services so neither
    /// control depends on the other.
    /// </summary>
    internal static class GameRowContextMenuBuilder
    {
        /// <summary>
        /// Builds the game-level context menu (Refresh, Open in Library, optional Manage Achievements,
        /// Clear Data, Exclude from Summaries/Refreshes). The Manage Achievements item is omitted when
        /// <paramref name="openManageAchievements"/> is null (e.g. when already inside that window).
        /// </summary>
        public static ContextMenu BuildGameMenu(
            object data,
            FrameworkElement resourceOwner,
            ICommand refreshGameCommand,
            ICommand openGameInLibraryCommand,
            Action<Guid> openManageAchievements,
            IPlayniteAPI playniteApi,
            AchievementOverridesService overridesService,
            ICacheManager cacheManager,
            ILogger logger)
        {
            var menu = new ContextMenu();
            menu.Items.Add(CreateMenuItem(resourceOwner, "LOCPlayAch_Menu_RefreshGame",
                () => ExecuteCommand(refreshGameCommand, data)));
            menu.Items.Add(CreateMenuItem(resourceOwner, "LOCPlayAch_Menu_OpenGameInLibrary",
                () => ExecuteCommand(openGameInLibraryCommand, data)));

            if (openManageAchievements != null)
            {
                menu.Items.Add(CreateMenuItem(resourceOwner, "LOCPlayAch_Menu_ManageAchievements", () =>
                {
                    if (TryGetGameId(data, out var gameId))
                    {
                        openManageAchievements(gameId);
                    }
                }));
            }

            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem(resourceOwner, "LOCPlayAch_Menu_ClearData",
                () => ClearGameData(data, playniteApi, overridesService, cacheManager, logger)));
            menu.Items.Add(CreateMenuItem(resourceOwner, "LOCPlayAch_Common_Action_ExcludeFromSummaries",
                () => ExcludeGameFromSummaries(data, overridesService)));
            menu.Items.Add(CreateMenuItem(resourceOwner, "LOCPlayAch_Menu_ExcludeFromRefreshes",
                () => ExcludeGameFromRefreshes(data, playniteApi, overridesService, clearDataWhenExcluding: false)));
            menu.Items.Add(CreateMenuItem(resourceOwner, "LOCPlayAch_Menu_ExcludeFromRefreshesAndClearData",
                () => ExcludeGameFromRefreshes(data, playniteApi, overridesService, clearDataWhenExcluding: true)));

            return menu;
        }

        public static MenuItem CreateMenuItem(FrameworkElement resourceOwner, string resourceKey, Action onClick)
        {
            var text = resourceOwner?.TryFindResource(resourceKey) as string
                ?? ResourceProvider.GetString(resourceKey)
                ?? resourceKey;
            var item = new MenuItem { Header = text };
            item.Click += (_, __) => onClick?.Invoke();
            return item;
        }

        public static void ExecuteCommand(ICommand command, object parameter)
        {
            if (command != null && command.CanExecute(parameter))
            {
                command.Execute(parameter);
            }
        }

        public static bool TryGetGameId(object data, out Guid gameId)
        {
            switch (data)
            {
                case GameSummaryItem game when game.PlayniteGameId.HasValue:
                    gameId = game.PlayniteGameId.Value; return true;
                case AchievementDisplayItem ach when ach.PlayniteGameId.HasValue:
                    gameId = ach.PlayniteGameId.Value; return true;
                case RecentAchievementItem recent when recent.PlayniteGameId.HasValue:
                    gameId = recent.PlayniteGameId.Value; return true;
                case Guid id when id != Guid.Empty:
                    gameId = id; return true;
                default:
                    gameId = Guid.Empty; return false;
            }
        }

        private static void ClearGameData(
            object data,
            IPlayniteAPI playniteApi,
            AchievementOverridesService overridesService,
            ICacheManager cacheManager,
            ILogger logger)
        {
            if (!TryGetGameId(data, out var gameId))
            {
                return;
            }

            var game = playniteApi?.Database?.Games?.Get(gameId);
            if (game == null)
            {
                return;
            }

            var result = playniteApi?.Dialogs?.ShowMessage(
                string.Format(ResourceProvider.GetString("LOCPlayAch_Menu_ClearData_ConfirmSingle"), game.Name),
                ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning) ?? MessageBoxResult.None;

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                if (overridesService != null)
                {
                    overridesService.ClearGameData(game.Id, game.Name);
                }
                else
                {
                    cacheManager?.RemoveGameCache(game.Id);
                }

                playniteApi?.Dialogs?.ShowMessage(
                    ResourceProvider.GetString("LOCPlayAch_Status_Succeeded"),
                    ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                logger?.Error(ex, $"Failed to clear data for game '{game.Name}' ({game.Id}).");
                playniteApi?.Dialogs?.ShowMessage(
                    string.Format(ResourceProvider.GetString("LOCPlayAch_Status_Failed"), ex.Message),
                    ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void ExcludeGameFromSummaries(object data, AchievementOverridesService overridesService)
        {
            if (!TryGetGameId(data, out var gameId))
            {
                return;
            }

            overridesService?.SetExcludedFromSummaries(gameId, true);
        }

        private static void ExcludeGameFromRefreshes(
            object data,
            IPlayniteAPI playniteApi,
            AchievementOverridesService overridesService,
            bool clearDataWhenExcluding)
        {
            if (!TryGetGameId(data, out var gameId))
            {
                return;
            }

            var game = playniteApi?.Database?.Games?.Get(gameId);
            if (game == null)
            {
                return;
            }

            if (clearDataWhenExcluding)
            {
                var result = playniteApi?.Dialogs?.ShowMessage(
                    string.Format(ResourceProvider.GetString("LOCPlayAch_Menu_Exclude_ConfirmSingle"), game.Name),
                    ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) ?? MessageBoxResult.None;

                if (result != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            overridesService?.SetExcludedByUser(
                gameId,
                excluded: true,
                clearCachedDataWhenExcluding: clearDataWhenExcluding);
        }
    }
}
