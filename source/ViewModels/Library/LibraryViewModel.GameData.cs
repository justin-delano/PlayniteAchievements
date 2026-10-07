using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PlayniteAchievements.Services.Library;
using PlayniteAchievements.Services.Workshop;
using PlayniteAchievements.ViewModels.Workshop;

namespace PlayniteAchievements.ViewModels.Library
{
    /// <summary>
    /// Workshop game data on the Library page: one row per Workshop item, built from the games'
    /// records, used in each game that has it; update merges a newer version into the games behind
    /// it, reset applies a game's kept package again, and unlink or delete end the records.
    /// </summary>
    public sealed partial class LibraryViewModel
    {
        /// <summary>The game data row of the game the page was opened for, once; null when there is none.</summary>
        private LibraryItemRow TakeFocusedRow(IEnumerable<LibraryItemRow> rows)
        {
            if (!(_focusGameId is Guid gameId))
            {
                return null;
            }

            _focusGameId = null;
            var key = LibraryTargetKeys.GameData(gameId);
            return rows.FirstOrDefault(row => row.IsGameData
                                              && row.Uses.Any(use => string.Equals(use.TargetKey, key, StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>
        /// Works out which games' data was edited since its Workshop apply. That reads each game's
        /// data and icons from disk, so it runs off the UI thread; the rows are rebuilt when the
        /// answer changed, and a newer pass discards an older one's result.
        /// </summary>
        private async Task RefreshEditedGameDataAsync()
        {
            var generation = ++_editedGeneration;
            var service = _plugin.GameDataLinks;
            List<Guid> edited;
            try
            {
                edited = await Task.Run(() => service.All
                    .Where(pair => service.IsEdited(pair.Key, pair.Value))
                    .Select(pair => pair.Key)
                    .ToList());
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "Failed telling which games' Workshop game data was edited.");
                return;
            }

            if (generation != _editedGeneration || _lifetime.IsCancellationRequested || _editedGameData.SetEquals(edited))
            {
                return;
            }

            _editedGameData.Clear();
            _editedGameData.UnionWith(edited);
            Reload(reconcile: false);
        }

        /// <summary>
        /// The row of one Workshop item's game data: named and described by the Workshop once the
        /// index is read, used in each game that has it.
        /// </summary>
        private LibraryItemRow BuildGameDataRow(GameDataItemGroup group)
        {
            var item = new LibraryItem
            {
                Id = LibraryItem.WorkshopId(group.WorkshopItemId),
                Kind = LibraryItemKind.GameData,
                Name = group.Name,
                Origin = LibraryItemOrigin.Workshop,
                WorkshopItemId = group.WorkshopItemId,
                Version = group.HighestVersion
            };

            WorkshopItem indexItem = null;
            _index?.TryGetValue(group.WorkshopItemId, out indexItem);
            var row = new LibraryItemRow(item, null, null, null) { IndexItem = indexItem };
            row.Uses = group.Games
                .Select(pair =>
                {
                    var use = new LibraryTargetUse(
                        LibraryTargetKeys.GameData(pair.Key),
                        pair.Value,
                        _editedGameData.Contains(pair.Key),
                        GameDataLinkService.HasUpdate(pair.Value, indexItem),
                        canReset: true);
                    return new LibraryUseRow(row, use, GameNameOf(pair.Key), UseState(item, use));
                })
                .OrderBy(use => use.Label, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            ApplyPublished(row);
            if (_thumbnails.TryGetValue(group.WorkshopItemId, out var thumbnail))
            {
                row.ThumbnailPath = thumbnail;
            }

            return row;
        }


        /// <summary>Downloads the Workshop's newer version once and merges it into every game behind it, keeping their edits.</summary>
        private async Task UpdateGameDataAsync(LibraryItemRow row)
        {
            var item = row.IndexItem;
            if (item == null)
            {
                return;
            }

            var games = GameDataLinkService.GamesBehind(_plugin.GameDataLinks.All, item);
            if (games.Count == 0)
            {
                return;
            }

            StatusMessage = string.Format(L("LOCPlayAch_Workshop_Downloading"), item.Name);
            await RunGameDataAsync(
                applier => applier.UpdateAsync(item, games, _lifetime.Token),
                isUpdate: true,
                $"Failed updating the game data {item.Id}.");
        }

        /// <summary>After a Playnite confirmation, applies the game's kept package again as published, as Manage Achievements does.</summary>
        private async Task ResetGameDataAsync(LibraryUseRow use)
        {
            if (!LibraryTargetKeys.TryGetGameId(use.TargetKey, out var gameId))
            {
                return;
            }

            var name = use.Owner.Name;
            if (Confirm != null && !Confirm(string.Format(L("LOCPlayAch_Workshop_ResetConfirmGameData"), name, use.Label)))
            {
                return;
            }

            var link = use.Use.Link;
            await RunGameDataAsync(
                applier => applier.ResetAsync(gameId, link, name, _lifetime.Token),
                isUpdate: false,
                $"Failed resetting {use.TargetKey}.");
        }

        private async Task RunGameDataAsync(Func<GameDataPackageApplier, Task<WorkshopInstallResult>> apply, bool isUpdate, string failure)
        {
            IsBusy = true;
            ErrorMessage = null;
            try
            {
                var applier = new GameDataPackageApplier(_plugin.WorkshopClient, _plugin.WorkshopInstaller, _plugin.GameDataLinks);
                var result = await apply(applier);
                StatusMessage = WorkshopViewModel.DescribeInstall(result, isUpdate) ?? L("LOCPlayAch_Status_Succeeded");
            }
            catch (OperationCanceledException)
            {
                StatusMessage = null;
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, failure);
                ErrorMessage = string.Format(L("LOCPlayAch_Status_Failed"), ex.Message);
                StatusMessage = null;
            }
            finally
            {
                IsBusy = false;
                if (!_lifetime.IsCancellationRequested)
                {
                    Reload(reconcile: false);
                }
            }
        }
    }
}
