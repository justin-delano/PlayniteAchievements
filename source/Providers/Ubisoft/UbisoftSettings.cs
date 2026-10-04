using Newtonsoft.Json;
using PlayniteAchievements.Providers.Settings;

namespace PlayniteAchievements.Providers.Ubisoft
{
    /// <summary>
    /// Ubisoft Connect provider settings. The session lives in connect.ubisoft.com's local storage in
    /// Playnite's shared browser store and is never copied here; the persisted id and name identify the
    /// signed-in account.
    /// </summary>
    public sealed class UbisoftSettings : ProviderSettingsBase
    {
        private string _userId;
        private string _nameOnPlatform;
        private bool _useExophaseForRarity = true;

        public override string ProviderKey => "Ubisoft";

        /// <summary>The Ubisoft account (user) id of the signed-in account.</summary>
        public string UserId
        {
            get => _userId;
            set => SetValue(ref _userId, value);
        }

        /// <summary>The Ubisoft Connect username of the signed-in account.</summary>
        public string NameOnPlatform
        {
            get => _nameOnPlatform;
            set => SetValue(ref _nameOnPlatform, value);
        }

        /// <summary>
        /// When true, fills achievement rarity from Exophase after native scanning. Ubisoft publishes
        /// no completion rates, so this is the only rarity source.
        /// </summary>
        public bool UseExophaseForRarity
        {
            get => _useExophaseForRarity;
            set => SetValue(ref _useExophaseForRarity, value);
        }

        [JsonIgnore]
        public bool HasUserId => !string.IsNullOrWhiteSpace(UserId);
    }
}
