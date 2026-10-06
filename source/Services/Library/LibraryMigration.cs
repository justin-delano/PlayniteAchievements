using System;
using System.Collections.Generic;
using System.Linq;
using PlayniteAchievements.Services.Workshop;

namespace PlayniteAchievements.Services.Library
{
    /// <summary>One change the migration makes to the library.</summary>
    public sealed class LibraryMigrationStep
    {
        /// <summary>The item the step replaces (a local item that is a Workshop install), or null for an add or update in place.</summary>
        public string ReplacesId { get; set; }

        public LibraryItem Item { get; set; }

        /// <summary>A local item to add in the same step, for the user's edited copy a reinstall left beside the new file.</summary>
        public LibraryItem AlsoAdd { get; set; }
    }

    /// <summary>A game a Workshop game-data item was installed onto, with the baseline its updates merge against.</summary>
    public sealed class LibraryGameDataInstall
    {
        public string LibraryItemId { get; set; }

        public string WorkshopItemId { get; set; }

        public Guid PlayniteGameId { get; set; }

        public string BaselineFile { get; set; }

        public DateTime InstalledUtc { get; set; }
    }

    /// <summary>What the migration does, or did.</summary>
    public sealed class LibraryMigrationPlan
    {
        public List<LibraryMigrationStep> Steps { get; } = new List<LibraryMigrationStep>();

        /// <summary>Every game-data install the registry records; the links to them move to the library later.</summary>
        public List<LibraryGameDataInstall> GameDataInstalls { get; } = new List<LibraryGameDataInstall>();

        /// <summary>Local ids replaced by Workshop ids, so links held under the old id can follow.</summary>
        public IEnumerable<KeyValuePair<string, string>> RenamedIds =>
            Steps.Where(step => step.ReplacesId != null && !string.Equals(step.ReplacesId, step.Item.Id, StringComparison.OrdinalIgnoreCase))
                .Select(step => new KeyValuePair<string, string>(step.ReplacesId, step.Item.Id));

        /// <summary>True when the run created the library index (no library.json existed).</summary>
        public bool CreatedIndex { get; set; }
    }

    /// <summary>
    /// Brings what <c>installed.json</c> records into the library. A look (colors, sounds,
    /// notification style, frame, or a bundle part) becomes a Workshop item when a preset file of
    /// its kind still has the hash the install recorded for that part; a preset the user changed
    /// since stays a local item. Showcase pages and game data, which were applied rather than
    /// saved as presets, become Workshop items without a package. Planning is pure; running it
    /// is idempotent, so it runs at every startup and also picks up installs made since.
    /// </summary>
    public static class LibraryMigration
    {
        public const string ColorsPart = "colors";
        public const string SoundsPart = "sounds";
        public const string ToastPart = "toast";
        public const string FramePart = "frame";

        /// <summary>The library kind of a package part, or null for an unknown part.</summary>
        public static LibraryItemKind? KindOfPart(string part)
        {
            switch (part?.Trim().ToLowerInvariant())
            {
                case ColorsPart: return LibraryItemKind.Colors;
                case SoundsPart: return LibraryItemKind.Sounds;
                case ToastPart: return LibraryItemKind.Toast;
                case FramePart: return LibraryItemKind.Frame;
                default: return null;
            }
        }

        /// <summary>
        /// The library id of one part of a Workshop item: <c>ws:&lt;id&gt;</c> for the part a
        /// single-kind item is made of, <c>ws:&lt;id&gt;#&lt;part&gt;</c> for a bundle part or
        /// for the frame a notification style also carries.
        /// </summary>
        public static string LibraryIdFor(string workshopItemId, WorkshopItemKind kind, string part)
        {
            return string.Equals(PrimaryPart(kind), part, StringComparison.OrdinalIgnoreCase)
                ? LibraryItem.WorkshopId(workshopItemId)
                : LibraryItem.WorkshopId(workshopItemId, part);
        }

        /// <summary>The part=hash pairs of a recorded install hash (<c>part=hash;part=hash</c>).</summary>
        public static IReadOnlyList<KeyValuePair<string, string>> ParsePartHashes(string joined)
        {
            var result = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrWhiteSpace(joined))
            {
                return result;
            }

            foreach (var entry in joined.Split(';'))
            {
                var separator = entry.IndexOf('=');
                if (separator <= 0 || separator == entry.Length - 1)
                {
                    continue;
                }

                result.Add(new KeyValuePair<string, string>(
                    entry.Substring(0, separator).Trim().ToLowerInvariant(),
                    entry.Substring(separator + 1).Trim()));
            }

