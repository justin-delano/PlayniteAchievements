using System;
using System.Windows;
using System.Windows.Controls;
using PlayniteAchievements.Services.ThemeTransition;
using Playnite.SDK;

namespace PlayniteAchievements.Views.ThemeTransition
{
    /// <summary>
    /// Progress window for theme transition operations.
    /// </summary>
    public partial class ThemeTransitionProgressWindow : UserControl
    {
        private readonly ThemeTransitionProgressViewModel _viewModel;
        private readonly ILogger _logger;

        public ThemeTransitionProgressWindow(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _viewModel = new ThemeTransitionProgressViewModel(_logger);
            DataContext = _viewModel;

            InitializeComponent();

            _viewModel.RequestClose += (s, e) => RequestClose?.Invoke(this, EventArgs.Empty);
        }

        public string WindowTitle => _viewModel.WindowTitle;

        public event EventHandler RequestClose;

        /// <summary>
        /// Sets the result of the transition operation.
        /// </summary>
        public void SetResult(TransitionResult result)
        {
            _viewModel.SetResult(result);
        }

        /// <summary>
        /// Sets the current progress state.
        /// </summary>
        public void SetProgress(string message, double percent = 0, bool isIndeterminate = true)
        {
            _viewModel.SetProgress(message, percent, isIndeterminate);
        }

        /// <summary>
        /// Sets the details text shown below the progress bar.
        /// </summary>
        public void SetDetails(string details)
        {
            _viewModel.SetDetails(details);
        }
    }
}
