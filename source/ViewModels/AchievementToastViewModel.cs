using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Playnite.SDK;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Settings;

namespace PlayniteAchievements.ViewModels
{
    public sealed class AchievementToastViewModel
    {
        private const string DefaultIcon =
            "pack://application:,,,/PlayniteAchievements;component/Resources/UnlockedAchIcon.png";

        private readonly AchievementUnlockedEventArgs _args;
        private readonly PersistedSettings _settings;
        private readonly RarityTier _rarity;

        public AchievementToastViewModel(AchievementUnlockedEventArgs args, PersistedSettings settings)
        {
            _args = args ?? new AchievementUnlockedEventArgs();
            _settings = settings ?? new PersistedSettings();
            _rarity = ParseRarity(_args.RarityTier);
        }

        public bool IsFriendUnlock => _args.IsFriendUnlock;

        // Provider identity, bindable so a single toast/frame template can restyle per provider
        // with DataTriggers (e.g. trigger on ProviderKey, tint with ProviderColorHex).
        public string ProviderKey => _args.ProviderKey;
        public string ProviderName => Providers.ProviderRegistry.GetLocalizedName(ProviderKey);
        public string ProviderColorHex => Providers.ProviderRegistry.GetProviderColorHex(ProviderKey);

        // Raw fields consumed by the unlock-screenshot feature (not shown in the toast UI).
        internal bool IsPreview => _args.IsPreview;
        internal string AchievementName => IsGameCompletionNotification ? TitleText : _args.DisplayName;
        internal int AchievementNumber => _args.AchievementNumber;
        internal int TotalCount => _args.TotalCount;
        internal Guid PlayniteGameId => _args.PlayniteGameId;

        // The header identifies who unlocked the achievement (or who completed the game), so it
        // is mandatory for friend unlocks and completion notifications; otherwise it honors the
        // user's toggle. The completion notification also forces its title and game name — they
        // are the message.
        public bool ShowHeader => IsFriendUnlock || IsGameCompletionNotification || _settings.ToastShowHeader;
        public bool ShowName => (_settings.ToastShowName || IsGameCompletionNotification) && !string.IsNullOrWhiteSpace(TitleText);
        public bool ShowDescription => _settings.ToastShowDescription && !string.IsNullOrWhiteSpace(_args.Description);
        public bool ShowCategory => _settings.ToastShowCategory && HasDistinctCategory;
        public bool ShowPercent => _settings.ToastShowRarityPercent && _args.GlobalPercent.HasValue;
        public bool IsCapstone => _args.IsCapstone;

        /// <summary>
        /// True when this notification belongs to the game's completion moment: the unlock batch
        /// that pushed the game to complete, and the standalone completion notification. Frames
        /// restyle on it; the toast keeps its rarity styling for regular unlocks.
        /// </summary>
        public bool IsCompleted => _args.CompletesGame;

        /// <summary>
        /// True for the standalone "Congratulations! Game Complete!" notification that follows
        /// the completing unlock's wave.
        /// </summary>
        public bool IsGameCompletionNotification => _args.IsGameCompletionNotification;

        public bool HasTrophy => !string.IsNullOrWhiteSpace(_args.TrophyType);
        private bool HasRarityData => _args.GlobalPercent.HasValue || !string.IsNullOrWhiteSpace(_args.RarityTier);
        public bool ShowBadge => _settings.ToastShowRarityBadge && (IsCapstone || IsGameCompletionNotification || HasTrophy || HasRarityData);
        public bool ShowGameName => (_settings.ToastShowGameName || IsGameCompletionNotification) && !string.IsNullOrWhiteSpace(_args.GameName);
        public bool ShowGameCategorySeparator => ShowGameName && ShowCategory;
        public bool HasFriendAvatar => !string.IsNullOrWhiteSpace(FriendAvatar);

        // A category that just repeats the game name (common for single-list providers) reads as
        // a duplicate, so it is force-hidden in both the toast and the frame.
        private bool HasDistinctCategory =>
            !string.IsNullOrWhiteSpace(_args.Category) &&
            !string.Equals(_args.Category?.Trim(), _args.GameName?.Trim(), StringComparison.OrdinalIgnoreCase);

        // Unlock timestamp bindings (local time, current-culture formatting like the grids'
        // UnlockTimeText). A midnight time-of-day means a date-only provider timestamp, so the
        // time portion is suppressed. Available to both toast and frame templates.
        private DateTime? UnlockTimeLocal => Common.DateTimeUtilities.AsLocalFromUtc(_args.UnlockTimeUtc);
        public bool HasUnlockTime => UnlockTimeLocal.HasValue;
        // Toast-scoped visibility for the unlock datetime on the header line (off by default).
        // The header toggle governs only the "Achievement unlocked" text; the datetime is
        // independent, and the separator needs both.
        public bool ShowUnlockTime => _settings.ToastShowUnlockTime && HasUnlockTime;
        public bool ShowHeaderDateSeparator => ShowHeader && ShowUnlockTime;
        public bool ShowFriendAvatar => ShowHeader && HasFriendAvatar;
        public string UnlockDateText => UnlockTimeLocal?.ToString("d") ?? string.Empty;
        public string UnlockTimeText => UnlockTimeLocal.HasValue && UnlockTimeLocal.Value.TimeOfDay != TimeSpan.Zero
            ? UnlockTimeLocal.Value.ToString("t")
            : string.Empty;
        public string UnlockDateTimeText => UnlockTimeLocal.HasValue
            ? UnlockTimeLocal.Value.TimeOfDay != TimeSpan.Zero
                ? UnlockTimeLocal.Value.ToString("g")
                : UnlockTimeLocal.Value.ToString("d")
            : string.Empty;

