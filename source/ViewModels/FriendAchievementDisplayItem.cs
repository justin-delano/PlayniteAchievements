using System;
using PlayniteAchievements.Common;

namespace PlayniteAchievements.ViewModels
{
    public sealed class FriendAchievementDisplayItem : AchievementDisplayItem
    {
        public int AppId { get; set; }

        public DateTime? FriendUnlockTimeUtc
        {
            get => UnlockTimeUtc;
            set => UnlockTimeUtc = value;
        }

        public DateTime? FriendUnlockTimeLocal => DateTimeUtilities.AsLocalFromUtc(FriendUnlockTimeUtc);

        public override bool ShowUnlockDate => FriendUnlockTimeUtc.HasValue;

        public override bool ShowLockedProgress => !ShowUnlockDate;
    }
}
