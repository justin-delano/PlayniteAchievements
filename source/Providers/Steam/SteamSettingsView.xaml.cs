using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Playnite.SDK;
using PlayniteAchievements.Providers.ImportedGameMetadata;
using PlayniteAchievements.Providers.Local;
using PlayniteAchievements.Providers.Settings;
using PlayniteAchievements.Services.Logging;
using PlayniteAchievements.Models;
using PlayniteAchievements.Views.Helpers;

namespace PlayniteAchievements.Providers.Steam
{
    /// <summary>
    /// Settings view for the Steam provider.
    /// </summary>
    public partial class SteamSettingsView : ProviderSettingsViewBase, IAuthRefreshable
    {
        private static readonly ILogger Logger = PluginLogger.GetLogger(nameof(SteamSettingsView));

        private readonly IPlayniteAPI _api;
        private readonly SteamSessionManager _sessionManager;
        private readonly SteamOwnedGamesImporter _ownedGamesImporter;
        private SteamSettings _steamSettings;
        private CancellationTokenSource _steamImportCts;

        public ObservableCollection<ImportedGameMetadataSourceOption> AvailableMetadataSources { get; } = new ObservableCollection<ImportedGameMetadataSourceOption>();

        #region DependencyProperties

        public static readonly DependencyProperty AuthBusyProperty =
            DependencyProperty.Register(
                nameof(AuthBusy),
                typeof(bool),
                typeof(SteamSettingsView),
                new PropertyMetadata(false));

        public bool AuthBusy
        {
            get => (bool)GetValue(AuthBusyProperty);
            set => SetValue(AuthBusyProperty, value);
        }

        public static readonly DependencyProperty FullyConfiguredProperty =
            DependencyProperty.Register(
                nameof(FullyConfigured),
                typeof(bool),
                typeof(SteamSettingsView),
                new PropertyMetadata(false));

        public bool FullyConfigured
        {
            get => (bool)GetValue(FullyConfiguredProperty);
            set => SetValue(FullyConfiguredProperty, value);
        }

        public static readonly DependencyProperty WebAuthenticatedProperty =
            DependencyProperty.Register(
                nameof(WebAuthenticated),
                typeof(bool),
                typeof(SteamSettingsView),
                new PropertyMetadata(false));

        public bool WebAuthenticated
        {
            get => (bool)GetValue(WebAuthenticatedProperty);
            set => SetValue(WebAuthenticatedProperty, value);
        }

        public static readonly DependencyProperty WebAuthStatusProperty =
            DependencyProperty.Register(
                nameof(WebAuthStatus),
                typeof(string),
                typeof(SteamSettingsView),
                new PropertyMetadata(
                    ResourceProvider.GetString("LOCPlayAch_Auth_NotChecked")));

        public string WebAuthStatus
        {
            get => (string)GetValue(WebAuthStatusProperty);
            set => SetValue(WebAuthStatusProperty, value);
        }

        #endregion

        public new SteamSettings Settings => _steamSettings;

        public SteamSettingsView(SteamSessionManager sessionManager, IPlayniteAPI api)
        {
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _ownedGamesImporter = new SteamOwnedGamesImporter(_api, Logger, _sessionManager);
            InitializeComponent();
            AuthLabel.Text = string.Format(
                ResourceProvider.GetString("LOCPlayAch_Settings_ProviderAuth"),
                ResourceProvider.GetString("LOCPlayAch_Provider_Steam"));
        }

        public override void Initialize(IProviderSettings settings)
        {
            _steamSettings = settings as SteamSettings;
            base.Initialize(settings);
            ImportedGameMetadataSourceComboBox.ItemsSource = AvailableMetadataSources;
            RefreshAvailableMetadataSources();
            _ = RefreshAuthStatusAsync();
        }

        private void RefreshAvailableMetadataSources()
        {
            AvailableMetadataSources.Clear();

            foreach (var option in ImportedGameMetadataSourceCatalog.GetAvailableOptions(_api, Logger))
            {
                AvailableMetadataSources.Add(option);
            }

            if (_steamSettings == null)
            {
                return;
            }

            var normalizedSelectedId = ImportedGameMetadataSourceCatalog.NormalizeMetadataSourceId(
                _api,
                Logger,
                _steamSettings.ImportedGameMetadataSourceId);
            if (!string.Equals(_steamSettings.ImportedGameMetadataSourceId, normalizedSelectedId, StringComparison.OrdinalIgnoreCase))
            {
                _steamSettings.ImportedGameMetadataSourceId = normalizedSelectedId;
            }

            if (!AvailableMetadataSources.Any(option => string.Equals(option.Id, _steamSettings.ImportedGameMetadataSourceId, StringComparison.OrdinalIgnoreCase)))
            {
                _steamSettings.ImportedGameMetadataSourceId = string.Empty;
            }
        }

