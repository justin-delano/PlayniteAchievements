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
    public partial class ScanProgressWindow : UserControl
    {
        private readonly ScanProgressWindowViewModel _viewModel;
        private readonly AchievementManager _achievementManager;
        private readonly ILogger _logger;

        public ScanProgressWindow(AchievementManager achievementManager, ILogger logger)
        {
            _achievementManager = achievementManager ?? throw new ArgumentNullException(nameof(achievementManager));
            _logger = logger;

            _viewModel = new ScanProgressWindowViewModel(achievementManager, logger);
            DataContext = _viewModel;

            InitializeComponent();

            _viewModel.RequestClose += (s, e) => RequestClose?.Invoke(this, EventArgs.Empty);

            _achievementManager.RebuildProgress += OnRebuildProgress;
            Unloaded += (s, e) => _achievementManager.RebuildProgress -= OnRebuildProgress;
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
