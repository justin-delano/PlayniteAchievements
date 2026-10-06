using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Library;
using PlayniteAchievements.Services.Workshop;
using PlayniteAchievements.Views.Helpers;

namespace PlayniteAchievements.Views.Settings.Controls
{
    /// <summary>
    /// What a <see cref="LibraryPresetPicker"/> applies to and saves from: one target of one
    /// library kind, such as the colors, or the notification look of one scope.
    /// </summary>
    internal interface ILibraryPickerTarget
    {
        LibraryItemKind Kind { get; }

        /// <summary>The preset folder of the kind, where Save writes.</summary>
        ILibraryPackageFolder Folder { get; }

        /// <summary>How the target stands against the item it follows; unlinked when it follows none.</summary>
        LibraryLinkState GetState();

        /// <summary>Applies the item to the target (and, for a tracked target, follows it).</summary>
        void Apply(LibraryItem item);

        /// <summary>Follows an item just saved from the target.</summary>
        void Link(LibraryItem item);

        /// <summary>Writes the target's current values as a package at the given path.</summary>
        void ExportCurrent(string path);
    }

    /// <summary>A picker target made of delegates, for a card whose target changes with its own selectors.</summary>
    internal sealed class DelegatePickerTarget : ILibraryPickerTarget
    {
        public LibraryItemKind Kind { get; set; }

        public ILibraryPackageFolder Folder { get; set; }

        public Func<LibraryLinkState> State { get; set; }

        public Action<LibraryItem> ApplyItem { get; set; }

        public Action<LibraryItem> LinkItem { get; set; }

        public Action<string> Export { get; set; }

        public LibraryLinkState GetState() => State?.Invoke() ?? LibraryLinkState.Unlinked;

        public void Apply(LibraryItem item) => ApplyItem?.Invoke(item);

        public void Link(LibraryItem item) => LinkItem?.Invoke(item);

        public void ExportCurrent(string path) => Export?.Invoke(path);
    }

    /// <summary>The single settings target of a kind (colors, sounds), applied to the live settings.</summary>
    internal sealed class SettingsPickerTarget : ILibraryPickerTarget
    {
        private readonly PlayniteAchievementsPlugin _plugin;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly ISettingsLibraryAdapter _adapter;
        private readonly Action<string> _export;

        public SettingsPickerTarget(
            PlayniteAchievementsPlugin plugin,
            PlayniteAchievementsSettings settings,
            ISettingsLibraryAdapter adapter,
            ILibraryPackageFolder folder,
            Action<string> export)
        {
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            Folder = folder ?? throw new ArgumentNullException(nameof(folder));
            _export = export;
        }

        public LibraryItemKind Kind => _adapter.Kind;

        public ILibraryPackageFolder Folder { get; }

        public LibraryLinkState GetState() => _plugin.LibraryApplyService.GetSettingsState(_adapter, _settings.Persisted);

        public void Apply(LibraryItem item) => _plugin.LibraryApplyService.ApplyToSettings(_adapter, item, _settings.Persisted);

        public void Link(LibraryItem item) => _plugin.LibraryApplyService.LinkSettings(_adapter, item, _settings.Persisted);

        public void ExportCurrent(string path) => _export?.Invoke(path);
    }

    /// <summary>
    /// A ready-made entry a card lists ahead of the library items (the built-in color palettes):
    /// applied through the card, never followed, and shown as selected while
    /// <see cref="IsCurrent"/> holds.
    /// </summary>
    internal sealed class LibraryPickerBuiltIn
    {
        public string Label { get; set; }

        /// <summary>Small color swatches drawn before the label; empty for none.</summary>
        public IReadOnlyList<System.Windows.Media.Brush> Swatches { get; set; }

        /// <summary>Writes the entry onto the target and ends any link the target had.</summary>
        public Action Apply { get; set; }

        /// <summary>Whether the target's current values are this entry's.</summary>
        public Func<bool> IsCurrent { get; set; }
    }

    /// <summary>What a settings card hands the <see cref="LibraryPresetPicker"/>.</summary>
    internal sealed class LibraryPresetPickerOptions
    {
        /// <summary>Entries listed first, in their own order, before the library items by name.</summary>
        public IReadOnlyList<LibraryPickerBuiltIn> BuiltIns { get; set; }

