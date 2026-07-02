using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Friends;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Providers;
using PlayniteAchievements.Providers.Exophase;
using PlayniteAchievements.Services;
using PlayniteAchievements.Services.Friends;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using ObservableObject = PlayniteAchievements.Common.ObservableObject;
using RelayCommand = PlayniteAchievements.Common.RelayCommand;

namespace PlayniteAchievements.ViewModels
{
    internal sealed class FriendsSettingsViewModel : ObservableObject
    {
        private readonly PlayniteAchievementsSettings _settings;
        private readonly PlayniteAchievementsPlugin _plugin;
        private readonly ProviderRegistry _providerRegistry;
        private readonly ILogger _logger;
        private readonly IFriendCacheManager _friendCache;
        private string _manualExophaseUsername;
        private string _statusText;
        private bool _isBusy;

        public FriendsSettingsViewModel(
            PlayniteAchievementsSettings settings,
            PlayniteAchievementsPlugin plugin,
            ProviderRegistry providerRegistry,
            ILogger logger)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _plugin = plugin;
            _providerRegistry = providerRegistry;
            _logger = logger;
            _friendCache = plugin?.RefreshRuntime?.Cache as IFriendCacheManager;

            AutoDiscoverProviders = new ObservableCollection<FriendAutoDiscoverProviderItem>();
            Friends = new ObservableCollection<FriendSettingsRowItem>();
            RefreshAutoDiscoverCommand = new AsyncCommand(_ => RefreshAutoDiscoverAsync(), _ => !IsBusy);
            AddManualFriendCommand = new AsyncCommand(_ => AddManualExophaseFriendAsync(), _ => !IsBusy && !string.IsNullOrWhiteSpace(ManualExophaseUsername));
            RemoveFriendCommand = new RelayCommand(RemoveFriend, value => value is FriendSettingsRowItem row && row.CanRemove);

            Initialize();
        }

        public ObservableCollection<FriendAutoDiscoverProviderItem> AutoDiscoverProviders { get; }

        public ObservableCollection<FriendSettingsRowItem> Friends { get; }

        public ICommand RefreshAutoDiscoverCommand { get; }

        public ICommand AddManualFriendCommand { get; }

        public ICommand RemoveFriendCommand { get; }