            return result;
        }

        /// <summary>The changes that bring <paramref name="records"/> into <paramref name="items"/> (an already reconciled library).</summary>
        public static LibraryMigrationPlan Plan(IEnumerable<WorkshopInstalledItem> records, IEnumerable<LibraryItem> items)
        {
            var plan = new LibraryMigrationPlan();
            var working = (items ?? Enumerable.Empty<LibraryItem>()).Where(item => item != null).Select(item => item.Clone()).ToList();
            var valid = (records ?? Enumerable.Empty<WorkshopInstalledItem>())
                .Where(record => record != null && !string.IsNullOrWhiteSpace(record.Id))
                .ToList();

            foreach (var record in valid.Where(record => IsLook(record.Kind)).OrderBy(record => record.InstalledUtc))
            {
                foreach (var pair in ParsePartHashes(record.ContentHash))
                {
                    var kind = KindOfPart(pair.Key);
                    if (kind != null)
                    {
                        PlanLookPart(plan, working, record, pair.Key, kind.Value, pair.Value);
                    }
                }
            }

            foreach (var record in valid.Where(record => record.Kind == WorkshopItemKind.ShowcasePage))
            {
                PlanWithoutPackage(plan, working, record, LibraryItemKind.ShowcasePage);
            }

            foreach (var group in valid.Where(record => record.Kind == WorkshopItemKind.GameCustomData)
                         .GroupBy(record => record.Id, StringComparer.OrdinalIgnoreCase))
            {
                var newest = group.OrderByDescending(record => record.InstalledUtc).First();
                var libraryId = PlanWithoutPackage(plan, working, newest, LibraryItemKind.GameData);
                foreach (var record in group.Where(record => record.PlayniteGameId.HasValue))
                {
                    plan.GameDataInstalls.Add(new LibraryGameDataInstall
                    {
                        LibraryItemId = libraryId,
                        WorkshopItemId = record.Id,
                        PlayniteGameId = record.PlayniteGameId.Value,
                        BaselineFile = record.BaselineFile,
                        InstalledUtc = record.InstalledUtc
                    });
                }
            }

            return plan;
        }

        /// <summary>
        /// Plans against the reconciled library and applies the plan. Safe to run at every
        /// startup: a second run finds nothing left to do.
        /// </summary>
        public static LibraryMigrationPlan Run(LibraryStore store, WorkshopInstalledRegistry registry)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            var createdIndex = !store.IndexExists;
            store.Reconcile();
            var plan = Plan(registry?.Items, store.Items);
            plan.CreatedIndex = createdIndex;
            foreach (var step in plan.Steps)
            {
                if (step.ReplacesId != null)
                {
                    store.Replace(step.ReplacesId, step.Item);
                }
                else
                {
                    store.Upsert(step.Item);
                }

                if (step.AlsoAdd != null)
                {
                    store.Upsert(step.AlsoAdd);
                }
            }

