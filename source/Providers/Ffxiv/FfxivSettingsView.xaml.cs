using Playnite.SDK;
using PlayniteAchievements.Providers.Settings;
using PlayniteAchievements.Services.Logging;
using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace PlayniteAchievements.Providers.Ffxiv
{
    /// <summary>
    /// Settings view for the Final Fantasy XIV provider.
    /// </summary>
    public partial class FfxivSettingsView : ProviderSettingsViewBase, IAuthRefreshable
    {
        private static readonly ILogger Logger = PluginLogger.GetLogger(nameof(FfxivSettingsView));

        private FfxivSettings _ffxivSettings;

        public static readonly DependencyProperty IsAuthenticatedProperty =
            DependencyProperty.Register(nameof(IsAuthenticated), typeof(bool), typeof(FfxivSettingsView), new PropertyMetadata(false));

        public bool IsAuthenticated
        {
            get => (bool)GetValue(IsAuthenticatedProperty);
            set => SetValue(IsAuthenticatedProperty, value);
        }

        public static readonly DependencyProperty AuthStatusProperty =
            DependencyProperty.Register(nameof(AuthStatus), typeof(string), typeof(FfxivSettingsView), new PropertyMetadata(string.Empty));

        public string AuthStatus
        {
            get => (string)GetValue(AuthStatusProperty);
            set => SetValue(AuthStatusProperty, value);
        }

        public new FfxivSettings Settings => _ffxivSettings;

        public FfxivSettingsView()
        {
            InitializeComponent();
            AuthLabel.Text = string.Format(
                ResourceProvider.GetString("LOCPlayAch_Settings_ProviderAuth"),
                ResourceProvider.GetString("LOCPlayAch_Provider_FFXIV"));
        }

        public override void Initialize(IProviderSettings settings)
        {
            _ffxivSettings = settings as FfxivSettings;
            base.Initialize(settings);

            if (_ffxivSettings is INotifyPropertyChanged notify)
            {
                notify.PropertyChanged -= Settings_PropertyChanged;
                notify.PropertyChanged += Settings_PropertyChanged;
            }

            RefreshAuthStatus();
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e == null)
            {
                RefreshAuthStatus();
                return;
            }

            // Editing the character invalidates any previously resolved id.
            if (string.Equals(e.PropertyName, nameof(FfxivSettings.CharacterName), StringComparison.Ordinal) ||
                string.Equals(e.PropertyName, nameof(FfxivSettings.World), StringComparison.Ordinal) ||
                string.Equals(e.PropertyName, nameof(FfxivSettings.Region), StringComparison.Ordinal))
            {
                if (_ffxivSettings != null)
                {
                    _ffxivSettings.ResolvedCharacterId = 0;
                }

                RefreshAuthStatus();
            }
        }

        private void RefreshAuthStatus()
        {
            var configured = !string.IsNullOrWhiteSpace(_ffxivSettings?.CharacterName) &&
                             !string.IsNullOrWhiteSpace(_ffxivSettings?.World);

            IsAuthenticated = configured;
            AuthStatus = configured
                ? ResourceProvider.GetString("LOCPlayAch_Auth_Authenticated")
                : ResourceProvider.GetString("LOCPlayAch_Common_NotAuthenticated");
        }

        public Task RefreshAuthStatusAsync()
        {
            RefreshAuthStatus();
            return Task.CompletedTask;
        }

        private async void VerifyCharacter_Click(object sender, RoutedEventArgs e)
        {
            if (_ffxivSettings == null ||
                string.IsNullOrWhiteSpace(_ffxivSettings.CharacterName) ||
                string.IsNullOrWhiteSpace(_ffxivSettings.World))
            {
                RefreshAuthStatus();
                return;
            }

            SetAuthStatusVisualState(pending: true, success: false);
            AuthStatus = ResourceProvider.GetString("LOCPlayAch_Settings_FFXIV_Verifying");

            try
            {
                using (var client = new FfxivApiClient(Logger))
                {
                    var id = await client.ResolveCharacterIdAsync(
                        _ffxivSettings.CharacterName,
                        _ffxivSettings.World,
                        _ffxivSettings.Region,
                        CancellationToken.None).ConfigureAwait(true);

                    if (!id.HasValue || id.Value <= 0)
                    {
                        IsAuthenticated = false;
                        SetAuthStatusVisualState(pending: false, success: false);
                        AuthStatus = ResourceProvider.GetString("LOCPlayAch_Settings_FFXIV_CharacterNotFound");
                        return;
                    }

                    var character = await client.FetchCharacterAsync(id.Value, CancellationToken.None).ConfigureAwait(true);

                    _ffxivSettings.ResolvedCharacterId = id.Value;
                    IsAuthenticated = true;
                    SetAuthStatusVisualState(pending: false, success: true);

                    var displayName = character?.Name ?? _ffxivSettings.CharacterName;
                    var displayServer = character?.Server ?? _ffxivSettings.World;
                    AuthStatus = string.Format(
                        ResourceProvider.GetString("LOCPlayAch_Auth_AuthenticatedAs"),
                        $"{displayName} ({displayServer})");

                    if (character?.Achievements?.Public == false)
                    {
                        AuthStatus = ResourceProvider.GetString("LOCPlayAch_Settings_FFXIV_AchievementsPrivate");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to verify FFXIV character.");
                IsAuthenticated = false;
                SetAuthStatusVisualState(pending: false, success: false);
                AuthStatus = string.Format(
                    ResourceProvider.GetString("LOCPlayAch_Status_Failed"),
                    ex.Message);
            }
        }
    }
}
