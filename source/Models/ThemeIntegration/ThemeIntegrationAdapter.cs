using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PlayniteAchievements.Models.ThemeIntegration
{
    using Achievements;

    /// <summary>
    /// Adapts GameAchievementData to theme-exposed properties.
    /// This is the core adapter that converts internal data to theme-accessible format.
    /// </summary>
    public class ThemeIntegrationAdapter
    {
        private readonly PlayniteAchievementsSettings _settings;

        public ThemeIntegrationAdapter(PlayniteAchievementsSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>
        /// Build an immutable snapshot from cached data. Intended to be executed off the UI thread.
        /// </summary>
        public static ThemeIntegrationSnapshot BuildSnapshot(
            Guid gameId,
            GameAchievementData data,
            double ultraRareThreshold,
            double rareThreshold,
            double uncommonThreshold)
        {
            if (data == null || data.NoAchievements)
            {
                return null;
            }

            var achievements = data.Achievements ?? new List<AchievementDetail>();

            // Calculate basic counts (avoid LINQ to reduce allocations).
            int total = achievements.Count;
            int unlocked = 0;
            for (int i = 0; i < achievements.Count; i++)
            {
                if (achievements[i]?.Unlocked == true)
                {
                    unlocked++;
                }
            }

            int locked = total - unlocked;
            double percent = total > 0 ? Math.Round(unlocked * 100.0 / total, 2) : 0;
            bool is100Percent = unlocked == total && total > 0;

            // Make exactly one copy for theme bindings.
            // This avoids exposing provider-owned lists while also avoiding duplicate copies.
            var all = achievements.ToList();

            // SuccessStory compatibility expects these lists to contain *all* achievements:
            // - Ascending: locked (null date) first, then unlocked by date.
            // - Descending: unlocked newest first, locked (null date) last.
            // Themes (e.g. Aniki ReMake) toggle visibility based on unlock state.
            var asc = all
                .OrderBy(a => a?.UnlockTimeUtc)
                .ThenBy(a => a?.DisplayName)
                .ToList();

            var desc = all
                .OrderByDescending(a => a?.UnlockTimeUtc)
                .ThenBy(a => a?.DisplayName)
                .ToList();

            // Rarity-sorted lists (ascending = rarest first, descending = common first)
            var rarityAsc = all
                .OrderBy(a => a?.GlobalPercentUnlocked ?? 100)
                .ThenBy(a => a?.DisplayName)
                .ToList();

            var rarityDesc = all
                .OrderByDescending(a => a?.GlobalPercentUnlocked ?? 100)
                .ThenBy(a => a?.DisplayName)
                .ToList();

            // Calculate rarity stats in a single pass.
            // NOTE: We intentionally match previous boundary behavior:
            // - percent == threshold belongs to the lower bucket.
            // - percent <= 0 is excluded from buckets (minPercent is exclusive).
            var common = new AchievementRarityStats();
            var uncommon = new AchievementRarityStats();
            var rare = new AchievementRarityStats();
            var ultra = new AchievementRarityStats();

            for (int i = 0; i < all.Count; i++)
            {
                var a = all[i];
                if (a == null)
                {
                    continue;
                }

                var p = a.GlobalPercentUnlocked ?? 100;
                if (p <= 0)
                {
                    continue;
                }

                var target = p > uncommonThreshold
                    ? common
                    : (p > rareThreshold ? uncommon : (p > ultraRareThreshold ? rare : ultra));

                target.Total++;
                if (a.Unlocked)
                {
                    target.Unlocked++;
                }
                else
                {
                    target.Locked++;
                }
            }

            return new ThemeIntegrationSnapshot(
                gameId,
                data.LastUpdatedUtc,
                total,
                unlocked,
                locked,
                percent,
                is100Percent,
                all,
                asc,
                desc,
                rarityAsc,
                rarityDesc,
                common,
                uncommon,
                rare,
                ultra);
        }

        /// <summary>
        /// Apply a snapshot to theme-exposed settings properties. Intended to be executed on the UI thread.
        /// </summary>
        public void ApplySnapshot(ThemeIntegrationSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Total <= 0)
            {
                ClearThemeProperties();
                return;
            }

            // Update existing basic properties (for backwards compatibility)
            _settings.HasData = true;
            _settings.Total = snapshot.Total;
            _settings.Unlocked = snapshot.Unlocked;
            _settings.Percent = snapshot.Percent;

            // --SUCCESSSTORY--
            // Always populate SuccessStory-compatible properties so themes can bind to either
            // surface (native or SuccessStory naming) without needing a separate helper plugin.
            _settings.SuccessStoryTheme.Is100Percent = snapshot.Is100Percent;
            _settings.SuccessStoryTheme.Locked = snapshot.Locked;
            _settings.SuccessStoryTheme.Unlocked = snapshot.Unlocked;
            _settings.SuccessStoryTheme.TotalGamerScore = 0;
            _settings.SuccessStoryTheme.EstimateTimeToUnlock = string.Empty;

            _settings.SuccessStoryTheme.Common = snapshot.Common;
            _settings.SuccessStoryTheme.NoCommon = snapshot.Uncommon;
            _settings.SuccessStoryTheme.Rare = snapshot.Rare;
            _settings.SuccessStoryTheme.UltraRare = snapshot.UltraRare;

            _settings.SuccessStoryTheme.ListAchievements = snapshot.AllAchievements;
            _settings.SuccessStoryTheme.ListAchUnlockDateAsc = snapshot.UnlockDateAsc;
            _settings.SuccessStoryTheme.ListAchUnlockDateDesc = snapshot.UnlockDateDesc;
            // --END SUCCESSSTORY--

            // Native PlayniteAchievements properties (always exposed)
            _settings.NativeTheme.HasAchievements = true;
            _settings.NativeTheme.AllUnlocked = snapshot.Unlocked == snapshot.Total && snapshot.Total > 0;
            _settings.NativeTheme.AchievementCount = snapshot.Total;
            _settings.NativeTheme.UnlockedCount = snapshot.Unlocked;
            _settings.NativeTheme.LockedCount = snapshot.Locked;
            _settings.NativeTheme.ProgressPercentage = snapshot.Percent;

            // Rarity stats
            _settings.NativeTheme.CommonStats = snapshot.Common;
            _settings.NativeTheme.UncommonStats = snapshot.Uncommon;
            _settings.NativeTheme.RareStats = snapshot.Rare;
            _settings.NativeTheme.UltraRareStats = snapshot.UltraRare;

            // Achievement lists for theme binding
            _settings.NativeTheme.AllAchievements = snapshot.AllAchievements;
            _settings.NativeTheme.AchievementsNewestFirst = snapshot.UnlockDateDesc;
            _settings.NativeTheme.AchievementsOldestFirst = snapshot.UnlockDateAsc;

            // Fullscreen single-game achievement lists
            _settings.FullscreenSingleGameUnlockAsc = snapshot.UnlockDateAsc;
            _settings.FullscreenSingleGameUnlockDesc = snapshot.UnlockDateDesc;
            _settings.FullscreenSingleGameRarityAsc = snapshot.RarityAsc;
            _settings.FullscreenSingleGameRarityDesc = snapshot.RarityDesc;
        }

        /// <summary>
        /// Legacy synchronous entrypoint used by older call sites.
        /// Prefer ThemeIntegrationUpdateService which builds snapshots off the UI thread.
        /// </summary>
        public void UpdateThemeProperties(Game game, GameAchievementData data)
        {
            if (data == null || data.NoAchievements)
            {
                ClearThemeProperties();
                return;
            }

            var snapshot = BuildSnapshot(
                game?.Id ?? Guid.Empty,
                data,
                _settings.Persisted.UltraRareThreshold,
                _settings.Persisted.RareThreshold,
                _settings.Persisted.UncommonThreshold);

            ApplySnapshot(snapshot);
        }

        /// <summary>
        /// Clear all theme properties when no game is selected or game has no achievements.
        /// </summary>
        public void ClearThemeProperties()
        {
            // Clear existing basic properties
            _settings.HasData = false;
            _settings.Total = 0;
            _settings.Unlocked = 0;
            _settings.Percent = 0;

            // Clear native properties
            _settings.NativeTheme.HasAchievements = false;
            _settings.NativeTheme.AllUnlocked = false;
            _settings.NativeTheme.AchievementCount = 0;
            _settings.NativeTheme.UnlockedCount = 0;
            _settings.NativeTheme.LockedCount = 0;
            _settings.NativeTheme.ProgressPercentage = 0;

            // Clear native collections
            _settings.NativeTheme.AllAchievements = new List<AchievementDetail>();
            _settings.NativeTheme.AchievementsNewestFirst = new List<AchievementDetail>();
            _settings.NativeTheme.AchievementsOldestFirst = new List<AchievementDetail>();

            // Clear native rarity stats
            _settings.NativeTheme.CommonStats = new AchievementRarityStats();
            _settings.NativeTheme.UncommonStats = new AchievementRarityStats();
            _settings.NativeTheme.RareStats = new AchievementRarityStats();
            _settings.NativeTheme.UltraRareStats = new AchievementRarityStats();

            // --SUCCESSSTORY--
            _settings.SuccessStoryTheme.Is100Percent = false;
            _settings.SuccessStoryTheme.Locked = 0;
            _settings.SuccessStoryTheme.Unlocked = 0;
            _settings.SuccessStoryTheme.TotalGamerScore = 0;
            _settings.SuccessStoryTheme.EstimateTimeToUnlock = string.Empty;
            _settings.SuccessStoryTheme.ListAchievements = new List<AchievementDetail>();
            _settings.SuccessStoryTheme.ListAchUnlockDateAsc = new List<AchievementDetail>();
            _settings.SuccessStoryTheme.ListAchUnlockDateDesc = new List<AchievementDetail>();
            _settings.SuccessStoryTheme.Common = new AchievementRarityStats();
            _settings.SuccessStoryTheme.NoCommon = new AchievementRarityStats();
            _settings.SuccessStoryTheme.Rare = new AchievementRarityStats();
            _settings.SuccessStoryTheme.UltraRare = new AchievementRarityStats();
            // --END SUCCESSSTORY--

            // Clear fullscreen single-game properties
            _settings.FullscreenSingleGameUnlockAsc = null;
            _settings.FullscreenSingleGameUnlockDesc = null;
            _settings.FullscreenSingleGameRarityAsc = null;
            _settings.FullscreenSingleGameRarityDesc = null;
        }
    }
}
