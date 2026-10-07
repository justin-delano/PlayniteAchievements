using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Images;
using PlayniteAchievements.Services.Showcase;
using PlayniteAchievements.Services.UI;
using PlayniteAchievements.Services.Workshop;

namespace PlayniteAchievements.Services.Library
{
    /// <summary>One file the folder migration moves, by paths relative to the user data folder.</summary>
    public sealed class LibraryFolderMove
    {
        public string From { get; set; }

        public string To { get; set; }
    }

    /// <summary>What a run of <see cref="LibraryFolderMigration"/> did.</summary>
    public sealed class LibraryFolderMigrationResult
    {
        /// <summary>The files moved, by paths relative to the user data folder.</summary>
        public List<LibraryFolderMove> Moved { get; } = new List<LibraryFolderMove>();

        /// <summary>Moved files that took a free name because the new folder already had their name.</summary>
        public List<LibraryFolderMove> Renamed { get; } = new List<LibraryFolderMove>();

        /// <summary>Files that could not be moved this run; the journal keeps them for the next.</summary>
        public int Failed { get; set; }

        /// <summary>Library index entries pointed at a moved file.</summary>
        public int RewrittenItems { get; set; }

        /// <summary>Links and recorded installs pointed at a moved baseline.</summary>
        public int RewrittenBaselines { get; set; }

        public bool DeletedRetiredInstalls { get; set; }

        /// <summary>Folders removed because no file was left in them, relative to the user data folder.</summary>
        public List<string> RemovedFolders { get; } = new List<string>();

        public bool HasChanges =>
            Moved.Count > 0 || Failed > 0 || RewrittenItems > 0 || RewrittenBaselines > 0 ||
            DeletedRetiredInstalls || RemovedFolders.Count > 0;
    }

    /// <summary>
    /// Moves the preset folders that sat at the root of the user data folder into the library
    /// folder (<see cref="LibraryStore.FolderOf"/>) and the game data baselines from the Workshop
    /// folder into the library's game data folder, then points <c>library.json</c>,
    /// <c>links.json</c> and an un-migrated <c>installed.json</c> at the new places. Runs at
    /// startup before anything reads the library, so a reconcile never sees the files as gone.
    /// <para>
    /// Crash-safe and idempotent: the moves are written to a journal
    /// (<c>library\folders.migration.json</c>) before the first file moves, each move is a
    /// same-volume rename, and the journal is deleted only after every file moved and the index
    /// and links point at the new paths; a run that finds the journal finishes it. A file whose
    /// name the new folder already holds keeps both: the moved file takes a free name, recorded
    /// in the journal before the move, which the index follows. A path is rewritten only once its
    /// file is at the new place, so an entry whose file could not be moved keeps a path that
    /// still exists. Old folders, and folders other features create before writing anything,
    /// are removed only when no file is left in them. Also deletes the retired
    /// <c>installed.migrated.json</c>.
    /// </para>
    /// </summary>
    public static class LibraryFolderMigration
    {
        public const string JournalFileName = "folders.migration.json";

        private const int SchemaVersion = 1;

        // The recording service's buffer root; the service is not linked into the tests.
        private const string RecordingBufferFolderName = "RecordingBuffer";

        private const string OldNotificationPresetsFolder = "notification_style_presets";

        /// <summary>Each old folder with the folder its files move to, relative to the user data folder.</summary>
        public static IReadOnlyList<LibraryFolderMove> FolderMoves { get; } = new[]
        {
            new LibraryFolderMove { From = "color_presets", To = LibraryStore.FolderOf(LibraryItemKind.Colors) },
            new LibraryFolderMove { From = "unlock_sound_presets", To = LibraryStore.FolderOf(LibraryItemKind.Sounds) },
            new LibraryFolderMove { From = Path.Combine(OldNotificationPresetsFolder, "toast"), To = LibraryStore.FolderOf(LibraryItemKind.Toast) },
            new LibraryFolderMove { From = Path.Combine(OldNotificationPresetsFolder, "frame"), To = LibraryStore.FolderOf(LibraryItemKind.Frame) },
            new LibraryFolderMove { From = "showcase_presets", To = LibraryStore.FolderOf(LibraryItemKind.ShowcasePage) },
            new LibraryFolderMove
            {
                From = Path.Combine(WorkshopIdentityStore.DirectoryName, WorkshopBaselineStore.FolderName),
                To = LibraryStore.GameDataBaselinesFolder
            }
        };

