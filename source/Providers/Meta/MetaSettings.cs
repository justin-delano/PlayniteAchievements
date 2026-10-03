using Newtonsoft.Json;
using PlayniteAchievements.Providers.Settings;

namespace PlayniteAchievements.Providers.Meta
{
    /// <summary>
    /// Meta Quest provider settings. The access token is the oc_www_at cookie in Playnite's shared
    /// browser store and is never copied here; the persisted user id and alias identify the account
    /// whose unlock feed is read.
    /// </summary>
    public sealed class MetaSettings : ProviderSettingsBase
    {
        private string _userId;
        private string _alias;

        /// <inheritdoc />
        public override string ProviderKey => "Meta";

        /// <summary>
        /// The numeric Meta user id returned by graph.oculus.com/me.
        /// </summary>
        public string UserId
        {
            get => _userId;
            set => SetValue(ref _userId, value);
        }

        /// <summary>
        /// The Meta Horizon alias (username) of the logged-in account.
        /// </summary>
        public string Alias
        {
            get => _alias;
            set => SetValue(ref _alias, value);
        }

        [JsonIgnore]
        public bool HasUserId => !string.IsNullOrWhiteSpace(UserId);
    }
}
