namespace PlayniteAchievements.Models.Settings
{
    /// <summary>
    /// A place in the settings window: a tab, a page of that tab, and for the notification
    /// appearance page the platform and surface to show. Read by the settings window, which
    /// switches to it in place or opens on it.
    /// </summary>
    public sealed class SettingsNavigationRequest
    {
        public const string DisplayTab = "Display";
        public const string NotificationsTab = "Notifications";
        public const string WorkshopTab = "Workshop";

        public const string ColorsPage = "Colors";
        public const string BehaviorPage = "Behavior";
        public const string AppearancePage = "Appearance";

        public SettingsNavigationRequest(string tabKey, string pageKey = null)
        {
            TabKey = tabKey;
            PageKey = pageKey;
        }

        public string TabKey { get; }

        /// <summary>The page within the tab; null keeps the tab's current page.</summary>
        public string PageKey { get; }

        /// <summary>The Appearance page's platform: a provider key, or null for the default.</summary>
        public string ProviderKey { get; set; }

        /// <summary>The Appearance page's surface: true for the frame, false for the notification, null to keep it.</summary>
        public bool? IsFrame { get; set; }

        /// <summary>Brings the Behavior page's unlock sounds picker into view.</summary>
        public bool ShowSounds { get; set; }
    }
}
