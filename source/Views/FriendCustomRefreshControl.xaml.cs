using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Friends;
using PlayniteAchievements.Providers;
using PlayniteAchievements.Services;
using PlayniteAchievements.Services.UI;
using PlayniteAchievements.Views.Helpers;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace PlayniteAchievements.Views
{
    public partial class FriendCustomRefreshControl : UserControl, INotifyPropertyChanged
    {
        public sealed class ScopeOptionItem
        {
            public FriendRefreshScope Scope { get; set; }
            public string DisplayName { get; set; }
        }

        public sealed class ProviderOptionItem : ObservableObject
        {
            private bool _isSelected;

            public string ProviderKey { get; set; }
            public string ProviderName { get; set; }
            public bool IsSelectable { get; set; }
            public string StatusText { get; set; }

            public bool IsSelected
            {
                get => _isSelected;
                set => SetValue(ref _isSelected, value);
            }
        }

        private const string WindowPlacementKey = "FriendCustomRefresh";

        private readonly IPlayniteAPI _api;
        private readonly RefreshRuntime _refreshRuntime;
        private readonly Guid? _selectedGameId;
        private readonly string _selectedGameName;
        private FriendRefreshScope _selectedScope = FriendRefreshScope.Recent;
        private string _summaryText;
        private bool _canRun;

        public event EventHandler RequestClose;
        public event PropertyChangedEventHandler PropertyChanged;

        public bool? DialogResult { get; private set; }
        public FriendCustomRefreshOptions ResultOptions { get; private set; }

        public ObservableCollection<ProviderOptionItem> ProviderOptions { get; } =
            new ObservableCollection<ProviderOptionItem>();

        public ObservableCollection<ScopeOptionItem> ScopeOptions { get; } =
            new ObservableCollection<ScopeOptionItem>();

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

        public FriendCustomRefreshControl(
            IPlayniteAPI api,
            RefreshRuntime refreshRuntime,
            Guid? selectedGameId,
            string selectedGameName)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _refreshRuntime = refreshRuntime ?? throw new ArgumentNullException(nameof(refreshRuntime));
            _selectedGameId = selectedGameId.HasValue && selectedGameId.Value != Guid.Empty ? selectedGameId : null;
            _selectedGameName = selectedGameName;

            InitializeComponent();
            DataContext = this;
            InitializeProviders();
            InitializeScopes();
            RecalculateSummary();
        }

        public static bool TryShowDialog(
            IPlayniteAPI api,
            RefreshRuntime refreshRuntime,
            Action persistSettingsForUi,
            PlayniteAchievementsSettings settings,
            ILogger logger,
            Guid? selectedGameId,
            string selectedGameName,
            out FriendCustomRefreshOptions options)
        {
            options = null;
            var control = new FriendCustomRefreshControl(api, refreshRuntime, selectedGameId, selectedGameName);
            var window = PlayniteUiProvider.CreateExtensionWindow(
                ResourceProvider.GetString("LOCPlayAch_RefreshMode_FriendsCustom") ?? "Friends Custom Refresh",
                control,
                new WindowOptions
                {
                    Width = 560,
                    Height = 440,
                    CanBeResizable = true,
                    ShowCloseButton = true,
                    ShowMinimizeButton = false,
                    ShowMaximizeButton = false
                });

            window.MinWidth = 480;
            window.MinHeight = 360;
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
                var isAuthenticated = provider.AuthSession == null && provider.IsAuthenticated;
                var option = new ProviderOptionItem
                {
                    ProviderKey = provider.ProviderKey,
                    ProviderName = provider.ProviderName,
                    IsSelectable = isEnabled && isAuthenticated,
                    IsSelected = isEnabled && isAuthenticated,
                    StatusText = !isEnabled ? disabledText : (isAuthenticated ? readyText : noAuthText)
                };
                option.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(ProviderOptionItem.IsSelected))
                    {
                        RecalculateSummary();
                    }
                };
                ProviderOptions.Add(option);
            }
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

        private void RunButton_Click(object sender, RoutedEventArgs e)
        {
            var providerKeys = ProviderOptions
                .Where(option => option.IsSelectable && option.IsSelected)
                .Select(option => option.ProviderKey)
                .ToList();

            ResultOptions = new FriendCustomRefreshOptions
            {
                ProviderKeys = providerKeys,
                Scope = SelectedScope,
                PlayniteGameIds = SelectedScope == FriendRefreshScope.SelectedGame && _selectedGameId.HasValue
                    ? new[] { _selectedGameId.Value }
                    : null
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
