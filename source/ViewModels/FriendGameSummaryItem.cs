using PlayniteAchievements.Common;
using System;

namespace PlayniteAchievements.ViewModels
{
    public sealed class FriendGameSummaryItem : ObservableObject
    {
        private string _gameName;
        private int _friendCount;
        private int _unlockedAchievementsCount;
        private DateTime? _lastUnlockUtc;

        public string ProviderKey { get; set; }
        public int AppId { get; set; }
        public Guid? PlayniteGameId { get; set; }

        public string GameName
        {
            get => _gameName;
            set => SetValue(ref _gameName, value);
        }

        public int FriendCount
        {
            get => _friendCount;
            set => SetValue(ref _friendCount, value);
        }

        public int UnlockedAchievementsCount
        {
            get => _unlockedAchievementsCount;
            set => SetValue(ref _unlockedAchievementsCount, value);
        }

        public DateTime? LastUnlockUtc
        {
            get => _lastUnlockUtc;
            set
            {
                if (SetValueAndReturn(ref _lastUnlockUtc, value))
                {
                    OnPropertyChanged(nameof(LastUnlockLocal));
                }
            }
        }

        public DateTime? LastUnlockLocal => LastUnlockUtc?.ToLocalTime();
    }
}
