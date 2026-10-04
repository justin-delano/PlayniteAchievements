namespace PlayniteAchievements.Models.Settings
{
    /// <summary>
    /// One way to recognize the game a portable custom-data file was exported for on another
    /// machine, where the exporter's Playnite game id means nothing. A file carries every key it
    /// can: the provider's identity for each provider that services the game (Steam app id,
    /// RetroAchievements game id, and so on) and, for games no provider services, the name and
    /// platform the exporter saw.
    /// </summary>
    public sealed class PortableGameKey
    {
        /// <summary>The servicing provider's key ("Steam", "RetroAchievements"); null for a name-only key.</summary>
        public string ProviderKey { get; set; }

        /// <summary>The original platform when <see cref="ProviderKey"/> is a proxy such as Exophase.</summary>
        public string ProviderPlatformKey { get; set; }

        /// <summary>The provider's numeric id for the game, when it has one.</summary>
        public int? ProviderGameId { get; set; }

        /// <summary>The provider's string id for the game, when it has one.</summary>
        public string ProviderGameKey { get; set; }

        /// <summary>The game's name as the exporter saw it.</summary>
        public string Name { get; set; }

        /// <summary>The Playnite platform name, when the library records one.</summary>
        public string Platform { get; set; }

        public PortableGameKey Clone()
        {
            return new PortableGameKey
            {
                ProviderKey = ProviderKey,
                ProviderPlatformKey = ProviderPlatformKey,
                ProviderGameId = ProviderGameId,
                ProviderGameKey = ProviderGameKey,
                Name = Name,
                Platform = Platform
            };
        }
    }
}