        /// <summary>Color swatches for a library item of the card's kind; null or empty for none.</summary>
        public Func<LibraryItem, IReadOnlyList<System.Windows.Media.Brush>> ItemSwatches { get; set; }

        public PlayniteAchievementsPlugin Plugin { get; set; }

        public PlayniteAchievementsSettings Settings { get; set; }

        /// <summary>
        /// The target the picker acts on, asked again at every refresh; for a card whose target
        /// changes with its own selectors. When null, the target is the settings target of
        /// <see cref="Adapter"/>.
        /// </summary>
        public Func<ILibraryPickerTarget> Target { get; set; }

        /// <summary>The settings target the card edits, when <see cref="Target"/> is null.</summary>
        public ISettingsLibraryAdapter Adapter { get; set; }

        /// <summary>The preset folder of the adapter's kind, when <see cref="Target"/> is null.</summary>
        public PackagePresetStore Presets { get; set; }

        /// <summary>Writes the target's current values as a package at the given path, when <see cref="Target"/> is null.</summary>
        public Action<string> ExportCurrent { get; set; }

        /// <summary>Runs after the picker changed the target's values, so the card can redraw and re-apply them.</summary>
        public Action TargetChanged { get; set; }

        /// <summary>The nested settings object whose own changes edit the target (the rarity colors, the sound slots).</summary>
        public Func<INotifyPropertyChanged> NestedValue { get; set; }

        public ILogger Logger { get; set; }
    }

    /// <summary>
    /// The preset row of a settings card whose target follows a library item: a dropdown of the
    /// kind's library items in one list (the user's own and Workshop ones alike), Save and Delete,
    /// and the card's own buttons. Choosing an item applies it to the target, which then follows
    /// it; the item it follows is the one shown, and nothing is shown while it follows none.
    /// Inside the settings window Cancel undoes a settings target's value and link together.
    /// Deleting a preset removes a file, which no Cancel brings back, so its links go from the
    /// edit snapshot and the games too.
    /// </summary>
    public partial class LibraryPresetPicker : UserControl, IDisposable
    {
        public static readonly DependencyProperty ExtraContentProperty = DependencyProperty.Register(
            nameof(ExtraContent),
            typeof(object),
            typeof(LibraryPresetPicker),
            new PropertyMetadata(null));


        private LibraryPresetPickerOptions _options;
        private ILibraryPickerTarget _fixedTarget;
        private PersistedSettingsSubscription _persistedSubscription;
        private INotifyPropertyChanged _nestedValue;
        private bool _suppressSelection;

        // A choice made while the list was open, applied when it closes.
        private bool _applyOnClose;
        private bool _refreshPending;
        private ILibraryPickerTarget _target;
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
            public Choice(string label, string detail, LibraryItem item, LibraryPickerBuiltIn builtIn, IReadOnlyList<System.Windows.Media.Brush> swatches)
            {
                Label = label;
                Detail = detail;
                Item = item;
                BuiltIn = builtIn;
                Swatches = swatches ?? Array.Empty<System.Windows.Media.Brush>();
            }

            public string Label { get; }

            public string Detail { get; }

            public LibraryItem Item { get; }

            public LibraryPickerBuiltIn BuiltIn { get; }

            public IReadOnlyList<System.Windows.Media.Brush> Swatches { get; }

            public bool HasSwatches => Swatches.Count > 0;

            public override string ToString() => Label;
        }

        internal void Initialize(LibraryPresetPickerOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            if (_options.Plugin == null || _options.Settings == null)
            {
                throw new ArgumentException("The picker needs the plugin and the settings.", nameof(options));
            }

            if (_options.Target == null)
            {
                if (_options.Adapter == null || _options.Presets == null)
                {
                    throw new ArgumentException("The picker needs a target, or an adapter and its preset store.", nameof(options));
                }

                _fixedTarget = new SettingsPickerTarget(
                    _options.Plugin,
                    _options.Settings,
                    _options.Adapter,
                    new PackagePresetFolder(_options.Presets),
                    _options.ExportCurrent);
            }

            _persistedSubscription = new PersistedSettingsSubscription(
                _options.Settings,
                (s, e) => ScheduleRefresh(),
                ScheduleRefresh);
            Refresh(reconcile: false);
        }

