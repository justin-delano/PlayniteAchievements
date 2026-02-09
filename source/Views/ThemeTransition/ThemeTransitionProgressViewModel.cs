using System;
using System.Windows.Input;
using PlayniteAchievements.Common;
using PlayniteAchievements.Services.ThemeTransition;
using Playnite.SDK;
using CommonRelayCommand = PlayniteAchievements.Common.RelayCommand;

namespace PlayniteAchievements.Views.ThemeTransition
{
    /// <summary>
    /// View model for the theme transition progress window.
    /// </summary>
    public class ThemeTransitionProgressViewModel : ObservableObject
    {
        private readonly ILogger _logger;
        private string _progressMessage;
        private double _progressPercent;
        private string _progressPercentText;
        private bool _isIndeterminate;
        private string _detailsText;
        private bool _showCompleteButtons;

        public ThemeTransitionProgressViewModel(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _progressMessage = ResourceProvider.GetString("LOCPlayAch_ThemeTransition_Processing") ?? "Processing theme...";
            _progressPercent = 0;
            _progressPercentText = "0%";
            _isIndeterminate = true;
            _detailsText = string.Empty;
            _showCompleteButtons = false;

            ContinueCommand = new CommonRelayCommand(_ =>
            {
                _logger.Info("User clicked Continue on theme transition progress window.");
                RequestClose?.Invoke(this, EventArgs.Empty);
            });
        }

        public string WindowTitle => ResourceProvider.GetString("LOCPlayAch_ThemeTransition_Title") ?? "Theme Transition";

        public string ProgressMessage
        {
            get => _progressMessage;
            private set
            {
                _progressMessage = value;
                OnPropertyChanged();
            }
        }

        public double ProgressPercent
        {
            get => _progressPercent;
            private set
            {
                _progressPercent = value;
                OnPropertyChanged();
            }
        }

        public string ProgressPercentText
        {
            get => _progressPercentText;
            private set
            {
                _progressPercentText = value;
                OnPropertyChanged();
            }
        }

        public bool IsIndeterminate
        {
            get => _isIndeterminate;
            private set
            {
                _isIndeterminate = value;
                OnPropertyChanged();
            }
        }

        public bool ShowPercentage => !IsIndeterminate;

        public string DetailsText
        {
            get => _detailsText;
            private set
            {
                _detailsText = value;
                OnPropertyChanged();
            }
        }

        public bool ShowCompleteButtons
        {
            get => _showCompleteButtons;
            private set
            {
                _showCompleteButtons = value;
                OnPropertyChanged();
            }
        }

        public ICommand ContinueCommand { get; }

        public event EventHandler RequestClose;

        /// <summary>
        /// Sets the result of the transition operation and updates the UI.
        /// </summary>
        public void SetResult(TransitionResult result)
        {
            if (result.Success)
            {
                ProgressMessage = ResourceProvider.GetString("LOCPlayAch_ThemeTransition_Success") ?? "Theme transitioned successfully!";
                DetailsText = result.Message;
                ProgressPercent = 100;
                ProgressPercentText = "100%";
                IsIndeterminate = false;
                ShowCompleteButtons = true;
            }
            else
            {
                ProgressMessage = string.Format(
                    ResourceProvider.GetString("LOCPlayAch_ThemeTransition_Failed") ?? "Theme transition failed: {0}",
                    result.Message);
                DetailsText = result.Message;
                ProgressPercent = 0;
                ProgressPercentText = "0%";
                IsIndeterminate = false;
                ShowCompleteButtons = true;
            }

            OnPropertyChanged(nameof(ShowPercentage));
        }

        /// <summary>
        /// Sets the current progress state.
        /// </summary>
        public void SetProgress(string message, double percent = 0, bool isIndeterminate = true)
        {
            ProgressMessage = message;
            ProgressPercent = percent;
            ProgressPercentText = $"{percent:F0}%";
            IsIndeterminate = isIndeterminate;
            ShowCompleteButtons = false;

            OnPropertyChanged(nameof(ShowPercentage));
        }

        /// <summary>
        /// Sets the details text shown below the progress bar.
        /// </summary>
        public void SetDetails(string details)
        {
            DetailsText = details;
        }
    }
}
