using System;
using PlayniteAchievements.Common;

namespace PlayniteAchievements.ViewModels
{
    public class GameOverviewItem : ObservableObject
    {
        private string _gameName;
        public string GameName { get => _gameName; set => SetValue(ref _gameName, value); }

        private string _gameLogo;
        public string GameLogo { get => _gameLogo; set => SetValue(ref _gameLogo, value); }

        private string _gameCoverPath;
        public string GameCoverPath { get => _gameCoverPath; set => SetValue(ref _gameCoverPath, value); }

        public int AppId { get; set; } // Stays as AppId is immutable ID
        public Guid? PlayniteGameId { get; set; }

        private int _totalAchievements;
        public int TotalAchievements 
        { 
            get => _totalAchievements; 
            set 
            { 
                if (_totalAchievements != value)
                {
                    SetValue(ref _totalAchievements, value); 
                    OnPropertyChanged(nameof(Progression)); 
                    OnPropertyChanged(nameof(ProgressionText)); 
                }
            } 
        }

        private int _unlockedAchievements;
        public int UnlockedAchievements 
        { 
            get => _unlockedAchievements; 
            set 
            { 
                if (_unlockedAchievements != value)
                {
                    SetValue(ref _unlockedAchievements, value); 
                    OnPropertyChanged(nameof(Progression)); 
                    OnPropertyChanged(nameof(ProgressionText)); 
                }
            } 
        }

        private int _commonCount;
        public int CommonCount { get => _commonCount; set => SetValue(ref _commonCount, value); }

        private int _uncommonCount;
        public int UncommonCount { get => _uncommonCount; set => SetValue(ref _uncommonCount, value); }

        private int _rareCount;
        public int RareCount { get => _rareCount; set => SetValue(ref _rareCount, value); }

        private int _ultraRareCount;
        public int UltraRareCount { get => _ultraRareCount; set => SetValue(ref _ultraRareCount, value); }

        private DateTime? _lastPlayed;
        public DateTime? LastPlayed 
        { 
            get => _lastPlayed; 
            set 
            { 
                if (_lastPlayed != value)
                {
                    SetValue(ref _lastPlayed, value); 
                    OnPropertyChanged(nameof(LastPlayedText)); 
                }
            } 
        }

        private bool _isPerfect;
        public bool IsPerfect { get => _isPerfect; set => SetValue(ref _isPerfect, value); }

        private string _provider;
        public string Provider { get => _provider; set => SetValue(ref _provider, value); }


        public double Progression => TotalAchievements > 0
            ? (double)UnlockedAchievements / TotalAchievements * 100
            : 0;

        public string ProgressionText => $"{Progression:F0}%";

        public string LastPlayedText => LastPlayed.HasValue
            ? LastPlayed.Value.ToLocalTime().ToString("g")
            : "";
    }
}
