using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Library;
using PlayniteAchievements.Services.Workshop;
using PlayniteAchievements.ViewModels.Workshop;
using AsyncCommand = PlayniteAchievements.Common.AsyncCommand;
using ObservableObject = PlayniteAchievements.Common.ObservableObject;
using RelayCommand = PlayniteAchievements.Common.RelayCommand;

namespace PlayniteAchievements.ViewModels.Library
{
    /// <summary>
    /// The Library page: every library item (the user's own presets and Workshop items alike),
    /// grouped by kind and filtered by kind and text, with where each is used and how those
    /// places stand. Actions: update (a newer Workshop version, or a re-saved preset its
    /// followers have not taken), reinstall, export, share, rename and delete. The list follows
    /// the library, the per-game links and the settings links as they change. UI thread.
    /// </summary>
    public sealed class LibraryViewModel : ObservableObject, IDisposable
    {
        private static readonly LibraryItemKind[] KindOrder =
        {
            LibraryItemKind.Colors,
            LibraryItemKind.Toast,
            LibraryItemKind.Frame,
            LibraryItemKind.Sounds,
            LibraryItemKind.ShowcasePage,
            LibraryItemKind.GameData
        };

        private readonly PlayniteAchievementsPlugin _plugin;
        private readonly ILogger _logger;
        private readonly LibraryStore _library;
        private readonly LibraryUpdateService _updates;
        private readonly GameLinkStore _gameLinks;
        private readonly PersistedSettingsSubscription _settingsSubscription;
        private readonly Dispatcher _dispatcher;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly Dictionary<string, string> _thumbnails = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IReadOnlyList<Brush>> _swatches = new Dictionary<string, IReadOnlyList<Brush>>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, WorkshopItem> _index;
        private bool _indexRequested;
        private bool _reloadQueued;

        private LibraryKindFilter _selectedFilter;
        private string _searchText = string.Empty;
        private LibraryItemRow _selectedRow;
        private bool _isBusy;
        private string _statusMessage;
        private string _errorMessage;

