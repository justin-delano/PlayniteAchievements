using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Images;
using System;
using System.Collections.Generic;

namespace PlayniteAchievements.Services.GameCustomData
{
    /// <summary>Which of the three .pa package forms a package is.</summary>
    public enum GameCustomDataPackageShape
    {
        /// <summary>A whole-game package with a custom-data.pa manifest.</summary>
        Manifest,

        /// <summary>A custom-achievements package: a CSV of definitions and their icons, no manifest.</summary>
        CustomAchievementsCsv,

        /// <summary>Icons only, named by achievement API name, with neither manifest nor CSV.</summary>
        ImageOnly
    }

    /// <summary>One icon of an image-only package, named by the achievement it is for.</summary>
    public sealed class GameCustomDataImageOnlyEntry
    {
        public GameCustomDataImageOnlyEntry(string apiName, AchievementIconVariant variant, string path)
        {
            ApiName = apiName;
            Variant = variant;
            Path = path;
        }

        /// <summary>The achievement API name the entry's file name carries.</summary>
        public string ApiName { get; }

        /// <summary>Locked when the file name ends in .locked, unlocked otherwise.</summary>
        public AchievementIconVariant Variant { get; }

        /// <summary>The absolute path the entry was extracted to.</summary>
        public string Path { get; }
    }

    /// <summary>
    /// A .pa package read for display without being imported: its contents as an import would see
    /// them just before saving, with every bundled image extracted to a scratch directory.
    /// </summary>
    public sealed class GameCustomDataPortablePackage
    {
        public GameCustomDataPortablePackage(
            GameCustomDataPackageShape shape,
            GameCustomDataPortableFile manifest,
            CustomAchievementTextImportResult customAchievements,
            IReadOnlyDictionary<string, string> extractedImages,
            IReadOnlyList<GameCustomDataImageOnlyEntry> imageOnlyEntries)
        {
            Shape = shape;
            Manifest = manifest;
            CustomAchievements = customAchievements;
            ExtractedImages = extractedImages ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ImageOnlyEntries = imageOnlyEntries ?? Array.Empty<GameCustomDataImageOnlyEntry>();
        }

        /// <summary>Which package form was read.</summary>
        public GameCustomDataPackageShape Shape { get; }

        /// <summary>
        /// For <see cref="GameCustomDataPackageShape.Manifest"/>, the normalized manifest with
        /// personal state stripped and image paths pointing at the extracted files; null otherwise.
        /// Its PlayniteGameId is empty and its CustomProviderId is the package's, unresolved.
        /// </summary>
        public GameCustomDataPortableFile Manifest { get; }

        /// <summary>
        /// For <see cref="GameCustomDataPackageShape.CustomAchievementsCsv"/>, the CSV parse with
        /// icon paths pointing at the extracted files; null otherwise. It can carry errors.
        /// </summary>
        public CustomAchievementTextImportResult CustomAchievements { get; }

        /// <summary>Every package entry that was extracted, keyed by normalized entry name, to its absolute path.</summary>
        public IReadOnlyDictionary<string, string> ExtractedImages { get; }

        /// <summary>For <see cref="GameCustomDataPackageShape.ImageOnly"/>, the icons the package carries; empty otherwise.</summary>
        public IReadOnlyList<GameCustomDataImageOnlyEntry> ImageOnlyEntries { get; }
    }
}
