using Newtonsoft.Json.Linq;
using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Services.Workshop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ObservableObject = PlayniteAchievements.Common.ObservableObject;

namespace PlayniteAchievements.ViewModels.Workshop
{
    /// <summary>One Workshop item as the browser lists it: the index entry plus local state.</summary>
    public sealed class WorkshopItemViewModel : ObservableObject
    {
        private string _previewPath;
        private string _coverPath;
        private string _readme;
        private bool _isInstalled;
        private bool _hasUpdate;
        private bool _isApplied;
        private string _localGameName;
        private bool _isMine;

        public WorkshopItemViewModel(WorkshopItem item)
        {
            Item = item ?? throw new ArgumentNullException(nameof(item));
            SearchText = string.Join(" ", new[]
            {
                item.Name, item.Description, item.Author, item.Game?.Name, string.Join(" ", item.Tags ?? new List<string>())
            }.Where(text => !string.IsNullOrWhiteSpace(text))).ToLowerInvariant();
        }

        public WorkshopItem Item { get; }

        public string Id => Item.Id;
        public WorkshopItemKind Kind => Item.Kind;
        public string Name => Item.Name;
        public string Description => Item.Description;
        public string Author => Item.Author;
        public string Version => Item.Version;
        public string Updated => Item.Updated;
        private long? _liveDownloads;

        /// <summary>The release API count fetched when the item was picked; null until then.</summary>
        public long? LiveDownloads
        {
            get => _liveDownloads;
            set
            {
                if (_liveDownloads == value)
                {
                    return;
                }

                _liveDownloads = value;
                OnPropertyChanged(nameof(LiveDownloads));
                OnPropertyChanged(nameof(Downloads));
            }
        }

        /// <summary>Live count when known, otherwise the count the index carried at its last build.</summary>
        public long Downloads => _liveDownloads ?? Item.Downloads?.Total ?? 0;
        public string GameName => Item.Game?.Name;
        public string Tags => string.Join(", ", Item.Tags ?? new List<string>());
        public bool HasTags => Item.Tags != null && Item.Tags.Count > 0;
        public bool HasGame => Item.Game != null;
        public string FolderUrl => Item.Urls?.Folder;
        public string KindLabel => KindLabelFor(Kind);
        public bool IsBundle => Kind == WorkshopItemKind.Bundle;
        public string SearchText { get; }

        /// <summary>Sortable date; the index writes yyyy-MM-dd.</summary>
        public DateTime UpdatedDate =>
            DateTime.TryParse(Item.Updated, out var date) ? date : DateTime.MinValue;

        public string PreviewPath
        {
            get => _previewPath;
            set => SetValue(ref _previewPath, value, nameof(PreviewPath), nameof(HasPreview), nameof(ThumbnailPath), nameof(HasThumbnail));
        }

        public bool HasPreview => !string.IsNullOrWhiteSpace(_previewPath);

        /// <summary>The sharer's optional cover image as a cached local file; null until fetched or when the item has none.</summary>
        public string CoverPath
        {
            get => _coverPath;
            set => SetValue(ref _coverPath, value, nameof(CoverPath), nameof(HasCover), nameof(ThumbnailPath), nameof(HasThumbnail));
        }

        public bool HasCover => !string.IsNullOrWhiteSpace(_coverPath);

        /// <summary>The list thumbnail: the cover when the item has one, otherwise the preview.</summary>
        public string ThumbnailPath => HasCover ? _coverPath : _previewPath;

        /// <summary>True when the item has a cover or a preview to show; otherwise the kind glyph stands in.</summary>
        public bool HasThumbnail => HasCover || HasPreview;

        /// <summary>The IcoFont glyph standing in for an item with neither a cover nor a preview.</summary>
        public string KindGlyph => KindGlyphFor(Kind);

        public static string KindGlyphFor(WorkshopItemKind kind)
        {
            switch (kind)
            {
                case WorkshopItemKind.Colors: return ""; // paint
                case WorkshopItemKind.NotificationStyle: return ""; // notification
                case WorkshopItemKind.ScreenshotFrame: return ""; // picture
                case WorkshopItemKind.ShowcasePage: return ""; // dashboard-web
                case WorkshopItemKind.UnlockSounds: return ""; // music-note
                case WorkshopItemKind.Bundle: return ""; // box
                default: return ""; // trophy
            }
        }

        public string Readme
        {
            get => _readme;
            set => SetValue(ref _readme, value, nameof(Readme), nameof(HasReadme));
        }

        public bool HasReadme => !string.IsNullOrWhiteSpace(_readme);

        /// <summary>True when the item is in the library; for game data, when its game has it applied.</summary>
        public bool IsInstalled
        {
            get => _isInstalled;
            set => SetValue(ref _isInstalled, value, nameof(IsInstalled), nameof(ActionLabel), nameof(CanInstall));
        }

        /// <summary>For a look in the library, true when a setting or game uses it, as the Library page shows.</summary>
        public bool IsApplied
        {
            get => _isApplied;
            set => SetValue(ref _isApplied, value, nameof(IsApplied), nameof(InstalledLabel), nameof(ActionLabel));
        }

