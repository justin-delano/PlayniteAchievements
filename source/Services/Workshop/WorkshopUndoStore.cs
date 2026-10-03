using Newtonsoft.Json;
using Playnite.SDK;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PlayniteAchievements.Services.Workshop
{
    /// <summary>Which settings a Workshop install can touch, and therefore what a snapshot holds.</summary>
    [Flags]
    public enum WorkshopSettingsSlices
    {
        None = 0,
        Colors = 1,
        Sounds = 2,
        NotificationStyle = 4,
        Showcase = 8
    }

    /// <summary>One "before" snapshot: the settings slices an install replaced, as they were.</summary>
    public sealed class WorkshopUndoEntry
    {
        public string Id { get; set; }
        public string ItemId { get; set; }
        public string ItemName { get; set; }
        public DateTime CreatedUtc { get; set; }
        public WorkshopSettingsSlices Slices { get; set; }
        public RarityColorSettings RarityColors { get; set; }
        public Dictionary<string, string> ProviderColorOverrides { get; set; }
        public Dictionary<string, ResourceOverrideSetting> ResourceOverrides { get; set; }
        public UnlockSoundSettings UnlockSounds { get; set; }
        public NotificationStyleSettings NotificationStyle { get; set; }
        public ShowcaseSettings Showcase { get; set; }
        public GridOptionsCatalog GridOptions { get; set; }
    }

    /// <summary>
    /// "Revert to before &lt;item&gt;": before a Workshop install writes into the global settings,
    /// the slices it will replace are copied to <c>UserData\workshop\undo\</c>; restoring assigns
    /// them back through the same setters the install used. Covers the settings-based kinds
    /// (colors, sounds, notification styles, showcase pages, themes). Per-game data is not
    /// snapshotted here: a game's custom data can be exported as a .pa before installing.
    /// Managed files (images, sounds) are left on disk, so a restored path still resolves.
    /// </summary>
    public sealed class WorkshopUndoStore
    {
        public const int MaxEntries = 10;

        private readonly string _directory;
        private readonly ILogger _logger;
        private readonly object _sync = new object();
        private readonly JsonSerializerSettings _json = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        public WorkshopUndoStore(string pluginUserDataPath, ILogger logger = null)
        {
            _directory = string.IsNullOrWhiteSpace(pluginUserDataPath)
                ? null
                : Path.Combine(pluginUserDataPath, WorkshopInstalledRegistry.DirectoryName, "undo");
            _logger = logger;
        }

        /// <summary>Snapshots, newest first.</summary>
        public IReadOnlyList<WorkshopUndoEntry> List()
        {
            lock (_sync)
            {
                if (_directory == null || !Directory.Exists(_directory))
                {
                    return Array.Empty<WorkshopUndoEntry>();
                }

                var entries = new List<WorkshopUndoEntry>();
                foreach (var path in Directory.GetFiles(_directory, "*.json"))
                {
                    try
                    {
                        var entry = JsonConvert.DeserializeObject<WorkshopUndoEntry>(File.ReadAllText(path), _json);
                        if (entry != null && !string.IsNullOrWhiteSpace(entry.Id))
                        {
                            entries.Add(entry);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.Debug(ex, $"Skipping unreadable Workshop undo snapshot: {path}");
                    }
                }

                return entries.OrderByDescending(entry => entry.CreatedUtc).ToList();
            }
        }

        /// <summary>
        /// Copies the slices an install is about to replace. Returns the snapshot id, or null when
        /// nothing was requested or the store has nowhere to write.
        /// </summary>
        public string Snapshot(PersistedSettings persisted, WorkshopSettingsSlices slices, string itemId, string itemName)
        {
            if (persisted == null || slices == WorkshopSettingsSlices.None || _directory == null)
            {
                return null;
            }

            var entry = new WorkshopUndoEntry
            {
                Id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6),
                ItemId = itemId,
                ItemName = itemName,
                CreatedUtc = DateTime.UtcNow,
                Slices = slices
            };

            if (slices.HasFlag(WorkshopSettingsSlices.Colors))
            {
                entry.RarityColors = persisted.RarityColors?.Clone();
                entry.ProviderColorOverrides = new Dictionary<string, string>(
                    persisted.ProviderColorOverrides ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                entry.ResourceOverrides = (persisted.ResourceOverrides ?? new Dictionary<string, ResourceOverrideSetting>())
                    .Where(pair => pair.Value != null)
                    .ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.OrdinalIgnoreCase);
            }

            if (slices.HasFlag(WorkshopSettingsSlices.Sounds))
            {
                entry.UnlockSounds = persisted.UnlockSounds?.Clone();
            }

            if (slices.HasFlag(WorkshopSettingsSlices.NotificationStyle))
            {
                entry.NotificationStyle = persisted.NotificationStyle?.Clone();
            }

            if (slices.HasFlag(WorkshopSettingsSlices.Showcase))
            {
                entry.Showcase = persisted.Showcase?.Clone();
                entry.GridOptions = persisted.GridOptions?.Clone();
            }

            lock (_sync)
            {
                try
                {
                    Directory.CreateDirectory(_directory);
                    File.WriteAllText(PathFor(entry.Id), JsonConvert.SerializeObject(entry, _json));
                    Trim();
                }
                catch (Exception ex)
                {
                    _logger?.Warn(ex, "Failed writing a Workshop undo snapshot; the install proceeds without undo.");
                    return null;
                }
            }

            return entry.Id;
        }

        /// <summary>
        /// Puts the snapshot's slices back. The caller persists the settings and refreshes
        /// application resources afterwards, exactly as after an install. Returns the slices
        /// restored, or None when the snapshot is gone.
        /// </summary>
        public WorkshopSettingsSlices Restore(string snapshotId, PersistedSettings persisted)
        {
            if (persisted == null || string.IsNullOrWhiteSpace(snapshotId) || _directory == null)
            {
                return WorkshopSettingsSlices.None;
            }

            WorkshopUndoEntry entry;
            lock (_sync)
            {
                var path = PathFor(snapshotId);
                if (!File.Exists(path))
                {
                    return WorkshopSettingsSlices.None;
                }

                entry = JsonConvert.DeserializeObject<WorkshopUndoEntry>(File.ReadAllText(path), _json);
            }

            if (entry == null)
            {
                return WorkshopSettingsSlices.None;
            }

            if (entry.Slices.HasFlag(WorkshopSettingsSlices.Colors))
            {
                persisted.RarityColors = entry.RarityColors ?? RarityColorSettings.CreateDefault();
                persisted.ProviderColorOverrides = entry.ProviderColorOverrides ?? new Dictionary<string, string>();
                persisted.ResourceOverrides = entry.ResourceOverrides ?? new Dictionary<string, ResourceOverrideSetting>();
            }

            if (entry.Slices.HasFlag(WorkshopSettingsSlices.Sounds))
            {
                persisted.UnlockSounds = entry.UnlockSounds ?? UnlockSoundSettings.CreateDefault();
            }

            if (entry.Slices.HasFlag(WorkshopSettingsSlices.NotificationStyle))
            {
                persisted.NotificationStyle = entry.NotificationStyle ?? NotificationStyleSettings.CreateDefault();
            }

            if (entry.Slices.HasFlag(WorkshopSettingsSlices.Showcase))
            {
                if (entry.Showcase != null)
                {
                    persisted.Showcase = entry.Showcase;
                }

                if (entry.GridOptions != null)
                {
                    persisted.GridOptions = entry.GridOptions;
                }
            }

            return entry.Slices;
        }

        public void Delete(string snapshotId)
        {
            if (string.IsNullOrWhiteSpace(snapshotId) || _directory == null)
            {
                return;
            }

            lock (_sync)
            {
                try
                {
                    var path = PathFor(snapshotId);
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Debug(ex, $"Failed deleting Workshop undo snapshot {snapshotId}.");
                }
            }
        }

        private string PathFor(string id)
        {
            var safe = new string(id.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
            return Path.Combine(_directory, safe + ".json");
        }

        private void Trim()
        {
            var files = new DirectoryInfo(_directory).GetFiles("*.json")
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Skip(MaxEntries);
            foreach (var file in files)
            {
                try
                {
                    file.Delete();
                }
                catch (IOException)
                {
                }
            }
        }
    }
}
