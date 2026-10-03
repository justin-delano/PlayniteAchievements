using Playnite.SDK;
using Playnite.SDK.Models;
using PlayniteAchievements.Common;
using PlayniteAchievements.Services.Workshop;
using System;
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
        private readonly Dictionary<string, WorkshopGameMatch> _matches = new Dictionary<string, WorkshopGameMatch>(StringComparer.OrdinalIgnoreCase);

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
                new WorkshopKindOption(WorkshopItemKind.Theme, WorkshopItemViewModel.KindLabelFor(WorkshopItemKind.Theme)),
                new WorkshopKindOption(WorkshopItemKind.GameCustomData, WorkshopItemViewModel.KindLabelFor(WorkshopItemKind.GameCustomData))
            };
            SortOptions = new List<WorkshopSortOption>
            {
                new WorkshopSortOption(WorkshopSort.MostDownloaded, ResourceProvider.GetString("LOCPlayAch_Workshop_SortMostDownloaded")),
                new WorkshopSortOption(WorkshopSort.Newest, ResourceProvider.GetString("LOCPlayAch_Workshop_SortNewest")),
                new WorkshopSortOption(WorkshopSort.Name, ResourceProvider.GetString("LOCPlayAch_Column_Name"))
            };
            _selectedKind = focusGameId.HasValue
                ? KindOptions.Last()
                : KindOptions.FirstOrDefault(option => option.Kind == focusKind) ?? KindOptions[0];
            _selectedSort = SortOptions[0];

            ItemsView = CollectionViewSource.GetDefaultView(Items);
            ItemsView.Filter = FilterItem;

            RefreshCommand = new AsyncCommand(async _ => await LoadAsync());
            InstallCommand = new AsyncCommand(async parameter => await InstallAsync(parameter as WorkshopItemViewModel ?? SelectedItem), _ => !IsBusy);
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
        public IReadOnlyList<WorkshopSortOption> SortOptions { get; }

        public AsyncCommand RefreshCommand { get; }
        public AsyncCommand InstallCommand { get; }
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
                Items.Clear();
                _matches.Clear();
                foreach (var item in index.Items.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var row = new WorkshopItemViewModel(item);
                    ApplyLocalState(row);
                    Items.Add(row);
                }

                ApplySort();
                RebuildInstalledList();

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

        private void RebuildInstalledList()
        {
            InstalledItems.Clear();
            foreach (var row in Items.Where(row => row.IsInstalled).OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase))
            {
                InstalledItems.Add(row);
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

            // A kind tab also lists the themes that carry that part ("also in themes"), so a user
            // looking for sounds sees sound packs first and bundles containing sounds after them.
            if (_selectedKind?.Kind is WorkshopItemKind kind && row.Kind != kind &&
                !(row.Kind == WorkshopItemKind.Theme && ThemePartFor(kind) is ThemePackParts part &&
                  WorkshopInstaller.ThemePartsOf(row.Item).HasFlag(part)))
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

        /// <summary>The theme part a kind tab corresponds to, or null for kinds themes never carry.</summary>
        public static ThemePackParts? ThemePartFor(WorkshopItemKind kind)
        {
            switch (kind)
            {
                case WorkshopItemKind.Colors: return ThemePackParts.Colors;
                case WorkshopItemKind.UnlockSounds: return ThemePackParts.Sounds;
                case WorkshopItemKind.NotificationStyle: return ThemePackParts.Toast;
                case WorkshopItemKind.ScreenshotFrame: return ThemePackParts.Frame;
                default: return null;
            }
        }

        /// <summary>
        /// When a theme is installed from a part's tab, the part picker starts with just that
        /// part ticked; from the Themes or All tab every available part is ticked.
        /// </summary>
        public ThemePackParts PreferredThemeParts =>
            _selectedKind?.Kind is WorkshopItemKind kind && ThemePartFor(kind) is ThemePackParts part
                ? part
                : ThemePackParts.All;

        private void ApplySort()
        {
            ItemsView.SortDescriptions.Clear();
            // Within a part tab, standalone items of that kind come before the bundles.
            if (_selectedKind?.Kind is WorkshopItemKind kind && kind != WorkshopItemKind.Theme)
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

        /// <summary>Asks the window to pick theme parts; null cancels.</summary>
        public Func<WorkshopItemViewModel, Services.Workshop.ThemePackParts, Services.Workshop.ThemePackParts?> PickThemeParts { get; set; }

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

                if (row.Kind == WorkshopItemKind.Theme)
                {
                    var available = WorkshopInstaller.ThemePartsOf(row.Item);
                    var picked = PickThemeParts?.Invoke(row, available) ?? available;
                    if (picked == Services.Workshop.ThemePackParts.None)
                    {
                        return;
                    }

                    request.ThemeParts = picked;
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

                Directory.CreateDirectory(scratch);
                request.PackagePath = Path.Combine(scratch, row.Item.Package?.File ?? "package.zip");

                var total = Math.Max(1, row.Item.Package?.SizeBytes ?? 1);
                StatusMessage = string.Format(ResourceProvider.GetString("LOCPlayAch_Workshop_Downloading"), row.Name);
                ProgressFraction = 0;
                await _client.DownloadPackageAsync(
                    row.Item,
                    request.PackagePath,
                    new Progress<long>(received => ProgressFraction = Math.Min(1, (double)received / total)),
                    _lifetime.Token);

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
            _lifetime.Cancel();
            _lifetime.Dispose();
        }
    }
}
