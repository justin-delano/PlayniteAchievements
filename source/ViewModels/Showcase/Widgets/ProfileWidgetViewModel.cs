using System;
using System.Linq;
using System.Windows;
using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Overview;
using PlayniteAchievements.Services.Showcase;

namespace PlayniteAchievements.ViewModels.Showcase.Widgets
{
    /// <summary>
    /// Backs the Profile widget: avatar, display name, and (outside compact) subtitle, with an
    /// expanded stats line and streak line. Properties notify so the reused view model updates in
    /// place when the viewport density changes.
    /// </summary>
    public sealed class ProfileWidgetViewModel : ShowcaseWidgetViewModelBase
    {
        private string _backgroundPath;
        private bool _hasBackground;
        private string _avatarPath;
        private bool _hasAvatar;
        private double _avatarSize = 72;
        private CornerRadius _avatarCornerRadius = new CornerRadius(38);
        private int _avatarDecodePixel = 144;
        private string _displayName;
        private string _subtitle;
        private bool _showSubtitle;
        private string _statsLine;
        private bool _showStats;
        private string _streaksLine;
        private bool _showStreaks;

        public string BackgroundPath { get => _backgroundPath; private set => SetValue(ref _backgroundPath, value); }

        public bool HasBackground { get => _hasBackground; private set => SetValue(ref _hasBackground, value); }

        public string AvatarPath { get => _avatarPath; private set => SetValue(ref _avatarPath, value); }

        public bool HasAvatar { get => _hasAvatar; private set => SetValue(ref _hasAvatar, value); }

        public double AvatarSize { get => _avatarSize; private set => SetValue(ref _avatarSize, value); }

        public CornerRadius AvatarCornerRadius { get => _avatarCornerRadius; private set => SetValue(ref _avatarCornerRadius, value); }

        public int AvatarDecodePixel { get => _avatarDecodePixel; private set => SetValue(ref _avatarDecodePixel, value); }

        public string DisplayName { get => _displayName; private set => SetValue(ref _displayName, value); }

        public string Subtitle { get => _subtitle; private set => SetValue(ref _subtitle, value); }

        public bool ShowSubtitle { get => _showSubtitle; private set => SetValue(ref _showSubtitle, value); }

        public string StatsLine { get => _statsLine; private set => SetValue(ref _statsLine, value); }

        public bool ShowStats { get => _showStats; private set => SetValue(ref _showStats, value); }

        public string StreaksLine { get => _streaksLine; private set => SetValue(ref _streaksLine, value); }

        public bool ShowStreaks { get => _showStreaks; private set => SetValue(ref _showStreaks, value); }

        protected override void Refresh()
        {
            var profile = Projection?.Profile ?? new ShowcaseProfileSettings();
            var snapshot = Projection?.Snapshot ?? new OverviewDataSnapshot();
            var compact = Density == WidgetViewportDensity.Compact;
            var expanded = Density == WidgetViewportDensity.Expanded;

            BackgroundPath = profile.BackgroundPath;
            HasBackground = !string.IsNullOrWhiteSpace(profile.BackgroundPath);

            AvatarPath = profile.AvatarPath;
            HasAvatar = !string.IsNullOrWhiteSpace(profile.AvatarPath);
            AvatarSize = compact ? 42 : 72;
            AvatarCornerRadius = new CornerRadius((AvatarSize + 4) / 2);
            AvatarDecodePixel = Math.Max(64, (int)Math.Ceiling(AvatarSize * 2));

            DisplayName = string.IsNullOrWhiteSpace(profile.DisplayName)
                ? ResourceProvider.GetString("LOCPlayAch_Showcase_Profile_DefaultName")
                : profile.DisplayName;

            Subtitle = profile.Subtitle;
            ShowSubtitle = !string.IsNullOrWhiteSpace(profile.Subtitle) && !compact;

            ShowStats = expanded;
            StatsLine = expanded
                ? string.Format(
                    FormattingCulture.Current,
                    ResourceProvider.GetString("LOCPlayAch_Showcase_ProfileStats"),
                    snapshot.TotalUnlocked,
                    snapshot.GlobalProgressionPercent,
                    snapshot.CompletedGames)
                : string.Empty;

            var currentStreak = Projection?.Statistics?.FirstOrDefault(item =>
                string.Equals(item?.Key, "currentStreak", StringComparison.Ordinal));
            var longestStreak = Projection?.Statistics?.FirstOrDefault(item =>
                string.Equals(item?.Key, "longestStreak", StringComparison.Ordinal));
            ShowStreaks = expanded && currentStreak != null && longestStreak != null;
            StreaksLine = ShowStreaks
                ? string.Format(
                    FormattingCulture.Current,
                    ResourceProvider.GetString("LOCPlayAch_Showcase_ProfileStreaks"),
                    ShowcaseStatisticFormatter.Format(currentStreak),
                    ShowcaseStatisticFormatter.Format(longestStreak))
                : string.Empty;
        }
    }
}
