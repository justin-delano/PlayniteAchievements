using PlayniteAchievements.Services.GameCustomData;
using PlayniteAchievements.Services.Notifications;
using PlayniteAchievements.Services.Sound;

namespace PlayniteAchievements.Services.Workshop.Preview
{
    /// <summary>
    /// The stores <see cref="WorkshopPreviewModelBuilder"/> reads packages with, and for game data
    /// the library game the package is compared against. A store a kind does not use may be null.
    /// </summary>
    public sealed partial class WorkshopPreviewContext
    {
        /// <summary>Reads color set packages and the colors part of a bundle.</summary>
        public ColorPackPortableStore ColorPackPortableStore { get; set; }

        /// <summary>Reads notification and frame style packages and the style parts of a bundle.</summary>
        public NotificationStylePortableStore NotificationStylePortableStore { get; set; }

        /// <summary>Extracts sound packs and the sounds part of a bundle.</summary>
        public UnlockSoundPortableStore UnlockSoundPortableStore { get; set; }

        /// <summary>Reads bundle manifests and extracts their parts.</summary>
        public BundlePortableStore BundlePortableStore { get; set; }

        /// <summary>Reads game data packages.</summary>
        public GameCustomDataStore GameCustomDataStore { get; set; }

        /// <summary>
        /// The library game a game data package is compared against, or null to list what the
        /// package carries without a comparison.
        /// </summary>
        public GameCustomDataPreviewSource GameDataSource { get; set; }
    }
}
