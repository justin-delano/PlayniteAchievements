namespace PlayniteAchievements.Models.Achievements.Scoring
{
    /// <summary>
    /// Running platform score sums: unlocked provider points per platform, keyed by the game's
    /// effective provider key (see <see cref="ScoreCardTypes.TryGetPlatformScore"/>).
    /// </summary>
    public sealed class PlatformScoreTotals
    {
        public int Gamerscore { get; private set; }

        public int EpicXp { get; private set; }

        public int RetroPoints { get; private set; }

        /// <summary>Adds a game's platform score points to the total its platform counts toward, if any.</summary>
        public void Add(string effectiveProviderKey, int platformScorePoints)
        {
            if (platformScorePoints <= 0 ||
                !ScoreCardTypes.TryGetPlatformScore(effectiveProviderKey, out var type))
            {
                return;
            }

            switch (type)
            {
                case ScoreCardType.Gamerscore:
                    Gamerscore = AddClamped(Gamerscore, platformScorePoints);
                    break;
                case ScoreCardType.EpicXp:
                    EpicXp = AddClamped(EpicXp, platformScorePoints);
                    break;
                case ScoreCardType.RetroPoints:
                    RetroPoints = AddClamped(RetroPoints, platformScorePoints);
                    break;
            }
        }

        /// <summary>The platform total for a platform card type; 0 for Collection and Prestige.</summary>
        public int Get(ScoreCardType type)
        {
            switch (type)
            {
                case ScoreCardType.Gamerscore:
                    return Gamerscore;
                case ScoreCardType.EpicXp:
                    return EpicXp;
                case ScoreCardType.RetroPoints:
                    return RetroPoints;
                default:
                    return 0;
            }
        }

        private static int AddClamped(int current, int value)
        {
            return current > int.MaxValue - value ? int.MaxValue : current + value;
        }
    }
}
