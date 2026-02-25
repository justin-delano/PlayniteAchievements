using System;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models.Achievements;
using Playnite.SDK;

namespace PlayniteAchievements.ViewModels
{
    public class RecentAchievementItem : ObservableObject
    {
        private const string DefaultIconPackUri = "pack://application:,,,/PlayniteAchievements;component/Resources/HiddenAchIcon.png";

        public string ApiName { get; set; }
        public Guid? PlayniteGameId { get; set; }

        private bool _hidden;
        public bool Hidden
        {
            get => _hidden;
            set
            {
                if (SetValueAndReturn(ref _hidden, value))
                {
                    OnPropertyChanged(nameof(HiddenTitleSuffix));
                }
            }
        }

        private string _name;
        public string Name 
        { 
            get => _name; 
            set => SetValue(ref _name, value); 
        }

        private string _description;
        public string Description 
        { 
            get => _description; 
            set => SetValue(ref _description, value); 
        }

        private string _gameName;
        public string GameName 
        { 
            get => _gameName; 
            set => SetValue(ref _gameName, value); 
        }

        private string _iconPath;
        public string IconPath
        {
            get => _iconPath;
            set
            {
                if (SetValueAndReturn(ref _iconPath, value))
                {
                    OnPropertyChanged(nameof(DisplayIcon));
                }
            }
        }

        public string DisplayIcon
        {
            get
            {
                if (string.IsNullOrWhiteSpace(IconPath))
                {
                    return DefaultIconPackUri;
                }

                return IconPath;
            }
        }

        private DateTime _unlockTime;
        public DateTime UnlockTime 
        { 
            get => _unlockTime; 
            set 
            {
                // Use explicit check because SetValue is void
                if (_unlockTime != value)
                {
                    SetValue(ref _unlockTime, value);
                    OnPropertyChanged(nameof(UnlockTimeText));
                }
            } 
        }

        private double? _globalPercentUnlocked;
        /// <summary>
        /// Global unlock percentage. Null if rarity data is not available for this provider.
        /// </summary>
        public double? GlobalPercentUnlocked
        {
            get => _globalPercentUnlocked;
            set
            {
                if (SetValueAndReturn(ref _globalPercentUnlocked, value))
                {
                    OnPropertyChanged(nameof(GlobalPercent));
                    OnPropertyChanged(nameof(GlobalPercentText));
                    OnPropertyChanged(nameof(HasRarity));
                    OnPropertyChanged(nameof(Rarity));
                    OnPropertyChanged(nameof(RarityBrush));
                }
            }
        }

        /// <summary>
        /// Non-nullable version for backwards compatibility. Returns 0 if null.
        /// </summary>
        public double GlobalPercent => GlobalPercentUnlocked ?? 0;

        private int? _pointsValue;
        public int? PointsValue
        {
            get => _pointsValue;
            set
            {
                if (SetValueAndReturn(ref _pointsValue, value))
                {
                    OnPropertyChanged(nameof(Points));
                    OnPropertyChanged(nameof(PointsText));
                }
            }
        }

        private int? _progressNum;
        public int? ProgressNum
        {
            get => _progressNum;
            set => SetValue(ref _progressNum, value);
        }

        private int? _progressDenom;
        public int? ProgressDenom
        {
            get => _progressDenom;
            set => SetValue(ref _progressDenom, value);
        }

        private string _gameIconPath;
        public string GameIconPath 
        { 
            get => _gameIconPath; 
            set => SetValue(ref _gameIconPath, value); 
        }

        private string _gameCoverPath;
        public string GameCoverPath 
        { 
            get => _gameCoverPath; 
            set => SetValue(ref _gameCoverPath, value); 
        }

        private string _trophyType;
        /// <summary>
        /// Trophy type for PlayStation games: "bronze", "silver", "gold", "platinum".
        /// Null for non-PlayStation achievements.
        /// </summary>
        public string TrophyType
        {
            get => _trophyType;
            set
            {
                if (SetValueAndReturn(ref _trophyType, value))
                {
                    OnPropertyChanged(nameof(HasTrophyType));
                }
            }
        }

        /// <summary>
        /// True if this achievement has PlayStation trophy type data.
        /// </summary>
        public bool HasTrophyType => !string.IsNullOrWhiteSpace(TrophyType);

        public string UnlockTimeText => UnlockTime.ToLocalTime().ToString("g");
        public string GlobalPercentText => GlobalPercentUnlocked.HasValue ? $"{GlobalPercentUnlocked.Value:F1}%" : "-";
        public int Points => PointsValue ?? 0;
        public string PointsText => PointsValue.HasValue ? PointsValue.Value.ToString() : "-";

        /// <summary>
        /// True if rarity percentage data is available for this achievement.
        /// </summary>
        public bool HasRarity => GlobalPercentUnlocked.HasValue;

        public RarityTier Rarity => RarityHelper.GetRarityTier(GlobalPercentUnlocked ?? 100);

        public System.Windows.Media.SolidColorBrush RarityBrush => Rarity.ToBrush();
        public string HiddenTitleSuffix => Hidden ? ResourceProvider.GetString("LOCPlayAch_Achievements_HiddenTitle_WithParens") : string.Empty;

        public void UpdateFrom(RecentAchievementItem other)
        {
            if (other == null) return;

            // ApiName is immutable/key
            if (ApiName != other.ApiName) ApiName = other.ApiName;
            PlayniteGameId = other.PlayniteGameId;

            Name = other.Name;
            Description = other.Description;
            GameName = other.GameName;
            IconPath = other.IconPath;
            UnlockTime = other.UnlockTime;
            GlobalPercentUnlocked = other.GlobalPercentUnlocked;
            PointsValue = other.PointsValue;
            ProgressNum = other.ProgressNum;
            ProgressDenom = other.ProgressDenom;
            GameIconPath = other.GameIconPath;
            GameCoverPath = other.GameCoverPath;
            TrophyType = other.TrophyType;
        }
    }
}
