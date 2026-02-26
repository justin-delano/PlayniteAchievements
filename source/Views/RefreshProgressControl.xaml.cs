using System;
using System.Windows;
using System.Windows.Controls;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Services;
using PlayniteAchievements.ViewModels;
using Playnite.SDK;

namespace PlayniteAchievements.Views
{
    public partial class RefreshProgressControl : UserControl
    {
        private readonly RefreshProgressViewModel _viewModel;
        private readonly AchievementService _achievementService;
        private readonly ILogger _logger;

        public RefreshProgressControl(
            AchievementService achievementService,
            ILogger logger,
            Guid? singleGameRefreshId = null,
            Action<Guid> openSingleGameAction = null)
        {
            _achievementService = achievementService ?? throw new ArgumentNullException(nameof(achievementService));
            _logger = logger;

            _viewModel = new RefreshProgressViewModel(
                achievementService,
                logger,
                singleGameRefreshId,
                openSingleGameAction);
            DataContext = _viewModel;

            InitializeComponent();

            _viewModel.RequestClose += (s, e) => RequestClose?.Invoke(this, EventArgs.Empty);

            _achievementService.RebuildProgress += OnRebuildProgress;
            Unloaded += (s, e) => _achievementService.RebuildProgress -= OnRebuildProgress;
        }

        public string WindowTitle => _viewModel.WindowTitle;

        public event EventHandler RequestClose;

        private void OnRebuildProgress(object sender, ProgressReport report)
        {
            Dispatcher?.InvokeIfNeeded(() =>
            {
                try
                {
                    _viewModel.OnProgress(report);
                }
                catch (Exception ex)
                {
                    _logger?.Debug($"Progress UI update error: {ex.Message}");
                }
            });
        }
    }
}



