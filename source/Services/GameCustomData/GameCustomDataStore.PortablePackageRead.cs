using PlayniteAchievements.Models.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace PlayniteAchievements.Services.GameCustomData
{
    public sealed partial class GameCustomDataStore
    {
        /// <summary>
        /// Reads a .pa package the way an import would, without importing it: nothing is saved,
        /// no managed icon or notification image is written, and no custom provider is created.
        /// Bundled images are extracted into <paramref name="imageScratchDirectory"/>, which the
        /// caller owns and deletes, including after a throw.
        /// </summary>
        /// <remarks>
        /// A manifest package runs the import's pre-save sequence: the image rewrites (with the
        /// same entry validation, so a package an import rejects throws here too), the legacy icon
        /// map sync, normalization and <see cref="PortablePersonalState.Strip(GameCustomDataPortableFile)"/>,
        /// then the same empty-data check. It stops before the steps that need a target game:
        /// resolving <see cref="GameCustomDataPortableFile.CustomProviderId"/> against the local
        /// catalog (an import may create the provider), stamping the game id, and building the
        /// record with <see cref="GameCustomDataFile.FromPortable"/> plus the target's exclusions
        /// and local state. A caller that wants the record for a game does those itself.
        ///
        /// A custom-achievements package returns the CSV parse with icons extracted, as
        /// <see cref="ImportCustomAchievementsPackage"/> does before the caller merges it.
        ///
        /// An image-only package lists its icons by API name. An import also matches them
        /// against the target game's cached achievements and drops the rest; that needs a game,
        /// so it is left to the caller.
        /// </remarks>
        public GameCustomDataPortablePackage ReadPortablePackage(string sourcePath, string imageScratchDirectory)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                throw new ArgumentException("Source path is required.", nameof(sourcePath));
            }

            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("Package file not found.", sourcePath);
            }

            var sink = new ScratchPackageImageSink(imageScratchDirectory);
            using (var archive = ZipFile.OpenRead(sourcePath))
            {
                var entriesByName = IndexPackageEntries(archive);

                // Custom-achievements packages import without an extension check; the other two
                // forms go through ImportReplacePortable, which requires one.
                var isCustomAchievementsPackage = IsCustomAchievementsPackage(entriesByName.Keys);
                if (!isCustomAchievementsPackage && !IsPortablePackagePath(sourcePath))
                {
                    throw new InvalidOperationException("Only .PA files are supported.");
                }

                if (entriesByName.ContainsKey(PortablePackageManifestEntryName))
                {
                    var portable = ReadPortableManifestOrNull(entriesByName);
                    RewritePackageManifestImages(sink, entriesByName, portable);

                    var normalized = GameCustomDataNormalizer.NormalizePortable(portable, Guid.Empty);
                    PortablePersonalState.Strip(normalized);
                    if (!GameCustomDataNormalizer.HasPortableData(normalized))
                    {
                        throw new InvalidOperationException("Imported .PA.ZIP does not contain any portable custom data.");
                    }

                    return new GameCustomDataPortablePackage(
                        shape: GameCustomDataPackageShape.Manifest,
                        manifest: normalized,
                        customAchievements: null,
                        extractedImages: CopyExtracted(sink),
                        imageOnlyEntries: null);
                }

                if (isCustomAchievementsPackage)
                {
                    var parsed = ReadCustomAchievementsPackage(sink, entriesByName);
                    return new GameCustomDataPortablePackage(
                        shape: GameCustomDataPackageShape.CustomAchievementsCsv,
                        manifest: null,
                        customAchievements: parsed,
                        extractedImages: CopyExtracted(sink),
                        imageOnlyEntries: null);
                }

                var imageOnlyEntries = new List<GameCustomDataImageOnlyEntry>();
                foreach (var pair in entriesByName.OrderBy(a => a.Key, StringComparer.OrdinalIgnoreCase))
                {
                    if (!TryParseImageOnlyPackageEntry(pair.Key, out var apiName, out var variant))
                    {
                        continue;
                    }

                    imageOnlyEntries.Add(new GameCustomDataImageOnlyEntry(apiName, variant, sink.Extract(pair.Value, null)));
                }

                return new GameCustomDataPortablePackage(
                    shape: GameCustomDataPackageShape.ImageOnly,
                    manifest: null,
                    customAchievements: null,
                    extractedImages: CopyExtracted(sink),
                    imageOnlyEntries: imageOnlyEntries);
            }
        }

        private static IReadOnlyDictionary<string, string> CopyExtracted(ScratchPackageImageSink sink)
        {
            return sink.Extracted.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        }
    }
}
