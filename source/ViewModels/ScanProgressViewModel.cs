using System;
using System.Windows.Input;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Services;
using Playnite.SDK;
using RelayCommand = PlayniteAchievements.Common.RelayCommand;

namespace PlayniteAchievements.ViewModels
{
    public class ScanProgressViewModel : ObservableObject
    {
        private readonly AchievementManager _achievementManager;
        private readonly Guid? _singleGameScanId;
        private readonly Action<Guid> _openSingleGameAction;

        private double _progressPercent;
        private string _progressMessage;
        private bool _isComplete;
        private bool _completedSuccessfully;

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
                    OnPropertyChanged(nameof(ShowOpenSingleGameButton));
                }
            }
        }

        public bool ShowInProgressButtons => !IsComplete && IsScanning;
        public bool ShowCompleteButtons => IsComplete;
        public bool ShowOpenSingleGameButton => IsComplete &&
                                                _completedSuccessfully &&
                                                _singleGameScanId.HasValue &&
                                                _openSingleGameAction != null;

        public string ScanRunningNote => ResourceProvider.GetString("LOCPlayAch_Progress_ScanRunningNote");

        public string WindowTitle => ResourceProvider.GetString("LOCPlayAch_Title_Scan");

        public ICommand HideCommand { get; }
        public RelayCommand CancelCommand { get; }
        public ICommand ContinueCommand { get; }
        public RelayCommand OpenSingleGameCommand { get; }

        public ScanProgressViewModel(
            AchievementManager achievementManager,
            ILogger logger,
            Guid? singleGameScanId = null,
            Action<Guid> openSingleGameAction = null)
        {
            _achievementManager = achievementManager ?? throw new ArgumentNullException(nameof(achievementManager));
            _singleGameScanId = singleGameScanId;
            _openSingleGameAction = openSingleGameAction;

            HideCommand = new RelayCommand(_ => HideWindow());
            CancelCommand = new RelayCommand(_ => CancelScan(), _ => _achievementManager.IsRebuilding);
            ContinueCommand = new RelayCommand(_ => Continue());
            OpenSingleGameCommand = new RelayCommand(_ => OpenSingleGame(), _ => ShowOpenSingleGameButton);

            IsComplete = false;
            ApplyScanStatus(_achievementManager.GetScanStatusSnapshot());
        }

        public void OnProgress(ProgressReport report)
        {
            if (report == null) return;

            ApplyScanStatus(_achievementManager.GetScanStatusSnapshot(report));
        }

        private void ApplyScanStatus(ScanStatusSnapshot status)
        {
            if (status == null)
            {
                return;
            }

            ProgressPercent = status.ProgressPercent;
            ProgressMessage = status.Message ?? string.Empty;

            var completedSuccessfully = status.IsFinal && !status.IsCanceled;
            if (_completedSuccessfully != completedSuccessfully)
            {
                _completedSuccessfully = completedSuccessfully;
                OnPropertyChanged(nameof(ShowOpenSingleGameButton));
            }

            if (status.IsFinal || status.IsCanceled)
            {
                IsComplete = true;
            }
            else if (status.IsScanning)
            {
                IsComplete = false;
            }

            OnPropertyChanged(nameof(IsScanning));
            OnPropertyChanged(nameof(ShowInProgressButtons));
            OnPropertyChanged(nameof(ShowCompleteButtons));
            OnPropertyChanged(nameof(ShowOpenSingleGameButton));

            CancelCommand?.RaiseCanExecuteChanged();
            OpenSingleGameCommand?.RaiseCanExecuteChanged();
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

        private void OpenSingleGame()
        {
            if (!ShowOpenSingleGameButton || !_singleGameScanId.HasValue)
            {
                return;
            }

            RequestClose?.Invoke(this, EventArgs.Empty);
            _openSingleGameAction?.Invoke(_singleGameScanId.Value);
        }

        public event EventHandler RequestClose;
    }
}
