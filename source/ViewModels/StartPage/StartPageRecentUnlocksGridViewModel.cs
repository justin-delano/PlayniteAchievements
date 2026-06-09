using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Sidebar;
using PlayniteAchievements.Services.StartPage;

namespace PlayniteAchievements.ViewModels.StartPage
{
    public sealed class StartPageRecentUnlocksGridViewModel : StartPageWidgetViewModelBase
    {
        public StartPageRecentUnlocksGridViewModel(
            StartPageDataCoordinator dataCoordinator,
            PlayniteAchievementsSettings settings,
            ILogger logger)
            : base(dataCoordinator, settings, logger)
        {
        }

        public BulkObservableCollection<AchievementDisplayItem> Items { get; } =
            new BulkObservableCollection<AchievementDisplayItem>();

        public bool UseCoverImages => PersistedSettings?.UseCoverImages ?? true;

        public bool ShowColumnHeaders => PersistedSettings?.ShowAchievementGridColumnHeaders ?? true;

        protected override void ApplySnapshot(SidebarDataSnapshot snapshot)
        {
            Items.ReplaceAll(StartPageWidgetProjection.ProjectRecentUnlocks(
                snapshot?.RecentAchievements,
                PersistedSettings));
            OnPropertyChanged(nameof(UseCoverImages));
            OnPropertyChanged(nameof(ShowColumnHeaders));
        }

        protected override void OnPersistedSettingsChanged(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName) ||
                propertyName == nameof(PersistedSettings.UseCoverImages))
            {
                OnPropertyChanged(nameof(UseCoverImages));
            }

            if (string.IsNullOrEmpty(propertyName) ||
                propertyName == nameof(PersistedSettings.ShowAchievementGridColumnHeaders))
            {
                OnPropertyChanged(nameof(ShowColumnHeaders));
            }
        }
    }
}
