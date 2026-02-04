using PlayniteAchievements.Common;
using System;
using System.Windows.Input;

namespace PlayniteAchievements.Models.ThemeIntegration
{
    /// <summary>
    /// Lightweight theme-facing item used by fullscreen themes (e.g. Aniki ReMake) for trophy/game lists.
    /// Properties need setters for XAML TwoWay/OneWayToSource bindings (required by Aniki-ReMake theme).
    /// </summary>
    public sealed class FullscreenAchievementGameItem : ObservableObject
    {
        private int _progress;
        private int _gs90Count;
        private int _gs30Count;
        private int _gs15Count;

        public Guid GameId { get; }
        public string Name { get; }
        public string Platform { get; }
        public string CoverImagePath { get; }

        // Need setter for XAML TwoWay/OneWayToSource bindings
        public int Progress
        {
            get => _progress;
            set => SetValue(ref _progress, Math.Max(0, Math.Min(100, value)));
        }

        public int GS90Count
        {
            get => _gs90Count;
            set => SetValue(ref _gs90Count, Math.Max(0, value));
        }

        public int GS30Count
        {
            get => _gs30Count;
            set => SetValue(ref _gs30Count, Math.Max(0, value));
        }

        public int GS15Count
        {
            get => _gs15Count;
            set => SetValue(ref _gs15Count, Math.Max(0, value));
        }

        public DateTime LatestUnlocked { get; }
        public DateTime LastUnlockDate { get; }
        public ICommand OpenAchievementWindow { get; }

        public FullscreenAchievementGameItem(
            Guid gameId,
            string name,
            string platform,
            string coverImagePath,
            int progress,
            int gs90Count,
            int gs30Count,
            int gs15Count,
            DateTime latestUnlocked,
            DateTime lastUnlockDate,
            ICommand openAchievementWindow)
        {
            GameId = gameId;
            Name = name ?? string.Empty;
            Platform = platform ?? "Unknown";
            CoverImagePath = coverImagePath ?? string.Empty;
            _progress = Math.Max(0, Math.Min(100, progress));
            _gs90Count = Math.Max(0, gs90Count);
            _gs30Count = Math.Max(0, gs30Count);
            _gs15Count = Math.Max(0, gs15Count);
            LatestUnlocked = latestUnlocked;
            LastUnlockDate = lastUnlockDate;
            OpenAchievementWindow = openAchievementWindow;
        }
    }
}
