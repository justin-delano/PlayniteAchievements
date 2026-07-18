using System;

namespace PlayniteAchievements.Models
{
    public sealed class AchievementUnlockedEventArgs : EventArgs
    {
        public Guid PlayniteGameId { get; set; }
        public string GameName { get; set; }
        public string ProviderKey { get; set; }
        public string ApiName { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public string IconPath { get; set; }
        public double? GlobalPercent { get; set; }
        public string RarityTier { get; set; }
        public string TrophyType { get; set; }
        public bool IsHardcore { get; set; }
        public int? Points { get; set; }
        public int? ScaledPoints { get; set; }
        public DateTime? UnlockTimeUtc { get; set; }
        public int UnlockedCount { get; set; }
        public int TotalCount { get; set; }
        public bool GameCompleted { get; set; }
        public bool IsFriendUnlock { get; set; }
        public string FriendExternalUserId { get; set; }
        public string FriendDisplayName { get; set; }
        public string FriendAvatarPath { get; set; }
        public string FriendAvatarUrl { get; set; }
        public bool IsCapstone { get; set; }

        /// <summary>
        /// True when this notification belongs to the game's completion moment: the unlock batch
        /// that pushed the game to complete (all unlocked, or the capstone unlocked), or the
        /// completion notification itself. Exposed to templates as IsCompleted.
        /// </summary>
        public bool CompletesGame { get; set; }

        /// <summary>
        /// True for the standalone "Congratulations! Game Complete!" notification emitted in its
        /// own wave after the completing unlock's toasts. Not an achievement unlock: it never
        /// produces recording clips, and of the screenshot variants only the framed one applies.
        /// </summary>
        public bool IsGameCompletionNotification { get; set; }

        /// <summary>
        /// 1-based position of this achievement within the game's provider/custom sort order.
        /// Used for stable, interpretable screenshot filenames. 0 when unknown (e.g. friends).
        /// </summary>
        public int AchievementNumber { get; set; }

        /// <summary>
        /// Set for example/test toasts fired from the settings preview. Bypasses the
        /// notification enablement gates in <see cref="Services.UI.ToastNotificationService"/> so
        /// the toast always shows on screen regardless of the user's enable toggles.
        /// </summary>
        public bool IsPreview { get; set; }
    }
}