        public async Task RefreshAuthStatusAsync()
        {
            try
            {
                var result = await _sessionManager.ProbeAuthStateAsync(CancellationToken.None);
                UpdateAuthStatusFromResult(result);
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Steam auth probe failed during settings refresh.");
                UpdateAuthStatusFromResult(AuthProbeResult.ProbeFailed());
            }
        }

        private void UpdateAuthStatusFromResult(AuthProbeResult result)
        {
            var hasWebAuth = result.IsSuccess;
            var probedSteamUserId = hasWebAuth && !string.IsNullOrWhiteSpace(result.UserId)
                ? result.UserId.Trim()
                : null;

            if (_steamSettings != null && !string.Equals(_steamSettings.SteamUserId, probedSteamUserId, StringComparison.Ordinal))
            {
                _steamSettings.SteamUserId = probedSteamUserId;
            }

            WebAuthenticated = hasWebAuth;
            FullyConfigured = hasWebAuth;

            if (hasWebAuth)
            {
                WebAuthStatus = ResourceProvider.GetString("LOCPlayAch_Auth_Authenticated");
            }
            else
            {
                var localized = ResourceProvider.GetString(result.MessageKey);
                WebAuthStatus = string.IsNullOrWhiteSpace(localized) || string.Equals(localized, result.MessageKey, StringComparison.Ordinal)
                    ? ResourceProvider.GetString("LOCPlayAch_Common_NotAuthenticated")
                    : localized;
            }
        }

        private async void LoginWeb_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetAuthBusy(true);
                var result = await _sessionManager.AuthenticateInteractiveAsync(forceInteractive: true, ct: CancellationToken.None);
                if (result.IsSuccess)
                {
                    await RefreshAuthStatusAsync();
                    PlayniteAchievementsPlugin.NotifySettingsSaved();
                }
                else
                {
                    UpdateAuthStatusFromResult(result);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Steam web login failed");
            }
            finally
            {
                SetAuthBusy(false);
            }
        }

