using PlayniteAchievements.Common;
using System;

namespace PlayniteAchievements.ViewModels
{
    public sealed class FriendSummaryItem : ObservableObject
    {
        private string _displayName;
        private string _avatarUrl;
        private int _sharedGamesCount;
        private int _unlockedAchievementsCount;
        private DateTime? _lastUnlockUtc;

        public string ProviderKey { get; set; }
        public string ExternalUserId { get; set; }

        public string DisplayName
        {
            get => _displayName;
            set => SetValue(ref _displayName, value);
        }

        public string AvatarUrl
        {
            get => _avatarUrl;
            set => SetValue(ref _avatarUrl, value);
        }

        public int SharedGamesCount
        {
            get => _sharedGamesCount;
            set
            {
                if (SetValueAndReturn(ref _sharedGamesCount, value))
                {
                    OnPropertyChanged(nameof(CountsText));
                }
            }
        }

        public int UnlockedAchievementsCount
        {
            get => _unlockedAchievementsCount;
            set
            {
                if (SetValueAndReturn(ref _unlockedAchievementsCount, value))
                {
                    OnPropertyChanged(nameof(CountsText));
                }
            }
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

        public string CountsText => $"{UnlockedAchievementsCount:N0} / {SharedGamesCount:N0}";
    }
}
