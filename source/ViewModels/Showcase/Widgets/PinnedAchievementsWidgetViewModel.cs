using System;
using System.Linq;
using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Showcase;
using RelayCommand = PlayniteAchievements.Common.RelayCommand;

namespace PlayniteAchievements.ViewModels.Showcase.Widgets
{
    /// <summary>
    /// A single pinned-achievement row: icon, name, and (outside compact) game name, with a
    /// reorder/unpin context menu. Missing pins render dimmed with remembered or "unavailable"
    /// text. Mutations persist and raise the configuration-changed event, which rebuilds the
    /// dashboard and re-materializes the rows.
    /// </summary>
    public sealed class PinnedAchievementRowViewModel
    {
        private readonly PinnedAchievementReference _pin;

        public PinnedAchievementRowViewModel(ShowcaseAchievementItem item, bool showGameName)
        {
            _pin = item.Pin;
            IconPath = item.IconPath;
            IsMissing = item.IsMissing;
            IconOpacity = item.IsMissing ? 0.35 : 1.0;
            ShowGameName = showGameName;
            DisplayName = item.IsMissing && string.IsNullOrWhiteSpace(_pin?.LastKnownAchievementName)
                ? ResourceProvider.GetString("LOCPlayAch_Showcase_UnavailableAchievement")
                : item.Name;
            GameName = item.IsMissing && string.IsNullOrWhiteSpace(_pin?.LastKnownGameName)
                ? ResourceProvider.GetString("LOCPlayAch_Showcase_UnavailableGame")
                : item.GameName;

            MoveEarlierCommand = new RelayCommand(_ => Move(-1));
            MoveLaterCommand = new RelayCommand(_ => Move(1));
            UnpinCommand = new RelayCommand(_ => Unpin());
        }

        public string IconPath { get; }

        public bool IsMissing { get; }

        public double IconOpacity { get; }

        public string DisplayName { get; }

        public string GameName { get; }

        public bool ShowGameName { get; }

        public RelayCommand MoveEarlierCommand { get; }

        public RelayCommand MoveLaterCommand { get; }

        public RelayCommand UnpinCommand { get; }

        private static ShowcaseSettings Settings =>
            PlayniteAchievementsPlugin.Instance?.Settings?.Persisted?.Showcase;

        private void Move(int direction)
        {
            if (_pin != null &&
                ShowcasePinService.MoveAchievement(Settings, _pin.GameId, _pin.ApiName, direction))
            {
                ShowcaseConfigurationCommit.Commit();
            }
        }

        private void Unpin()
        {
            if (_pin == null)
            {
                return;
            }

            ShowcasePinService.ToggleAchievement(
                Settings,
                _pin.GameId,
                _pin.ApiName,
                _pin.LastKnownGameName,
                _pin.LastKnownAchievementName);
            ShowcaseConfigurationCommit.Commit();
        }
    }

    /// <summary>Backs the PinnedAchievements widget: pinned rows capped to 2 when compact.</summary>
    public sealed class PinnedAchievementsWidgetViewModel : ShowcaseWidgetViewModelBase
    {
        public BulkObservableCollection<PinnedAchievementRowViewModel> Items { get; } =
            new BulkObservableCollection<PinnedAchievementRowViewModel>();

        protected override void Refresh()
        {
            var achievements = Projection?.Achievements ?? Array.Empty<ShowcaseAchievementItem>();
            var limit = Density == WidgetViewportDensity.Compact ? 2 : achievements.Count;
            var showGameName = Density != WidgetViewportDensity.Compact;
            Items.ReplaceAll(achievements
                .Take(limit)
                .Select(item => new PinnedAchievementRowViewModel(item, showGameName)));
        }
    }
}
