using PlayniteAchievements.Models.Settings;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PlayniteAchievements.Services.Workshop
{
    /// <summary>
    /// Decides whether installed Workshop game data is still on its game, by comparing the
    /// game's current custom data with the baseline the install recorded. Only the package's
    /// achievement-level changes count: text and icon overrides, category assignments, notes,
    /// the manual capstone and authored achievements. Game-level leftovers such as category
    /// order or category images do not keep an item installed once those are gone, so a reset
    /// in the editor makes the item installable again.
    /// </summary>
    public static class WorkshopGameDataPresence
    {
        /// <summary>
        /// True when <paramref name="current"/> still carries any achievement-level change the
        /// install wrote (<paramref name="baseline"/>). A null baseline (an install recorded
        /// before baselines existed) or a baseline with no achievement-level changes falls back
        /// to whether the game has any custom data at all.
        /// </summary>
        public static bool IsPresent(GameCustomDataFile baseline, GameCustomDataFile current, bool currentHasAnyData)
        {
            if (current == null)
            {
                return false;
            }

            if (baseline == null || !HasAchievementLevelData(baseline))
            {
                return currentHasAnyData;
            }

            return SharesKeys(baseline.AchievementOverrides, current.AchievementOverrides)
                   || SharesKeys(baseline.AchievementUnlockedIconOverrides, current.AchievementUnlockedIconOverrides)
                   || SharesKeys(baseline.AchievementLockedIconOverrides, current.AchievementLockedIconOverrides)
                   || SharesKeys(baseline.AchievementCategoryOverrides, current.AchievementCategoryOverrides)
                   || SharesKeys(baseline.AchievementNotes, current.AchievementNotes)
                   || SharesCustomAchievements(baseline.CustomAchievements, current.CustomAchievements)
                   || (!string.IsNullOrWhiteSpace(baseline.ManualCapstoneApiName)
                       && string.Equals(baseline.ManualCapstoneApiName, current.ManualCapstoneApiName, StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasAchievementLevelData(GameCustomDataFile data)
        {
            return (data.AchievementOverrides?.Count ?? 0) > 0
                   || (data.AchievementUnlockedIconOverrides?.Count ?? 0) > 0
                   || (data.AchievementLockedIconOverrides?.Count ?? 0) > 0
                   || (data.AchievementCategoryOverrides?.Count ?? 0) > 0
                   || (data.AchievementNotes?.Count ?? 0) > 0
                   || (data.CustomAchievements?.Count ?? 0) > 0
                   || !string.IsNullOrWhiteSpace(data.ManualCapstoneApiName);
        }

        private static bool SharesKeys<TValue>(IDictionary<string, TValue> baseline, IDictionary<string, TValue> current)
        {
            if (baseline == null || current == null || baseline.Count == 0 || current.Count == 0)
            {
                return false;
            }

            var currentKeys = new HashSet<string>(current.Keys, StringComparer.OrdinalIgnoreCase);
            return baseline.Keys.Any(currentKeys.Contains);
        }

        private static bool SharesCustomAchievements(
            IReadOnlyCollection<CustomAchievementDefinition> baseline,
            IReadOnlyCollection<CustomAchievementDefinition> current)
        {
            if (baseline == null || current == null || baseline.Count == 0 || current.Count == 0)
            {
                return false;
            }

            var currentIds = new HashSet<string>(
                current.Select(definition => definition?.Id).Where(id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.OrdinalIgnoreCase);
            return baseline.Any(definition => !string.IsNullOrWhiteSpace(definition?.Id) && currentIds.Contains(definition.Id));
        }
    }
}
