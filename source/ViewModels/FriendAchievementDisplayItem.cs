using System;
using System.Runtime.Serialization;
using System.Windows.Input;
using Playnite.SDK.Data;
using PlayniteAchievements.Common;
using PlayniteAchievements.Services.Friends;

namespace PlayniteAchievements.ViewModels
{
    public sealed class FriendAchievementDisplayItem : AchievementDisplayItem
    {
        [DontSerialize]
        [IgnoreDataMember]
        public ICommand SetDynamicFriendScopeProviderCommand { get; set; }

        [DontSerialize]
        [IgnoreDataMember]
        public ICommand SetDynamicFriendScopeUserCommand { get; set; }

        [DontSerialize]
        [IgnoreDataMember]
        public ICommand SetDynamicFriendScopeGameCommand { get; set; }

        [DontSerialize]
        [IgnoreDataMember]
        public string FriendScopeKey => FriendOverviewProjection.BuildFriendKey(ProviderKey, FriendExternalUserId)
            ?? FriendOverviewProjection.AllScopeKey;

        [DontSerialize]
        [IgnoreDataMember]
        public string FriendGameScopeKey => FriendOverviewProjection.BuildGameUnlockKey(ProviderKey, ProviderGameKey, AppId, PlayniteGameId)
            ?? FriendOverviewProjection.AllScopeKey;

        public int AppId { get; set; }

        public string ProviderGameKey { get; set; }

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