        /// <summary>Folders removed whenever no file is left in them, besides the old folders, relative to the user data folder.</summary>
        public static IReadOnlyList<string> FoldersRemovedWhenEmpty { get; } = new[]
        {
            OldNotificationPresetsFolder,
            AchievementToastTemplateResolver.CustomTemplatesDirectoryName,
            ShowcaseImageStore.RootFolderName,
            FallbackIconStore.RootFolderName,
            RecordingBufferFolderName
        };

        private sealed class JournalFile
        {
            public int SchemaVersion { get; set; }

            public List<LibraryFolderMove> Moves { get; set; } = new List<LibraryFolderMove>();
        }

        /// <param name="pluginUserDataPath">The plugin's user data folder.</param>
        /// <param name="warn">Optional sink for failures; a failure leaves the journal for the next run.</param>
        public static LibraryFolderMigrationResult Run(string pluginUserDataPath, Action<Exception, string> warn = null)
        {
            if (string.IsNullOrWhiteSpace(pluginUserDataPath))
            {
                throw new ArgumentException("Plugin user data path is required.", nameof(pluginUserDataPath));
            }

            var root = pluginUserDataPath;
            var result = new LibraryFolderMigrationResult();
            var journalPath = Path.Combine(root, LibraryStore.DirectoryName, JournalFileName);
            var resuming = File.Exists(journalPath);
            var moves = (resuming ? ReadJournal(journalPath, warn) : null) ?? Plan(root);

            if (moves.Count > 0 || resuming)
            {
                // The journal is on disk before any file moves, so a crash at any point resumes.
                WriteJournal(journalPath, moves);
                Execute(root, moves, journalPath, result, warn);

                result.RewrittenItems = RewriteIndex(root, moves, warn);
                result.RewrittenBaselines =
                    RewriteBaselines(Path.Combine(root, LibraryStore.DirectoryName, GameLinkStore.FileName), root, moves, warn) +
                    RewriteBaselines(Path.Combine(root, WorkshopIdentityStore.DirectoryName, WorkshopIdentityStore.InstalledFileName), root, moves, warn);

                if (result.Failed > 0)
                {
                    return result;
                }

                File.Delete(journalPath);
            }

            result.DeletedRetiredInstalls = TryDeleteFile(
                Path.Combine(root, WorkshopIdentityStore.DirectoryName, WorkshopIdentityStore.RetiredInstalledFileName),
                warn);

            foreach (var folder in FolderMoves.Select(move => move.From).Concat(FoldersRemovedWhenEmpty))
            {
                if (TryRemoveIfNoFiles(Path.Combine(root, folder), warn))
                {
                    result.RemovedFolders.Add(folder);
                }
            }

            return result;
        }

        /// <summary>Every file under the old folders, each with its place under the new folder.</summary>
        public static List<LibraryFolderMove> Plan(string pluginUserDataPath)
        {
            var moves = new List<LibraryFolderMove>();
            foreach (var folder in FolderMoves)
            {
                var directory = Path.Combine(pluginUserDataPath, folder.From);
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                // Ordinal order puts a baseline before its icon-hashes sibling, which follows it.
                foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                             .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    var rest = file.Substring(directory.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    moves.Add(new LibraryFolderMove
                    {
                        From = Path.Combine(folder.From, rest),
                        To = Path.Combine(folder.To, rest)
                    });
                }
            }

            return moves;
        }

        /// <summary>
        /// Where a path relative to the user data folder lives after the migration, or null when
        /// it does not move or its file is not at the new place yet.
        /// </summary>
        public static string MapRelative(string pluginUserDataPath, IEnumerable<LibraryFolderMove> moves, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative))
            {
                return null;
            }

