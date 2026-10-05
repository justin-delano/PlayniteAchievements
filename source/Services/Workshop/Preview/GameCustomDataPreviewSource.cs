using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Images;
using System;

namespace PlayniteAchievements.Services.Workshop.Preview
{
    /// <summary>
    /// The library game a game data package is compared against: its achievements before and
    /// without overlays, its stored custom data, and the baseline the last Workshop install of the
    /// same item left behind.
    /// </summary>
    public sealed class GameCustomDataPreviewSource
    {
        /// <summary>The library game's id.</summary>
        public Guid GameId { get; set; }

        /// <summary>The library game's name, shown as what the preview compares against.</summary>
        public string GameName { get; set; }

        /// <summary>
        /// The game's cached achievements with no overlays applied, or null for a game with only
        /// custom achievements. Never modified: the preview hydrates a copy.
        /// </summary>
        public GameAchievementData RawData { get; set; }

        /// <summary>The game's achievements as they show now, with every overlay applied.</summary>
        public GameAchievementData CurrentData { get; set; }

        /// <summary>The game's stored custom data, or null when it has none.</summary>
        public GameCustomDataFile Current { get; set; }

        /// <summary>The data the previous install of this item left behind, or null for a first install.</summary>
        public GameCustomDataFile Baseline { get; set; }

        /// <summary>The settings the hydration falls back to for global options such as separate locked icons.</summary>
        public PersistedSettings Persisted { get; set; }

        /// <summary>Resolves managed icon paths of the game's current data for display; may be null.</summary>
        public ManagedCustomIconService ManagedCustomIconService { get; set; }

        /// <summary>
        /// True for an image that is published: every after-install row is built unlocked with no
        /// unlock time or progress, so no row's icon, masking or state follows the sharer's own
        /// progress. The before rows are left as they are.
        /// </summary>
        public bool HidePersonalProgress { get; set; }
    }
}
