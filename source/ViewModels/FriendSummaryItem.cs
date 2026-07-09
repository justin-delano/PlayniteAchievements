using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Providers;
using PlayniteAchievements.Services;
using PlayniteAchievements.Services.Friends;
using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Windows.Input;
using ObservableObject = PlayniteAchievements.Common.ObservableObject;

namespace PlayniteAchievements.ViewModels
{
    public sealed class FriendSummaryItem : ObservableObject
    {
        [DontSerialize]
        [IgnoreDataMember]
        public ICommand SetDynamicFriendScopeProviderCommand { get; set; }

        [DontSerialize]
        [IgnoreDataMember]
        public ICommand SetDynamicFriendScopeUserCommand { get; set; }

        private string _displayName;
        private string _avatarPath;
        private int _sharedGamesCount;
        private int _gamesWithUnlocksCount;
        private int _unlockedAchievementsCount;
        private int _collectionScore;
        private int _prestigeScore;
        private int _collectionLevel;
        private int _prestigeLevel;
        private int _recentUnlockCount;
        private DateTime? _lastUnlockUtc;
        private DateTime? _lastRefreshedUtc;
        private long _totalPlaytimeMinutes;
        private string _providerKey;
        private string _externalUserId;
        private string _mergedFriendId;

        public string ProviderKey
        {
            get => _providerKey;
            set
            {
                if (SetValueAndReturn(ref _providerKey, value))
                {
                    OnPropertyChanged(nameof(FriendScopeKey));
                    OnPropertyChanged(nameof(Key));
                }
            }
        }

        public string ExternalUserId
        {
            get => _externalUserId;
            set
            {
                if (SetValueAndReturn(ref _externalUserId, value))
                {
                    OnPropertyChanged(nameof(FriendScopeKey));
                    OnPropertyChanged(nameof(Key));
                }
            }
        }

        public string MergedFriendId
        {
            get => _mergedFriendId;
            set
            {
                if (SetValueAndReturn(ref _mergedFriendId, value))
                {
                    OnPropertyChanged(nameof(IsMergedFriend));
                    OnPropertyChanged(nameof(FriendScopeKey));
                    OnPropertyChanged(nameof(Key));
                    OnPropertyChanged(nameof(ProviderDisplayName));
                    OnPropertyChanged(nameof(ProviderIconKey));
                    OnPropertyChanged(nameof(ProviderColorHex));
                }
            }
        }

        public List<FriendAccountRef> MemberAccounts { get; set; } = new List<FriendAccountRef>();

        public List<string> MemberProviderKeys { get; set; } = new List<string>();

        [DontSerialize]
        [IgnoreDataMember]
        public bool IsMergedFriend => !string.IsNullOrWhiteSpace(MergedFriendId);

        [DontSerialize]
        [IgnoreDataMember]
        public string FriendScopeKey => FriendOverviewProjection.GetFriendScopeKey(this);

        [DontSerialize]
        [IgnoreDataMember]
        public string Key => FriendScopeKey;

        [DontSerialize]
        [IgnoreDataMember]
        public string Label => DisplayName;

        [DontSerialize]
        [IgnoreDataMember]
        public int Count => UnlockedAchievementsCount;

        public string ProviderDisplayName
        {
            get
            {
                if (IsMergedFriend)
                {
                    return MemberProviderDisplayText ??
                           ResourceProvider.GetString("LOCPlayAch_FriendsSettings_Merged") ??
                           "Merged";
                }

                var localized = PlayniteAchievements.Providers.ProviderRegistry.GetLocalizedName(ProviderKey);
                return string.IsNullOrWhiteSpace(localized) ? ProviderKey : localized;
            }
        }

        public string MemberProviderDisplayText
        {
            get
            {
                var providers = (MemberProviderKeys ?? new List<string>())
                    .Where(provider => !string.IsNullOrWhiteSpace(provider))
                    .Select(provider =>
                    {
                        var localized = PlayniteAchievements.Providers.ProviderRegistry.GetLocalizedName(provider);
                        return string.IsNullOrWhiteSpace(localized) ? provider : localized;
                    })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (providers.Count == 0)
                {
                    return null;
                }

                return string.Join(" + ", providers);
            }
        }

        public string ProviderIconKey => IsMergedFriend || string.IsNullOrWhiteSpace(ProviderKey) ? null : "ProviderIcon" + ProviderKey;
        public string ProviderColorHex =>
            IsMergedFriend ? null : PlayniteAchievements.Providers.ProviderRegistry.GetProviderColorHex(ProviderKey);

        public string DisplayName
        {
            get => _displayName;
            set
            {
                if (SetValueAndReturn(ref _displayName, value))
                {
                    OnPropertyChanged(nameof(Label));
                }
            }
        }

        public string AvatarPath
        {
            get => _avatarPath;
            set => SetValue(ref _avatarPath, value);
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

        public int GamesWithUnlocksCount
        {
            get => _gamesWithUnlocksCount;
            set => SetValue(ref _gamesWithUnlocksCount, value);
        }

        public int UnlockedAchievementsCount
        {
            get => _unlockedAchievementsCount;
            set
            {
                if (SetValueAndReturn(ref _unlockedAchievementsCount, value))
                {
                    OnPropertyChanged(nameof(CountsText));
                    OnPropertyChanged(nameof(Count));
                }
            }
        }

        public int CollectionScore
        {
            get => _collectionScore;
            set => SetValue(ref _collectionScore, Math.Max(0, value));
        }

        public int PrestigeScore
        {
            get => _prestigeScore;
            set => SetValue(ref _prestigeScore, Math.Max(0, value));
        }

        public int CollectionLevel
        {
            get => _collectionLevel;
            set => SetValue(ref _collectionLevel, Math.Max(0, value));
        }

        public int PrestigeLevel
        {
            get => _prestigeLevel;
            set => SetValue(ref _prestigeLevel, Math.Max(0, value));
        }

        public int RecentUnlockCount
        {
            get => _recentUnlockCount;
            set => SetValue(ref _recentUnlockCount, value);
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

        public DateTime? LastRefreshedUtc
        {
            get => _lastRefreshedUtc;
            set
            {
                if (SetValueAndReturn(ref _lastRefreshedUtc, value))
                {
                    OnPropertyChanged(nameof(LastRefreshedLocal));
                }
            }
        }

        public DateTime? LastRefreshedLocal => LastRefreshedUtc?.ToLocalTime();

        public long TotalPlaytimeMinutes
        {
            get => _totalPlaytimeMinutes;
            set
            {
                if (SetValueAndReturn(ref _totalPlaytimeMinutes, Math.Max(0, value)))
                {
                    OnPropertyChanged(nameof(TotalPlaytimeSeconds));
                    OnPropertyChanged(nameof(TotalPlaytimeText));
                }
            }
        }

        public ulong TotalPlaytimeSeconds => (ulong)Math.Max(0, TotalPlaytimeMinutes) * 60UL;

        public string TotalPlaytimeText => PlayniteGameMetadataFormatter.FormatPlaytime(TotalPlaytimeSeconds);

        public string CountsText => $"{UnlockedAchievementsCount:N0} / {SharedGamesCount:N0}";
    }
}
