using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Playnite.SDK;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Friends;
using PlayniteAchievements.Providers.Settings;
using PlayniteAchievements.Services;
using PlayniteAchievements.Services.Logging;

namespace PlayniteAchievements.Providers.Exophase
{
    public partial class ExophaseSettingsView : ProviderSettingsViewBase, IAuthRefreshable
    {
        private static readonly ILogger Logger = PluginLogger.GetLogger(nameof(ExophaseSettingsView));
        private readonly ExophaseSessionManager _sessionManager;
        private ExophaseSettings _exophaseSettings;

        public static readonly DependencyProperty AuthBusyProperty =
            DependencyProperty.Register(nameof(AuthBusy), typeof(bool), typeof(ExophaseSettingsView), new PropertyMetadata(false));
        public bool AuthBusy { get => (bool)GetValue(AuthBusyProperty); set => SetValue(AuthBusyProperty, value); }

        public static readonly DependencyProperty IsAuthenticatedProperty =
            DependencyProperty.Register(nameof(IsAuthenticated), typeof(bool), typeof(ExophaseSettingsView), new PropertyMetadata(false));
        public bool IsAuthenticated { get => (bool)GetValue(IsAuthenticatedProperty); set => SetValue(IsAuthenticatedProperty, value); }

        public static readonly DependencyProperty AuthStatusProperty =
            DependencyProperty.Register(nameof(AuthStatus), typeof(string), typeof(ExophaseSettingsView), new PropertyMetadata(string.Empty));
        public string AuthStatus { get => (string)GetValue(AuthStatusProperty); set => SetValue(AuthStatusProperty, value); }

        public new ExophaseSettings Settings => _exophaseSettings;

        public ExophaseSettingsView(ExophaseSessionManager sessionManager)
        {
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            InitializeComponent();
            ConnectionLabel.Text = string.Format(
                ResourceProvider.GetString("LOCPlayAch_Settings_ProviderConnection"),
                ResourceProvider.GetString("LOCPlayAch_Provider_Exophase"));
            AuthLabel.Text = string.Format(
                ResourceProvider.GetString("LOCPlayAch_Settings_ProviderAuth"),
                ResourceProvider.GetString("LOCPlayAch_Provider_Exophase"));
        }

        public override void Initialize(IProviderSettings settings)
        {
            _exophaseSettings = settings as ExophaseSettings;
            base.Initialize(settings);
            SetAuthStatusVisualState(pending: true, success: false);
            SetAuthStatusByKey("LOCPlayAch_Auth_NotChecked");
            RefreshExophaseFriendsGrid();
        }

        private void UpdateAuthStatus(AuthProbeResult result)
        {
            var isAuthenticated = result?.IsSuccess ?? false;
            Logger.Info($"[ExophaseSettings] UpdateAuthStatus: IsAuthenticated={isAuthenticated}, " +
                $"Outcome={result?.Outcome}, MessageKey='{result?.MessageKey ?? "null"}'");

            SetAuthenticated(isAuthenticated);
            SetAuthStatusVisualState(pending: false, success: isAuthenticated);

            if (isAuthenticated)
            {
                var status = ResourceProvider.GetString("LOCPlayAch_Auth_Authenticated");
                Logger.Info($"[ExophaseSettings] Setting authenticated status: '{status}'");
                SetAuthStatus(status);
                return;
            }

            var localized = !string.IsNullOrWhiteSpace(result?.MessageKey)
                ? ResourceProvider.GetString(result.MessageKey)
                : null;

            Logger.Debug($"[ExophaseSettings] MessageKey localization: key='{result?.MessageKey}', localized='{localized ?? "null"}'");

            var finalStatus = string.IsNullOrWhiteSpace(localized) || string.Equals(localized, result?.MessageKey, StringComparison.Ordinal)
                ? ResourceProvider.GetString("LOCPlayAch_Common_NotAuthenticated")
                : localized;

            Logger.Info($"[ExophaseSettings] Setting not-authenticated status: '{finalStatus}'");
            SetAuthStatus(finalStatus);
        }

        public async Task RefreshAuthStatusAsync()
        {
            Logger.Info("[ExophaseSettings] RefreshAuthStatusAsync START");
            AuthProbeResult result;
            try
            {
                SetAuthStatusByKey("LOCPlayAch_Auth_Checking");
                Logger.Debug("[ExophaseSettings] Calling ProbeAuthStateAsync...");
                result = await _sessionManager.ProbeAuthStateAsync(CancellationToken.None);
                Logger.Info($"[ExophaseSettings] ProbeAuthStateAsync result: IsSuccess={result?.IsSuccess}, " +
                    $"Outcome={result?.Outcome}, UserId='{result?.UserId ?? "null"}', MessageKey='{result?.MessageKey ?? "null"}'");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[ExophaseSettings] Auth probe failed during settings refresh");
                result = AuthProbeResult.ProbeFailed();
            }

            UpdateAuthStatus(result);
            Logger.Info("[ExophaseSettings] RefreshAuthStatusAsync COMPLETE");
        }