            return plan;
        }

        private static void PlanLookPart(
            LibraryMigrationPlan plan,
            List<LibraryItem> working,
            WorkshopInstalledItem record,
            string part,
            LibraryItemKind kind,
            string recordedHash)
        {
            var libraryId = LibraryIdFor(record.Id, record.Kind, part);
            var existing = Find(working, libraryId);
            var preferredName = PackagePresetStore.SanitizeName(record.Name);

            if (existing != null && SameHash(existing.ContentHash, recordedHash))
            {
                if (!string.Equals(existing.Version, record.Version, StringComparison.Ordinal))
                {
                    var updated = existing.Clone();
                    updated.Version = record.Version;
                    updated.Name = record.Name ?? updated.Name;
                    Apply(plan, working, null, updated, null);
                }

                return;
            }

            // The local preset file that is this install, unchanged since it was written.
            var candidate = working
                .Where(item => item.Kind == kind && item.Origin == LibraryItemOrigin.Local
                               && !string.IsNullOrEmpty(item.RelativePath) && SameHash(item.ContentHash, recordedHash))
                .OrderByDescending(item => string.Equals(item.Name, preferredName, StringComparison.OrdinalIgnoreCase))
                .ThenBy(item => item.AddedUtc)
                .FirstOrDefault();
            if (candidate == null)
            {
                // The user changed the preset since the install (or deleted it): it stays local.
                return;
            }

            var item = new LibraryItem
            {
                Id = libraryId,
                Kind = kind,
                Name = string.IsNullOrWhiteSpace(record.Name) ? candidate.Name : record.Name,
                RelativePath = candidate.RelativePath,
                Origin = LibraryItemOrigin.Workshop,
                WorkshopItemId = record.Id,
                Part = part,
                Version = record.Version,
                ContentHash = candidate.ContentHash,
                FileLength = candidate.FileLength,
                FileWriteUtc = candidate.FileWriteUtc,
                AddedUtc = existing?.AddedUtc ?? (record.InstalledUtc == default(DateTime) ? candidate.AddedUtc : record.InstalledUtc)
            };

            // An update that found the earlier copy edited wrote beside it: the Workshop item moves
            // to the new file, and the edited file it leaves stays as a local item.
            LibraryItem leftBehind = null;
            if (existing != null && !string.IsNullOrEmpty(existing.RelativePath))
            {
                leftBehind = new LibraryItem
                {
                    Id = LibraryItem.NewLocalId(),
                    Kind = existing.Kind,
                    Name = System.IO.Path.GetFileNameWithoutExtension(existing.RelativePath),
                    RelativePath = existing.RelativePath,
                    Origin = LibraryItemOrigin.Local,
                    ContentHash = existing.ContentHash,
                    Version = existing.ContentHash,
                    FileLength = existing.FileLength,
                    FileWriteUtc = existing.FileWriteUtc,
                    AddedUtc = existing.AddedUtc
                };
            }

            Apply(plan, working, candidate.Id, item, leftBehind);
        }

        private static string PlanWithoutPackage(LibraryMigrationPlan plan, List<LibraryItem> working, WorkshopInstalledItem record, LibraryItemKind kind)
        {
            var libraryId = LibraryItem.WorkshopId(record.Id);
            var existing = Find(working, libraryId);
            if (existing != null)
            {
                // An item with a stored package is kept up to date by installs, not by the registry.
                if (string.IsNullOrEmpty(existing.RelativePath)
                    && !string.Equals(existing.Version, record.Version, StringComparison.Ordinal))
                {
                    var updated = existing.Clone();
                    updated.Version = record.Version;
                    updated.Name = record.Name ?? updated.Name;
                    Apply(plan, working, null, updated, null);
                }

                return libraryId;
            }

            Apply(plan, working, null, new LibraryItem
            {
                Id = libraryId,
                Kind = kind,
                Name = record.Name,
                Origin = LibraryItemOrigin.Workshop,
                WorkshopItemId = record.Id,
                Version = record.Version,
                AddedUtc = record.InstalledUtc
            }, null);
            return libraryId;
        }

        private static void Apply(LibraryMigrationPlan plan, List<LibraryItem> working, string replacesId, LibraryItem item, LibraryItem alsoAdd)
        {
            plan.Steps.Add(new LibraryMigrationStep
            {
                ReplacesId = replacesId,
                Item = item.Clone(),
                AlsoAdd = alsoAdd?.Clone()
            });

            working.RemoveAll(existing =>
                string.Equals(existing.Id, item.Id, StringComparison.OrdinalIgnoreCase) ||
                (replacesId != null && string.Equals(existing.Id, replacesId, StringComparison.OrdinalIgnoreCase)));
            working.Add(item.Clone());
            if (alsoAdd != null)
            {
                working.Add(alsoAdd.Clone());
            }
        }

        private static LibraryItem Find(List<LibraryItem> working, string id)
        {
            return working.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        private static bool SameHash(string left, string right)
        {
            return !string.IsNullOrEmpty(left) && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLook(WorkshopItemKind kind)
        {
            return kind == WorkshopItemKind.Colors
                   || kind == WorkshopItemKind.UnlockSounds
                   || kind == WorkshopItemKind.NotificationStyle
                   || kind == WorkshopItemKind.ScreenshotFrame
                   || kind == WorkshopItemKind.Bundle;
        }

        private static string PrimaryPart(WorkshopItemKind kind)
        {
            switch (kind)
            {
                case WorkshopItemKind.Colors: return ColorsPart;
                case WorkshopItemKind.UnlockSounds: return SoundsPart;
                case WorkshopItemKind.NotificationStyle: return ToastPart;
                case WorkshopItemKind.ScreenshotFrame: return FramePart;
                default: return null;
            }
        }
    }
}
