using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PlayniteAchievements.Models.Settings;
using System;
using System.Collections.Generic;

namespace PlayniteAchievements.Services.Workshop
{
    /// <summary>The item kinds the Workshop lists; names match the repository manifests.</summary>
    public enum WorkshopItemKind
    {
        NotificationStyle,
        ScreenshotFrame,
        ShowcasePage,
        UnlockSounds,
        Theme,
        GameCustomData
    }

    /// <summary>
    /// The Workshop index file (<c>index/v1.json</c> in the repository): every published item
    /// with its download statistics and the URLs the extension fetches.
    /// </summary>
    public sealed class WorkshopIndexFile
    {
        public const int SupportedSchemaVersion = 1;

        [JsonProperty("schemaVersion")] public int SchemaVersion { get; set; }
        [JsonProperty("generated")] public DateTime? Generated { get; set; }
        [JsonProperty("commit")] public string Commit { get; set; }
        [JsonProperty("repository")] public string Repository { get; set; }
        [JsonProperty("items")] public List<WorkshopItem> Items { get; set; } = new List<WorkshopItem>();
    }

    /// <summary>One published item: its manifest plus the index's statistics and URLs.</summary>
    public sealed class WorkshopItem
    {
        [JsonProperty("schemaVersion")] public int SchemaVersion { get; set; }
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("kind")] public WorkshopItemKind Kind { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("description")] public string Description { get; set; }
        [JsonProperty("author")] public string Author { get; set; }
        [JsonProperty("authorGitHub")] public string AuthorGitHub { get; set; }
        [JsonProperty("ownerHash")] public string OwnerHash { get; set; }
        [JsonProperty("version")] public string Version { get; set; }
        [JsonProperty("license")] public string License { get; set; }
        [JsonProperty("tags")] public List<string> Tags { get; set; } = new List<string>();
        [JsonProperty("minPluginVersion")] public string MinPluginVersion { get; set; }
        [JsonProperty("created")] public string Created { get; set; }
        [JsonProperty("updated")] public string Updated { get; set; }
        [JsonProperty("game")] public WorkshopGame Game { get; set; }
        /// <summary>Per-kind counts of what the package customizes; shape differs by kind.</summary>
        [JsonProperty("contents")] public JObject Contents { get; set; }
        [JsonProperty("package")] public WorkshopPackage Package { get; set; }
        [JsonProperty("preview")] public string Preview { get; set; }
        [JsonProperty("downloads")] public WorkshopDownloads Downloads { get; set; }
        [JsonProperty("urls")] public WorkshopUrls Urls { get; set; }

        /// <summary>The theme parts a bundle carries, or empty for other kinds.</summary>
        public IReadOnlyList<string> ThemeParts
        {
            get
            {
                var parts = Contents?["parts"] as JArray;
                var result = new List<string>();
                if (parts != null)
                {
                    foreach (var part in parts)
                    {
                        var text = part?.ToString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            result.Add(text);
                        }
                    }
                }

                return result;
            }
        }
    }

    public sealed class WorkshopGame
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("platform")] public string Platform { get; set; }
        [JsonProperty("keys")] public List<PortableGameKey> Keys { get; set; } = new List<PortableGameKey>();
    }

    public sealed class WorkshopPackage
    {
        [JsonProperty("file")] public string File { get; set; }
        [JsonProperty("formatKind")] public string FormatKind { get; set; }
        [JsonProperty("formatVersion")] public int FormatVersion { get; set; }
        [JsonProperty("sizeBytes")] public long SizeBytes { get; set; }
        [JsonProperty("sha256")] public string Sha256 { get; set; }
        [JsonProperty("release")] public WorkshopRelease Release { get; set; }
    }

    public sealed class WorkshopRelease
    {
        [JsonProperty("tag")] public string Tag { get; set; }
        [JsonProperty("url")] public string Url { get; set; }
    }

    public sealed class WorkshopDownloads
    {
        [JsonProperty("total")] public long Total { get; set; }
        [JsonProperty("current")] public long Current { get; set; }
    }

    public sealed class WorkshopUrls
    {
        [JsonProperty("package")] public string Package { get; set; }
        [JsonProperty("preview")] public string Preview { get; set; }
        [JsonProperty("readme")] public string Readme { get; set; }
        [JsonProperty("folder")] public string Folder { get; set; }
    }
}