        private async void Auth_Check_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetAuthBusy(true);
                await RefreshAuthStatusAsync();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Exophase auth check failed");
            }
            finally
            {
                SetAuthBusy(false);
            }
        }

        private async void LoginWeb_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetAuthBusy(true);
                var result = await _sessionManager.AuthenticateInteractiveAsync(forceInteractive: true, CancellationToken.None);
                if (result.IsSuccess)
                {
                    await RefreshAuthStatusAsync();
                    PlayniteAchievementsPlugin.NotifySettingsSaved();
                }
                else
                {
                    UpdateAuthStatus(result);
                }
            }
            catch (Exception ex) { Logger.Error(ex, "Exophase login failed"); }
            finally { SetAuthBusy(false); }
        }

        private async void Logout_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetAuthBusy(true);
                _sessionManager.ClearSession();
                await RefreshAuthStatusAsync();
                PlayniteAchievementsPlugin.NotifySettingsSaved();
            }
            catch (Exception ex) { Logger.Error(ex, "Exophase logout failed"); }
            finally { SetAuthBusy(false); }
        }

        private void SetAuthBusy(bool busy)
        {
            if (Dispatcher.CheckAccess()) AuthBusy = busy;
            else Dispatcher.BeginInvoke(new Action(() => AuthBusy = busy));
        }

        private void SetAuthenticated(bool authenticated)
        {
            if (Dispatcher.CheckAccess())
            {
                IsAuthenticated = authenticated;
            }
            else
            {
                Dispatcher.BeginInvoke(new Action(() => IsAuthenticated = authenticated));
            }
        }

        private void SetAuthStatus(string status)
        {
            var normalized = status ?? string.Empty;

            if (Dispatcher.CheckAccess())
            {
                AuthStatus = normalized;
            }
            else
            {
                Dispatcher.BeginInvoke(new Action(() => AuthStatus = normalized));
            }
        }

        private void SetAuthStatusByKey(string key)
        {
            var localized = ResourceProvider.GetString(key);

            if (string.IsNullOrWhiteSpace(localized) || string.Equals(localized, key, StringComparison.Ordinal))
            {
                localized = ResourceProvider.GetString("LOCPlayAch_Common_NotAuthenticated");
            }

            SetAuthStatus(localized);
        }

        private void ExophasePlatform_CheckboxLoaded(object sender, RoutedEventArgs e)
        {
            if (!(sender is CheckBox checkbox) || _exophaseSettings?.ManagedProviders == null)
            {
                return;
            }

            var token = checkbox.Tag as string;
            if (string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            checkbox.IsChecked = _exophaseSettings.ManagedProviders.Contains(token);
        }

        private void ExophasePlatform_CheckChanged(object sender, RoutedEventArgs e)
        {
            if (!(sender is CheckBox checkbox) || _exophaseSettings == null)
            {
                return;
            }

            var token = checkbox.Tag as string;
            if (string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            var managedProviders = _exophaseSettings.ManagedProviders != null
                ? new System.Collections.Generic.HashSet<string>(_exophaseSettings.ManagedProviders, StringComparer.OrdinalIgnoreCase)
                : new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (checkbox.IsChecked == true)
            {
                managedProviders.Add(token);
            }
            else
            {
                managedProviders.Remove(token);
            }

            _exophaseSettings.ManagedProviders = managedProviders;
        }

        private ExophaseFriendSettings SelectedExophaseFriend =>
            ExophaseFriendsGrid?.SelectedItem as ExophaseFriendSettings;

        private void AddExophaseFriend_Click(object sender, RoutedEventArgs e)
        {
            if (_exophaseSettings == null)
            {
                return;
            }

            var username = NewFriendUsernameTextBox?.Text;
            if (string.IsNullOrWhiteSpace(username))
            {
                return;
            }

            _exophaseSettings.AddOrUpdateFriend(username);
            NewFriendUsernameTextBox.Text = string.Empty;
            RefreshExophaseFriendsGrid(username.Trim());
        }

        private void RemoveExophaseFriend_Click(object sender, RoutedEventArgs e)
        {
            var selected = SelectedExophaseFriend;
            if (_exophaseSettings == null || selected == null)
            {
                return;
            }

            _exophaseSettings.RemoveFriend(selected.Username);
            RefreshExophaseFriendsGrid();
        }

        private void ExophaseFriendsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshSelectedFriendEditors();
        }

        private void ExophaseFriendScope_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = SelectedExophaseFriend;
            if (_exophaseSettings == null || selected == null || !(ExophaseFriendScopeComboBox?.SelectedItem is ComboBoxItem item))
            {
                return;
            }

            selected.LibraryScope = string.Equals(item.Tag as string, "Full", StringComparison.OrdinalIgnoreCase)
                ? FriendLibraryScope.Full
                : FriendLibraryScope.Shared;
            _exophaseSettings.Friends = _exophaseSettings.Friends;
            RefreshExophaseFriendsGrid(selected.Username);
        }

        private void FriendPlatform_CheckboxLoaded(object sender, RoutedEventArgs e)
        {
            RefreshFriendPlatformCheckbox(sender as CheckBox);
        }

        private void FriendPlatform_CheckChanged(object sender, RoutedEventArgs e)
        {
            var selected = SelectedExophaseFriend;
            if (_exophaseSettings == null || selected == null || !(sender is CheckBox checkbox))
            {
                return;
            }

            var token = GetFriendPlatformToken(checkbox);
            if (string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            var platforms = new HashSet<string>(selected.SelectedPlatforms ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            if (checkbox.IsChecked == true)
            {
                platforms.Add(token.Trim().ToLowerInvariant());
            }
            else
            {
                platforms.Remove(token.Trim());
            }

            selected.SelectedPlatforms = platforms.OrderBy(platform => platform, StringComparer.OrdinalIgnoreCase).ToList();
            _exophaseSettings.Friends = _exophaseSettings.Friends;
            RefreshExophaseFriendsGrid(selected.Username);
        }

        private void RefreshExophaseFriendsGrid(string selectedUsername = null)
        {
            if (ExophaseFriendsGrid == null || _exophaseSettings == null)
            {
                return;
            }

            _exophaseSettings.Friends = _exophaseSettings.Friends;
            ExophaseFriendsGrid.ItemsSource = null;
            ExophaseFriendsGrid.ItemsSource = _exophaseSettings.Friends;

            var selected = _exophaseSettings.Friends.FirstOrDefault(friend =>
                string.Equals(friend.Username, selectedUsername, StringComparison.OrdinalIgnoreCase));
            if (selected != null)
            {
                ExophaseFriendsGrid.SelectedItem = selected;
            }
            else if (_exophaseSettings.Friends.Count > 0 && ExophaseFriendsGrid.SelectedItem == null)
            {
                ExophaseFriendsGrid.SelectedItem = _exophaseSettings.Friends[0];
            }

            RefreshSelectedFriendEditors();
        }

        private void RefreshSelectedFriendEditors()
        {
            var selected = SelectedExophaseFriend;
            if (ExophaseFriendScopeComboBox != null)
            {
                foreach (var item in ExophaseFriendScopeComboBox.Items.OfType<ComboBoxItem>())
                {
                    var isFull = selected?.LibraryScope == FriendLibraryScope.Full;
                    if (string.Equals(item.Tag as string, isFull ? "Full" : "Shared", StringComparison.OrdinalIgnoreCase))
                    {
                        ExophaseFriendScopeComboBox.SelectedItem = item;
                        break;
                    }
                }
            }

            RefreshFriendPlatformCheckboxes();
        }

        private void RefreshFriendPlatformCheckboxes()
        {
            RefreshFriendPlatformCheckboxes(this);
        }

        private void RefreshFriendPlatformCheckboxes(DependencyObject root)
        {
            if (root == null)
            {
                return;
            }

            var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                RefreshFriendPlatformCheckbox(child as CheckBox);
                RefreshFriendPlatformCheckboxes(child);
            }
        }

        private void RefreshFriendPlatformCheckbox(CheckBox checkbox)
        {
            var selected = SelectedExophaseFriend;
            if (checkbox == null || checkbox.Tag == null || selected == null)
            {
                return;
            }

            var token = GetFriendPlatformToken(checkbox);
            if (string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            checkbox.IsChecked = (selected.SelectedPlatforms ?? new List<string>())
                .Contains(token, StringComparer.OrdinalIgnoreCase);
        }

        private static string GetFriendPlatformToken(CheckBox checkbox)
        {
            var tag = checkbox?.Tag as string;
            if (string.IsNullOrWhiteSpace(tag) ||
                !tag.StartsWith("friend:", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return tag.Substring("friend:".Length).Trim();
        }
    }
}

