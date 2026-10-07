using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Achievements;
using PlayniteAchievements.Services.Images;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace PlayniteAchievements.Services.GameCustomData
{
    /// <summary>
    /// The custom-achievements form of the .pa package: a zip holding one CSV of custom
    /// achievement definitions (<see cref="CustomAchievementCsvFormat"/>, plus Unlocked Icon and
    /// Locked Icon columns) and the bundled icon files under the package images folder. It has
    /// no <see cref="PortablePackageManifestEntryName"/> entry, which is what tells it apart from
    /// a whole-game package. Nothing in the plugin writes one; a package that exists still
    /// imports, and never brings unlock state or progress (<see cref="PortablePersonalState"/>).
    /// </summary>
    public sealed partial class GameCustomDataStore
    {
        public const string CustomAchievementsPackageCsvEntryName = "custom-achievements.csv";

        /// <summary>
        /// Whether a .pa package carries custom achievements only (the CSV and no manifest), and
        /// so merges into the editor instead of replacing the game's custom data.
        /// </summary>
        public bool IsCustomAchievementsPackage(string sourcePath)
        {
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("Package file not found.", sourcePath);
            }

            using (var archive = ZipFile.OpenRead(sourcePath))
            {
                return IsCustomAchievementsPackage(
                    archive.Entries.Select(entry => NormalizeArchiveEntryName(entry.FullName)));
            }
        }

        private static bool IsCustomAchievementsPackage(IEnumerable<string> entryNames)
        {
            var names = new HashSet<string>(
                entryNames.Where(name => !string.IsNullOrWhiteSpace(name)),
                StringComparer.OrdinalIgnoreCase);
            return !names.Contains(PortablePackageManifestEntryName) &&
                   names.Contains(CustomAchievementsPackageCsvEntryName);
        }

        /// <summary>
        /// Reads the package CSV and copies any bundled icons into this game's managed custom
        /// icon folder, returning definitions whose icon paths point at those managed files.
        /// Icon values that are URLs or rooted local paths pass through unchanged.
        /// </summary>
        public CustomAchievementTextImportResult ImportCustomAchievementsPackage(Guid playniteGameId, string sourcePath)
        {
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("Package file not found.", sourcePath);
            }

            using (var archive = ZipFile.OpenRead(sourcePath))
            {
                return ReadCustomAchievementsPackage(
                    new ManagedPackageImageSink(this, playniteGameId),
                    IndexPackageEntries(archive));
            }
        }

        /// <summary>
        /// Parses the package CSV, strips personal state, and hands each bundled icon to the sink,
        /// returning definitions whose icon paths point at the files the sink wrote. Returns the
        /// parse result untouched when it has errors or no definitions.
        /// </summary>
        private static CustomAchievementTextImportResult ReadCustomAchievementsPackage(
            IPackageImageSink sink,
            IReadOnlyDictionary<string, ZipArchiveEntry> entriesByName)
        {
            if (!entriesByName.TryGetValue(CustomAchievementsPackageCsvEntryName, out var csvEntry))
            {
                throw new InvalidOperationException(
                    "Package does not contain " + CustomAchievementsPackageCsvEntryName + ".");
            }

            string csvText;
            using (var reader = new StreamReader(csvEntry.Open()))
            {
                csvText = reader.ReadToEnd();
            }

            var result = new CustomAchievementTextImportService().Import(csvText);
            if (result.HasErrors || result.Definitions.Count == 0)
            {
                return result;
            }

            foreach (var definition in result.Definitions)
            {
                PortablePersonalState.Strip(definition);
            }

            var fileStems = AchievementIconCachePathBuilder.BuildFileStems(
                result.Definitions.Select(definition => CustomAchievementProjectionService.BuildApiName(definition?.Id)));
            foreach (var definition in result.Definitions)
            {
                var apiName = CustomAchievementProjectionService.BuildApiName(definition?.Id);
                if (definition == null || string.IsNullOrWhiteSpace(apiName))
                {
                    continue;
                }

                definition.UnlockedIconPath = ImportPackagedCustomAchievementIcon(
                    sink, entriesByName, fileStems, apiName, definition.UnlockedIconPath, AchievementIconVariant.Unlocked);
                definition.LockedIconPath = ImportPackagedCustomAchievementIcon(
                    sink, entriesByName, fileStems, apiName, definition.LockedIconPath, AchievementIconVariant.Locked);
            }

            return result;
        }

        private static string ImportPackagedCustomAchievementIcon(
            IPackageImageSink sink,
            IReadOnlyDictionary<string, ZipArchiveEntry> entriesByName,
            IReadOnlyDictionary<string, string> fileStems,
            string apiName,
            string value,
            AchievementIconVariant variant)
        {
            var normalizedValue = NormalizeText(value);
            if (string.IsNullOrWhiteSpace(normalizedValue) || Path.IsPathRooted(normalizedValue))
            {
                return normalizedValue;
            }

            return RewritePackageCustomAchievementImage(
                sink,
                entriesByName,
                fileStems,
                apiName,
                normalizedValue,
                variant);
        }
    }
}
