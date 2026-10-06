using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Playnite.SDK;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Library;
using PlayniteAchievements.Services.Workshop;
using PlayniteAchievements.Views.Helpers;

namespace PlayniteAchievements.Views.Settings.Controls
{
    /// <summary>What a settings card hands the <see cref="LibraryPresetPicker"/>.</summary>
    internal sealed class LibraryPresetPickerOptions
    {
        public PlayniteAchievementsPlugin Plugin { get; set; }

        public PlayniteAchievementsSettings Settings { get; set; }

        /// <summary>The target the card edits.</summary>
        public ISettingsLibraryAdapter Adapter { get; set; }

        /// <summary>The preset folder of the adapter's kind.</summary>
        public PackagePresetStore Presets { get; set; }

        /// <summary>Writes the target's current values as a package at the given path.</summary>
        public Action<string> ExportCurrent { get; set; }

        /// <summary>Runs after the picker changed the target's values, so the card can redraw and re-apply them.</summary>
        public Action TargetChanged { get; set; }

        /// <summary>The nested settings object whose own changes edit the target (the rarity colors, the sound slots).</summary>
        public Func<INotifyPropertyChanged> NestedValue { get; set; }

        public ILogger Logger { get; set; }
    }

    /// <summary>
    /// The preset row of a settings card whose target follows a library item: a dropdown of the
    /// kind's library items in one list (the user's own and Workshop ones alike, plus Custom while
    /// the target follows none of them), Save and Delete, the card's own buttons, and a status line with Update, Reset and
    /// Stop following. Choosing an item applies it through <see cref="LibraryApplyService"/> to
    /// the live settings, so inside the settings window Cancel undoes the value and the link
    /// together. Deleting a preset removes a file, which no Cancel brings back, so its links go
    /// from the edit snapshot too.
    /// </summary>
    public partial class LibraryPresetPicker : UserControl, IDisposable
    {
        public static readonly DependencyProperty ExtraContentProperty = DependencyProperty.Register(
            nameof(ExtraContent),
            typeof(object),
            typeof(LibraryPresetPicker),
            new PropertyMetadata(null));


        private LibraryPresetPickerOptions _options;
        private PersistedSettingsSubscription _persistedSubscription;
        private INotifyPropertyChanged _nestedValue;
        private bool _suppressSelection;

        // A choice made while the list was open, applied when it closes.
        private bool _applyOnClose;
        private bool _refreshPending;
        private LibraryLinkState _state = LibraryLinkState.Unlinked;

        public LibraryPresetPicker()
        {
            InitializeComponent();
        }

        /// <summary>The card's own buttons, shown after Save and Delete.</summary>
        public object ExtraContent
        {
            get => GetValue(ExtraContentProperty);
            set => SetValue(ExtraContentProperty, value);
        }

        private sealed class Choice
        {
            public Choice(string label, string detail, LibraryItem item)
            {
                Label = label;
                Detail = detail;
                Item = item;
            }

            public string Label { get; }

            public string Detail { get; }

            /// <summary>The library item, or null for the Custom entry.</summary>
            public LibraryItem Item { get; }

            public override string ToString() => Label;
        }

        internal void Initialize(LibraryPresetPickerOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            if (_options.Plugin == null || _options.Settings == null || _options.Adapter == null || _options.Presets == null)
            {
                throw new ArgumentException("The picker needs the plugin, settings, adapter and preset store.", nameof(options));
            }

            _persistedSubscription = new PersistedSettingsSubscription(
                _options.Settings,
                (s, e) => ScheduleRefresh(),
                ScheduleRefresh);
            Refresh(reconcile: false);
        }

