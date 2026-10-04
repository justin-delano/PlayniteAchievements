using System;

namespace PlayniteAchievements.Services.Workshop.Preview
{
    public sealed partial class WorkshopPreviewContext
    {
        /// <summary>
        /// The plugin's stores, as <see cref="WorkshopInstaller"/> reads them. The game data
        /// comparison source is left null for the caller to set.
        /// </summary>
        public static WorkshopPreviewContext FromPlugin(PlayniteAchievementsPlugin plugin)
        {
            if (plugin == null)
            {
                throw new ArgumentNullException(nameof(plugin));
            }

            return new WorkshopPreviewContext
            {
                ColorPackPortableStore = plugin.ColorPackPortableStore,
                NotificationStylePortableStore = plugin.NotificationStylePortableStore,
                UnlockSoundPortableStore = plugin.UnlockSoundPortableStore,
                BundlePortableStore = plugin.BundlePortableStore,
                GameCustomDataStore = plugin.GameCustomDataStore
            };
        }
    }
}
