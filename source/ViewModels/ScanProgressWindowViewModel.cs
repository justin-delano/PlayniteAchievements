using System;
using System.Windows.Input;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Services;
using Playnite.SDK;
using RelayCommand = PlayniteAchievements.Common.RelayCommand;

namespace PlayniteAchievements.ViewModels
{
    public class ScanProgressWindowViewModel : ObservableObject
    {
        private readonly AchievementManager _achievementManager;
        private readonly ILogger _logger;

        private double _progressPercent;
        private string _progressMessage;
        private bool _isComplete;

        public bool IsScanning => _achievementManager.IsRebuilding;

        public double ProgressPercent
        {
            get => _progressPercent;
            set => SetValue(ref _progressPercent, value);
        }

        public string ProgressMessage
        {
            get => _progressMessage;
            set => SetValue(ref _progressMessage, value);
        }

        public bool IsComplete
        {
            get => _isComplete;
            set
            {
                if (SetValueAndReturn(ref _isComplete, value))
                {
                    OnPropertyChanged(nameof(ShowInProgressButtons));
                    OnPropertyChanged(nameof(ShowCompleteButtons));
                }
            }
        }

        public bool ShowInProgressButtons => !IsComplete && IsScanning;
        public bool ShowCompleteButtons => IsComplete;

        public string ScanRunningNote => ResourceProvider.GetString("LOCPlayAch_Progress_ScanRunningNote");

        public string WindowTitle => ResourceProvider.GetString("LOCPlayAch_Title_PluginName");

        public ICommand HideCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand ContinueCommand { get; }

        public ScanProgressWindowViewModel(AchievementManager achievementManager, ILogger logger)
        {
            _achievementManager = achievementManager ?? throw new ArgumentNullException(nameof(achievementManager));
            _logger = logger;

            HideCommand = new RelayCommand(_ => HideWindow());
            CancelCommand = new RelayCommand(_ => CancelScan(), _ => _achievementManager.IsRebuilding);
            ContinueCommand = new RelayCommand(_ => Continue());

            IsComplete = false;

            var lastReport = _achievementManager.GetLastRebuildProgress();
            if (lastReport != null)
            {
                ProgressPercent = CalculatePercent(lastReport);
                ProgressMessage = lastReport.Message ?? ResourceProvider.GetString("LOCPlayAch_Status_Starting");
            }
            else
            {
                ProgressPercent = 0;
                ProgressMessage = ResourceProvider.GetString("LOCPlayAch_Status_Starting");
            }
        }

        public void OnProgress(ProgressReport report)
        {
            if (report == null) return;

            ProgressPercent = CalculatePercent(report);
            ProgressMessage = report.Message ?? string.Empty;

            // Raise property change for IsScanning to update button visibility
            OnPropertyChanged(nameof(IsScanning));
            OnPropertyChanged(nameof(ShowInProgressButtons));
            OnPropertyChanged(nameof(ShowCompleteButtons));

            if (report.IsCanceled || (report.TotalSteps > 0 && report.CurrentStep >= report.TotalSteps))
            {
                IsComplete = true;
            }
            else if (ProgressPercent >= 100)
            {
                IsComplete = true;
            }
        }

        private double CalculatePercent(ProgressReport report)
        {
            if (report == null) return 0;

            var pct = report.PercentComplete;
            if ((pct <= 0 || double.IsNaN(pct)) && report.TotalSteps > 0)
            {
                pct = Math.Max(0, Math.Min(100, (report.CurrentStep * 100.0) / report.TotalSteps));
            }
            return pct;
        }

        private void HideWindow()
        {
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        private void CancelScan()
        {
            _achievementManager.CancelCurrentRebuild();
        }

        private void Continue()
        {
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler RequestClose;
    }
}
