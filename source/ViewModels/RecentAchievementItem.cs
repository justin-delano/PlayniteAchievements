using System;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models.Achievement;

namespace PlayniteAchievements.ViewModels
{
    public class RecentAchievementItem : ObservableObject
    {
        private const string DefaultIconPackUri = "pack://application:,,,/PlayniteAchievements;component/Resources/HiddenAchIcon.png";

        public string ApiName { get; set; }

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

        private string _unlockedIconUrl;
        public string UnlockedIconUrl 
        { 
            get => _unlockedIconUrl; 
            set
            {
                if (SetValueAndReturn(ref _unlockedIconUrl, value))
                {
                    OnPropertyChanged(nameof(DisplayIcon));
                }
            }
        }

        public string DisplayIcon
        {
            get
            {
                if (string.IsNullOrWhiteSpace(UnlockedIconUrl))
                {
                    return DefaultIconPackUri;
                }

                return UnlockedIconUrl;
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

        private double _globalPercent;
        public double GlobalPercent 
        { 
            get => _globalPercent; 
            set 
            { 
                // Use explicit check
                if (Math.Abs(_globalPercent - value) > 0.001)
                {
                    SetValue(ref _globalPercent, value);
                    OnPropertyChanged(nameof(GlobalPercentText));
                    OnPropertyChanged(nameof(RarityIconKey));
                    OnPropertyChanged(nameof(RarityBrush));
                }
            } 
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

        public string UnlockTimeText => UnlockTime.ToLocalTime().ToString("g");
        public string GlobalPercentText => $"{GlobalPercent:F1}%";

        public string RarityIconKey => RarityHelper.GetRarityIconKey(GlobalPercent);

        public System.Windows.Media.SolidColorBrush RarityBrush => RarityHelper.GetRarityBrush(GlobalPercent);

        public void UpdateFrom(RecentAchievementItem other)
        {
            if (other == null) return;

            // ApiName is immutable/key
            if (ApiName != other.ApiName) ApiName = other.ApiName;

            Name = other.Name;
            Description = other.Description;
            GameName = other.GameName;
            UnlockedIconUrl = other.UnlockedIconUrl;
            UnlockTime = other.UnlockTime;
            GlobalPercent = other.GlobalPercent;
            GameIconPath = other.GameIconPath;
            GameCoverPath = other.GameCoverPath;
        }
    }
}