        /// <summary>Restates the list and the selection once the dispatcher is idle; repeated calls coalesce.</summary>
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

        /// <summary>Restates the list and the selection now, for a card whose target just changed.</summary>
        public void RefreshNow()
        {
            Refresh(reconcile: false);
        }

        /// <summary>
        /// Adds a preset file the card copied into the preset folder (a file import) to the
        /// library as a local item, and shows it in the list.
        /// </summary>
        internal LibraryItem AddLocalPreset(PackagePresetInfo saved)
        {
            return saved == null || _fixedTarget == null ? null : AddLocalPreset(saved.FilePath, saved.Name, _fixedTarget.Kind);
        }

        /// <summary>Adds a preset file in the folder of <paramref name="kind"/> to the library as a local item.</summary>
        internal LibraryItem AddLocalPreset(string filePath, string name, LibraryItemKind kind)
        {
            if (string.IsNullOrWhiteSpace(filePath) || _options == null)
            {
                return null;
            }

            var library = _options.Plugin.LibraryStore;
            var item = library.FindByPath(filePath) ?? new LibraryItem
            {
                Id = LibraryItem.NewLocalId(),
                Kind = kind,
                Origin = LibraryItemOrigin.Local
            };
            item.Name = name;
            item.RelativePath = filePath;
            item.ContentHash = null;
            var stored = library.Upsert(item);
            Refresh(reconcile: false);
            return stored;
        }

        // ---- list and selection ---------------------------------------------------------------

        private Choice SelectedChoice => PresetSelector?.SelectedItem as Choice;

        private ILibraryPickerTarget CurrentTarget()
        {
            if (_options == null)
            {
                return null;
            }

            if (_options.Target == null)
            {
                return _fixedTarget;
            }

            try
            {
                return _options.Target();
            }
            catch (Exception ex)
            {
                _options.Logger?.Warn(ex, "Failed resolving the preset picker's target.");
                return null;
            }
        }

        private void Refresh(bool reconcile)
        {
            if (_options == null || _options.Settings?.Persisted == null)
            {
                return;
            }

            TrackNestedValue();
            _target = CurrentTarget();

            var library = _options.Plugin.LibraryStore;
            List<LibraryItem> items;
            try
            {
                if (reconcile)
                {
                    library.Reconcile();
                }

                items = _target == null
                    ? new List<LibraryItem>()
                    : library.Items.Where(item => item.Kind == _target.Kind).ToList();
                _state = _target?.GetState() ?? LibraryLinkState.Unlinked;
            }
            catch (Exception ex)
            {
                _options.Logger?.Warn(ex, $"Failed reading the library for {_target?.Kind}.");
                items = new List<LibraryItem>();
                _state = LibraryLinkState.Unlinked;
            }

            // One list: the card's built-in entries in their own order, then the library items by
            // name, the user's own presets and Workshop items alike; a Workshop item shows its
            // version, which is what update tracking compares. The preset the target follows is
            // preselected; with none, a built-in entry whose values are current, else nothing.
            var choices = (_options.BuiltIns ?? Array.Empty<LibraryPickerBuiltIn>())
                .Select(builtIn => new Choice(builtIn.Label, null, null, builtIn, builtIn.Swatches))
                .Concat(items
                    .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Select(item => new Choice(item.Name, item.IsWorkshop ? item.Version : null, item, null, SwatchesOf(item))))
                .ToList();

            _suppressSelection = true;
            try
            {
                PresetSelector.ItemsSource = choices;
                PresetSelector.SelectedItem = _state.IsFollowing
                    ? choices.FirstOrDefault(choice => SameId(choice.Item, _state.Item))
                    : choices.FirstOrDefault(choice => choice.BuiltIn != null && IsCurrent(choice.BuiltIn));
            }
            finally
            {
                _suppressSelection = false;
            }

            UpdateButtons();
        }

        private IReadOnlyList<System.Windows.Media.Brush> SwatchesOf(LibraryItem item)
        {
            try
            {
                return _options?.ItemSwatches?.Invoke(item);
            }
            catch (Exception ex)
            {
                _options?.Logger?.Debug(ex, $"Failed reading the swatches of {item?.Name}.");
                return null;
            }
        }