        private async void SteamAuth_Check_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetAuthBusy(true);
                await RefreshAuthStatusAsync();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Steam auth check failed");
            }
            finally
            {
                SetAuthBusy(false);
            }
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
            catch (Exception ex)
            {
                Logger.Error(ex, "Steam logout failed");
            }
            finally
            {
                SetAuthBusy(false);
            }
        }

        private async void ImportOwnedGames_Click(object sender, RoutedEventArgs e)
        {
            await ImportOwnedGamesAsync(showDialog: true, ct: CancellationToken.None);
        }

        private async Task ImportOwnedGamesAsync(bool showDialog, CancellationToken ct)
        {
            if (showDialog)
            {
                StartOwnedGamesImportWithProgressWindow();
                return;
            }

            try
            {
                SetAuthBusy(true);
                await _ownedGamesImporter.ImportOwnedGamesAsync(ct, null, _steamSettings).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Steam owned-games import failed");
                if (showDialog)
                {
                    _api.Dialogs.ShowMessage(
                        string.Format(
                            ResourceProvider.GetString("LOCPlayAch_Settings_Steam_ImportOwnedGamesFailed"),
                            ex.Message),
                        ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            finally
            {
                SetAuthBusy(false);
            }
        }

        private void StartOwnedGamesImportWithProgressWindow()
        {
            SetAuthBusy(true);
            _steamImportCts?.Dispose();
            _steamImportCts = new CancellationTokenSource();

            var progressControl = new LocalImportProgressControl
            {
                DialogTitle = "Importing Steam Games"
            };

            var window = PlayniteUiProvider.CreateExtensionWindow(
                "Import Steam Games",
                progressControl,
                new WindowOptions
                {
                    Width = 430,
                    Height = 250,
                    CanBeResizable = false,
                    ShowCloseButton = true,
                    ShowMinimizeButton = false,
                    ShowMaximizeButton = false
                });

            var settingsWindow = Window.GetWindow(this);
            if (settingsWindow != null)
            {
                window.Owner = settingsWindow;
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }

            progressControl.RequestClose += (s, e) => window.Close();
            progressControl.CancelRequested += (s, e) => _steamImportCts?.Cancel();
            window.Closed += (s, e) =>
            {
                if (_steamImportCts != null && !_steamImportCts.IsCancellationRequested && progressControl.ShowCancelButton)
                {
                    _steamImportCts.Cancel();
                }
            };

            window.Show();

            var progress = new Progress<SteamOwnedGamesImporter.ImportProgressInfo>(info =>
            {
                if (info == null)
                {
                    return;
                }

                var percent = 0d;
                if (info.Current.HasValue && info.Max.HasValue && info.Max.Value > 0)
                {
                    percent = Math.Max(0d, Math.Min(100d, (info.Current.Value * 100d) / info.Max.Value));
                }

                progressControl.Update(percent, info.Text, info.IsIndeterminate ? "Working..." : string.Empty);
            });

            Task.Run(async () =>
            {
                try
                {
                    var result = await _ownedGamesImporter
                        .ImportOwnedGamesAsync(_steamImportCts.Token, progress, _steamSettings)
                        .ConfigureAwait(false);

                    var summary = BuildOwnedGamesImportSummaryText(result);
                    Dispatcher.Invoke(() =>
                    {
                        if (result?.WasCanceled == true)
                        {
                            progressControl.MarkCancelled(summary);
                        }
                        else
                        {
                            progressControl.MarkCompleted(summary);
                        }
                    });
                }
                catch (OperationCanceledException)
                {
                    Dispatcher.Invoke(() => progressControl.MarkCancelled("Steam import cancelled."));
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Steam owned-games import failed");
                    Dispatcher.Invoke(() =>
                    {
                        progressControl.MarkFailed(
                            string.Format(
                                ResourceProvider.GetString("LOCPlayAch_Settings_Steam_ImportOwnedGamesFailed"),
                                ex.Message));
                    });
                }
                finally
                {
                    Dispatcher.Invoke(() =>
                    {
                        _steamImportCts?.Dispose();
                        _steamImportCts = null;
                        SetAuthBusy(false);
                    });
                }
            });
        }

        private static string BuildOwnedGamesImportSummaryText(SteamOwnedGamesImporter.ImportResult result)
        {
            if (result == null || !result.IsAuthenticated)
            {
                return ResourceProvider.GetString("LOCPlayAch_Settings_Steam_ImportOwnedGamesNotAuthenticated");
            }

            if (!result.HasSteamLibraryPlugin)
            {
                return ResourceProvider.GetString("LOCPlayAch_Settings_Steam_ImportOwnedGamesMissingLibraryPlugin");
            }

            if (result.OwnedCount <= 0)
            {
                return ResourceProvider.GetString("LOCPlayAch_Settings_Steam_ImportOwnedGamesNoneFound");
            }

            if (result.ImportedCount <= 0)
            {
                if (result.UpdatedCount > 0)
                {
                    return string.Format(
                        ResourceProvider.GetString("LOCPlayAch_Settings_Steam_ImportOwnedGamesUpdatedOnlySummary"),
                        result.UpdatedCount,
                        result.FailedCount);
                }

                return string.Format(
                    ResourceProvider.GetString("LOCPlayAch_Settings_Steam_ImportOwnedGamesAlreadyPresent"),
                    result.OwnedCount);
            }

            return result.UpdatedCount > 0
                ? string.Format(
                    ResourceProvider.GetString("LOCPlayAch_Settings_Steam_ImportOwnedGamesSummaryWithUpdates"),
                    result.ImportedCount,
                    result.UpdatedCount,
                    result.ExistingCount,
                    result.FailedCount)
                : string.Format(
                    ResourceProvider.GetString("LOCPlayAch_Settings_Steam_ImportOwnedGamesSummary"),
                    result.ImportedCount,
                    result.ExistingCount,
                    result.FailedCount);
        }

        private void SetAuthBusy(bool busy)
        {
            if (Dispatcher.CheckAccess())
            {
                AuthBusy = busy;
            }
            else
            {
                Dispatcher.BeginInvoke(new Action(() => AuthBusy = busy));
            }
        }
    }
}

