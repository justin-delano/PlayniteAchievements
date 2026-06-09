using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Sidebar;
using PlayniteAchievements.Services.StartPage;

namespace PlayniteAchievements.ViewModels.StartPage
{
    public sealed class StartPageGamesOverviewGridViewModel : StartPageWidgetViewModelBase
    {
        public StartPageGamesOverviewGridViewModel(
            StartPageDataCoordinator dataCoordinator,
            PlayniteAchievementsSettings settings,
            ILogger logger)
            : base(dataCoordinator, settings, logger)
        {
        }

        public BulkObservableCollection<GameOverviewItem> Items { get; } =
            new BulkObservableCollection<GameOverviewItem>();

        public bool ShowGameMetadata => PersistedSettings?.ShowSidebarGameMetadata ?? true;

        public bool UseCoverImages => PersistedSettings?.UseCoverImages ?? true;

        protected override void ApplySnapshot(SidebarDataSnapshot snapshot)
        {
            Items.ReplaceAll(StartPageWidgetProjection.ProjectGamesOverview(
                snapshot?.GamesOverview,
                PersistedSettings));
            OnPropertyChanged(nameof(ShowGameMetadata));
            OnPropertyChanged(nameof(UseCoverImages));
        }

        protected override void OnPersistedSettingsChanged(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName) ||
                propertyName == nameof(PersistedSettings.ShowSidebarGameMetadata))
            {
                OnPropertyChanged(nameof(ShowGameMetadata));
            }

            if (string.IsNullOrEmpty(propertyName) ||
                propertyName == nameof(PersistedSettings.UseCoverImages))
            {
                OnPropertyChanged(nameof(UseCoverImages));
            }
        }
    }
}