        private bool IsCurrent(LibraryPickerBuiltIn builtIn)
        {
            try
            {
                return builtIn.IsCurrent?.Invoke() == true;
            }
            catch (Exception ex)
            {
                _options?.Logger?.Debug(ex, $"Failed comparing the current values with {builtIn.Label}.");
                return false;
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
            var builtIn = SelectedChoice?.BuiltIn;
            if (builtIn != null)
            {
                if (!_state.IsFollowing && IsCurrent(builtIn))
                {
                    return;
                }

                Execute(
                    () => builtIn.Apply?.Invoke(),
                    $"Failed applying {builtIn.Label}.",
                    targetChanged: true);
                return;
            }

            var item = SelectedChoice?.Item;
            var target = _target;
            if (item == null || target == null || (_state.IsFollowing && SameId(item, _state.Item)))
            {
                return;
            }

            Execute(
                () => target.Apply(item),
                $"Failed applying the {target.Kind} preset {item.Name}.",
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
            var target = CurrentTarget();
            if (_options == null || target == null)
            {
                return;
            }

            var folder = target.Folder;
            var library = _options.Plugin.LibraryStore;
            var scratch = PortablePackage.CreateScratchDirectory("PresetSave");
            try
            {
                var suggested = _state.IsFollowing && !_state.Item.IsWorkshop ? _state.Item.Name : null;
                if (!PresetNamePrompt.TryAsk(_options.Plugin, suggested, PackagePresetStore.SanitizeName, PackagePresetStore.MaxNameLength, out var name))
                {
                    return;
                }

                var workshopNamed = WorkshopItemNamed(library, target.Kind, name);
                var existing = folder.Find(name);
                if (workshopNamed != null || (existing != null && library.FindByPath(existing)?.IsWorkshop == true))
                {
                    var fork = folder.UniqueName(name);
                    if (string.Equals(fork, name, StringComparison.OrdinalIgnoreCase))
                    {
                        fork = folder.UniqueName(name + " (2)");
                    }

                    ShowMessage(string.Format(L("LOCPlayAch_Library_SavedAsCopy"), name, fork), MessageBoxImage.Information);
                    name = fork;
                    existing = null;
                }

                if (existing != null && !Confirm(string.Format(L("LOCPlayAch_Presets_OverwriteConfirm"), folder.NameOf(existing))))
                {
                    return;
                }

                var package = Path.Combine(scratch, "preset" + LibraryStore.ExtensionOf(target.Kind));
                target.ExportCurrent(package);
                var saved = folder.Save(name, package);
                var item = AddLocalPreset(saved, folder.NameOf(saved), target.Kind);
                if (item != null)
                {
                    target.Link(item);
                }
            }
            catch (Exception ex)
            {
                _options.Logger?.Error(ex, $"Failed saving the {target.Kind} preset.");
                ShowMessage(string.Format(L("LOCPlayAch_Status_Failed"), ex.Message), MessageBoxImage.Error);
            }
            finally
            {
                PortablePackage.TryDeleteDirectory(scratch);
            }

            Refresh(reconcile: false);
        }

        /// <summary>Deletes the selected preset and ends every target's link to it, edit snapshot and games included.</summary>
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
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                }

                library.Remove(item.Id);
                _options.Plugin.LibraryUpdateService.UnlinkItem(item.Id);
            }
            catch (Exception ex)
            {
                _options.Logger?.Error(ex, $"Failed deleting the {item.Kind} preset {item.Name}.");
                ShowMessage(string.Format(L("LOCPlayAch_Status_Failed"), ex.Message), MessageBoxImage.Error);
            }

            Refresh(reconcile: false);
        }

        private void Execute(Action action, string failure, bool targetChanged)
        {
            var done = false;
            try
            {
                action();
                done = true;
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
        }

        private static LibraryItem WorkshopItemNamed(LibraryStore library, LibraryItemKind kind, string sanitizedName)
        {
            return library.Items.FirstOrDefault(item =>
                item.IsWorkshop
                && item.Kind == kind
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
