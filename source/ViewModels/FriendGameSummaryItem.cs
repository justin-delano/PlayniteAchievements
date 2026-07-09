using PlayniteAchievements.Services;
using System;

namespace PlayniteAchievements.ViewModels
{
    public sealed class FriendGameSummaryItem : GameSummaryItem
    {
        private int _friendCount;
        private int _friendsWithUnlocksCount;
        private int _friendUnlockedAchievementsCount;
        private int _uniqueFriendUnlockedAchievementsCount;
        private long _totalFriendPlaytimeMinutes;
        private long _averageFriendPlaytimeMinutes;
        private DateTime? _lastFriendPlayedUtc;
        private DateTime? _lastFriendScrapedUtc;
        private string _lastFriendScrapeStatus;

        public int FriendCount
        {
            get => _friendCount;
            set
            {
                if (SetValueAndReturn(ref _friendCount, value))
                {
                    OnPropertyChanged(nameof(FriendCompletionPercent));
                    OnPropertyChanged(nameof(FriendCompletionText));
                }
            }
        }

        public int FriendsWithUnlocksCount
        {
            get => _friendsWithUnlocksCount;
            set => SetValue(ref _friendsWithUnlocksCount, value);
        }

        public int UnlockedAchievementsCount
        {
            get => FriendUnlockedAchievementsCount;
            set => FriendUnlockedAchievementsCount = value;
        }

        public int FriendUnlockedAchievementsCount
        {
            get => _friendUnlockedAchievementsCount;
            set => SetValue(ref _friendUnlockedAchievementsCount, value);
        }

        public int UniqueFriendUnlockedAchievementsCount
        {
            get => _uniqueFriendUnlockedAchievementsCount;
            set
            {
                if (SetValueAndReturn(ref _uniqueFriendUnlockedAchievementsCount, value))
                {
                    UnlockedAchievements = Math.Max(0, value);
                    OnPropertyChanged(nameof(FriendCompletionPercent));
                    OnPropertyChanged(nameof(FriendCompletionText));
                }
            }
        }

        public int FriendCompletionPercent => TotalAchievements > 0
            ? (int)Math.Round(Math.Max(0, UniqueFriendUnlockedAchievementsCount) * 100d / TotalAchievements)
            : 0;

        public string FriendCompletionText => TotalAchievements > 0
            ? $"{FriendCompletionPercent:N0}%"
            : string.Empty;

        public DateTime? LastFriendUnlockUtc
        {
            get => LastUnlockUtc;
            set
            {
                LastUnlockUtc = value;
                OnPropertyChanged(nameof(LastFriendUnlockLocal));
            }
        }

        public DateTime? LastFriendUnlockLocal => LastUnlockUtc?.ToLocalTime();

        public long TotalFriendPlaytimeMinutes
        {
            get => _totalFriendPlaytimeMinutes;
            set
            {
                if (SetValueAndReturn(ref _totalFriendPlaytimeMinutes, Math.Max(0, value)))
                {
                    OnPropertyChanged(nameof(TotalFriendPlaytimeSeconds));
                    OnPropertyChanged(nameof(TotalFriendPlaytimeText));
                }
            }
        }

        public ulong TotalFriendPlaytimeSeconds => (ulong)Math.Max(0, TotalFriendPlaytimeMinutes) * 60UL;

        public string TotalFriendPlaytimeText =>
            PlayniteGameMetadataFormatter.FormatPlaytime(TotalFriendPlaytimeSeconds);

        public long AverageFriendPlaytimeMinutes
        {
            get => _averageFriendPlaytimeMinutes;
            set
            {
                if (SetValueAndReturn(ref _averageFriendPlaytimeMinutes, Math.Max(0, value)))
                {
                    OnPropertyChanged(nameof(AverageFriendPlaytimeSeconds));
                    OnPropertyChanged(nameof(AverageFriendPlaytimeText));
                }
            }
        }

        public ulong AverageFriendPlaytimeSeconds => (ulong)Math.Max(0, AverageFriendPlaytimeMinutes) * 60UL;

        public string AverageFriendPlaytimeText =>
            PlayniteGameMetadataFormatter.FormatPlaytime(AverageFriendPlaytimeSeconds);

        public DateTime? LastFriendPlayedUtc
        {
            get => _lastFriendPlayedUtc;
            set
            {
                if (SetValueAndReturn(ref _lastFriendPlayedUtc, value))
                {
                    OnPropertyChanged(nameof(LastFriendPlayedLocal));
                }
            }
        }

        public DateTime? LastFriendPlayedLocal => LastFriendPlayedUtc?.ToLocalTime();

        public DateTime? LastFriendScrapedUtc
        {
            get => _lastFriendScrapedUtc;
            set
            {
                if (SetValueAndReturn(ref _lastFriendScrapedUtc, value))
                {
                    OnPropertyChanged(nameof(LastFriendScrapedLocal));
                }
            }
        }

        public DateTime? LastFriendScrapedLocal => LastFriendScrapedUtc?.ToLocalTime();

        public string LastFriendScrapeStatus
        {
            get => _lastFriendScrapeStatus;
            set
            {
                if (SetValueAndReturn(ref _lastFriendScrapeStatus, value))
                {
                    OnPropertyChanged(nameof(LastFriendScrapeStatusDisplay));
                }
            }
        }

        public string LastFriendScrapeStatusDisplay => string.IsNullOrWhiteSpace(LastFriendScrapeStatus)
            ? string.Empty
            : LastFriendScrapeStatus;
    }
}
