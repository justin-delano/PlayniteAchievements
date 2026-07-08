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
