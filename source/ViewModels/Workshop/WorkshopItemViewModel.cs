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
        private string _readme;
        private bool _isInstalled;
        private bool _hasUpdate;
        private string _localGameName;

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
        public string License => Item.License;
        public string Updated => Item.Updated;
        public long Downloads => Item.Downloads?.Total ?? 0;
        public string GameName => Item.Game?.Name;
        public string Tags => string.Join(", ", Item.Tags ?? new List<string>());
        public bool HasTags => Item.Tags != null && Item.Tags.Count > 0;
        public bool HasGame => Item.Game != null;
        public string FolderUrl => Item.Urls?.Folder;
        public string KindLabel => KindLabelFor(Kind);
        public string SearchText { get; }

        /// <summary>Sortable date; the index writes yyyy-MM-dd.</summary>
        public DateTime UpdatedDate =>
            DateTime.TryParse(Item.Updated, out var date) ? date : DateTime.MinValue;

        public string PreviewPath
        {
            get => _previewPath;
            set => SetValue(ref _previewPath, value, nameof(PreviewPath), nameof(HasPreview));
        }

        public bool HasPreview => !string.IsNullOrWhiteSpace(_previewPath);

        public string Readme
        {
            get => _readme;
            set => SetValue(ref _readme, value);
        }

        public bool IsInstalled
        {
            get => _isInstalled;
            set => SetValue(ref _isInstalled, value, nameof(IsInstalled), nameof(ActionLabel), nameof(CanInstall));
        }

        public bool HasUpdate
        {
            get => _hasUpdate;
            set => SetValue(ref _hasUpdate, value, nameof(HasUpdate), nameof(ActionLabel), nameof(CanInstall));
        }

        /// <summary>For game data, the library game it matched, when it did.</summary>
        public string LocalGameName
        {
            get => _localGameName;
            set => SetValue(ref _localGameName, value, nameof(LocalGameName), nameof(IsInLibrary));
        }

        public bool IsInLibrary => Kind != WorkshopItemKind.GameCustomData || !string.IsNullOrWhiteSpace(_localGameName);

        /// <summary>True when this extension is too old to import the item.</summary>
        public bool RequiresNewerPlugin => WorkshopInstalledRegistry.IsNewer(Item.MinPluginVersion, PluginManifest.Version);

        public string RequiresNewerPluginText =>
            string.Format(ResourceProvider.GetString("LOCPlayAch_Workshop_RequiresNewerPlugin"), Item.MinPluginVersion);

        public string ActionLabel => HasUpdate
            ? ResourceProvider.GetString("LOCPlayAch_Workshop_Update")
            : IsInstalled
                ? ResourceProvider.GetString("LOCPlayAch_Workshop_Tab_Installed")
                : ResourceProvider.GetString("LOCPlayAch_Workshop_Install");

        public bool CanInstall => !RequiresNewerPlugin && (!IsInstalled || HasUpdate);

        /// <summary>A readable line from the manifest's `contents` counts.</summary>
        public string ContentsSummary => SummarizeContents(Item.Contents, Kind);

        public static string KindLabelFor(WorkshopItemKind kind)
        {
            switch (kind)
            {
                case WorkshopItemKind.NotificationStyle: return ResourceProvider.GetString("LOCPlayAch_Workshop_Kind_NotificationStyle");
                case WorkshopItemKind.ScreenshotFrame: return ResourceProvider.GetString("LOCPlayAch_Workshop_Kind_ScreenshotFrame");
                case WorkshopItemKind.ShowcasePage: return ResourceProvider.GetString("LOCPlayAch_Workshop_Kind_ShowcasePage");
                case WorkshopItemKind.UnlockSounds: return ResourceProvider.GetString("LOCPlayAch_Workshop_Kind_UnlockSounds");
                case WorkshopItemKind.Theme: return ResourceProvider.GetString("LOCPlayAch_Workshop_Kind_Theme");
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
                    parts.Add(token.Value<int>() + " " + label);
                }
                else if (token.Type == JTokenType.Boolean && token.Value<bool>())
                {
                    parts.Add(label);
                }
            }

            switch (kind)
            {
                case WorkshopItemKind.GameCustomData:
                    Count("achievementIcons", "icons");
                    Count("customAchievements", "custom achievements");
                    Count("categories", "categories");
                    Count("capstones", "capstones");
                    Count("achievementOverrides", "overrides");
                    Count("notes", "notes");
                    Count("achievementOrder", "order");
                    break;
                case WorkshopItemKind.UnlockSounds:
                    if (contents["slots"] is JArray slots)
                    {
                        parts.Add(string.Join(", ", slots.Select(s => s.ToString())));
                    }

                    break;
                case WorkshopItemKind.Theme:
                    if (contents["parts"] is JArray themeParts)
                    {
                        parts.Add(string.Join(", ", themeParts.Select(s => s.ToString())));
                    }

                    break;
                case WorkshopItemKind.ShowcasePage:
                    Count("widgets", "widgets");
                    Count("images", "images");
                    break;
                default:
                    Count("toastTemplate", "custom template");
                    Count("frameTemplate", "custom frame template");
                    Count("images", "images");
                    Count("kindStyles", "per-kind styles");
                    break;
            }

            return string.Join(", ", parts);
        }
    }
}