        public string ManualExophaseUsername
        {
            get => _manualExophaseUsername;
            set
            {
                if (SetValueAndReturn(ref _manualExophaseUsername, value))
                {
                    (AddManualFriendCommand as AsyncCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public string StatusText
        {
            get => _statusText;
            private set
            {
                if (SetValueAndReturn(ref _statusText, value))
                {
                    OnPropertyChanged(nameof(HasStatusText));
                }
            }
        }

        public bool HasStatusText => !string.IsNullOrWhiteSpace(StatusText);

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetValueAndReturn(ref _isBusy, value))
                {
                    (RefreshAutoDiscoverCommand as AsyncCommand)?.RaiseCanExecuteChanged();
                    (AddManualFriendCommand as AsyncCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        private void Initialize()
        {
            var migrated = _settings.Persisted?.MigrateLegacyProviderFriends() == true;
            var seeded = false;
            if (_friendCache != null)
            {
                seeded |= FriendSettingsSyncService.MergeCachedFriends(_settings.Persisted, _friendCache, "Steam");
                seeded |= FriendSettingsSyncService.MergeCachedFriends(_settings.Persisted, _friendCache, "Exophase", FriendSettingsSource.Manual);
                FriendSettingsSyncService.SyncConfiguredFriendsToCache(_settings.Persisted, _friendCache, _logger);
            }

            BuildAutoDiscoverProviders();
            RebuildFriends();
            if (migrated || seeded)
            {
                PersistAndNotify(null);
            }
        }

        private void BuildAutoDiscoverProviders()
        {
            AutoDiscoverProviders.Clear();
            AutoDiscoverProviders.Add(new FriendAutoDiscoverProviderItem(
                "Steam",
                ProviderRegistry.GetLocalizedName("Steam"),
                isAvailable: IsFriendProviderAvailable("Steam"),
                isSelected: _settings.Persisted?.IsFriendAutoDiscoverEnabled("Steam") == true,
                onChanged: OnAutoDiscoverProviderChanged));
        }

        private bool IsFriendProviderAvailable(string providerKey)
        {
            return !string.IsNullOrWhiteSpace(providerKey) &&
                   _providerRegistry?.TryGetProvider(providerKey, out var provider) == true &&
                   provider?.Friends != null;
        }

        private void RebuildFriends()
        {
            Friends.Clear();
            var rows = (_settings.Persisted?.Friends ?? new ObservableCollection<FriendSettingsEntry>())
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.ProviderKey) && !string.IsNullOrWhiteSpace(entry.ExternalUserId))
                .OrderBy(entry => entry.IsIgnored)
                .ThenBy(entry => ProviderRegistry.GetLocalizedName(entry.ProviderKey), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(entry => entry.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .Select(entry => new FriendSettingsRowItem(entry, OnFriendRowChanged))
                .ToList();

            foreach (var row in rows)
            {
                Friends.Add(row);
            }
        }

        private void OnAutoDiscoverProviderChanged(FriendAutoDiscoverProviderItem item)
        {
            if (item == null)
            {
                return;
            }

            _settings.Persisted?.SetFriendAutoDiscoverEnabled(item.ProviderKey, item.IsSelected);
            PersistAndNotify(item.ProviderKey);
        }

        private void OnFriendRowChanged(FriendSettingsRowItem row)
        {
            if (row == null)
            {
                return;
            }

            row.WriteToEntry();
            if (row.IsIgnored)
            {
                _friendCache?.DeleteFriendData(row.ProviderKey, row.ExternalUserId);
            }

            PersistAndNotify(row.ProviderKey);
        }

        private async Task RefreshAutoDiscoverAsync()
        {
            var providerKeys = AutoDiscoverProviders
                .Where(item => item.IsSelected && item.IsAvailable)
                .Select(item => item.ProviderKey)
                .ToList();
            if (providerKeys.Count == 0)
            {
                StatusText = ResourceProvider.GetString("LOCPlayAch_FriendsSettings_NoAutoProviders") ??
                             "No auto-discovery platforms are enabled.";
                return;
            }

            await RefreshRosterAsync(providerKeys, refreshStatus: true).ConfigureAwait(true);
        }

        private async Task AddManualExophaseFriendAsync()
        {
            var username = ExophaseSettings.NormalizeUsername(ManualExophaseUsername);
            if (string.IsNullOrWhiteSpace(username))
            {
                return;
            }

            _settings.Persisted?.AddOrUpdateFriend(
                "Exophase",
                username,
                username,
                null,
                null,
                FriendSettingsSource.Manual,
                FriendLibraryScope.Shared,
                Enumerable.Empty<string>());
            ManualExophaseUsername = string.Empty;
            PersistAndNotify("Exophase");

            await RefreshRosterAsync(new[] { "Exophase" }, refreshStatus: false).ConfigureAwait(true);
        }

        private async Task RefreshRosterAsync(IReadOnlyCollection<string> providerKeys, bool refreshStatus)
        {
            if (_plugin?.RefreshRuntime == null)
            {
                return;
            }

            try
            {
                var resolvedProviderKeys = providerKeys ?? Array.Empty<string>();
                IsBusy = true;
                StatusText = ResourceProvider.GetString("LOCPlayAch_Status_Starting") ?? "Starting...";
                var saved = await _plugin.RefreshRuntime
                    .RefreshFriendRosterAsync(resolvedProviderKeys, CancellationToken.None)
                    .ConfigureAwait(true);

                foreach (var key in resolvedProviderKeys)
                {
                    FriendSettingsSyncService.MergeCachedFriends(
                        _settings.Persisted,
                        _friendCache,
                        key,
                        string.Equals(key, "Exophase", StringComparison.OrdinalIgnoreCase)
                            ? FriendSettingsSource.Manual
                            : FriendSettingsSource.AutoDiscovered);
                }

                FriendSettingsSyncService.SyncConfiguredFriendsToCache(_settings.Persisted, _friendCache, _logger);
                RebuildFriends();
                PersistAndNotify(null);
                StatusText = refreshStatus
                    ? string.Format(
                        ResourceProvider.GetString("LOCPlayAch_FriendsSettings_RefreshCompleteFormat") ??
                        "Friends refreshed: {0:N0}.",
                        Math.Max(0, saved))
                    : null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "Failed to refresh friend roster from Friends settings.");
                StatusText = ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void RemoveFriend(object parameter)
        {
            if (!(parameter is FriendSettingsRowItem row) || !row.CanRemove)
            {
                return;
            }

            if (_settings.Persisted?.RemoveFriendSetting(row.ProviderKey, row.ExternalUserId) == true)
            {
                _friendCache?.DeleteFriendData(row.ProviderKey, row.ExternalUserId);
                RebuildFriends();
                PersistAndNotify(row.ProviderKey);
            }
        }

        private void PersistAndNotify(string providerKey)
        {
            FriendSettingsSyncService.SyncConfiguredFriendsToCache(_settings.Persisted, _friendCache, _logger, providerKey);
            try
            {
                _plugin?.PersistSettingsForUi();
                _plugin?.ThemeIntegrationService?.RequestUpdate(null, forceRefresh: true);
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "Failed to persist Friends settings changes.");
            }
        }
    }

    internal sealed class FriendAutoDiscoverProviderItem : ObservableObject
    {
        private readonly Action<FriendAutoDiscoverProviderItem> _onChanged;
        private bool _isSelected;

        public FriendAutoDiscoverProviderItem(
            string providerKey,
            string displayName,
            bool isAvailable,
            bool isSelected,
            Action<FriendAutoDiscoverProviderItem> onChanged)
        {
            ProviderKey = providerKey;
            DisplayName = displayName;
            IsAvailable = isAvailable;
            _isSelected = isSelected;
            _onChanged = onChanged;
        }

        public string ProviderKey { get; }

        public string DisplayName { get; }

        public bool IsAvailable { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetValueAndReturn(ref _isSelected, value))
                {
                    _onChanged?.Invoke(this);
                }
            }
        }
    }

    internal sealed class FriendSettingsRowItem : ObservableObject
    {
        private readonly FriendSettingsEntry _entry;
        private readonly Action<FriendSettingsRowItem> _onChanged;
        private bool _isIgnored;
        private bool _useFullLibrary;

        public FriendSettingsRowItem(
            FriendSettingsEntry entry,
            Action<FriendSettingsRowItem> onChanged)
        {
            _entry = entry ?? throw new ArgumentNullException(nameof(entry));
            _onChanged = onChanged;
            ProviderKey = entry.ProviderKey;
            ExternalUserId = entry.ExternalUserId;
            DisplayName = string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.ExternalUserId : entry.DisplayName;
            AvatarSource = !string.IsNullOrWhiteSpace(entry.AvatarPath) ? entry.AvatarPath : entry.AvatarUrl;
            Source = entry.Source;
            _isIgnored = entry.IsIgnored;
            _useFullLibrary = entry.LibraryScope == FriendLibraryScope.Full;

            var selected = new HashSet<string>(entry.SelectedPlatforms ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            Platforms = new ObservableCollection<FriendSettingsPlatformToggle>(
                ExophaseFriendPlatformCatalog.Entries.Select(platform => new FriendSettingsPlatformToggle(
                    platform.Token,
                    ResourceProvider.GetString(platform.LabelKey),
                    selected.Contains(platform.Token),
                    OnPlatformChanged)));
        }

        public string ProviderKey { get; }

        public string ExternalUserId { get; }

        public string DisplayName { get; }

        public string AvatarSource { get; }

        public FriendSettingsSource Source { get; }

        public bool IsExophase => string.Equals(ProviderKey, "Exophase", StringComparison.OrdinalIgnoreCase);

        public bool SupportsPlatformSelection => IsExophase;

        public bool CanRemove => Source == FriendSettingsSource.Manual;

        public string ProviderDisplayName => ProviderRegistry.GetLocalizedName(ProviderKey);

        public string PlatformText => IsExophase
            ? PlatformButtonLabel
            : ProviderDisplayName;

        public ObservableCollection<FriendSettingsPlatformToggle> Platforms { get; }

        public bool IsIgnored
        {
            get => _isIgnored;
            set
            {
                if (SetValueAndReturn(ref _isIgnored, value))
                {
                    _onChanged?.Invoke(this);
                }
            }
        }

        public bool UseFullLibrary
        {
            get => _useFullLibrary;
            set
            {
                if (SetValueAndReturn(ref _useFullLibrary, value))
                {
                    _onChanged?.Invoke(this);
                }
            }
        }

        public int SelectedPlatformCount => Platforms.Count(platform => platform.IsSelected);

        public string PlatformButtonLabel => SelectedPlatformCount == 0
            ? ResourceProvider.GetString("LOCPlayAch_Exophase_SelectPlatforms")
            : string.Format(
                ResourceProvider.GetString("LOCPlayAch_Exophase_PlatformsSelectedCount"),
                SelectedPlatformCount);

        public void WriteToEntry()
        {
            _entry.IsIgnored = IsIgnored;
            _entry.LibraryScope = UseFullLibrary ? FriendLibraryScope.Full : FriendLibraryScope.Shared;
            _entry.SelectedPlatforms = Platforms
                .Where(platform => platform.IsSelected)
                .Select(platform => platform.Token)
                .ToList();
        }

        private void OnPlatformChanged()
        {
            OnPropertyChanged(nameof(SelectedPlatformCount));
            OnPropertyChanged(nameof(PlatformButtonLabel));
            OnPropertyChanged(nameof(PlatformText));
            _onChanged?.Invoke(this);
        }
    }

    internal sealed class FriendSettingsPlatformToggle : ObservableObject
    {
        private readonly Action _onChanged;
        private bool _isSelected;

        public FriendSettingsPlatformToggle(string token, string label, bool isSelected, Action onChanged)
        {
            Token = token;
            Label = label;
            _isSelected = isSelected;
            _onChanged = onChanged;
        }

        public string Token { get; }

        public string Label { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetValueAndReturn(ref _isSelected, value))
                {
                    _onChanged?.Invoke();
                }
            }
        }
    }
}
