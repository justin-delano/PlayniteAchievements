using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Media;
using Playnite.SDK;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Library;
using PlayniteAchievements.Services.Workshop;
using PlayniteAchievements.ViewModels.Workshop;
using ObservableObject = PlayniteAchievements.Common.ObservableObject;

namespace PlayniteAchievements.ViewModels.Library
{
    /// <summary>A kind filter chip of the Library page: null kind means every kind.</summary>
    public sealed class LibraryKindFilter : ObservableObject
    {
        private int _count;
        private bool _isSelected;

        public LibraryKindFilter(LibraryItemKind? kind, string label)
        {
            Kind = kind;
            Label = label;
        }

        public LibraryItemKind? Kind { get; }

        public string Label { get; }

        public int Count
        {
            get => _count;
            set => SetValue(ref _count, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetValue(ref _isSelected, value);
        }
    }

    /// <summary>One place a library item is used, with its state and what can be done there.</summary>
    public sealed class LibraryUseRow
    {
        public LibraryUseRow(LibraryItemRow owner, LibraryTargetUse use, string label, string state)
        {
            Owner = owner;
            Use = use;
            Label = label;
            State = state;
        }

        public LibraryItemRow Owner { get; }

        public LibraryTargetUse Use { get; }

        public string TargetKey => Use.TargetKey;

        public string Label { get; }

        public string State { get; }

        /// <summary>Reset applies the item again as published; offered where the target was edited.</summary>
        public bool CanReset => Use.CanReset && Use.IsEdited;
    }

    /// <summary>
    /// One library item as the Library page lists it, with its uses and the Workshop entry it came
    /// from. Workshop game data is listed the same way from the games' records (it is not stored
    /// in the library): one row per Workshop item, used in each game that has it.
    /// </summary>
    public sealed class LibraryItemRow : ObservableObject
    {
        private string _thumbnailPath;
        private WorkshopItem _indexItem;
        private string _publishedItemId;
        private WorkshopItem _publishedItem;

        public LibraryItemRow(LibraryItem item, string filePath, IReadOnlyList<LibraryUseRow> uses, IReadOnlyList<Brush> swatches)
        {
            Item = item ?? throw new ArgumentNullException(nameof(item));
            FilePath = filePath;
            Uses = uses ?? Array.Empty<LibraryUseRow>();
            Swatches = swatches ?? Array.Empty<Brush>();
        }

        public LibraryItem Item { get; }

        public string Id => Item.Id;

        public LibraryItemKind Kind => Item.Kind;

        /// <summary>The name; for game data, the Workshop's once the index is read.</summary>
        public string Name => IsGameData
            ? FirstNonEmpty(_indexItem?.Name, Item.Name, Item.WorkshopItemId)
            : Item.Name;

        public bool IsWorkshop => Item.IsWorkshop;

        /// <summary>Workshop game data, listed from the games that have it rather than from the library.</summary>
        public bool IsGameData => Item.Kind == LibraryItemKind.GameData;

        /// <summary>The Workshop's description, once the index is read.</summary>
        public string Description => _indexItem?.Description;

        public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

        /// <summary>The preset file, or null when the item has no stored package.</summary>
        public string FilePath { get; }

        public bool HasFile => !string.IsNullOrEmpty(FilePath);

        public IReadOnlyList<LibraryUseRow> Uses { get; set; }

        public bool HasUses => Uses.Count > 0;

        public IReadOnlyList<Brush> Swatches { get; }

        public bool HasSwatches => Swatches.Count > 0;

        public string KindLabel => KindLabelFor(Kind);

        /// <summary>Groups the list in the order of the kind chips.</summary>
        public int KindOrder => (int)Kind;

        public string KindGlyph => WorkshopItemViewModel.KindGlyphFor(WorkshopKindOf(Kind));

        /// <summary>The Workshop index entry of a Workshop item, once the index is read.</summary>
        public WorkshopItem IndexItem
        {
            get => _indexItem;
            set => SetValue(ref _indexItem, value, nameof(IndexItem), nameof(HasUpdate), nameof(UpdateVersion), nameof(CanUpdate),
                nameof(CanReinstall), nameof(Author), nameof(Secondary), nameof(UpdateTag), nameof(Name), nameof(VersionText),
                nameof(Description), nameof(HasDescription));
        }

        public string ThumbnailPath
        {
            get => _thumbnailPath;
            set => SetValue(ref _thumbnailPath, value, nameof(ThumbnailPath), nameof(HasThumbnail), nameof(ShowGlyph));
        }

        public bool HasThumbnail => !string.IsNullOrEmpty(_thumbnailPath);

        /// <summary>The kind glyph stands in when there is neither an image nor color swatches.</summary>
        public bool ShowGlyph => !HasThumbnail && !HasSwatches;

        public string Author => _indexItem?.Author ?? Item.Author;