        /// <summary>Restates the list and the status once the dispatcher is idle; repeated calls coalesce.</summary>
        public void ScheduleRefresh()
        {
            if (_refreshPending || _options == null)
            {
                return;
            }

            _refreshPending = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _refreshPending = false;
                Refresh(reconcile: false);
            }));
        }

        /// <summary>
        /// Adds a preset file the card copied into the preset folder (a file import) to the
        /// library as a local item, and shows it in the list.
        /// </summary>
        internal LibraryItem AddLocalPreset(PackagePresetInfo saved)
        {
            if (saved == null || _options == null)
            {
                return null;
            }

            var library = _options.Plugin.LibraryStore;
            var item = library.FindByPath(saved.FilePath) ?? new LibraryItem
            {
                Id = LibraryItem.NewLocalId(),
                Kind = _options.Adapter.Kind,
                Origin = LibraryItemOrigin.Local
            };
            item.Name = saved.Name;
            item.RelativePath = saved.FilePath;
            item.ContentHash = null;
            var stored = library.Upsert(item);
            Refresh(reconcile: false);
            return stored;
        }

        // ---- list and status ------------------------------------------------------------------

        private PersistedSettings Persisted => _options?.Settings?.Persisted;

        private Choice SelectedChoice => PresetSelector?.SelectedItem as Choice;

        private void Refresh(bool reconcile)
        {
            var persisted = Persisted;
            if (_options == null || persisted == null)
            {
                return;
            }

            TrackNestedValue();

            var library = _options.Plugin.LibraryStore;
            List<LibraryItem> items;
            try
            {
                if (reconcile)
                {
                    library.Reconcile();
                }

                items = library.Items.Where(item => item.Kind == _options.Adapter.Kind).ToList();
                _state = _options.Plugin.LibraryApplyService.GetSettingsState(_options.Adapter, persisted);
            }
            catch (Exception ex)
            {
                _options.Logger?.Warn(ex, $"Failed reading the library for {_options.Adapter.Kind}.");
                items = new List<LibraryItem>();
                _state = LibraryLinkState.Unlinked;
            }

            // One list by name, the user's own presets and Workshop items alike; a Workshop item
            // shows its version, which is what update tracking compares.
            // The preset the target follows is preselected; with none, nothing is, as before.
            var choices = items
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(item => new Choice(item.Name, item.IsWorkshop ? item.Version : null, item))
                .ToList();

            _suppressSelection = true;
            try
            {
                PresetSelector.ItemsSource = choices;
                PresetSelector.SelectedItem = _state.IsFollowing
                    ? choices.FirstOrDefault(choice => SameId(choice.Item, _state.Item))
                    : null;
            }
            finally
            {
                _suppressSelection = false;
            }

            UpdateButtons();
        }

        private void TrackNestedValue()
        {
            var next = _options?.NestedValue?.Invoke();
            if (ReferenceEquals(next, _nestedValue))
            {
                return;
            }

            if (_nestedValue != null)
            {
                _nestedValue.PropertyChanged -= OnNestedValueChanged;
            }

            _nestedValue = next;
            if (_nestedValue != null)
            {
                _nestedValue.PropertyChanged += OnNestedValueChanged;
            }
        }

        private void OnNestedValueChanged(object sender, PropertyChangedEventArgs e)
        {
            ScheduleRefresh();
        }

        // ---- actions --------------------------------------------------------------------------

        private void PresetSelector_DropDownOpened(object sender, EventArgs e)
        {
            // Presets saved or installed elsewhere show up when the list opens.
            Refresh(reconcile: true);
        }

        private void PresetSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelection || _options == null)
            {
                return;
            }

            UpdateButtons();

            // Picking applies. While the list is open, moving through it (arrow keys) only marks
            // the choice; it is applied once the list closes, so browsing never applies each
            // preset on the way.
            if (PresetSelector.IsDropDownOpen)
            {
                _applyOnClose = true;
                return;
            }

            ApplySelected();
        }

        private void PresetSelector_DropDownClosed(object sender, EventArgs e)
        {
            if (!_applyOnClose)
            {
                return;
            }

            _applyOnClose = false;
            ApplySelected();
        }

        private void UpdateButtons()
        {
            DeleteButton.IsEnabled = SelectedChoice?.Item != null;
        }

        /// <summary>
        /// Applies the selected preset to the target and follows it, unless the target already
        /// follows it.
        /// </summary>
        private void ApplySelected()
        {
            var item = SelectedChoice?.Item;
            if (item == null || _options == null || (_state.IsFollowing && SameId(item, _state.Item)))
            {
                return;
            }

            Execute(
                () => _options.Plugin.LibraryApplyService.ApplyToSettings(_options.Adapter, item, Persisted),
                $"Failed applying the {_options.Adapter.Kind} preset {item.Name}.",
                targetChanged: true);
        }

        /// <summary>
        /// Saves the target's current values as a local preset and follows it. Saving over a
        /// local preset is a new version of it; a name a Workshop item has gets a local copy
        /// under a free name instead.
        /// </summary>
        private void Save_Click(object sender, RoutedEventArgs e)
        {
            Keyboard.ClearFocus();
            if (_options == null || Persisted == null)
            {
                return;
            }

            var presets = _options.Presets;
            var library = _options.Plugin.LibraryStore;
            try
            {
                var suggested = _state.IsFollowing && !_state.Item.IsWorkshop ? _state.Item.Name : null;
                if (!PresetNamePrompt.TryAsk(_options.Plugin, suggested, PackagePresetStore.SanitizeName, PackagePresetStore.MaxNameLength, out var name))
                {
                    return;
                }

                var workshopNamed = WorkshopItemNamed(library, name);
                var existing = presets.Find(name);
                if (workshopNamed != null || (existing != null && library.FindByPath(existing.FilePath)?.IsWorkshop == true))
                {
                    var fork = presets.UniqueName(name);
                    if (string.Equals(fork, name, StringComparison.OrdinalIgnoreCase))
                    {
                        fork = presets.UniqueName(name + " (2)");
                    }

                    ShowMessage(string.Format(L("LOCPlayAch_Library_SavedAsCopy"), name, fork), MessageBoxImage.Information);
                    name = fork;
                    existing = null;
                }

                if (existing != null && !Confirm(string.Format(L("LOCPlayAch_Presets_OverwriteConfirm"), existing.Name)))
                {
                    return;
                }

                var saved = presets.Save(name, path => _options.ExportCurrent(path));
                var item = AddLocalPreset(saved);
                if (item != null)
                {
                    _options.Plugin.LibraryApplyService.LinkSettings(_options.Adapter, item, Persisted);
                }
            }
            catch (Exception ex)
            {
                _options.Logger?.Error(ex, $"Failed saving the {_options.Adapter.Kind} preset.");
                ShowMessage(string.Format(L("LOCPlayAch_Status_Failed"), ex.Message), MessageBoxImage.Error);
            }

            Refresh(reconcile: false);
        }

        /// <summary>Deletes the selected preset and ends every target's link to it, edit snapshot included.</summary>
        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            Keyboard.ClearFocus();
            var item = SelectedChoice?.Item;
            if (_options == null || item == null)
            {
                return;
            }

            if (!Confirm(string.Format(L("LOCPlayAch_Presets_DeleteConfirm"), item.Name)))
            {
                return;
            }

            try
            {
                var library = _options.Plugin.LibraryStore;
                var path = library.FullPath(item);
                if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                }

                library.Remove(item.Id);
                _options.Plugin.UpdateSettingsIncludingEditSnapshot(settings => LibraryApplyService.UnlinkItem(settings, item.Id));
                LibraryApplyService.UnlinkItem(Persisted, item.Id);
            }
            catch (Exception ex)
            {
                _options.Logger?.Error(ex, $"Failed deleting the {_options.Adapter.Kind} preset {item.Name}.");
                ShowMessage(string.Format(L("LOCPlayAch_Status_Failed"), ex.Message), MessageBoxImage.Error);
            }

            Refresh(reconcile: false);
        }

        private bool Execute(Func<bool> action, string failure, bool targetChanged)
        {
            var done = false;
            try
            {
                done = action();
            }
            catch (Exception ex)
            {
                _options.Logger?.Error(ex, failure);
                ShowMessage(string.Format(L("LOCPlayAch_Status_Failed"), ex.Message), MessageBoxImage.Error);
            }

            if (targetChanged && done)
            {
                _options.TargetChanged?.Invoke();
            }

            Refresh(reconcile: false);
            return done;
        }

        private void Execute(Action action, string failure, bool targetChanged)
        {
            Execute(() =>
            {
                action();
                return true;
            }, failure, targetChanged);
        }

        private LibraryItem WorkshopItemNamed(LibraryStore library, string sanitizedName)
        {
            return library.Items.FirstOrDefault(item =>
                item.IsWorkshop
                && item.Kind == _options.Adapter.Kind
                && string.Equals(PackagePresetStore.SanitizeName(item.Name), sanitizedName, StringComparison.OrdinalIgnoreCase));
        }

        private static bool SameId(LibraryItem left, LibraryItem right)
        {
            return left != null && right != null && string.Equals(left.Id, right.Id, StringComparison.OrdinalIgnoreCase);
        }

        private bool Confirm(string message)
        {
            return _options?.Plugin?.PlayniteApi?.Dialogs?.ShowMessage(
                       message,
                       L("LOCPlayAch_Title_PluginName"),
                       MessageBoxButton.YesNo,
                       MessageBoxImage.Question) == MessageBoxResult.Yes;
        }

        private void ShowMessage(string message, MessageBoxImage image)
        {
            _options?.Plugin?.PlayniteApi?.Dialogs?.ShowMessage(
                message,
                L("LOCPlayAch_Title_PluginName"),
                MessageBoxButton.OK,
                image);
        }

        private static string L(string key)
        {
            return ResourceProvider.GetString(key);
        }

        public void Dispose()
        {
            _persistedSubscription?.Dispose();
            _persistedSubscription = null;
            if (_nestedValue != null)
            {
                _nestedValue.PropertyChanged -= OnNestedValueChanged;
                _nestedValue = null;
            }
        }
    }
}
