using System;
using System.Collections.Generic;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Services.Achievements;
using PlayniteAchievements.Services.GameCustomData;
using PlayniteAchievements.Services.Images;

namespace PlayniteAchievements.Services.Hydration
{
    /// <summary>
    /// The achievement-level steps of hydration, in the order <c>GameDataHydrator</c> runs
    /// them: custom achievement projection, the per-achievement overlays, then the icon overrides.
    /// Takes the resolved record and icon override maps as arguments rather than reading the
    /// store, so a caller holding data that is not stored for the game (a package preview) runs
    /// the same steps.
    /// </summary>
    internal static class AchievementOverlayPipeline
    {
        /// <summary>
        /// Projects the custom achievements of <paramref name="customData"/> onto
        /// <paramref name="data"/>, applies the overlays, and stamps the icon overrides. The icon
        /// override maps are read only when the game has achievements.
        /// </summary>
        internal static void Apply(
            GameAchievementData data,
            Guid gameId,
            ResolvedGameCustomData customData,
            ManagedCustomIconService managedCustomIconService,
            Func<IReadOnlyDictionary<string, string>> unlockedIconOverrides,
            Func<IReadOnlyDictionary<string, string>> lockedIconOverrides)
        {
            if (data == null)
            {
                return;
            }

            AppendCustomAchievements(data, gameId, customData, managedCustomIconService);
            if (data.Achievements == null || data.Achievements.Count == 0)
            {
                return;
            }

            AchievementDetailHydrator.ApplyOverlays(data.Achievements, data.EffectiveProviderKey, customData);
            AchievementIconOverrideHelper.ApplyOverrides(
                gameId,
                data.Achievements,
                unlockedIconOverrides?.Invoke(),
                lockedIconOverrides?.Invoke(),
                managedCustomIconService,
                achievement => achievement.ApiName,
                (achievement, path) => achievement.UnlockedIconPath = path,
                (achievement, path) => achievement.LockedIconPath = path);
        }

        /// <summary>
        /// Replaces any custom rows on <paramref name="data"/> with fresh projections of the
        /// custom achievements in <paramref name="customData"/>, with no overlays applied.
        /// </summary>
        internal static void AppendCustomAchievements(
            GameAchievementData data,
            Guid gameId,
            ResolvedGameCustomData customData,
            ManagedCustomIconService managedCustomIconService)
        {
            if (data == null)
            {
                return;
            }

            data.Achievements ??= new List<AchievementDetail>();
            for (var i = data.Achievements.Count - 1; i >= 0; i--)
            {
                var achievement = data.Achievements[i];
                if (achievement?.IsCustom == true ||
                    CustomAchievementProjectionService.IsCustomApiName(achievement?.ApiName))
                {
                    data.Achievements.RemoveAt(i);
                }
            }

            var definitions = customData?.CustomAchievements;
            if (definitions == null || definitions.Count == 0)
            {
                return;
            }

            var projected = CustomAchievementProjectionService.ProjectAchievements(
                gameId,
                definitions,
                managedCustomIconService);
            if (projected.Count == 0)
            {
                return;
            }

            data.HasAchievements = true;
            data.Achievements.AddRange(projected);
        }
    }
}
