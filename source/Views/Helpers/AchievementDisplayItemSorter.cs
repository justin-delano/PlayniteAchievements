using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using PlayniteAchievements.ViewModels;

namespace PlayniteAchievements.Views.Helpers
{
    /// <summary>
    /// Provides sorting functionality for AchievementDisplayItem collections.
    /// Used by DataGrid controls to ensure consistent sorting behavior.
    /// </summary>
    public static class AchievementDisplayItemSorter
    {
        /// <summary>
        /// Sorts a list of achievement items in place.
        /// </summary>
        /// <param name="items">The list to sort.</param>
        /// <param name="sortMemberPath">The SortMemberPath from the DataGrid column.</param>
        /// <param name="direction">The sort direction.</param>
        /// <param name="currentSortPath">Reference to the current sort path tracker (for quick reverse optimization).</param>
        /// <param name="currentSortDirection">Reference to the current sort direction tracker.</param>
        public static void SortItems(
            List<AchievementDisplayItem> items,
            string sortMemberPath,
            ListSortDirection direction,
            ref string currentSortPath,
            ref ListSortDirection? currentSortDirection)
        {
            if (items == null || items.Count == 0 || string.IsNullOrWhiteSpace(sortMemberPath))
            {
                return;
            }

            // Quick reverse if same column and switching from Ascending to Descending
            if (currentSortPath == sortMemberPath &&
                currentSortDirection == ListSortDirection.Ascending &&
                direction == ListSortDirection.Descending)
            {
                items.Reverse();
                currentSortDirection = direction;
                return;
            }

            currentSortPath = sortMemberPath;
            currentSortDirection = direction;

            var comparison = GetComparison(sortMemberPath);
            if (comparison == null)
            {
                return;
            }

            items.Sort((a, b) =>
            {
                var result = comparison(a, b);
                return direction == ListSortDirection.Ascending ? result : -result;
            });
        }

        /// <summary>
        /// Creates a new sorted list from achievement items.
        /// </summary>
        /// <param name="items">The source items.</param>
        /// <param name="sortMemberPath">The SortMemberPath from the DataGrid column.</param>
        /// <param name="direction">The sort direction.</param>
        /// <returns>A new sorted list.</returns>
        public static List<AchievementDisplayItem> CreateSortedList(
            IEnumerable<AchievementDisplayItem> items,
            string sortMemberPath,
            ListSortDirection direction)
        {
            if (items == null)
            {
                return new List<AchievementDisplayItem>();
            }

            var list = items.ToList();
            if (list.Count == 0 || string.IsNullOrWhiteSpace(sortMemberPath))
            {
                return list;
            }

            var comparison = GetComparison(sortMemberPath);
            if (comparison == null)
            {
                return list;
            }

            list.Sort((a, b) =>
            {
                var result = comparison(a, b);
                return direction == ListSortDirection.Ascending ? result : -result;
            });

            return list;
        }

        /// <summary>
        /// Gets the sort value for an achievement item by sort path.
        /// Used for simple property-based sorting.
        /// </summary>
        public static object GetSortValue(AchievementDisplayItem item, string sortPath)
        {
            if (item == null || string.IsNullOrWhiteSpace(sortPath))
            {
                return null;
            }

            return sortPath switch
            {
                "DisplayName" => item.DisplayName ?? string.Empty,
                "UnlockTime" => item.UnlockTime,
                "GlobalPercent" => item.GlobalPercent,
                "CategoryType" => item.CategoryType ?? string.Empty,
                "CategoryLabel" => item.CategoryLabel ?? string.Empty,
                "TrophyType" => item.TrophyType ?? string.Empty,
                "Points" => item.Points,
                _ => null
            };
        }

        private static Comparison<AchievementDisplayItem> GetComparison(string sortMemberPath)
        {
            return sortMemberPath switch
            {
                "DisplayName" => (a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase),
                "UnlockTime" => CompareByUnlockTime,
                "GlobalPercent" => CompareByGlobalPercent,
                "CategoryType" => CompareByCategoryType,
                "CategoryLabel" => CompareByCategoryLabel,
                "TrophyType" => CompareByTrophyType,
                "Points" => (a, b) => a.Points.CompareTo(b.Points),
                _ => null
            };
        }

        private static int CompareByUnlockTime(AchievementDisplayItem a, AchievementDisplayItem b)
        {
            // Unlocked items come first, then by date (newest first for unlocked, oldest first for locked)
            var aUnlocked = a.Unlocked;
            var bUnlocked = b.Unlocked;

            if (aUnlocked && !bUnlocked) return -1;
            if (!aUnlocked && bUnlocked) return 1;

            var aTime = a.UnlockTime;
            var bTime = b.UnlockTime;

            // Both unlocked: newest first
            // Both locked: oldest first
            return aUnlocked
                ? bTime.CompareTo(aTime)
                : aTime.CompareTo(bTime);
        }

        private static int CompareByGlobalPercent(AchievementDisplayItem a, AchievementDisplayItem b)
        {
            // Lower percentage = rarer = should come first when ascending
            return a.GlobalPercent.CompareTo(b.GlobalPercent);
        }

        private static int CompareByCategoryType(AchievementDisplayItem a, AchievementDisplayItem b)
        {
            var result = string.Compare(a.CategoryType, b.CategoryType, StringComparison.OrdinalIgnoreCase);
            if (result != 0) return result;

            // Secondary sort by unlock status (unlocked first)
            return CompareByUnlockStatus(a, b);
        }

        private static int CompareByCategoryLabel(AchievementDisplayItem a, AchievementDisplayItem b)
        {
            var result = string.Compare(a.CategoryLabel, b.CategoryLabel, StringComparison.OrdinalIgnoreCase);
            if (result != 0) return result;

            // Secondary sort by unlock status (unlocked first)
            return CompareByUnlockStatus(a, b);
        }

        private static int CompareByTrophyType(AchievementDisplayItem a, AchievementDisplayItem b)
        {
            var aRank = GetTrophyRank(a.TrophyType);
            var bRank = GetTrophyRank(b.TrophyType);
            return aRank.CompareTo(bRank);
        }

        private static int CompareByUnlockStatus(AchievementDisplayItem a, AchievementDisplayItem b)
        {
            if (a.Unlocked && !b.Unlocked) return -1;
            if (!a.Unlocked && b.Unlocked) return 1;
            return 0;
        }

        private static int GetTrophyRank(string trophyType)
        {
            return trophyType?.ToUpperInvariant() switch
            {
                "PLATINUM" => 0,
                "GOLD" => 1,
                "SILVER" => 2,
                "BRONZE" => 3,
                _ => 4
            };
        }
    }
}
