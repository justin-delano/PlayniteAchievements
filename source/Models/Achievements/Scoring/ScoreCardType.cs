using System;

namespace PlayniteAchievements.Models.Achievements.Scoring
{
    /// <summary>
    /// The scores a score card can show: the two rarity-weighted library scores and three platform
    /// scores summed from the provider's own points.
    /// </summary>
    public enum ScoreCardType
    {
        Collection,
        Prestige,
        Gamerscore,
        EpicXp,
        RetroPoints
    }

    /// <summary>A score card slot: a <see cref="ScoreCardType"/>, or no card.</summary>
    public enum ScoreCardSlot
    {
        None = 0,
        Collection = 1,
        Prestige = 2,
        Gamerscore = 3,
        EpicXp = 4,
        RetroPoints = 5
    }

    public static class ScoreCardTypes
    {
        public static bool IsPlatformScore(ScoreCardType type) =>
            type == ScoreCardType.Gamerscore ||
            type == ScoreCardType.EpicXp ||
            type == ScoreCardType.RetroPoints;

        /// <summary>The level curve each card type climbs.</summary>
        public static AchievementLevelCurveSettings GetCurve(ScoreCardType type)
        {
            switch (type)
            {
                case ScoreCardType.Gamerscore:
                    return AchievementLevelCurveSettings.Gamerscore;
                case ScoreCardType.EpicXp:
                    return AchievementLevelCurveSettings.EpicXp;
                case ScoreCardType.RetroPoints:
                    return AchievementLevelCurveSettings.RetroAchievementsPoints;
                default:
                    return AchievementLevelCurveSettings.ModernDefault;
            }
        }

        public static AchievementLevelSnapshot Calculate(ScoreCardType type, int score) =>
            AchievementLevelCalculator.Calculate(score, GetCurve(type));

        /// <summary>
        /// The platform score a game's points count toward, by its effective provider key (the
        /// platform key for aggregator-sourced games, so Exophase Xbox games count as Xbox).
        /// </summary>
        public static bool TryGetPlatformScore(string effectiveProviderKey, out ScoreCardType type)
        {
            var key = effectiveProviderKey?.Trim();
            if (string.Equals(key, "Xbox", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "Xenia", StringComparison.OrdinalIgnoreCase))
            {
                type = ScoreCardType.Gamerscore;
                return true;
            }

            if (string.Equals(key, "Epic", StringComparison.OrdinalIgnoreCase))
            {
                type = ScoreCardType.EpicXp;
                return true;
            }

            if (string.Equals(key, "RetroAchievements", StringComparison.OrdinalIgnoreCase))
            {
                type = ScoreCardType.RetroPoints;
                return true;
            }

            type = ScoreCardType.Collection;
            return false;
        }

        public static bool TryGetCardType(ScoreCardSlot slot, out ScoreCardType type)
        {
            switch (slot)
            {
                case ScoreCardSlot.Collection:
                    type = ScoreCardType.Collection;
                    return true;
                case ScoreCardSlot.Prestige:
                    type = ScoreCardType.Prestige;
                    return true;
                case ScoreCardSlot.Gamerscore:
                    type = ScoreCardType.Gamerscore;
                    return true;
                case ScoreCardSlot.EpicXp:
                    type = ScoreCardType.EpicXp;
                    return true;
                case ScoreCardSlot.RetroPoints:
                    type = ScoreCardType.RetroPoints;
                    return true;
                default:
                    type = ScoreCardType.Collection;
                    return false;
            }
        }

        public static ScoreCardSlot ToSlot(ScoreCardType type)
        {
            switch (type)
            {
                case ScoreCardType.Prestige:
                    return ScoreCardSlot.Prestige;
                case ScoreCardType.Gamerscore:
                    return ScoreCardSlot.Gamerscore;
                case ScoreCardType.EpicXp:
                    return ScoreCardSlot.EpicXp;
                case ScoreCardType.RetroPoints:
                    return ScoreCardSlot.RetroPoints;
                default:
                    return ScoreCardSlot.Collection;
            }
        }

        public static ScoreCardSlot Normalize(ScoreCardSlot slot) =>
            Enum.IsDefined(typeof(ScoreCardSlot), slot) ? slot : ScoreCardSlot.None;
    }
}