        public LibraryViewModel(PlayniteAchievementsPlugin plugin, ILogger logger, LibraryItemKind? focusKind = null)
        {
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            _logger = logger;
            _library = plugin.LibraryStore;
            _updates = plugin.LibraryUpdateService;
            _gameLinks = plugin.GameLinkStore;
            _dispatcher = Dispatcher.CurrentDispatcher;

            Filters = new List<LibraryKindFilter> { new LibraryKindFilter(null, ResourceProvider.GetString("LOCPlayAch_Common_All")) };
            Filters.AddRange(KindOrder.Select(kind => new LibraryKindFilter(kind, LibraryItemRow.KindLabelFor(kind))));
            _selectedFilter = Filters.FirstOrDefault(filter => filter.Kind == focusKind) ?? Filters[0];
            _selectedFilter.IsSelected = true;

            RowsView = CollectionViewSource.GetDefaultView(Rows);
            RowsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LibraryItemRow.KindLabel)));
            RowsView.SortDescriptions.Add(new SortDescription(nameof(LibraryItemRow.KindOrder), ListSortDirection.Ascending));
            RowsView.SortDescriptions.Add(new SortDescription(nameof(LibraryItemRow.Name), ListSortDirection.Ascending));
            RowsView.Filter = FilterRow;

            SelectFilterCommand = new RelayCommand(parameter => SelectFilter(parameter as LibraryKindFilter));
            UpdateCommand = new AsyncCommand(_ => UpdateAsync(SelectedRow), _ => !IsBusy && SelectedRow?.CanUpdate == true);
            ReinstallCommand = new AsyncCommand(_ => ReinstallAsync(SelectedRow), _ => !IsBusy && SelectedRow?.CanReinstall == true);
            ShareCommand = new RelayCommand(_ => Share(SelectedRow), _ => SelectedRow?.HasFile == true);
            RenameCommand = new RelayCommand(_ => Rename(SelectedRow), _ => !IsBusy && SelectedRow != null);
            DeleteCommand = new RelayCommand(_ => Delete(SelectedRow), _ => !IsBusy && SelectedRow != null);
            ResetCommand = new RelayCommand(parameter => Reset(parameter as LibraryUseRow));
            StopFollowingCommand = new RelayCommand(parameter => StopFollowing(parameter as LibraryUseRow));

            _library.Changed += Source_Changed;
            _gameLinks.Changed += Source_Changed;
            if (plugin.Settings != null)
            {
                _settingsSubscription = new PersistedSettingsSubscription(
                    plugin.Settings,
                    (sender, e) =>
                    {
                        if (string.Equals(e?.PropertyName, nameof(PersistedSettings.LibraryLinks), StringComparison.Ordinal))
                        {
                            ScheduleReload();
                        }
                    },
                    ScheduleReload);
            }

            Reload(reconcile: true);
        }

        public ObservableCollection<LibraryItemRow> Rows { get; } = new ObservableCollection<LibraryItemRow>();

        public ICollectionView RowsView { get; }

        public List<LibraryKindFilter> Filters { get; }

        public RelayCommand SelectFilterCommand { get; }
        public AsyncCommand UpdateCommand { get; }
        public AsyncCommand ReinstallCommand { get; }
        public RelayCommand ShareCommand { get; }
        public RelayCommand RenameCommand { get; }
        public RelayCommand DeleteCommand { get; }
        public RelayCommand ResetCommand { get; }
        public RelayCommand StopFollowingCommand { get; }

        // ---- host callbacks ---------------------------------------------------------------------

        /// <summary>Asks a yes/no question through Playnite's dialogs; null answers yes.</summary>
        public Func<string, bool> Confirm { get; set; }

        /// <summary>Asks Merge, Replace or Cancel; null on Cancel.</summary>
        public Func<string, LibraryApplyMode?> ChooseMergeOrReplace { get; set; }

        /// <summary>Asks for a new name, starting from the given one; null on Cancel.</summary>
        public Func<string, string> AskName { get; set; }

        /// <summary>Opens the share dialog for a package file.</summary>
        public Action<WorkshopItemKind, string, string> OpenShare { get; set; }

        // ---- state ------------------------------------------------------------------------------

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetValueAndReturn(ref _searchText, value ?? string.Empty))
                {
                    RefreshView();
                }
            }
        }

        public LibraryItemRow SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (SetValueAndReturn(ref _selectedRow, value))
                {
                    OnPropertyChanged(nameof(HasSelection));
                    RaiseCommandStates();
                }
            }
        }

        public bool HasSelection => _selectedRow != null;

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetValueAndReturn(ref _isBusy, value))
                {
                    RaiseCommandStates();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetValue(ref _statusMessage, value, nameof(StatusMessage), nameof(HasStatus));
        }

        public bool HasStatus => !string.IsNullOrWhiteSpace(_statusMessage);

        public string ErrorMessage
        {
            get => _errorMessage;
            private set => SetValue(ref _errorMessage, value, nameof(ErrorMessage), nameof(HasError));
        }

        public bool HasError => !string.IsNullOrWhiteSpace(_errorMessage);

        public bool IsEmpty => !RowsView.Cast<object>().Any();

        // ---- list -------------------------------------------------------------------------------

        private void Source_Changed(object sender, EventArgs e) => ScheduleReload();

        /// <summary>Rebuilds the list once the dispatcher is idle; repeated changes coalesce.</summary>
        private void ScheduleReload()
        {
            if (_reloadQueued || _lifetime.IsCancellationRequested)
            {
                return;
            }

            _reloadQueued = true;
            _dispatcher.BeginInvoke(new Action(() =>
            {
                _reloadQueued = false;
                if (!_lifetime.IsCancellationRequested)
                {
                    Reload(reconcile: false);
                }
            }), DispatcherPriority.Background);
        }

        /// <summary>Reads the library and every item's uses into fresh rows, keeping the selection.</summary>
        public void Reload(bool reconcile)
        {
            List<LibraryItemRow> rows;
            try
            {
                if (reconcile)
                {
                    _library.Reconcile();
                }

                rows = _library.Items.Select(BuildRow).ToList();
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "Failed reading the library.");
                rows = new List<LibraryItemRow>();
            }

            var selectedId = _selectedRow?.Id;
            Rows.Clear();
            foreach (var row in rows)
            {
                Rows.Add(row);
            }

            foreach (var filter in Filters)
            {
                filter.Count = filter.Kind == null ? rows.Count : rows.Count(row => row.Kind == filter.Kind);
            }

            SelectedRow = rows.FirstOrDefault(row => string.Equals(row.Id, selectedId, StringComparison.OrdinalIgnoreCase));
            RefreshView();
            _ = EnsureIndexAsync();
        }

        private LibraryItemRow BuildRow(LibraryItem item)
        {
            var path = _library.FullPath(item);
            if (!string.IsNullOrEmpty(path) && !File.Exists(path))
            {
                path = null;
            }

            var row = new LibraryItemRow(item, path, null, item.Kind == LibraryItemKind.Colors ? SwatchesOf(item, path) : null);
            row.Uses = _updates.UsesOf(item).Select(use => new LibraryUseRow(row, use, TargetLabel(use.TargetKey), UseState(item, use))).ToList();
            if (item.IsWorkshop && _index != null && item.WorkshopItemId != null && _index.TryGetValue(item.WorkshopItemId, out var indexItem))
            {
                row.IndexItem = indexItem;
            }

            if (item.WorkshopItemId != null && _thumbnails.TryGetValue(item.WorkshopItemId, out var thumbnail))
            {
                row.ThumbnailPath = thumbnail;
            }

            return row;
        }

        private IReadOnlyList<Brush> SwatchesOf(LibraryItem item, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            var key = path + "|" + item.ContentHash;
            if (_swatches.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var brushes = new List<Brush>();
            try
            {
                var colors = _plugin.ColorPackPortableStore.Read(path)?.RarityColors;
                foreach (var value in new[] { colors?.Common, colors?.Uncommon, colors?.Rare, colors?.UltraRare })
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    if (ColorConverter.ConvertFromString(value) is Color color)
                    {
                        var brush = new SolidColorBrush(color);
                        brush.Freeze();
                        brushes.Add(brush);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"Failed reading the colors of {path}.");
            }

            _swatches[key] = brushes;
            return brushes;
        }

        private bool FilterRow(object value)
        {
            if (!(value is LibraryItemRow row))
            {
                return false;
            }

            if (_selectedFilter?.Kind is LibraryItemKind kind && row.Kind != kind)
            {
                return false;
            }

            var query = _searchText.Trim();
            return query.Length == 0 || row.SearchText.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SelectFilter(LibraryKindFilter filter)
        {
            if (filter == null)
            {
                return;
            }

            foreach (var other in Filters)
            {
                other.IsSelected = ReferenceEquals(other, filter);
            }

            _selectedFilter = filter;
            RefreshView();
        }

        private void RefreshView()
        {
            RowsView.Refresh();
            if (_selectedRow != null && !FilterRow(_selectedRow))
            {
                SelectedRow = null;
            }

            if (_selectedRow == null)
            {
                SelectedRow = RowsView.Cast<LibraryItemRow>().FirstOrDefault();
            }

            OnPropertyChanged(nameof(IsEmpty));
        }

        private void RaiseCommandStates()
        {
            UpdateCommand?.RaiseCanExecuteChanged();
            ReinstallCommand?.RaiseCanExecuteChanged();
            ShareCommand?.RaiseCanExecuteChanged();
            RenameCommand?.RaiseCanExecuteChanged();
            DeleteCommand?.RaiseCanExecuteChanged();
        }

        // ---- labels -----------------------------------------------------------------------------

        private string TargetLabel(string key)
        {
            if (string.Equals(key, LibraryTargetKeys.Colors, StringComparison.OrdinalIgnoreCase))
            {
                return L("LOCPlayAch_Settings_Display_Colors");
            }

            if (string.Equals(key, LibraryTargetKeys.Sounds, StringComparison.OrdinalIgnoreCase))
            {
                return L("LOCPlayAch_Workshop_Share_Sounds");
            }

            var surface = key.StartsWith("frame:", StringComparison.OrdinalIgnoreCase)
                ? L("LOCPlayAch_Workshop_Share_GlobalFrame")
                : L("LOCPlayAch_Workshop_Share_GlobalStyle");
            if (string.Equals(key, LibraryTargetKeys.ToastGlobal, StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, LibraryTargetKeys.FrameGlobal, StringComparison.OrdinalIgnoreCase))
            {
                return surface;
            }

            const string providerSegment = ":provider:";
            var providerAt = key.IndexOf(providerSegment, StringComparison.OrdinalIgnoreCase);
            if (providerAt >= 0)
            {
                return surface + " · " + ProviderName(key.Substring(providerAt + providerSegment.Length));
            }

            if (key.StartsWith("showcase:", StringComparison.OrdinalIgnoreCase))
            {
                var pageId = key.Substring("showcase:".Length);
                var page = _plugin.Settings?.Persisted?.Showcase?.Pages?.FirstOrDefault(candidate =>
                    string.Equals(candidate?.PageId, pageId, StringComparison.OrdinalIgnoreCase));
                return string.Format(L("LOCPlayAch_Workshop_Share_ShowcasePage"), page?.Name ?? pageId);
            }

            if (LibraryTargetKeys.TryGetGameId(key, out var gameId))
            {
                var game = _plugin.PlayniteApi?.Database?.Games?.Get(gameId)?.Name ?? gameId.ToString();
                return key.StartsWith("gamedata:", StringComparison.OrdinalIgnoreCase) ? game : surface + " · " + game;
            }

            return key;
        }

        private static string UseState(LibraryItem item, LibraryTargetUse use)
        {
            var applied = string.IsNullOrWhiteSpace(use.Link.AppliedVersion) ? item.Version : use.Link.AppliedVersion;
            var parts = new List<string>
            {
                string.Format(
                    L("LOCPlayAch_Library_Following"),
                    item.IsWorkshop && !string.IsNullOrWhiteSpace(applied) ? "v" + applied : item.Name)
            };
            if (use.IsEdited)
            {
                parts.Add(L("LOCPlayAch_Library_Edited"));
            }

            if (use.IsUpdateAvailable)
            {
                parts.Add(L("LOCPlayAch_Workshop_UpdateAvailable"));
            }

            return string.Join(" · ", parts);
        }

        private static string ProviderName(string providerKey)
        {
            var name = ResourceProvider.GetString("LOCPlayAch_Provider_" + providerKey);
            return string.IsNullOrWhiteSpace(name) || name.StartsWith("<!", StringComparison.Ordinal) ? providerKey : name;
        }

        private static string L(string key) => ResourceProvider.GetString(key);

        // ---- Workshop index ---------------------------------------------------------------------

        /// <summary>
        /// Reads the Workshop index once, so Workshop items learn their author, newer versions
        /// and preview images. A failure only leaves those out.
        /// </summary>
        private async Task EnsureIndexAsync()
        {
            if (_indexRequested || !Rows.Any(row => row.IsWorkshop))
            {
                return;
            }

            _indexRequested = true;
            try
            {
                var index = await _plugin.WorkshopClient.FetchIndexAsync(_lifetime.Token);
                _index = (index?.Items ?? new List<WorkshopItem>())
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Id))
                    .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
                foreach (var row in Rows.Where(row => row.IsWorkshop && row.Item.WorkshopItemId != null))
                {
                    if (_index.TryGetValue(row.Item.WorkshopItemId, out var item))
                    {
                        row.IndexItem = item;
                    }
                }

                RaiseCommandStates();
                await FetchThumbnailsAsync();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "The Library page could not read the Workshop index.");
            }
        }

        private async Task FetchThumbnailsAsync()
        {
            foreach (var item in _index.Values.Where(item => Rows.Any(row => string.Equals(row.Item.WorkshopItemId, item.Id, StringComparison.OrdinalIgnoreCase))).ToList())
            {
                if (_lifetime.IsCancellationRequested)
                {
                    return;
                }

                try
                {
                    var path = !string.IsNullOrWhiteSpace(item.Urls?.Cover)
                        ? await _plugin.WorkshopClient.FetchCoverAsync(item, _lifetime.Token)
                        : null;
                    if (path == null && !string.IsNullOrWhiteSpace(item.Urls?.Preview))
                    {
                        path = await _plugin.WorkshopClient.FetchPreviewAsync(item, _lifetime.Token);
                    }

                    if (path == null)
                    {
                        continue;
                    }

                    _thumbnails[item.Id] = path;
                    foreach (var row in Rows.Where(row => string.Equals(row.Item.WorkshopItemId, item.Id, StringComparison.OrdinalIgnoreCase)))
                    {
                        row.ThumbnailPath = path;
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger?.Debug(ex, $"Failed fetching the Workshop image of {item.Id}.");
                }
            }
        }

        // ---- actions ----------------------------------------------------------------------------

        /// <summary>
        /// Takes a newer Workshop version into the library and merges it into every place that
        /// follows the item; for a re-saved preset, merges it into the places that have not
        /// taken it yet. Edits made in those places are kept.
        /// </summary>
        private async Task UpdateAsync(LibraryItemRow row)
        {
            if (row == null || IsBusy)
            {
                return;
            }

            if (row.HasUpdate)
            {
                await InstallFromWorkshopAsync(row, LibraryApplyMode.Merge, isUpdate: true);
                return;
            }

            Run(() =>
            {
                var report = _updates.MergeIntoTargets(row.Id, LibraryApplyMode.Merge);
                var notes = new List<string>();
                if (report.KeptEdits > 0)
                {
                    notes.Add(string.Format(L("LOCPlayAch_Workshop_UpdateKeptEdits"), report.KeptEdits));
                }

                if (report.PendingTargets.Count > 0)
                {
                    notes.Add(L("LOCPlayAch_Library_UpdatesWhenApplied"));
                }

                StatusMessage = notes.Count > 0 ? string.Join("\n", notes) : L("LOCPlayAch_Status_Succeeded");
            }, $"Failed updating the followers of {row.Id}.");
        }

        /// <summary>Downloads the item again and writes it over the library copy; places that follow it merge or take it as published.</summary>
        private async Task ReinstallAsync(LibraryItemRow row)
        {
            if (row == null || IsBusy || !row.CanReinstall)
            {
                return;
            }

            var mode = LibraryApplyMode.Replace;
            if (row.HasUses)
            {
                var choice = ChooseMergeOrReplace?.Invoke(string.Format(L("LOCPlayAch_Library_ReinstallChoice"), row.Name, row.Uses.Count));
                if (choice == null)
                {
                    return;
                }

                mode = choice.Value;
            }

            await InstallFromWorkshopAsync(row, mode, isUpdate: false);
        }

        private async Task InstallFromWorkshopAsync(LibraryItemRow row, LibraryApplyMode mode, bool isUpdate)
        {
            var item = row.IndexItem;
            if (item == null)
            {
                return;
            }

            IsBusy = true;
            ErrorMessage = null;
            var scratch = Path.Combine(Path.GetTempPath(), "PlayniteAchievements", "LibraryInstall", Guid.NewGuid().ToString("N"));
            try
            {
                StatusMessage = string.Format(L("LOCPlayAch_Workshop_Downloading"), item.Name);
                Directory.CreateDirectory(scratch);
                var downloaded = Path.Combine(scratch, "download", item.Package?.File ?? "package.zip");
                Directory.CreateDirectory(Path.GetDirectoryName(downloaded));
                await _plugin.WorkshopClient.DownloadPackageAsync(item, downloaded, null, _lifetime.Token);

                StatusMessage = L("LOCPlayAch_Workshop_Installing");
                var installer = _plugin.WorkshopInstaller;
                var combined = new WorkshopInstallResult();
                var requests = BuildRequests(row, item, mode);
                for (var i = 0; i < requests.Count; i++)
                {
                    // Each install consumes its own copy of the package.
                    var copy = Path.Combine(scratch, i.ToString(), item.Package?.File ?? "package.zip");
                    Directory.CreateDirectory(Path.GetDirectoryName(copy));
                    File.Copy(downloaded, copy, overwrite: true);
                    requests[i].PackagePath = copy;
                    var result = await installer.InstallAsync(requests[i], _lifetime.Token);
                    Merge(combined, result);
                }

                if (requests.Count == 0)
                {
                    // Game data no game follows: the library takes the new version alone.
                    _updates.RecordWorkshopItem(new LibraryItem
                    {
                        Id = row.Id,
                        Kind = row.Kind,
                        Name = row.Name,
                        Origin = LibraryItemOrigin.Workshop,
                        WorkshopItemId = item.Id,
                        Version = item.Version,
                        Author = item.Author
                    });
                }

                // A reinstall wrote over a copy the library already listed, so it reads as an update.
                StatusMessage = WorkshopViewModel.DescribeInstall(combined, isUpdate: true);
            }
            catch (OperationCanceledException)
            {
                StatusMessage = null;
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed installing Workshop item {item.Id} from the library.");
                ErrorMessage = string.Format(L("LOCPlayAch_Status_Failed"), ex.Message);
                StatusMessage = null;
            }
            finally
            {
                IsBusy = false;
                PortablePackage.TryDeleteDirectory(scratch);
                Reload(reconcile: false);
            }
        }

        /// <summary>
        /// The installs that bring a Workshop item back: one for looks and showcase pages (only
        /// the bundle parts the library holds), one per game that follows game data.
        /// </summary>
        private List<WorkshopInstallRequest> BuildRequests(LibraryItemRow row, WorkshopItem item, LibraryApplyMode mode)
        {
            var requests = new List<WorkshopInstallRequest>();
            if (row.Kind == LibraryItemKind.GameData)
            {
                foreach (var key in _gameLinks.TargetsOf(row.Id))
                {
                    if (LibraryTargetKeys.TryGetGameId(key, out var gameId) && key.StartsWith("gamedata:", StringComparison.OrdinalIgnoreCase))
                    {
                        requests.Add(new WorkshopInstallRequest
                        {
                            Item = item,
                            TargetGameId = gameId,
                            GameDataMode = mode == LibraryApplyMode.Merge
                                ? WorkshopGameDataInstallMode.KeepEditsSinceInstall
                                : WorkshopGameDataInstallMode.Replace
                        });
                    }
                }

                return requests;
            }

            var request = new WorkshopInstallRequest { Item = item, FollowerMode = mode };
            if (item.Kind == WorkshopItemKind.Bundle)
            {
                var parts = BundleParts.None;
                foreach (var owned in _library.Items.Where(candidate =>
                             string.Equals(candidate.WorkshopItemId, item.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    if (Enum.TryParse(owned.Part, ignoreCase: true, out BundleParts part))
                    {
                        parts |= part;
                    }
                }

                request.Parts = parts == BundleParts.None ? BundleParts.All : parts;
            }

            requests.Add(request);
            return requests;
        }

        private static void Merge(WorkshopInstallResult into, WorkshopInstallResult result)
        {
            into.PresetNames.AddRange(result.PresetNames);
            into.LibraryItemIds.AddRange(result.LibraryItemIds);
            into.Warnings.AddRange(result.Warnings);
            into.UpdatedTargets += result.UpdatedTargets;
            into.KeptEdits += result.KeptEdits;
            into.PendingTargets += result.PendingTargets;
        }

        private void Share(LibraryItemRow row)
        {
            if (row?.HasFile == true)
            {
                OpenShare?.Invoke(LibraryItemRow.WorkshopKindOf(row.Kind), row.FilePath, row.Name);
            }
        }

        /// <summary>
        /// Renames the item, and its preset file with it; a name another preset has gets a free
        /// variant. The places that follow the item keep following it.
        /// </summary>
        private void Rename(LibraryItemRow row)
        {
            if (row == null)
            {
                return;
            }

            var requested = AskName?.Invoke(row.Name);
            if (string.IsNullOrWhiteSpace(requested) || string.Equals(requested, row.Name, StringComparison.Ordinal))
            {
                return;
            }

            Run(() =>
            {
                var item = row.Item.Clone();
                item.Name = requested;
                if (row.HasFile)
                {
                    var directory = Path.GetDirectoryName(row.FilePath);
                    var folder = new DirectoryPackageFolder(directory, LibraryStore.ExtensionOf(row.Kind));
                    var current = folder.NameOf(row.FilePath);
                    var name = string.Equals(current, requested, StringComparison.OrdinalIgnoreCase)
                        ? requested
                        : folder.UniqueName(requested);
                    var destination = Path.Combine(directory, name + LibraryStore.ExtensionOf(row.Kind));
                    if (!string.Equals(destination, row.FilePath, StringComparison.Ordinal))
                    {
                        File.Move(row.FilePath, destination);
                    }

                    item.RelativePath = destination;
                    item.Name = row.IsWorkshop ? requested : name;
                }

                _library.Upsert(item);
            }, $"Failed renaming the library item {row.Id}.");
        }

        /// <summary>Deletes the item and its file after a Playnite confirmation; the places that used it keep their look.</summary>
        private void Delete(LibraryItemRow row)
        {
            if (row == null)
            {
                return;
            }

            if (Confirm != null && !Confirm(string.Format(L("LOCPlayAch_Library_DeleteConfirm"), row.Name)))
            {
                return;
            }

            Run(() =>
            {
                if (row.HasFile && File.Exists(row.FilePath))
                {
                    File.Delete(row.FilePath);
                }

                _library.Remove(row.Id);
                _updates.UnlinkItem(row.Id);
            }, $"Failed deleting the library item {row.Id}.");
        }

        private void Reset(LibraryUseRow use)
        {
            if (use == null)
            {
                return;
            }

            Run(() => _updates.Reset(use.TargetKey), $"Failed resetting {use.TargetKey}.");
        }

        private void StopFollowing(LibraryUseRow use)
        {
            if (use == null)
            {
                return;
            }

            Run(() => _updates.StopFollowing(use.TargetKey), $"Failed ending the link of {use.TargetKey}.");
        }

        private void Run(Action action, string failure)
        {
            ErrorMessage = null;
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, failure);
                ErrorMessage = string.Format(L("LOCPlayAch_Status_Failed"), ex.Message);
            }

            Reload(reconcile: false);
        }

        public void Dispose()
        {
            _library.Changed -= Source_Changed;
            _gameLinks.Changed -= Source_Changed;
            _settingsSubscription?.Dispose();
            _lifetime.Cancel();
            _lifetime.Dispose();
        }
    }
}
