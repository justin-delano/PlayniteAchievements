using Playnite.SDK;
using Playnite.SDK.Plugins;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Friends;
using PlayniteAchievements.Providers;
using PlayniteAchievements.Services;
using PlayniteAchievements.Services.Friends;
using PlayniteAchievements.Services.UI;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.Views.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace PlayniteAchievements.Views
{
    public partial class FriendCustomRefreshControl : UserControl, INotifyPropertyChanged
    {
        public sealed class ScopeOptionItem
        {
            public FriendRefreshScope Scope { get; set; }
            public string DisplayName { get; set; }
        }

        public sealed class ProviderOptionItem : PlayniteAchievements.Common.ObservableObject
        {
            private bool _isSelected;
            private bool _isEnabled;
            private bool _isAuthenticated;
            private readonly string _enabledAndAuthText;
            private readonly string _disabledText;
            private readonly string _noAuthText;

            public string ProviderKey { get; }
            public string ProviderName { get; }

            public bool IsEnabled
            {
                get => _isEnabled;
                set
                {
                    if (SetValueAndReturn(ref _isEnabled, value))
                    {
                        OnStateChanged();
                    }
                }
            }

            public bool IsAuthenticated
            {
                get => _isAuthenticated;
                set
                {
                    if (SetValueAndReturn(ref _isAuthenticated, value))
                    {
                        OnStateChanged();
                    }
                }
            }

            public bool IsSelectable => IsEnabled && IsAuthenticated;

            public string StatusText
            {
                get
                {
                    if (!IsEnabled)
                    {
                        return _disabledText;
                    }

                    if (!IsAuthenticated)
                    {
                        return _noAuthText;
                    }

                    return _enabledAndAuthText;
                }
            }

            public bool IsSelected
            {
                get => _isSelected;
                set => SetValue(ref _isSelected, value);
            }

            public ProviderOptionItem(
                string providerKey,
                string providerName,
                bool isEnabled,
                bool isAuthenticated,
                string enabledAndAuthText,
                string disabledText,
                string noAuthText)
            {
                ProviderKey = providerKey;
                ProviderName = providerName;
                _enabledAndAuthText = enabledAndAuthText;
                _disabledText = disabledText;
                _noAuthText = noAuthText;
                _isEnabled = isEnabled;
                _isAuthenticated = isAuthenticated;
            }

            private void OnStateChanged()
            {
                OnPropertyChanged(nameof(IsSelectable));
                OnPropertyChanged(nameof(StatusText));

                if (!IsSelectable && IsSelected)
                {
                    IsSelected = false;
                }
            }
        }

        public sealed class SelectionItem : PlayniteAchievements.Common.ObservableObject
        {
            private bool _isSelected;

            public string Id { get; set; }
            public string DisplayName { get; set; }
            public string ProviderKey { get; set; }

            public bool IsSelected
            {
                get => _isSelected;
                set => SetValue(ref _isSelected, value);
            }
        }

        private const string WindowPlacementKey = "FriendCustomRefresh";

        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly IPlayniteAPI _api;
        private readonly RefreshRuntime _refreshRuntime;
        private readonly IFriendCacheManager _friendCache;
        private readonly Guid? _selectedGameId;
        private readonly string _selectedGameName;
        private FriendRefreshScope _selectedScope = FriendRefreshScope.Recent;
        private string _summaryText;
        private bool _canRun;
        private string _friendListSearchText;
        private string _gameListSearchText;
        private bool _showLibraryGames;

        private readonly Dictionary<string, IDataProvider> _providersByKey =
            new Dictionary<string, IDataProvider>(StringComparer.OrdinalIgnoreCase);
        private readonly List<SelectionItem> _allFriendItems = new List<SelectionItem>();
        private readonly List<SelectionItem> _allSharedGameItems = new List<SelectionItem>();
        private readonly List<SelectionItem> _allLibraryGameItems = new List<SelectionItem>();
        private readonly ICollectionView _friendView;
        private readonly ICollectionView _sharedGameView;
        private readonly ICollectionView _libraryGameView;

        public event EventHandler RequestClose;
        public event PropertyChangedEventHandler PropertyChanged;

        public bool? DialogResult { get; private set; }
        public FriendCustomRefreshOptions ResultOptions { get; private set; }

        public ObservableCollection<ProviderOptionItem> ProviderOptions { get; } =
            new ObservableCollection<ProviderOptionItem>();

        public ObservableCollection<ScopeOptionItem> ScopeOptions { get; } =
            new ObservableCollection<ScopeOptionItem>();

        public ICollectionView FriendItems => _friendView;

        public ICollectionView GameItems => _showLibraryGames ? _libraryGameView : _sharedGameView;

        public FriendRefreshScope SelectedScope
        {
            get => _selectedScope;
            set
            {
                if (_selectedScope == value)
                {
                    return;
                }

                _selectedScope = value;
                OnPropertyChanged(nameof(SelectedScope));
                RecalculateSummary();
            }
        }

        public string FriendListSearchText
        {
            get => _friendListSearchText;
            set
            {
                if (string.Equals(_friendListSearchText, value, StringComparison.Ordinal))
                {
                    return;
                }

                _friendListSearchText = value;
                OnPropertyChanged(nameof(FriendListSearchText));
                _friendView?.Refresh();
            }
        }

        public string GameListSearchText
        {
            get => _gameListSearchText;
            set
            {
                if (string.Equals(_gameListSearchText, value, StringComparison.Ordinal))
                {
                    return;
                }

                _gameListSearchText = value;
                OnPropertyChanged(nameof(GameListSearchText));
                _sharedGameView?.Refresh();
                _libraryGameView?.Refresh();
            }
        }

        public bool ShowLibraryGames
        {
            get => _showLibraryGames;
            set
            {
                if (_showLibraryGames == value)
                {
                    return;
                }

                _showLibraryGames = value;
                OnPropertyChanged(nameof(ShowLibraryGames));
                OnPropertyChanged(nameof(GameItems));
            }
        }

        public string SummaryText
        {
            get => _summaryText;
            private set
            {
                if (string.Equals(_summaryText, value, StringComparison.Ordinal))
                {
                    return;
                }

                _summaryText = value;
                OnPropertyChanged(nameof(SummaryText));
            }
        }

        public bool CanRun
        {
            get => _canRun;
            private set
            {
                if (_canRun == value)
                {
                    return;
                }

                _canRun = value;
                OnPropertyChanged(nameof(CanRun));
            }
        }

        internal FriendCustomRefreshControl(
            IPlayniteAPI api,
            RefreshRuntime refreshRuntime,
            IFriendCacheManager friendCache,
            Guid? selectedGameId,
            string selectedGameName)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _refreshRuntime = refreshRuntime ?? throw new ArgumentNullException(nameof(refreshRuntime));
            _friendCache = friendCache;
            _selectedGameId = selectedGameId.HasValue && selectedGameId.Value != Guid.Empty ? selectedGameId : null;
            _selectedGameName = selectedGameName;

            InitializeComponent();
            DataContext = this;
            InitializeProviders();
            InitializeScopes();
            LoadSelectionData();

            _friendView = CollectionViewSource.GetDefaultView(_allFriendItems);
            _friendView.Filter = FriendFilter;
            _sharedGameView = CollectionViewSource.GetDefaultView(_allSharedGameItems);
            _sharedGameView.Filter = SharedGameFilter;
            _libraryGameView = CollectionViewSource.GetDefaultView(_allLibraryGameItems);
            _libraryGameView.Filter = LibraryGameFilter;

            RecalculateSummary();
            _ = RefreshProviderAuthAsync();
        }

        internal static bool TryShowDialog(
            IPlayniteAPI api,
            RefreshRuntime refreshRuntime,
            IFriendCacheManager friendCache,
            Action persistSettingsForUi,
            PlayniteAchievementsSettings settings,
            ILogger logger,
            Guid? selectedGameId,
            string selectedGameName,
            out FriendCustomRefreshOptions options)
        {
            options = null;
            var control = new FriendCustomRefreshControl(api, refreshRuntime, friendCache, selectedGameId, selectedGameName);
            var window = PlayniteUiProvider.CreateExtensionWindow(
                ResourceProvider.GetString("LOCPlayAch_RefreshMode_FriendsCustom") ?? "Friends Custom Refresh",
                control,
                new WindowOptions
                {
                    Width = 760,
                    Height = 640,
                    CanBeResizable = true,
                    ShowCloseButton = true,
                    ShowMinimizeButton = false,
                    ShowMaximizeButton = false
                });

            window.MinWidth = 620;
            window.MinHeight = 480;
            WindowPlacementPersistenceService.Attach(
                window,
                settings?.Persisted,
                persistSettingsForUi,
                WindowPlacementKey,
                logger);
            control.RequestClose += (_, __) => window.Close();
            window.ShowDialog();

            if (control.DialogResult == true && control.ResultOptions != null)
            {
                options = control.ResultOptions;
                return true;
            }

            return false;
        }

        private void InitializeProviders()
        {
            var readyText = L("LOCPlayAch_CustomRefresh_ProviderStatus_Ready", "Ready");
            var disabledText = L("LOCPlayAch_Common_Status_Disabled", "Disabled");
            var noAuthText = L("LOCPlayAch_Common_NotAuthenticated", "Not authenticated");

            foreach (var provider in _refreshRuntime.Providers ?? Array.Empty<IDataProvider>())
            {
                if (provider?.Friends == null || ProviderUiPolicies.ShouldHideFromSetupSurfaces(provider.ProviderKey))
                {
                    continue;
                }

                var isEnabled = _refreshRuntime.ProviderRegistry?.IsProviderEnabled(provider.ProviderKey) ?? true;
                var isAuthenticated = isEnabled && provider.AuthSession == null && provider.IsAuthenticated;
                var option = new ProviderOptionItem(
                    provider.ProviderKey,
                    provider.ProviderName,
                    isEnabled,
                    isAuthenticated,
                    readyText,
                    disabledText,
                    noAuthText)
                {
                    IsSelected = isEnabled && isAuthenticated
                };
                option.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(ProviderOptionItem.IsSelected))
                    {
                        _friendView?.Refresh();
                        _sharedGameView?.Refresh();
                        RecalculateSummary();
                    }
                };
                ProviderOptions.Add(option);
                _providersByKey[provider.ProviderKey] = provider;
            }
        }

        private async Task RefreshProviderAuthAsync()
        {
            foreach (var option in ProviderOptions)
            {
                if (!_providersByKey.TryGetValue(option.ProviderKey, out var provider) || provider == null)
                {
                    continue;
                }

                try
                {
                    var isEnabled = _refreshRuntime.ProviderRegistry?.IsProviderEnabled(provider.ProviderKey) ?? true;
                    option.IsEnabled = isEnabled;
                    var wasSelectable = option.IsSelectable;
                    option.IsAuthenticated = isEnabled &&
                        await _refreshRuntime.IsProviderAuthenticatedAsync(provider, CancellationToken.None).ConfigureAwait(true);

                    // Default newly-confirmed providers to selected so the run is ready without re-checking.
                    if (!wasSelectable && option.IsSelectable)
                    {
                        option.IsSelected = true;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    option.IsAuthenticated = false;
                    Logger?.Warn(ex, $"Failed to refresh friend custom auth state for provider {option.ProviderKey}.");
                }
            }

            _friendView?.Refresh();
            _sharedGameView?.Refresh();
            _libraryGameView?.Refresh();
            RecalculateSummary();
        }

        private void InitializeScopes()
        {
            ScopeOptions.Add(new ScopeOptionItem { Scope = FriendRefreshScope.Recent, DisplayName = L("LOCPlayAch_RefreshModeShort_FriendsRecent", "Recent") });
            ScopeOptions.Add(new ScopeOptionItem { Scope = FriendRefreshScope.Full, DisplayName = L("LOCPlayAch_RefreshModeShort_FriendsFull", "Full") });
            ScopeOptions.Add(new ScopeOptionItem { Scope = FriendRefreshScope.Shared, DisplayName = L("LOCPlayAch_RefreshModeShort_FriendsShared", "Shared") });
            ScopeOptions.Add(new ScopeOptionItem { Scope = FriendRefreshScope.Installed, DisplayName = L("LOCPlayAch_RefreshModeShort_FriendsInstalled", "Installed") });
            if (_selectedGameId.HasValue)
            {
                ScopeOptions.Add(new ScopeOptionItem
                {
                    Scope = FriendRefreshScope.SelectedGame,
                    DisplayName = string.Format(
                        L("LOCPlayAch_FriendCustomRefresh_SelectedGameScope", "Selected game: {0}"),
                        string.IsNullOrWhiteSpace(_selectedGameName) ? _selectedGameId.Value.ToString("D") : _selectedGameName)
                });
            }
        }

        private void LoadSelectionData()
        {
            try
            {
                var data = _friendCache?.LoadFriendsOverviewData(false, 0);
                if (data != null)
                {
                    foreach (var friend in data.Friends ?? Enumerable.Empty<FriendSummaryItem>())
                    {
                        if (friend == null || string.IsNullOrWhiteSpace(friend.ExternalUserId))
                        {
                            continue;
                        }

                        _allFriendItems.Add(new SelectionItem
                        {
                            Id = friend.ExternalUserId,
                            DisplayName = string.IsNullOrWhiteSpace(friend.DisplayName) ? friend.ExternalUserId : friend.DisplayName,
                            ProviderKey = friend.ProviderKey
                        });
                    }

                    var seenGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var game in data.Games ?? Enumerable.Empty<FriendGameSummaryItem>())
                    {
                        if (game?.PlayniteGameId == null || game.PlayniteGameId == Guid.Empty)
                        {
                            continue;
                        }

                        var id = game.PlayniteGameId.Value.ToString();
                        if (!seenGames.Add(id))
                        {
                            continue;
                        }

                        _allSharedGameItems.Add(new SelectionItem
                        {
                            Id = id,
                            DisplayName = string.IsNullOrWhiteSpace(game.GameName) ? id : game.GameName,
                            ProviderKey = game.ProviderKey
                        });
                    }
                }

                foreach (var game in _api.Database?.Games ?? Enumerable.Empty<Playnite.SDK.Models.Game>())
                {
                    if (game == null || game.Id == Guid.Empty)
                    {
                        continue;
                    }

                    _allLibraryGameItems.Add(new SelectionItem
                    {
                        Id = game.Id.ToString(),
                        DisplayName = string.IsNullOrWhiteSpace(game.Name) ? game.Id.ToString() : game.Name
                    });
                }

                _allFriendItems.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase));
                _allSharedGameItems.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase));
                _allLibraryGameItems.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase));
            }
            catch (Exception ex)
            {
                Logger?.Warn(ex, "Failed to load friend custom refresh selection data.");
            }
        }

        private HashSet<string> GetSelectedProviderKeys()
        {
            return new HashSet<string>(
                ProviderOptions
                    .Where(option => option.IsSelectable && option.IsSelected)
                    .Select(option => option.ProviderKey)
                    .Where(key => !string.IsNullOrWhiteSpace(key)),
                StringComparer.OrdinalIgnoreCase);
        }

        private bool FriendFilter(object item)
        {
            if (!(item is SelectionItem selection))
            {
                return false;
            }

            var providers = GetSelectedProviderKeys();
            if (providers.Count > 0 && !providers.Contains(selection.ProviderKey ?? string.Empty))
            {
                return false;
            }

            return MatchesSearch(selection.DisplayName, _friendListSearchText);
        }

        private bool SharedGameFilter(object item)
        {
            if (!(item is SelectionItem selection))
            {
                return false;
            }

            var providers = GetSelectedProviderKeys();
            if (providers.Count > 0 && !providers.Contains(selection.ProviderKey ?? string.Empty))
            {
                return false;
            }

            return MatchesSearch(selection.DisplayName, _gameListSearchText);
        }

        private bool LibraryGameFilter(object item)
        {
            return item is SelectionItem selection && MatchesSearch(selection.DisplayName, _gameListSearchText);
        }

        private static bool MatchesSearch(string text, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }

            return (text ?? string.Empty).IndexOf(query.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private void RunButton_Click(object sender, RoutedEventArgs e)
        {
            var providerKeys = ProviderOptions
                .Where(option => option.IsSelectable && option.IsSelected)
                .Select(option => option.ProviderKey)
                .ToList();

            var friendIds = _allFriendItems
                .Where(item => item.IsSelected)
                .Select(item => item.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var gameSource = ShowLibraryGames ? _allLibraryGameItems : _allSharedGameItems;
            var gameIds = gameSource
                .Where(item => item.IsSelected)
                .Select(item => item.Id)
                .Where(id => Guid.TryParse(id, out _))
                .Select(Guid.Parse)
                .Distinct()
                .ToList();

            if (gameIds.Count == 0 && SelectedScope == FriendRefreshScope.SelectedGame && _selectedGameId.HasValue)
            {
                gameIds.Add(_selectedGameId.Value);
            }

            ResultOptions = new FriendCustomRefreshOptions
            {
                ProviderKeys = providerKeys,
                Scope = SelectedScope,
                LibraryScope = FriendRefreshPolicy.GetDefaultLibraryScope(SelectedScope),
                PlayniteGameIds = gameIds.Count > 0 ? gameIds : null,
                FriendExternalUserIds = friendIds.Count > 0 ? friendIds : null
            };
            DialogResult = true;
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        private void RecalculateSummary()
        {
            var selectedProviders = ProviderOptions
                .Where(option => option.IsSelectable && option.IsSelected)
                .Select(option => option.ProviderName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();
            var providerDisplay = selectedProviders.Count == 0
                ? L("LOCPlayAch_CustomRefresh_None", "None")
                : string.Join(", ", selectedProviders);
            var scopeDisplay = ScopeOptions
                .FirstOrDefault(option => option.Scope == SelectedScope)
                ?.DisplayName ?? SelectedScope.ToString();

            SummaryText = string.Format(
                L("LOCPlayAch_FriendCustomRefresh_SummaryFormat", "Providers: {0} | Scope: {1}"),
                providerDisplay,
                scopeDisplay);
            CanRun = selectedProviders.Count > 0 &&
                     (SelectedScope != FriendRefreshScope.SelectedGame || _selectedGameId.HasValue);
        }

        private static string L(string key, string fallback)
        {
            var value = ResourceProvider.GetString(key);
            return string.IsNullOrWhiteSpace(value) || value == key ? fallback : value;
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
