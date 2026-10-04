using Playnite.SDK;
using Playnite.SDK.Models;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Workshop;
using PlayniteAchievements.Services.Workshop.Preview;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using AsyncCommand = PlayniteAchievements.Common.AsyncCommand;
using ObservableObject = PlayniteAchievements.Common.ObservableObject;
using RelayCommand = PlayniteAchievements.Common.RelayCommand;

namespace PlayniteAchievements.ViewModels.Workshop
{
    public enum WorkshopSort
    {
        MostDownloaded,
        Newest,
        Name
    }

    /// <summary>A kind filter entry: null kind means every kind.</summary>
    public sealed class WorkshopKindOption
    {
        public WorkshopKindOption(WorkshopItemKind? kind, string label)
        {
            Kind = kind;
            Label = label;
        }

        public WorkshopItemKind? Kind { get; }

        public string Label { get; }
    }

    public sealed class WorkshopSortOption
    {
        public WorkshopSortOption(WorkshopSort sort, string label)
        {
            Sort = sort;
            Label = label;
        }

        public WorkshopSort Sort { get; }

        public string Label { get; }
    }

    /// <summary>A "Revert to before ..." row.</summary>
    public sealed class WorkshopUndoViewModel
    {
        public WorkshopUndoViewModel(WorkshopUndoEntry entry)
        {
            Entry = entry;
            Label = string.Format(
                ResourceProvider.GetString("LOCPlayAch_Workshop_RevertTo"),
                entry.ItemName,
                entry.CreatedUtc.ToLocalTime().ToString("g"));
        }

        public WorkshopUndoEntry Entry { get; }

        public string Label { get; }
    }

    public sealed class WorkshopSubmissionViewModel : ObservableObject
    {
        private string _stateLabel;

        public WorkshopSubmissionViewModel(WorkshopSubmissionRecord record)
        {
            Record = record;
            _stateLabel = StateLabel(record.LastState);
        }

        public WorkshopSubmissionRecord Record { get; }

        public string Name => Record.Name;

        public string KindLabel => WorkshopItemViewModel.KindLabelFor(Record.Kind);

        public string Submitted => Record.SubmittedUtc.ToLocalTime().ToString("g");

        public string Url => Record.IssueUrl;

        public string State
        {
            get => _stateLabel;
            set => SetValue(ref _stateLabel, value);
        }

        public static string StateLabel(string state)
        {
            switch (state)
            {
                case "needs-changes": return ResourceProvider.GetString("LOCPlayAch_Workshop_Status_NeedsChanges");
                case "in-review": return ResourceProvider.GetString("LOCPlayAch_Workshop_Status_InReview");
                case "published": return ResourceProvider.GetString("LOCPlayAch_Workshop_Status_Published");
                case "closed": return ResourceProvider.GetString("LOCPlayAch_Workshop_Status_Closed");
                default: return ResourceProvider.GetString("LOCPlayAch_Workshop_Status_Validating");
            }
        }
    }

    /// <summary>
    /// The Workshop window: loads the index, filters and sorts it, resolves local state per item
    /// (installed, update available, matching library game), and drives install, revert, and
    /// sharing. All collection work happens on the UI thread; network and disk work is awaited.
    /// </summary>
    public sealed class WorkshopViewModel : ObservableObject, IDisposable
    {
        private readonly PlayniteAchievementsPlugin _plugin;
        private readonly ILogger _logger;
        private readonly WorkshopClient _client;
        private readonly WorkshopInstalledRegistry _registry;
        private readonly WorkshopInstaller _installer;
        private readonly WorkshopUndoStore _undo;
        private readonly WorkshopGameMatcher _matcher;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly ConcurrentDictionary<string, WorkshopGameMatch> _matches = new ConcurrentDictionary<string, WorkshopGameMatch>(StringComparer.OrdinalIgnoreCase);

        private string _searchText = string.Empty;
        private WorkshopKindOption _selectedKind;
        private WorkshopSortOption _selectedSort;
        private bool _onlyMyGames = true;
        private WorkshopItemViewModel _selectedItem;
        private bool _isLoading;
        private bool _isBusy;
        private string _statusMessage;
        private string _errorMessage;
        private double _progressFraction;
        private Guid? _focusGameId;