        // Frame-scoped equivalents: the frame's header row shows "header • unlock datetime";
        // the separator needs both, and the datetime honors its own toggle.
        public bool FrameShowUnlockTime => _settings.FrameShowUnlockTime && HasUnlockTime;
        public bool FrameShowHeaderDateSeparator => FrameShowHeader && FrameShowUnlockTime;

        // Frame-scoped visibility/appearance: the screenshot frame honors its own FrameShow*
        // settings so the saved image can show different fields than the on-screen toast.
        public bool FrameShowHeader => IsFriendUnlock || IsGameCompletionNotification || _settings.FrameShowHeader;
        public bool FrameShowName => (_settings.FrameShowName || IsGameCompletionNotification) && !string.IsNullOrWhiteSpace(TitleText);
        public bool FrameShowDescription => _settings.FrameShowDescription && !string.IsNullOrWhiteSpace(_args.Description);
        public bool FrameShowCategory => _settings.FrameShowCategory && HasDistinctCategory;
        public bool FrameShowPercent => _settings.FrameShowRarityPercent && _args.GlobalPercent.HasValue;
        public bool FrameShowBadge => _settings.FrameShowRarityBadge && (IsCapstone || IsCompleted || HasTrophy || HasRarityData);
        public bool FrameShowGameName => (_settings.FrameShowGameName || IsGameCompletionNotification) && !string.IsNullOrWhiteSpace(_args.GameName);
        public bool FrameShowGameCategorySeparator => FrameShowGameName && FrameShowCategory;
        public bool FrameShowShineBorder => _settings.FrameShowRarityGlow && IsHardcore;

        /// <summary>
        /// The frame's "Game Complete!" line: shown on the completing unlock's framed screenshot.
        /// The completion notification's own frame hides it — its title already says it. The
        /// separator shows only when header text or the datetime precedes the line.
        /// </summary>
        public bool FrameShowGameCompleteLine => IsCompleted && !IsGameCompletionNotification;
        public bool FrameShowGameCompleteSeparator => FrameShowGameCompleteLine && (FrameShowHeader || FrameShowUnlockTime);

        // Mirrors TitleBrush but honors the frame's own rarity-colored-name toggle.
        public Brush FrameTitleBrush => _settings.FrameRarityColoredName
            ? FrameAccentBrush
            : Application.Current?.TryFindResource("PlayAch.Brush.Text") as Brush ?? Brushes.White;

        public Effect FrameRarityGlowEffect => _settings.FrameShowRarityGlow && !IsHardcore
            ? RarityAppearanceHelper.GetGlow(_rarity, 20, _settings)
            : null;

        public string HeaderText
        {
            get
            {
                if (IsGameCompletionNotification)
                {
                    if (IsFriendUnlock)
                    {
                        var completeFormat = ResourceProvider.GetString("LOCPlayAch_Toast_FriendGameComplete");
                        return string.Format(completeFormat, string.IsNullOrWhiteSpace(_args.FriendDisplayName) ? "Friend" : _args.FriendDisplayName);
                    }

                    return ResourceProvider.GetString("LOCPlayAch_Toast_Congratulations");
                }

                if (IsFriendUnlock)
                {
                    var format = ResourceProvider.GetString("LOCPlayAch_Toast_FriendUnlocked");
                    return string.Format(format, string.IsNullOrWhiteSpace(_args.FriendDisplayName) ? "Friend" : _args.FriendDisplayName);
                }

                return ResourceProvider.GetString("LOCPlayAch_Toast_AchievementUnlocked");
            }
        }

        public string TitleText
        {
            get
            {
                if (IsGameCompletionNotification)
                {
                    return ResourceProvider.GetString("LOCPlayAch_Toast_GameComplete");
                }

                return string.IsNullOrWhiteSpace(_args.DisplayName)
                    ? ResourceProvider.GetString("LOCPlayAch_Text_UnknownAchievement")
                    : _args.DisplayName;
            }
        }

        public string Description => _args.Description;
        public string Category => _args.Category;
        public string GameName => _args.GameName;
        public string IconPath => string.IsNullOrWhiteSpace(_args.IconPath) ? DefaultIcon : _args.IconPath;
        public string FriendAvatar => !string.IsNullOrWhiteSpace(_args.FriendAvatarPath)
            ? _args.FriendAvatarPath
            : _args.FriendAvatarUrl;

