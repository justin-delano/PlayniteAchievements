using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
// WinForms dialog: the WPF Microsoft.Win32 picker renders legacy-style on .NET Framework.
using DialogResult = System.Windows.Forms.DialogResult;
using OpenFileDialog = System.Windows.Forms.OpenFileDialog;
using Playnite.SDK;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Sound;
using PlayniteAchievements.Services.UI;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.Views.Helpers;

namespace PlayniteAchievements.Views.Settings.Notifications
{
    /// <summary>
    /// The Sounds tab of the notification Styles page: the global sound switches (shown for the
    /// global scope only) and the six tier rows of one scope's pack. The hosting section chooses
    /// the scope; this control edits the pack and keeps the sound host's preloaded set current.
    /// </summary>
    public partial class UnlockSoundsEditor : UserControl, IDisposable
    {
        private PlayniteAchievementsSettings _settings;
        private PersistedSettingsSubscription _persistedSubscription;
        private UnlockSoundSettingsViewModel _viewModel;
        private PlayniteAchievementsPlugin _plugin;
        private ILogger _logger;

        public UnlockSoundsEditor()
        {
            InitializeComponent();
        }

        /// <summary>Raised after a row's file changed the scope's pack.</summary>
        public event EventHandler SoundsChanged;

        internal void Initialize(PlayniteAchievementsSettings settings, PlayniteAchievementsPlugin plugin, ILogger logger)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _plugin = plugin;
            _logger = logger;

            GlobalSwitchesCard.DataContext = settings;
            var store = plugin?.UnlockSoundPortableStore;
            var library = plugin?.SoundsLibraryAdapter;
            _viewModel = new UnlockSoundSettingsViewModel(
                settings,
                plugin?.UnlockSounds,
                logger,
                store != null ? store.ImportFile : (Func<string, string>)null,
                library != null ? library.PruneUnreferenced : (Action<UnlockSoundSettings>)null);
            _viewModel.SoundsChanged += (s, e) => SoundsChanged?.Invoke(this, EventArgs.Empty);
            UnlockSoundRows.DataContext = _viewModel;

            _persistedSubscription = new PersistedSettingsSubscription(settings, OnPersistedPropertyChanged);
        }

        /// <summary>
        /// Shows <paramref name="scope"/>'s pack, editable when the scope owns one.
        /// <paramref name="showGlobalSwitches"/> shows the switches every scope shares.
        /// </summary>
        internal void SetScope(
            UnlockSoundScope scope,
            bool editable,
            bool showGlobalSwitches,
            string testProviderKey,
            Guid testGameId)
        {
            GlobalSwitchesCard.Visibility = showGlobalSwitches ? Visibility.Visible : Visibility.Collapsed;
            _viewModel?.SetScope(scope, editable, testProviderKey, testGameId);
        }

        /// <summary>Re-reads the rows and, debounced, re-applies the sound host's preloaded set.</summary>
        internal void RefreshAndApply()
        {
            _viewModel?.Refresh();
            _viewModel?.ScheduleApply();
        }

        /// <summary>True when any tier of the shown pack plays the user's own or a theme file, which is what a pack can carry.</summary>
        internal bool HasShareableSounds =>
            _viewModel?.ResolveAll()?.Any(s => s.Source == UnlockSoundSource.Custom || s.Source == UnlockSoundSource.Theme) == true;

        /// <summary>Writes what each tier of the shown pack plays now, minus the bundled defaults, as a sound pack.</summary>
        internal void ExportCurrent(string path, UnlockSoundPortableStore store)
        {
            var resolved = _viewModel?.ResolveAll()
                           ?? throw new InvalidOperationException("The unlock sounds are not available.");
            (store ?? throw new ArgumentNullException(nameof(store))).Export(resolved, path);
        }

        private void UnlockSoundBrowse_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is UnlockSoundRowItem row))
            {
                return;
            }

            var dialog = new OpenFileDialog
            {
                Filter = UnlockSoundResolver.BuildOpenFileDialogFilter(),
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                PickSound(row, dialog.FileName);
            }
        }

        // A sound file dropped on or pasted into a row's file cell.
        private void SoundPickTarget_Picked(object sender, FilePickedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is UnlockSoundRowItem row)
            {
                PickSound(row, e.PickedSource);
            }
        }

        private void PickSound(UnlockSoundRowItem row, string path)
        {
            try
            {
                _viewModel?.PickFile(row.Tier, path);
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, $"Could not use '{path}' as the {row.Tier} unlock sound.");
                _plugin?.PlayniteApi?.Dialogs?.ShowMessage(
                    string.Format(ResourceProvider.GetString("LOCPlayAch_Status_Failed"), ex.Message),
                    ResourceProvider.GetString("LOCPlayAch_Title_PluginName"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void UnlockSoundClear_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is UnlockSoundRowItem row)
            {
                row.CustomPath = null;
            }
        }

        private void UnlockSoundTest_Click(object sender, RoutedEventArgs e)
        {
            Keyboard.ClearFocus();
            if ((sender as FrameworkElement)?.DataContext is UnlockSoundRowItem row)
            {
                _viewModel?.Test(row);
            }
        }

        /// <summary>
        /// Plays what a theme ships for this tier. One candidate is not worth a menu, so the button
        /// plays it outright; two means the desktop and the fullscreen theme ship different files
        /// for the tier and the listener has to pick, which is the case this exists for. Either can
        /// be heard without restarting Playnite into the other mode.
        /// </summary>
        private void UnlockSoundTheme_Click(object sender, RoutedEventArgs e)
        {
            Keyboard.ClearFocus();
            if (!(sender is Button button) || !(button.DataContext is UnlockSoundRowItem row))
            {
                return;
            }

            if (row.ThemeCandidates.Count == 0)
            {
                return;
            }

            if (row.ThemeCandidates.Count == 1)
            {
                _viewModel?.TestFile(row.ThemeCandidates[0].Path);
                return;
            }

            var menu = button.ContextMenu;
            if (menu == null)
            {
                return;
            }

            menu.Items.Clear();
            foreach (var candidate in row.ThemeCandidates)
            {
                var path = candidate.Path;
                var item = new MenuItem { Header = candidate.Label };
                item.Click += (s, args) => _viewModel?.TestFile(path);
                menu.Items.Add(item);
            }

            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void OnPersistedPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e?.PropertyName)
            {
                case null:
                case "":
                case nameof(PersistedSettings.UnlockSounds):
                case nameof(PersistedSettings.ProviderUnlockSounds):
                    // The persisted instance was replaced (Cancel) or a pack object swapped.
                    _viewModel?.Refresh();
                    break;
                case nameof(PersistedSettings.AllowThemeUnlockSounds):
                    // Changes which file each tier resolves to, so the rows are restated and the
                    // host's preloaded set is rebuilt.
                    RefreshAndApply();
                    break;
                case nameof(PersistedSettings.UnlockSoundVolumePercent):
                case nameof(PersistedSettings.EnableUnlockSounds):
                    _viewModel?.ScheduleApply();
                    break;
            }

            // The tier badges are drawn from the rarity appearance settings, so they have to be
            // rebuilt when those change while this page is open.
            if (!string.IsNullOrEmpty(e?.PropertyName) &&
                RarityAppearanceHelper.IsAppearanceSettingPropertyName(e.PropertyName))
            {
                _viewModel?.Refresh();
            }
        }

        public void Dispose()
        {
            _persistedSubscription?.Dispose();
            _viewModel?.Dispose();
        }
    }
}