        public WorkshopViewModel(PlayniteAchievementsPlugin plugin, ILogger logger, Guid? focusGameId, WorkshopItemKind? focusKind = null)
        {
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            _logger = logger;
            _client = plugin.WorkshopClient;
            _registry = plugin.WorkshopRegistry;
            _installer = plugin.WorkshopInstaller;
            _undo = plugin.WorkshopUndo;
            _matcher = plugin.CreateWorkshopGameMatcher();
            _focusGameId = focusGameId;

            KindOptions = new List<WorkshopKindOption>
            {
                new WorkshopKindOption(null, ResourceProvider.GetString("LOCPlayAch_Common_All")),
                new WorkshopKindOption(WorkshopItemKind.Colors, WorkshopItemViewModel.KindLabelFor(WorkshopItemKind.Colors)),
                new WorkshopKindOption(WorkshopItemKind.NotificationStyle, WorkshopItemViewModel.KindLabelFor(WorkshopItemKind.NotificationStyle)),
                new WorkshopKindOption(WorkshopItemKind.ScreenshotFrame, WorkshopItemViewModel.KindLabelFor(WorkshopItemKind.ScreenshotFrame)),
                new WorkshopKindOption(WorkshopItemKind.ShowcasePage, WorkshopItemViewModel.KindLabelFor(WorkshopItemKind.ShowcasePage)),
                new WorkshopKindOption(WorkshopItemKind.UnlockSounds, WorkshopItemViewModel.KindLabelFor(WorkshopItemKind.UnlockSounds)),
                new WorkshopKindOption(WorkshopItemKind.Bundle, WorkshopItemViewModel.KindLabelFor(WorkshopItemKind.Bundle)),
                new WorkshopKindOption(WorkshopItemKind.GameCustomData, WorkshopItemViewModel.KindLabelFor(WorkshopItemKind.GameCustomData))
            };
            SortOptions = new List<WorkshopSortOption>
            {
                new WorkshopSortOption(WorkshopSort.MostDownloaded, ResourceProvider.GetString("LOCPlayAch_Workshop_SortMostDownloaded")),
                new WorkshopSortOption(WorkshopSort.Newest, ResourceProvider.GetString("LOCPlayAch_Workshop_SortNewest")),
                new WorkshopSortOption(WorkshopSort.Name, ResourceProvider.GetString("LOCPlayAch_Column_Name"))
            };
            // Opened from the window that owns one kind (or from a game): that kind alone is
            // offered, bundles carrying the part still list under it, and the selector hides.
            var focusedKind = focusGameId.HasValue ? WorkshopItemKind.GameCustomData : focusKind;
            if (focusedKind is WorkshopItemKind only)
            {
                KindOptions = KindOptions.Where(option => option.Kind == only).ToList();
            }

            _selectedKind = focusGameId.HasValue
                ? KindOptions.Last()
                : KindOptions.FirstOrDefault(option => option.Kind == focusKind) ?? KindOptions[0];
            _selectedSort = SortOptions[0];

            ItemsView = CollectionViewSource.GetDefaultView(Items);
            ItemsView.Filter = FilterItem;

            RefreshCommand = new AsyncCommand(async _ => await LoadAsync());
            InstallCommand = new AsyncCommand(async parameter => await InstallAsync(parameter as WorkshopItemViewModel ?? SelectedItem), _ => !IsBusy);
            PreviewCommand = new AsyncCommand(async parameter => await PreviewAsync(parameter as WorkshopItemViewModel ?? SelectedItem), _ => !IsBusy);
            OpenFolderCommand = new RelayCommand(parameter => OpenUrl((parameter as WorkshopItemViewModel ?? SelectedItem)?.FolderUrl));
            ReportCommand = new RelayCommand(parameter => Report(parameter as WorkshopItemViewModel ?? SelectedItem));
            RevertCommand = new RelayCommand(parameter => Revert(parameter as WorkshopUndoViewModel), _ => !IsBusy);
            OpenSubmissionCommand = new RelayCommand(parameter => OpenUrl((parameter as WorkshopSubmissionViewModel)?.Url));
            RefreshSubmissionsCommand = new AsyncCommand(async _ => await RefreshSubmissionStatesAsync());

            ReloadLocalState();
        }

        public ObservableCollection<WorkshopItemViewModel> Items { get; } = new ObservableCollection<WorkshopItemViewModel>();
        public ICollectionView ItemsView { get; }
        public ObservableCollection<WorkshopItemViewModel> InstalledItems { get; } = new ObservableCollection<WorkshopItemViewModel>();
        public ObservableCollection<WorkshopUndoViewModel> UndoEntries { get; } = new ObservableCollection<WorkshopUndoViewModel>();
        public ObservableCollection<WorkshopSubmissionViewModel> Submissions { get; } = new ObservableCollection<WorkshopSubmissionViewModel>();
        public IReadOnlyList<WorkshopKindOption> KindOptions { get; }