        public string PercentText => _args.GlobalPercent.HasValue
            ? AchievementRarityResolver.FormatPercent(_args.GlobalPercent.Value)
            : string.Empty;

        /// <summary>
        /// Parsed rarity tier for theme authors. Templates can bind to this directly for custom
        /// badge styles/triggers instead of using the plugin-generated BadgeImage.
        /// </summary>
        public RarityTier Rarity => _rarity;

        /// <summary>
        /// Rarity-colored brush for the left accent strip and countdown bar (completed color for
        /// capstones and the completion notification, otherwise the rarity color).
        /// </summary>
        public Brush AccentBrush => IsCapstone || IsGameCompletionNotification
            ? RarityAppearanceHelper.GetCompletedBrush(_settings)
            : RarityAppearanceHelper.GetBrush(_rarity, _settings);

        /// <summary>
        /// Frame-scoped accent: unlike the toast, the frame also switches to the completed color
        /// for the unlock batch that completed the game (IsCompleted).
        /// </summary>
        public Brush FrameAccentBrush => IsCapstone || IsCompleted
            ? RarityAppearanceHelper.GetCompletedBrush(_settings)
            : RarityAppearanceHelper.GetBrush(_rarity, _settings);

        // Capstone color takes precedence over rarity, matching the grid's RarityNameBrush.
        public Brush TitleBrush => _settings.ToastRarityColoredName
            ? AccentBrush
            : Application.Current?.TryFindResource("PlayAch.Brush.Text") as Brush ?? Brushes.White;

        public bool IsHardcore => _args.IsHardcore;

        /// <summary>
        /// Hardcore RetroAchievements unlocks get a crisp rarity-colored border in place of the
        /// soft glow, mirroring the datagrids. Both are gated on the rarity-glow toggle.
        /// </summary>
        public bool ShowShineBorder => _settings.ToastShowRarityGlow && IsHardcore;

        // Glossy metallic rarity border (matches RarityToShineBrush used by the datagrids).
        public Brush IconBorderBrush => RarityAppearanceHelper.GetShineBrush(_rarity, _settings);

        // Soft rarity glow for non-hardcore unlocks (matches PercentToRarityGlow, BlurRadius 20).
        public Effect RarityGlowEffect => _settings.ToastShowRarityGlow && !IsHardcore
            ? RarityAppearanceHelper.GetGlow(_rarity, 20, _settings)
            : null;

        public ImageSource BadgeImage => CreateBadge(IsCapstone || IsGameCompletionNotification);

        /// <summary>
        /// Frame-scoped badge: shows the completed badge for the completing unlock batch too,
        /// matching FrameAccentBrush.
        /// </summary>
        public ImageSource FrameBadgeImage => CreateBadge(IsCapstone || IsCompleted);

        private ImageSource CreateBadge(bool completed)
        {
            if (completed)
            {
                return RarityAppearanceHelper.CreateCompletedBadgePreview(_settings);
            }

            if (HasTrophy)
            {
                return RarityAppearanceHelper.CreateTrophyPreview(MapTrophyKey(_args.TrophyType), _settings);
            }

            return HasRarityData
                ? RarityAppearanceHelper.CreateBadgePreview(_rarity, _settings)
                : null;
        }

        /// <summary>
        /// UniPlaySong URI segment for this unlock's tier (e.g. "rareachievement"). Capstone and
        /// the completion notification take precedence over rarity; otherwise the rarity tier is
        /// used.
        /// </summary>
        public string SoundTierSegment
        {
            get
            {
                if (IsCapstone || IsGameCompletionNotification)
                {
                    return "capstoneachievement";
                }

                switch (_rarity)
                {
                    case RarityTier.UltraRare:
                        return "ultrarareachievement";
                    case RarityTier.Rare:
                        return "rareachievement";
                    case RarityTier.Uncommon:
                        return "uncommonachievement";
                    default:
                        return "commonachievement";
                }
            }
        }

        /// <summary>
        /// Rarity ranking used to pick a single representative sound when several unlocks show at
        /// once. Higher is rarer; capstone and the completion notification outrank all rarity
        /// tiers.
        /// </summary>
        public int SoundTierRank
        {
            get
            {
                if (IsCapstone || IsGameCompletionNotification)
                {
                    return 5;
                }

                switch (_rarity)
                {
                    case RarityTier.UltraRare:
                        return 4;
                    case RarityTier.Rare:
                        return 3;
                    case RarityTier.Uncommon:
                        return 2;
                    default:
                        return 1;
                }
            }
        }

        private static string MapTrophyKey(string trophyType)
        {
            switch (trophyType?.Trim().ToLowerInvariant())
            {
                case "platinum":
                    return "TrophyPlatinum";
                case "gold":
                    return "TrophyGold";
                case "silver":
                    return "TrophySilver";
                default:
                    return "TrophyBronze";
            }
        }

        private static RarityTier ParseRarity(string value)
        {
            if (!string.IsNullOrWhiteSpace(value) &&
                Enum.TryParse(value, ignoreCase: true, result: out RarityTier rarity))
            {
                return rarity;
            }

            return RarityTier.Common;
        }
    }
}
