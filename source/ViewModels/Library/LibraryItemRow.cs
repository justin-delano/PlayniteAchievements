using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Media;
using Playnite.SDK;
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

    /// <summary>One library item as the Library page lists it, with its uses and the Workshop entry it came from.</summary>
    public sealed class LibraryItemRow : ObservableObject
    {
        private string _thumbnailPath;
        private WorkshopItem _indexItem;

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

        public string Name => Item.Name;

        public bool IsWorkshop => Item.IsWorkshop;

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
                nameof(CanReinstall), nameof(Author), nameof(Secondary), nameof(UpdateTag));
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
            ? string.Join(" · ", new[] { Author, Item.Version }.Where(part => !string.IsNullOrWhiteSpace(part)))
            : SavedText;

        /// <summary>When the user last saved their own preset.</summary>
        public string SavedText => Item.UpdatedUtc.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);

        /// <summary>The version line of the detail pane: the Workshop version, or the saved date of the user's own.</summary>
        public string VersionText => IsWorkshop ? Item.Version : SavedText;

        public bool IsInUse => HasUses;

        public bool IsEdited => Uses.Any(use => use.Use.IsEdited);

        /// <summary>True when the Workshop has a newer version than the library holds.</summary>
        public bool HasUpdate => _indexItem != null && WorkshopInstalledRegistry.IsNewer(_indexItem.Version, Item.Version);

        public string UpdateVersion => HasUpdate ? _indexItem.Version : null;

        public string UpdateTag => HasUpdate
            ? ResourceProvider.GetString("LOCPlayAch_Workshop_Update") + " " + _indexItem.Version
            : ResourceProvider.GetString("LOCPlayAch_Workshop_Update");

        /// <summary>True when a place that follows the item has not taken its current version.</summary>
        public bool HasFollowerUpdate => Uses.Any(use => use.Use.IsUpdateAvailable);

        public bool ShowUpdateTag => HasUpdate || HasFollowerUpdate;

        public bool CanUpdate => HasUpdate || HasFollowerUpdate;

        public bool CanReinstall => IsWorkshop && _indexItem != null;

        public string SearchText => (Name + " " + Author + " " + KindLabel).ToLowerInvariant();

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