        /// <summary>False when the window was opened for one kind, so the kind selector is hidden.</summary>
        public bool ShowKindFilter => KindOptions.Count > 1;

        /// <summary>The one kind this window is scoped to, or null when it browses everything.</summary>
        private WorkshopItemKind? FocusedKind => KindOptions.Count == 1 ? KindOptions[0].Kind : null;
        public IReadOnlyList<WorkshopSortOption> SortOptions { get; }

        public AsyncCommand RefreshCommand { get; }
        public AsyncCommand InstallCommand { get; }
        public AsyncCommand PreviewCommand { get; }
        public RelayCommand OpenFolderCommand { get; }
        public RelayCommand ReportCommand { get; }
        public RelayCommand RevertCommand { get; }
        public RelayCommand OpenSubmissionCommand { get; }
        public AsyncCommand RefreshSubmissionsCommand { get; }

        public WorkshopInstalledRegistry Registry => _registry;

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetValueAndReturn(ref _searchText, value ?? string.Empty, nameof(SearchText)))
                {
                    ItemsView.Refresh();
                }
            }
        }

        public WorkshopKindOption SelectedKind
        {
            get => _selectedKind;
            set
            {
                if (SetValueAndReturn(ref _selectedKind, value, nameof(SelectedKind)))
                {
                    OnPropertyChanged(nameof(ShowOnlyMyGames));
                    ApplySort();
                    ItemsView.Refresh();
                }
            }
        }

        public WorkshopSortOption SelectedSort
        {
            get => _selectedSort;
            set
            {
                if (SetValueAndReturn(ref _selectedSort, value, nameof(SelectedSort)))
                {
                    ApplySort();
                }
            }
        }

        public bool OnlyMyGames
        {
            get => _onlyMyGames;
            set
            {
                if (SetValueAndReturn(ref _onlyMyGames, value, nameof(OnlyMyGames)))
                {
                    ItemsView.Refresh();
                }
            }
        }

        public bool ShowOnlyMyGames => _selectedKind?.Kind == null || _selectedKind.Kind == WorkshopItemKind.GameCustomData;

        public WorkshopItemViewModel SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (SetValueAndReturn(ref _selectedItem, value, nameof(SelectedItem)))
                {
                    OnPropertyChanged(nameof(HasSelection));
                    _ = LoadDetailsAsync(value);
                }
            }
        }

        public bool HasSelection => _selectedItem != null;

        public bool IsLoading
        {
            get => _isLoading;
            private set => SetValue(ref _isLoading, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetValueAndReturn(ref _isBusy, value, nameof(IsBusy)))
                {
                    InstallCommand.RaiseCanExecuteChanged();
                    PreviewCommand.RaiseCanExecuteChanged();
                    RevertCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set
            {
                if (SetValueAndReturn(ref _statusMessage, value, nameof(StatusMessage)))
                {
                    OnPropertyChanged(nameof(HasStatus));
                }
            }
        }

        public bool HasStatus => !string.IsNullOrWhiteSpace(_statusMessage);

        public string ErrorMessage
        {
            get => _errorMessage;
            private set
            {
                if (SetValueAndReturn(ref _errorMessage, value, nameof(ErrorMessage)))
                {
                    OnPropertyChanged(nameof(HasError));
                }
            }
        }

        public bool HasError => !string.IsNullOrWhiteSpace(_errorMessage);

        public double ProgressFraction
        {
            get => _progressFraction;
            private set => SetValue(ref _progressFraction, value);
        }

        public bool HasUndoEntries => UndoEntries.Count > 0;

        public bool HasSubmissions => Submissions.Count > 0;

        public bool HasNoInstalledItems => InstalledItems.Count == 0;

        // ---- loading -----------------------------------------------------------------------

        public async Task LoadAsync()
        {
            if (IsLoading)
            {
                return;
            }

            IsLoading = true;
            ErrorMessage = null;
            try
            {
                var index = await _client.FetchIndexAsync(_lifetime.Token);

                // Matching game data against the library and checking what is still installed
                // read the achievement cache and disk; that runs off the UI thread, on rows
                // nothing is bound to yet, against one snapshot of the library.
                _matcher.Reset();
                _matches.Clear();
                var rows = await Task.Run(() =>
                {
                    var built = new List<WorkshopItemViewModel>();
                    foreach (var item in index.Items.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        _lifetime.Token.ThrowIfCancellationRequested();
                        var row = new WorkshopItemViewModel(item);
                        ApplyLocalState(row);
                        built.Add(row);
                    }

                    return built;
                }, _lifetime.Token);

                Items.Clear();
                foreach (var row in rows)
                {
                    Items.Add(row);
                }

                ApplySort();
                RebuildInstalledList();
                _ = PrefetchPreviewsAsync(rows);

                if (_focusGameId.HasValue)
                {
                    var focused = Items.FirstOrDefault(row => _matches.TryGetValue(row.Id, out var match) && match.PlayniteGameId == _focusGameId.Value);
                    if (focused != null)
                    {
                        SearchText = focused.GameName ?? string.Empty;
                        SelectedItem = focused;
                    }
                    else
                    {
                        var game = _plugin.PlayniteApi?.Database?.Games?.Get(_focusGameId.Value);
                        SearchText = game?.Name ?? string.Empty;
                    }

                    _focusGameId = null;
                }

                ItemsView.Refresh();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "Failed loading the Workshop index.");
                ErrorMessage = string.Format(ResourceProvider.GetString("LOCPlayAch_Workshop_LoadFailed"), ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ApplyLocalState(WorkshopItemViewModel row)
        {
            WorkshopInstalledItem installed;
            if (row.Kind == WorkshopItemKind.GameCustomData)
            {
                var match = _matcher.Match(row.Item.Game?.Keys);
                if (match != null)
                {
                    _matches[row.Id] = match;
                    row.LocalGameName = match.GameName;
                }
                else
                {
                    row.LocalGameName = null;
                }

                installed = _registry.Find(row.Id, match?.PlayniteGameId) ?? _registry.Find(row.Id);
            }
            else
            {
                installed = _registry.Find(row.Id);
            }

            // The registry remembers installs; it does not see presets deleted from a card or a
            // game whose custom data was cleared. Check the thing itself and drop stale records,
            // so Installed means present, not merely installed once.
            if (installed != null && !IsStillPresent(row, installed))
            {
                _registry.Forget(row.Id, installed.PlayniteGameId);
                installed = null;
            }

            row.IsInstalled = installed != null;
            row.HasUpdate = installed != null && WorkshopInstalledRegistry.IsNewer(row.Version, installed.Version);
        }

        private void ReloadLocalState()
        {
            UndoEntries.Clear();
            foreach (var entry in _undo.List())
            {
                UndoEntries.Add(new WorkshopUndoViewModel(entry));
            }

            OnPropertyChanged(nameof(HasUndoEntries));

            Submissions.Clear();
            foreach (var record in _registry.Submissions)
            {
                Submissions.Add(new WorkshopSubmissionViewModel(record));
            }

            OnPropertyChanged(nameof(HasSubmissions));
        }

        /// <summary>
        /// Whether what an install created still exists: the preset named after the item for
        /// looks (any part for a bundle), a showcase page by that name, or custom data on the
        /// recorded game. Unknown kinds are taken as present.
        /// </summary>
        private bool IsStillPresent(WorkshopItemViewModel row, WorkshopInstalledItem installed)
        {
            try
            {
                var name = row.Name;
                switch (row.Kind)
                {
                    case WorkshopItemKind.Colors:
                        return _plugin.ColorPresetStore.Exists(name);
                    case WorkshopItemKind.UnlockSounds:
                        return _plugin.UnlockSoundPresetStore.Exists(name);
                    case WorkshopItemKind.NotificationStyle:
                        return _plugin.NotificationStylePresetStore.PresetExists(isFrame: false, name);
                    case WorkshopItemKind.ScreenshotFrame:
                        return _plugin.NotificationStylePresetStore.PresetExists(isFrame: true, name);
                    case WorkshopItemKind.Bundle:
                        return _plugin.ColorPresetStore.Exists(name)
                               || _plugin.UnlockSoundPresetStore.Exists(name)
                               || _plugin.NotificationStylePresetStore.PresetExists(isFrame: false, name)
                               || _plugin.NotificationStylePresetStore.PresetExists(isFrame: true, name);
                    case WorkshopItemKind.ShowcasePage:
                        return _plugin.Settings?.Persisted?.Showcase?.Pages?.Any(page =>
                                   string.Equals(page?.Name, name, StringComparison.OrdinalIgnoreCase)) ?? true;
                    case WorkshopItemKind.GameCustomData:
                        return !(installed.PlayniteGameId is Guid gameId)
                               || (_plugin.GameCustomDataStore?.HasPortableData(gameId) ?? true);
                    default:
                        return true;
                }
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, $"Could not verify the installed state of {row.Id}.");
                return true;
            }
        }

        private void RebuildInstalledList()
        {
            InstalledItems.Clear();
            foreach (var row in Items
                .Where(row => row.IsInstalled && MatchesKind(row, FocusedKind))
                .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase))
            {
                InstalledItems.Add(row);
            }

            OnPropertyChanged(nameof(HasNoInstalledItems));
        }

        /// <summary>
        /// Whether a row belongs under a kind filter: its own kind, or a bundle carrying that
        /// kind as a part ("also in bundles"). Null matches everything.
        /// </summary>
        private static bool MatchesKind(WorkshopItemViewModel row, WorkshopItemKind? kind)
        {
            if (!(kind is WorkshopItemKind wanted) || row.Kind == wanted)
            {
                return true;
            }

            return row.Kind == WorkshopItemKind.Bundle
                && BundlePartFor(wanted) is BundleParts part
                && WorkshopInstaller.BundlePartsOf(row.Item).HasFlag(part);
        }

        /// <summary>
        /// Fetches every row's preview in the background, a few at a time, so the list shows
        /// its images without each item having to be selected first. Cached files return at
        /// once; the detail pane's own fetch finds them already there.
        /// </summary>
        private async Task PrefetchPreviewsAsync(IReadOnlyList<WorkshopItemViewModel> rows)
        {
            var pending = rows
                .Where(row => row.PreviewPath == null && !string.IsNullOrWhiteSpace(row.Item?.Urls?.Preview))
                .ToList();
            if (pending.Count == 0)
            {
                return;
            }

            using (var gate = new SemaphoreSlim(4))
            {
                var fetches = pending.Select(async row =>
                {
                    await gate.WaitAsync(_lifetime.Token).ConfigureAwait(false);
                    try
                    {
                        var path = await _client.FetchPreviewAsync(row.Item, _lifetime.Token).ConfigureAwait(false);
                        if (path != null)
                        {
                            row.PreviewPath = path;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception ex)
                    {
                        _logger?.Debug(ex, $"Failed prefetching the Workshop preview for {row.Id}.");
                    }
                    finally
                    {
                        gate.Release();
                    }
                }).ToList();

                try
                {
                    await Task.WhenAll(fetches).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        private async Task LoadDetailsAsync(WorkshopItemViewModel row)
        {
            if (row == null)
            {
                return;
            }

            try
            {
                if (row.PreviewPath == null)
                {
                    row.PreviewPath = await _client.FetchPreviewAsync(row.Item, _lifetime.Token);
                }

                // The index carries the count as of its last build; the release API has the
                // live number, fetched once per item per window.
                if (row.LiveDownloads == null)
                {
                    row.LiveDownloads = await _client.FetchLiveDownloadsAsync(row.Item, _lifetime.Token);
                }

                if (row.Readme == null)
                {
                    row.Readme = await _client.FetchReadmeAsync(row.Item, _lifetime.Token) ?? row.Description;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"Failed loading Workshop details for {row.Id}.");
            }
        }

        // ---- filter and sort ---------------------------------------------------------------

        private bool FilterItem(object value)
        {
            if (!(value is WorkshopItemViewModel row))
            {
                return false;
            }

            // A kind tab also lists the bundles that carry that part ("also in bundles"), so a user
            // looking for sounds sees sound packs first and bundles containing sounds after them.
            if (!MatchesKind(row, _selectedKind?.Kind))
            {
                return false;
            }

            if (_onlyMyGames && row.Kind == WorkshopItemKind.GameCustomData && !row.IsInLibrary)
            {
                return false;
            }

            var query = _searchText.Trim();
            return query.Length == 0 || row.SearchText.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>The bundle part a kind tab corresponds to, or null for kinds bundles never carry.</summary>
        public static BundleParts? BundlePartFor(WorkshopItemKind kind)
        {
            switch (kind)
            {
                case WorkshopItemKind.Colors: return BundleParts.Colors;
                case WorkshopItemKind.UnlockSounds: return BundleParts.Sounds;
                case WorkshopItemKind.NotificationStyle: return BundleParts.Toast;
                case WorkshopItemKind.ScreenshotFrame: return BundleParts.Frame;
                default: return null;
            }
        }

        /// <summary>
        /// When a bundle is installed from a part's tab, the part picker starts with just that
        /// part ticked; from the Bundles or All tab every available part is ticked.
        /// </summary>
        public BundleParts PreferredBundleParts =>
            _selectedKind?.Kind is WorkshopItemKind kind && BundlePartFor(kind) is BundleParts part
                ? part
                : BundleParts.All;

        private void ApplySort()
        {
            ItemsView.SortDescriptions.Clear();
            // Within a part tab, standalone items of that kind come before the bundles.
            if (_selectedKind?.Kind is WorkshopItemKind kind && kind != WorkshopItemKind.Bundle)
            {
                ItemsView.SortDescriptions.Add(new SortDescription(nameof(WorkshopItemViewModel.IsBundle), ListSortDirection.Ascending));
            }

            switch (_selectedSort?.Sort ?? WorkshopSort.MostDownloaded)
            {
                case WorkshopSort.Newest:
                    ItemsView.SortDescriptions.Add(new SortDescription(nameof(WorkshopItemViewModel.UpdatedDate), ListSortDirection.Descending));
                    break;
                case WorkshopSort.Name:
                    ItemsView.SortDescriptions.Add(new SortDescription(nameof(WorkshopItemViewModel.Name), ListSortDirection.Ascending));
                    break;
                default:
                    ItemsView.SortDescriptions.Add(new SortDescription(nameof(WorkshopItemViewModel.Downloads), ListSortDirection.Descending));
                    break;
            }

            ItemsView.SortDescriptions.Add(new SortDescription(nameof(WorkshopItemViewModel.Name), ListSortDirection.Ascending));
        }

        // ---- install -----------------------------------------------------------------------

        /// <summary>Asks the window to pick bundle parts; null cancels.</summary>
        public Func<WorkshopItemViewModel, Services.Workshop.BundleParts, Services.Workshop.BundleParts?> PickBundleParts { get; set; }

        /// <summary>Asks the window to pick a library game for game data; null cancels.</summary>
        public Func<WorkshopItemViewModel, IReadOnlyList<Game>, Game> PickGame { get; set; }

        /// <summary>Asks the window to confirm a question; defaults to yes when unset.</summary>
        public Func<string, bool> Confirm { get; set; }

        public async Task InstallAsync(WorkshopItemViewModel row)
        {
            if (row == null || IsBusy || !row.CanInstall)
            {
                return;
            }

            IsBusy = true;
            ErrorMessage = null;
            var scratch = Path.Combine(Path.GetTempPath(), "PlayniteAchievements", "WorkshopInstall", Guid.NewGuid().ToString("N"));
            try
            {
                var request = new WorkshopInstallRequest { Item = row.Item };

                if (row.Kind == WorkshopItemKind.Bundle)
                {
                    var available = WorkshopInstaller.BundlePartsOf(row.Item);
                    var picked = PickBundleParts?.Invoke(row, available) ?? available;
                    if (picked == Services.Workshop.BundleParts.None)
                    {
                        return;
                    }

                    request.Parts = picked;
                }

                if (row.Kind == WorkshopItemKind.GameCustomData)
                {
                    var gameId = ResolveTargetGame(row);
                    if (gameId == null)
                    {
                        return;
                    }

                    var gameName = _plugin.PlayniteApi?.Database?.Games?.Get(gameId.Value)?.Name ?? row.GameName;
                    var question = string.Format(ResourceProvider.GetString("LOCPlayAch_Workshop_InstallConfirmGameData"), row.Name, gameName);
                    if (Confirm != null && !Confirm(question))
                    {
                        return;
                    }

                    request.TargetGameId = gameId;
                }

                // The install consumes its own copy, so the cached download stays for another
                // preview or install of the same version, and this scratch folder stays the only
                // thing the finally below cleans up.
                var downloaded = await EnsureDownloadedAsync(row);
                Directory.CreateDirectory(scratch);
                request.PackagePath = Path.Combine(scratch, row.Item.Package?.File ?? "package.zip");
                var packagePath = request.PackagePath;
                await Task.Run(() => File.Copy(downloaded, packagePath, overwrite: true), _lifetime.Token);

                StatusMessage = ResourceProvider.GetString("LOCPlayAch_Workshop_Installing");
                var result = await _installer.InstallAsync(request, _lifetime.Token);

                ApplyLocalState(row);
                RebuildInstalledList();
                ReloadLocalState();
                ItemsView.Refresh();

                var notes = new List<string>();
                if (result.PresetNames.Count > 0)
                {
                    notes.Add(string.Format(
                        ResourceProvider.GetString("LOCPlayAch_Workshop_SavedAsPreset"),
                        string.Join(", ", result.PresetNames.Distinct())));
                }

                notes.AddRange(result.Warnings);
                StatusMessage = notes.Count > 0 ? string.Join("\n", notes) : null;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed installing Workshop item {row.Id}.");
                ErrorMessage = string.Format(ResourceProvider.GetString("LOCPlayAch_Status_Failed"), ex.Message);
                StatusMessage = null;
            }
            finally
            {
                ProgressFraction = 0;
                IsBusy = false;
                try
                {
                    if (Directory.Exists(scratch))
                    {
                        Directory.Delete(scratch, recursive: true);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Debug(ex, "Failed cleaning the Workshop install scratch folder.");
                }
            }
        }

        private Guid? ResolveTargetGame(WorkshopItemViewModel row)
        {
            if (_matches.TryGetValue(row.Id, out var match) && match.Confidence == WorkshopGameMatchConfidence.Provider)
            {
                return match.PlayniteGameId;
            }

            // A name-only match or no match: let the user confirm or pick from the library.
            var candidates = _matcher.Candidates(row.Item.Game?.Keys);
            var games = candidates.Count > 0
                ? candidates
                : (_plugin.PlayniteApi?.Database?.Games?.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList() ?? new List<Game>());
            var picked = PickGame?.Invoke(row, games);
            return picked?.Id;
        }

        // ---- download cache ----------------------------------------------------------------

        /// <summary>The one package download kept between a preview and an install of the same version.</summary>
        private sealed class CachedDownload
        {
            public string ItemId { get; set; }
            public string Sha256 { get; set; }
            public string Path { get; set; }
            public string Directory { get; set; }
        }

        private CachedDownload _cachedDownload;

        /// <summary>
        /// The path of the row's package on disk: the cached download when it is the same item and
        /// package hash and the file still verifies, else a fresh download that replaces the cache.
        /// Reports progress through the status strip. UI thread.
        /// </summary>
        private async Task<string> EnsureDownloadedAsync(WorkshopItemViewModel row)
        {
            var item = row.Item;
            var sha256 = item.Package?.Sha256 ?? string.Empty;
            var cached = _cachedDownload;
            if (cached != null &&
                string.Equals(cached.ItemId, item.Id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(cached.Sha256, sha256, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(cached.Path))
            {
                var cachedPath = cached.Path;
                if (await Task.Run(() => WorkshopClient.VerifyPackage(item, cachedPath), _lifetime.Token))
                {
                    return cachedPath;
                }
            }

            DeleteCachedDownload();

            var directory = Path.Combine(Path.GetTempPath(), "PlayniteAchievements", WorkshopPreviewModelBuilder.ScratchFolderLabel, Guid.NewGuid().ToString("N"));
            var path = Path.Combine(directory, item.Package?.File ?? "package.zip");
            var total = Math.Max(1, item.Package?.SizeBytes ?? 1);
            StatusMessage = string.Format(ResourceProvider.GetString("LOCPlayAch_Workshop_Downloading"), row.Name);
            ProgressFraction = 0;
            try
            {
                Directory.CreateDirectory(directory);
                await _client.DownloadPackageAsync(
                    item,
                    path,
                    new Progress<long>(received => ProgressFraction = Math.Min(1, (double)received / total)),
                    _lifetime.Token);
            }
            catch
            {
                PortablePackage.TryDeleteDirectory(directory);
                throw;
            }

            _cachedDownload = new CachedDownload { ItemId = item.Id, Sha256 = sha256, Path = path, Directory = directory };
            return path;
        }

        private void DeleteCachedDownload()
        {
            var cached = _cachedDownload;
            _cachedDownload = null;
            if (cached != null)
            {
                PortablePackage.TryDeleteDirectory(cached.Directory);
            }
        }

        // ---- preview -----------------------------------------------------------------------

        /// <summary>
        /// Shows a preview of the package and returns true when the user chose to install it. The
        /// callee owns the model and disposes it. Set by the window; without it a preview is read
        /// and discarded.
        /// </summary>
        public Func<WorkshopItemViewModel, WorkshopPreviewModel, bool> ShowPreview { get; set; }

        /// <summary>
        /// Downloads the row's package (or reuses the cached download), reads it into a preview
        /// model off the UI thread and hands it to <see cref="ShowPreview"/>; installs when the
        /// preview asks for it. Game data is compared against the matched library game only when
        /// the match is certain enough to need no prompt; otherwise the package is listed alone.
        /// </summary>
        public async Task PreviewAsync(WorkshopItemViewModel row)
        {
            if (row == null || IsBusy)
            {
                return;
            }

            IsBusy = true;
            ErrorMessage = null;
            WorkshopPreviewModel model = null;
            var install = false;
            try
            {
                var packagePath = await EnsureDownloadedAsync(row);

                var context = WorkshopPreviewContext.FromPlugin(_plugin);
                var target = row.Kind == WorkshopItemKind.GameCustomData &&
                             _matches.TryGetValue(row.Id, out var match) &&
                             (match.Confidence == WorkshopGameMatchConfidence.Provider || match.Confidence == WorkshopGameMatchConfidence.Name)
                    ? match
                    : null;
                var kind = row.Kind;
                var itemId = row.Id;
                var dataService = _plugin.AchievementDataService;
                var persisted = _plugin.Settings?.Persisted;
                var managedIcons = _plugin.ManagedCustomIconService;
                var baselines = _installer.Baselines;

                model = await Task.Run(() =>
                {
                    if (target != null)
                    {
                        var gameId = target.PlayniteGameId;
                        GameCustomDataFile current = null;
                        context.GameCustomDataStore?.TryLoad(gameId, out current);
                        context.GameDataSource = new GameCustomDataPreviewSource
                        {
                            GameId = gameId,
                            GameName = target.GameName,
                            RawData = dataService?.GetRawGameAchievementData(gameId),
                            CurrentData = dataService?.GetGameAchievementData(gameId),
                            Current = current,
                            Baseline = baselines.Load(_registry.Find(itemId, gameId)),
                            Persisted = persisted,
                            ManagedCustomIconService = managedIcons
                        };
                    }

                    return WorkshopPreviewModelBuilder.Build(kind, packagePath, context);
                }, _lifetime.Token);

                StatusMessage = null;
                ProgressFraction = 0;
                IsBusy = false;

                var show = ShowPreview;
                if (show != null)
                {
                    var shown = model;
                    model = null;
                    install = show(row, shown);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed previewing Workshop item {row.Id}.");
                ErrorMessage = string.Format(ResourceProvider.GetString("LOCPlayAch_Status_Failed"), ex.Message);
                StatusMessage = null;
            }
            finally
            {
                model?.Dispose();
                ProgressFraction = 0;
                IsBusy = false;
            }

            if (install)
            {
                await InstallAsync(row);
            }
        }

        private void Revert(WorkshopUndoViewModel entry)
        {
            if (entry == null || IsBusy)
            {
                return;
            }

            IsBusy = true;
            try
            {
                _installer.Revert(entry.Entry.Id);
                ReloadLocalState();
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed reverting Workshop snapshot {entry.Entry.Id}.");
                ErrorMessage = string.Format(ResourceProvider.GetString("LOCPlayAch_Status_Failed"), ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task RefreshSubmissionStatesAsync()
        {
            var client = _plugin.WorkshopSubmissionClient;
            foreach (var submission in Submissions.ToList())
            {
                try
                {
                    var status = await client.GetStatusAsync(submission.Record.IssueNumber, _lifetime.Token);
                    submission.State = WorkshopSubmissionViewModel.StateLabel(status.State);
                    _registry.UpdateSubmissionState(submission.Record.IssueNumber, status.State);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger?.Debug(ex, $"Failed reading Workshop submission #{submission.Record.IssueNumber}.");
                }
            }
        }

        // ---- misc --------------------------------------------------------------------------

        private void Report(WorkshopItemViewModel row)
        {
            if (row == null)
            {
                return;
            }

            // The report form lives on the repository; prefill the item id.
            var repo = row.FolderUrl;
            var marker = "/tree/";
            var index = repo?.IndexOf(marker, StringComparison.Ordinal) ?? -1;
            if (index < 0)
            {
                OpenUrl(repo);
                return;
            }

            var repoRoot = repo.Substring(0, index);
            OpenUrl($"{repoRoot}/issues/new?template=report.yml&title={Uri.EscapeDataString("[Report] " + row.Name)}&item_id={Uri.EscapeDataString(row.Id)}");
        }

        private void OpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"Failed opening {url}.");
            }
        }

        public void Dispose()
        {
            DeleteCachedDownload();
            _lifetime.Cancel();
            _lifetime.Dispose();
        }
    }
}
