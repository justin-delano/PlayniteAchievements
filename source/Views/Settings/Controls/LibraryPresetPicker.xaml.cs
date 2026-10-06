using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
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
    /// kind's library items (My presets, Workshop, and Custom while the target follows none of
    /// them), Save and Delete, the card's own buttons, and a status line with Update, Reset and
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

        private static readonly Regex Placeholder = new Regex(@"\{(\d+)\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private LibraryPresetPickerOptions _options;
        private PersistedSettingsSubscription _persistedSubscription;
        private INotifyPropertyChanged _nestedValue;
        private bool _suppressSelection;
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
            public Choice(string group, string label, string detail, LibraryItem item)
            {
                Group = group;
                Label = label;
                Detail = detail;
                Item = item;
            }

            public string Group { get; }

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

            var choices = new List<Choice>();
            if (!_state.IsFollowing)
            {
                choices.Add(new Choice(string.Empty, L("LOCPlayAch_Common_Custom"), null, null));
            }

            var mine = L("LOCPlayAch_Library_MyPresets");
            choices.AddRange(items
                .Where(item => !item.IsWorkshop)
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(item => new Choice(mine, item.Name, null, item)));

            var workshop = L("LOCPlayAch_Workshop_Title");
            choices.AddRange(items
                .Where(item => item.IsWorkshop)
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(item => new Choice(workshop, item.Name, item.Version, item)));

            var view = new ListCollectionView(choices);
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Choice.Group)));

            _suppressSelection = true;
            try
            {
                PresetSelector.ItemsSource = view;
                PresetSelector.SelectedItem = _state.IsFollowing
                    ? choices.FirstOrDefault(choice => SameId(choice.Item, _state.Item))
                    : choices.FirstOrDefault(choice => choice.Item == null);
            }
            finally
            {
                _suppressSelection = false;
            }

            DeleteButton.IsEnabled = SelectedChoice?.Item != null;
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            StatusText.Inlines.Clear();
            if (_state.IsFollowing)
            {
                var item = _state.Item;
                if (item.IsWorkshop)
                {
                    AppendFormatted(L("LOCPlayAch_Library_Following"), item.Name, _state.Link.AppliedVersion ?? string.Empty);
                }
                else
                {
                    AppendFormatted(L("LOCPlayAch_Library_FollowingLocal"), item.Name);
                }

                if (_state.IsEdited)
                {
                    StatusText.Inlines.Add(new Run(" · " + L("LOCPlayAch_Library_Edited")));
                }

                if (_state.IsUpdateAvailable)
                {
                    StatusText.Inlines.Add(new Run(" · " + L("LOCPlayAch_Workshop_UpdateAvailable")));
                }
            }
            else
            {
                AppendFormatted(L("LOCPlayAch_Library_NotFollowing"), L("LOCPlayAch_Common_Custom"));
            }

            UpdateButton.Visibility = _state.IsFollowing && _state.IsUpdateAvailable ? Visibility.Visible : Visibility.Collapsed;
            ResetButton.Visibility = _state.IsFollowing && _state.IsEdited ? Visibility.Visible : Visibility.Collapsed;
            StopFollowingButton.Visibility = _state.Link != null ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Writes a localized format into the status line with its first argument (the name) in bold.</summary>
        private void AppendFormatted(string format, params string[] args)
        {
            var position = 0;
            foreach (Match match in Placeholder.Matches(format ?? string.Empty))
            {
                if (match.Index > position)
                {
                    StatusText.Inlines.Add(new Run(format.Substring(position, match.Index - position)));
                }

                var index = int.Parse(match.Groups[1].Value);
                var value = index < args.Length ? args[index] ?? string.Empty : string.Empty;
                StatusText.Inlines.Add(index == 0 ? (Inline)new Bold(new Run(value)) : new Run(value));
                position = match.Index + match.Length;
            }

            if (format != null && position < format.Length)
            {
                StatusText.Inlines.Add(new Run(format.Substring(position)));
            }
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

            var item = SelectedChoice?.Item;
            DeleteButton.IsEnabled = item != null;
            if (item == null || (_state.IsFollowing && SameId(item, _state.Item)))
            {
                return;
            }

            Execute(
                () => _options.Plugin.LibraryApplyService.ApplyToSettings(_options.Adapter, item, Persisted),
                $"Failed applying the {_options.Adapter.Kind} preset {item.Name}.",
                targetChanged: true);
        }

        private void Update_Click(object sender, RoutedEventArgs e)
        {
            Keyboard.ClearFocus();
            var kept = 0;
            var updated = Execute(
                () => _options.Plugin.LibraryApplyService.UpdateSettings(_options.Adapter, Persisted, out kept),
                $"Failed updating the {_options.Adapter.Kind} preset.",
                targetChanged: true);
            if (updated && kept > 0)
            {
                ShowMessage(string.Format(L("LOCPlayAch_Workshop_UpdateKeptEdits"), kept), MessageBoxImage.Information);
            }
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            Keyboard.ClearFocus();
            Execute(
                () => _options.Plugin.LibraryApplyService.ResetSettings(_options.Adapter, Persisted),
                $"Failed resetting the {_options.Adapter.Kind} preset.",
                targetChanged: true);
        }

        private void StopFollowing_Click(object sender, RoutedEventArgs e)
        {
            Keyboard.ClearFocus();
            Execute(
                () => LibraryApplyService.StopFollowing(_options.Adapter, Persisted),
                $"Failed to stop following the {_options.Adapter.Kind} preset.",
                targetChanged: false);
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

                if (existing == null && presets.Count() >= PackagePresetStore.MaxPresetCount)
                {
                    ShowMessage(string.Format(L("LOCPlayAch_Presets_MaxReached"), PackagePresetStore.MaxPresetCount), MessageBoxImage.Warning);
                    return;
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