        /// <summary>The secondary line: author and version for a Workshop item, the saved date for the user's own.</summary>
        public string Secondary => IsWorkshop
            ? string.Join(" · ", new[] { Author, WorkshopVersion }.Where(part => !string.IsNullOrWhiteSpace(part)))
            : SavedText;

        /// <summary>When the user last saved their own preset.</summary>
        public string SavedText => Item.UpdatedUtc.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);

        /// <summary>The version line of the detail pane: the Workshop version, or the saved date of the user's own.</summary>
        public string VersionText => IsWorkshop ? WorkshopVersion : SavedText;

        /// <summary>
        /// The library's version of a Workshop item; for game data, the Workshop's current version
        /// once the index is read, else the highest version a game applied.
        /// </summary>
        private string WorkshopVersion => IsGameData ? FirstNonEmpty(_indexItem?.Version, Item.Version) : Item.Version;

        public bool IsInUse => HasUses;

        public bool IsEdited => Uses.Any(use => use.Use.IsEdited);

        /// <summary>
        /// True when the Workshop has a newer version than the library holds. Game data has no
        /// library copy: a newer version shows on the games that are behind it.
        /// </summary>
        public bool HasUpdate => !IsGameData && _indexItem != null && WorkshopIdentityStore.IsNewer(_indexItem.Version, Item.Version);

        public string UpdateVersion => HasUpdate ? _indexItem.Version : null;

        /// <summary>"Update" with the Workshop's newer version, when the update brings one.</summary>
        public string UpdateTag => HasUpdate || (IsGameData && HasFollowerUpdate && _indexItem != null)
            ? ResourceProvider.GetString("LOCPlayAch_Workshop_Update") + " " + _indexItem.Version
            : ResourceProvider.GetString("LOCPlayAch_Workshop_Update");

        /// <summary>True when a place that follows the item has not taken its current version.</summary>
        public bool HasFollowerUpdate => Uses.Any(use => use.Use.IsUpdateAvailable);

        public bool ShowUpdateTag => HasUpdate || HasFollowerUpdate;

        public bool CanUpdate => HasUpdate || HasFollowerUpdate;

        /// <summary>Reinstall, rename, export and share act on a library copy, which game data does not have.</summary>
        public bool HasLibraryCopy => !IsGameData;

        public bool ShowReinstall => IsWorkshop && !IsGameData;

        public bool CanReinstall => ShowReinstall && _indexItem != null;

        /// <summary>The Workshop id this install published the item as, or null.</summary>
        public string PublishedItemId => _publishedItemId;

        /// <summary>The index entry of <see cref="PublishedItemId"/>, once the index is read.</summary>
        public WorkshopItem PublishedItem => _publishedItem;

        /// <summary>True when this install published the item to the Workshop.</summary>
        public bool IsMine => !string.IsNullOrEmpty(_publishedItemId);

        /// <summary>The published item's folder on GitHub, or null until the index names it.</summary>
        public string PublishedUrl => _publishedItem?.Urls?.Folder;

        public bool HasPublishedUrl => !string.IsNullOrWhiteSpace(PublishedUrl);

        /// <summary>Sets what the item is published as: its Workshop id and that id's index entry, when read.</summary>
        public void SetPublished(string itemId, WorkshopItem indexItem)
        {
            itemId = string.IsNullOrWhiteSpace(itemId) ? null : itemId;
            indexItem = itemId == null ? null : indexItem;
            if (string.Equals(_publishedItemId, itemId, StringComparison.Ordinal) && ReferenceEquals(_publishedItem, indexItem))
            {
                return;
            }

            _publishedItemId = itemId;
            _publishedItem = indexItem;
            OnPropertyChanged(nameof(PublishedItemId));
            OnPropertyChanged(nameof(PublishedItem));
            OnPropertyChanged(nameof(IsMine));
            OnPropertyChanged(nameof(PublishedUrl));
            OnPropertyChanged(nameof(HasPublishedUrl));
        }

        public string SearchText => (Name + " " + Author + " " + KindLabel).ToLowerInvariant();

        private static string FirstNonEmpty(params string[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }

        public static string KindLabelFor(LibraryItemKind kind)
        {
            return WorkshopItemViewModel.KindLabelFor(WorkshopKindOf(kind));
        }

        /// <summary>The Workshop kind an item of this library kind is shared as.</summary>
        public static WorkshopItemKind WorkshopKindOf(LibraryItemKind kind)
        {
            switch (kind)
            {
                case LibraryItemKind.Colors: return WorkshopItemKind.Colors;
                case LibraryItemKind.Sounds: return WorkshopItemKind.UnlockSounds;
                case LibraryItemKind.Toast: return WorkshopItemKind.NotificationStyle;
                case LibraryItemKind.Frame: return WorkshopItemKind.ScreenshotFrame;
                case LibraryItemKind.ShowcasePage: return WorkshopItemKind.ShowcasePage;
                default: return WorkshopItemKind.GameCustomData;
            }
        }
    }
}
