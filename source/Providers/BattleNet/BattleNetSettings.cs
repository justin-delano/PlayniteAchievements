using System;
using PlayniteAchievements.Providers.Settings;

namespace PlayniteAchievements.Providers.BattleNet
{
    public sealed class BattleNetSettings : ProviderSettingsBase
    {
        public override string ProviderKey => "BattleNet";

        private string _battleNetUserId;
        private string _battleTag;
        private string _battleNetClientId;
        private string _battleNetClientSecret;
        private string _battleNetOAuthRedirectUri = "http://localhost/playnite-achievements/battlenet/oauth";
        private string _battleNetAccessToken;
        private string _battleNetRefreshToken;
        private DateTime _battleNetTokenExpiresUtc;
        private int _sc2RegionId = 1;
        private int _sc2RealmId = 1;
        private int _sc2ProfileId;
        private string _wowRegion;
        private string _wowRealmSlug;
        private string _wowCharacter;

        public string BattleNetUserId
        {
            get => _battleNetUserId;
            set => SetValue(ref _battleNetUserId, value);
        }

        public string BattleTag
        {
            get => _battleTag;
            set => SetValue(ref _battleTag, value);
        }

        public string BattleNetClientId
        {
            get => _battleNetClientId;
            set => SetValue(ref _battleNetClientId, value);
        }

        public string BattleNetClientSecret
        {
            get => _battleNetClientSecret;
            set => SetValue(ref _battleNetClientSecret, value);
        }

        public string BattleNetOAuthRedirectUri
        {
            get => _battleNetOAuthRedirectUri;
            set => SetValue(ref _battleNetOAuthRedirectUri, value);
        }

        public string BattleNetAccessToken
        {
            get => _battleNetAccessToken;
            set => SetValue(ref _battleNetAccessToken, value);
        }

        public string BattleNetRefreshToken
        {
            get => _battleNetRefreshToken;
            set => SetValue(ref _battleNetRefreshToken, value);
        }

        public DateTime BattleNetTokenExpiresUtc
        {
            get => _battleNetTokenExpiresUtc;
            set => SetValue(ref _battleNetTokenExpiresUtc, value);
        }

        public int Sc2RegionId
        {
            get => _sc2RegionId;
            set => SetValue(ref _sc2RegionId, value);
        }

        public int Sc2RealmId
        {
            get => _sc2RealmId;
            set => SetValue(ref _sc2RealmId, value);
        }

        public int Sc2ProfileId
        {
            get => _sc2ProfileId;
            set => SetValue(ref _sc2ProfileId, value);
        }

        public string WowRegion
        {
            get => _wowRegion;
            set => SetValue(ref _wowRegion, value);
        }

        public string WowRealmSlug
        {
            get => _wowRealmSlug;
            set => SetValue(ref _wowRealmSlug, value);
        }

        public string WowCharacter
        {
            get => _wowCharacter;
            set => SetValue(ref _wowCharacter, value);
        }
    }
}