        /// <summary>The installed state's label: game data is applied to a game; a look is applied when something uses it, else in the library.</summary>
        public string InstalledLabel => ResourceProvider.GetString(Kind == WorkshopItemKind.GameCustomData || _isApplied
            ? "LOCPlayAch_Library_Applied"
            : "LOCPlayAch_Library_InLibrary");

        public bool HasUpdate
        {
            get => _hasUpdate;
            set => SetValue(ref _hasUpdate, value, nameof(HasUpdate), nameof(ActionLabel), nameof(CanInstall));
        }

        /// <summary>For game data, the library game it has been applied to or matched, when there is one.</summary>
        public string LocalGameName
        {
            get => _localGameName;
            set => SetValue(ref _localGameName, value, nameof(LocalGameName), nameof(IsInLibrary));
        }

        public bool IsInLibrary => Kind != WorkshopItemKind.GameCustomData || !string.IsNullOrWhiteSpace(_localGameName);

        /// <summary>True when this install's submitter key published the item.</summary>
        public bool IsMine
        {
            get => _isMine;
            set => SetValue(ref _isMine, value);
        }

        /// <summary>True when this extension is too old to import the item.</summary>
        public bool RequiresNewerPlugin => WorkshopIdentityStore.IsNewer(Item.MinPluginVersion, PluginManifest.Version);

        public string RequiresNewerPluginText =>
            string.Format(ResourceProvider.GetString("LOCPlayAch_Workshop_RequiresNewerPlugin"), Item.MinPluginVersion);

        /// <summary>
        /// Install, Update, or the installed label when there is nothing newer. Looks in the
        /// library are reinstalled from the Library page; game data on a game is reset from that
        /// game's Manage Achievements.
        /// </summary>
        public string ActionLabel => HasUpdate
            ? ResourceProvider.GetString("LOCPlayAch_Workshop_Update")
            : IsInstalled
                ? InstalledLabel
                : ResourceProvider.GetString("LOCPlayAch_Workshop_Install");

        public bool CanInstall => !RequiresNewerPlugin && (!IsInstalled || HasUpdate);

        /// <summary>A readable line from the manifest's `contents` counts.</summary>
        public string ContentsSummary => SummarizeContents(Item.Contents, Kind);

        public static string KindLabelFor(WorkshopItemKind kind)
        {
            switch (kind)
            {
                case WorkshopItemKind.Colors: return ResourceProvider.GetString("LOCPlayAch_Workshop_Kind_Colors");
                case WorkshopItemKind.NotificationStyle: return ResourceProvider.GetString("LOCNotifications");
                case WorkshopItemKind.ScreenshotFrame: return ResourceProvider.GetString("LOCPlayAch_Workshop_Kind_ScreenshotFrame");
                case WorkshopItemKind.ShowcasePage: return ResourceProvider.GetString("LOCPlayAch_Workshop_Kind_ShowcasePage");
                case WorkshopItemKind.UnlockSounds: return ResourceProvider.GetString("LOCPlayAch_Workshop_Share_Sounds");
                case WorkshopItemKind.Bundle: return ResourceProvider.GetString("LOCPlayAch_Workshop_Kind_Bundle");
                default: return ResourceProvider.GetString("LOCPlayAch_Workshop_Kind_GameCustomData");
            }
        }

        private static string SummarizeContents(JObject contents, WorkshopItemKind kind)
        {
            if (contents == null)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            void Count(string key, string label)
            {
                var token = contents[key];
                if (token == null)
                {
                    return;
                }

                if (token.Type == JTokenType.Integer && token.Value<int>() > 0)
                {
                    parts.Add(label + ": " + token.Value<int>().ToString("N0", FormattingCulture.Current));
                }
                else if (token.Type == JTokenType.Boolean && token.Value<bool>())
                {
                    parts.Add(label);
                }
            }

            switch (kind)
            {
                case WorkshopItemKind.GameCustomData:
                    Count("achievementIcons", "Icons");
                    Count("customAchievements", "Custom achievements");
                    Count("categories", "Categories");
                    Count("capstones", "Capstones");
                    Count("achievementOverrides", "Overrides");
                    Count("notes", "Notes");
                    Count("achievementOrder", "Order");
                    break;
                case WorkshopItemKind.UnlockSounds:
                    if (contents["slots"] is JArray slots)
                    {
                        parts.Add(string.Join(", ", slots.Select(s => s.ToString())));
                    }

                    break;
                case WorkshopItemKind.Bundle:
                    if (contents["parts"] is JArray bundleParts)
                    {
                        parts.Add(string.Join(", ", bundleParts.Select(s => s.ToString())));
                    }

                    break;
                case WorkshopItemKind.ShowcasePage:
                    Count("widgets", "Widgets");
                    Count("images", "Images");
                    break;
                case WorkshopItemKind.Colors:
                    Count("rarityColors", "Rarity colors");
                    Count("providerColors", "Provider colors");
                    Count("resourceOverrides", "Resource overrides");
                    break;
                default:
                    Count("toastTemplate", "Custom template");
                    Count("frameTemplate", "Custom frame template");
                    Count("images", "Images");
                    Count("kindStyles", "Per-kind styles");
                    break;
            }

            return string.Join(", ", parts);
        }
    }
}