            var normalized = Normalize(relative);
            var target = moves?.FirstOrDefault(move => SamePath(move.From, normalized))?.To;
            if (target == null)
            {
                foreach (var folder in FolderMoves)
                {
                    var prefix = Normalize(folder.From) + Path.DirectorySeparatorChar;
                    if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        target = Path.Combine(folder.To, normalized.Substring(prefix.Length));
                        break;
                    }
                }
            }

            if (target == null || SamePath(target, normalized))
            {
                return null;
            }

            // Only once the file is at the new place: a path whose file did not move still exists.
            return File.Exists(Path.Combine(pluginUserDataPath, target)) && !File.Exists(Path.Combine(pluginUserDataPath, normalized))
                ? target
                : null;
        }

        // ---- steps ------------------------------------------------------------------------------

        private static void Execute(
            string root,
            List<LibraryFolderMove> moves,
            string journalPath,
            LibraryFolderMigrationResult result,
            Action<Exception, string> warn)
        {
            foreach (var move in moves)
            {
                var from = Path.Combine(root, move.From);
                if (!File.Exists(from))
                {
                    // Moved by an earlier run, or gone.
                    continue;
                }

                try
                {
                    // An icon-hashes file follows its baseline, whatever name that took.
                    var owner = moves.FirstOrDefault(other =>
                        !ReferenceEquals(other, move) && SamePath(WorkshopBaselineStore.IconHashesPath(other.From), move.From));
                    var to = owner != null ? WorkshopBaselineStore.IconHashesPath(owner.To) : move.To;

                    if (File.Exists(Path.Combine(root, to)))
                    {
                        to = FreeName(root, to);
                        result.Renamed.Add(new LibraryFolderMove { From = move.From, To = to });
                    }

                    if (!SamePath(to, move.To))
                    {
                        // Recorded before the move, so a resume maps the file to the name it took.
                        move.To = to;
                        WriteJournal(journalPath, moves);
                    }

                    var destination = Path.Combine(root, to);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    File.Move(from, destination);
                    result.Moved.Add(new LibraryFolderMove { From = move.From, To = to });
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    warn?.Invoke(ex, $"Failed moving {move.From} into the library; the next startup tries again.");
                }
            }
        }

        /// <summary>Points the index entries of moved files at their new paths. Returns how many changed.</summary>
        private static int RewriteIndex(string root, List<LibraryFolderMove> moves, Action<Exception, string> warn)
        {
            var path = Path.Combine(root, LibraryStore.DirectoryName, LibraryStore.IndexFileName);
            var index = ReadJson(path, warn) as JObject;
            if (!(index?["Items"] is JArray items))
            {
                return 0;
            }

            var changed = 0;
            foreach (var item in items.OfType<JObject>())
            {
                var relative = item.Value<string>(nameof(LibraryItem.RelativePath));
                var mapped = MapRelative(root, moves, relative);
                if (mapped == null)
                {
                    continue;
                }

                item[nameof(LibraryItem.RelativePath)] = mapped;
                if (string.Equals(item.Value<string>(nameof(LibraryItem.Origin)), nameof(LibraryItemOrigin.Local), StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(Path.GetFileName(relative), Path.GetFileName(mapped), StringComparison.OrdinalIgnoreCase))
                {
                    // A local item is named by its file, as a reconcile names a renamed one.
                    item[nameof(LibraryItem.Name)] = Path.GetFileNameWithoutExtension(mapped);
                }

                changed++;
            }

            if (changed > 0)
            {
                WriteJson(path, index);
            }

            return changed;
        }

        /// <summary>
        /// Points every rooted <c>BaselineFile</c> in a JSON file (the links, or the installs
        /// <c>installed.json</c> records) at a moved baseline. Returns how many changed.
        /// </summary>
        private static int RewriteBaselines(string path, string root, List<LibraryFolderMove> moves, Action<Exception, string> warn)
        {
            var document = ReadJson(path, warn) as JContainer;
            if (document == null)
            {
                return 0;
            }

            var rootPrefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var changed = 0;
            foreach (var record in document.DescendantsAndSelf().OfType<JObject>())
            {
                var baseline = record.Value<string>(nameof(LibraryLink.BaselineFile));
                if (string.IsNullOrWhiteSpace(baseline) || !Path.IsPathRooted(baseline))
                {
                    continue;
                }

                string full;
                try
                {
                    full = Path.GetFullPath(baseline);
                }
                catch (Exception)
                {
                    continue;
                }

                if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var mapped = MapRelative(root, moves, full.Substring(rootPrefix.Length));
                if (mapped != null)
                {
                    record[nameof(LibraryLink.BaselineFile)] = Path.Combine(root, mapped);
                    changed++;
                }
            }

            if (changed > 0)
            {
                WriteJson(path, document);
            }

            return changed;
        }

        // ---- files ------------------------------------------------------------------------------

        /// <summary>The path with " (2)", " (3)" and so on before its extension, the first that no file has.</summary>
        private static string FreeName(string root, string relative)
        {
            var directory = Path.GetDirectoryName(relative) ?? string.Empty;
            var stem = Path.GetFileNameWithoutExtension(relative);
            var extension = Path.GetExtension(relative);
            for (var n = 2; n < 10000; n++)
            {
                var candidate = Path.Combine(directory, stem + " (" + n + ")" + extension);
                if (!File.Exists(Path.Combine(root, candidate)))
                {
                    return candidate;
                }
            }

            throw new IOException($"No free name for {relative}.");
        }

        private static List<LibraryFolderMove> ReadJournal(string path, Action<Exception, string> warn)
        {
            try
            {
                var journal = JsonConvert.DeserializeObject<JournalFile>(File.ReadAllText(path));
                return journal?.Moves?
                    .Where(move => move != null && !string.IsNullOrWhiteSpace(move.From) && !string.IsNullOrWhiteSpace(move.To))
                    .ToList();
            }
            catch (Exception ex)
            {
                // Planned again: files already moved are found at their new place by folder.
                warn?.Invoke(ex, "Failed reading the library folder migration journal; it is planned again.");
                return null;
            }
        }

        private static void WriteJournal(string path, List<LibraryFolderMove> moves)
        {
            WriteText(path, JsonConvert.SerializeObject(new JournalFile { SchemaVersion = SchemaVersion, Moves = moves }, Formatting.Indented));
        }

        /// <summary>The JSON in a file, with dates left as written; null when there is no file or it cannot be read.</summary>
        private static JToken ReadJson(string path, Action<Exception, string> warn)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                using (var reader = new JsonTextReader(new StringReader(File.ReadAllText(path))) { DateParseHandling = DateParseHandling.None })
                {
                    return JToken.ReadFrom(reader);
                }
            }
            catch (Exception ex)
            {
                // Its owner reports and backs up an unreadable file when it loads it.
                warn?.Invoke(ex, $"Failed reading {path} for the library folder migration; its paths are left as they are.");
                return null;
            }
        }

        private static void WriteJson(string path, JToken document)
        {
            WriteText(path, document.ToString(Formatting.Indented));
        }

        /// <summary>Writes beside the file and moves into place, as the library's stores do.</summary>
        private static void WriteText(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = path + ".tmp";
            File.WriteAllText(temp, text);
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(temp, path);
        }

        private static bool TryDeleteFile(string path, Action<Exception, string> warn)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return false;
                }

                File.Delete(path);
                return true;
            }
            catch (Exception ex)
            {
                warn?.Invoke(ex, $"Failed deleting {path}.");
                return false;
            }
        }

        /// <summary>Removes a folder that holds no file, at any depth. True when it was removed.</summary>
        private static bool TryRemoveIfNoFiles(string directory, Action<Exception, string> warn)
        {
            try
            {
                if (!Directory.Exists(directory) || Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Any())
                {
                    return false;
                }

                Directory.Delete(directory, recursive: true);
                return true;
            }
            catch (Exception ex)
            {
                warn?.Invoke(ex, $"Failed removing the empty folder {directory}.");
                return false;
            }
        }

        private static string Normalize(string relative)
        {
            return relative.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).Trim(Path.DirectorySeparatorChar);
        }

        private static bool SamePath(string left, string right)
        {
            return !string.IsNullOrEmpty(left) && !string.IsNullOrEmpty(right)
                   && string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
        }
    }
}
