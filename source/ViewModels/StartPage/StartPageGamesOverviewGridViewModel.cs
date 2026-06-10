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

        private StartPageGamesOverviewGridSettings WidgetSettings =>
            PersistedSettings?.StartPageGamesOverviewGrid ?? new StartPageGamesOverviewGridSettings();

        public bool ShowGameMetadata => WidgetSettings.ShowGameMetadata;

        public bool UseCoverImages => WidgetSettings.UseCoverImages;

        public bool EnableCompactGridMode => WidgetSettings.EnableCompactGridMode;

        public bool ShowCompletionBorder => WidgetSettings.ShowCompletionBorder;

        public bool ShowColumnHeaders => WidgetSettings.ShowColumnHeaders;

        public double? RowHeight => WidgetSettings.RowHeight;

        protected override void ApplySnapshot(SidebarDataSnapshot snapshot)
        {
            Items.ReplaceAll(StartPageWidgetProjection.ProjectGamesOverview(
                snapshot?.GamesOverview,
                PersistedSettings));
            OnPropertyChanged(nameof(ShowGameMetadata));
            OnPropertyChanged(nameof(UseCoverImages));
            OnPropertyChanged(nameof(EnableCompactGridMode));
            OnPropertyChanged(nameof(ShowCompletionBorder));
            OnPropertyChanged(nameof(ShowColumnHeaders));
            OnPropertyChanged(nameof(RowHeight));
        }

        protected override void OnPersistedSettingsChanged(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName) ||
                IsWidgetSettingsProperty(propertyName, nameof(StartPageGamesOverviewGridSettings.ShowGameMetadata)))
            {
                OnPropertyChanged(nameof(ShowGameMetadata));
            }

            if (string.IsNullOrEmpty(propertyName) ||
                IsWidgetSettingsProperty(propertyName, nameof(StartPageGamesOverviewGridSettings.UseCoverImages)))
            {
                OnPropertyChanged(nameof(UseCoverImages));
            }

            if (string.IsNullOrEmpty(propertyName) ||
                IsWidgetSettingsProperty(propertyName, nameof(StartPageGamesOverviewGridSettings.EnableCompactGridMode)))
            {
                OnPropertyChanged(nameof(EnableCompactGridMode));
            }

            if (string.IsNullOrEmpty(propertyName) ||
                IsWidgetSettingsProperty(propertyName, nameof(StartPageGamesOverviewGridSettings.ShowCompletionBorder)))
            {
                OnPropertyChanged(nameof(ShowCompletionBorder));
            }

            if (string.IsNullOrEmpty(propertyName) ||
                IsWidgetSettingsProperty(propertyName, nameof(StartPageGamesOverviewGridSettings.ShowColumnHeaders)))
            {
                OnPropertyChanged(nameof(ShowColumnHeaders));
            }

            if (string.IsNullOrEmpty(propertyName) ||
                IsWidgetSettingsProperty(propertyName, nameof(StartPageGamesOverviewGridSettings.RowHeight)) ||
                propertyName == nameof(PersistedSettings.StartPageGamesOverviewGridRowHeight))
            {
                OnPropertyChanged(nameof(RowHeight));
            }
        }

        protected override bool ShouldRefreshForPersistedSettingsChanged(string propertyName)
        {
            if (IsWidgetSettingsProperty(propertyName, nameof(StartPageGamesOverviewGridSettings.SortMode)) ||
                IsWidgetSettingsProperty(propertyName, nameof(StartPageGamesOverviewGridSettings.SortDescending)) ||
                IsWidgetSettingsProperty(propertyName, nameof(StartPageGamesOverviewGridSettings.MaxRows)) ||
                propertyName == nameof(PersistedSettings.StartPageGamesOverviewGridMaxRows))
            {
                return true;
            }

            if (IsWidgetSettingsProperty(propertyName))
            {
                return false;
            }

            return base.ShouldRefreshForPersistedSettingsChanged(propertyName);
        }

        private static bool IsWidgetSettingsProperty(string propertyName, string childPropertyName = null)
        {
            if (string.IsNullOrEmpty(propertyName))
            {
                return true;
            }

            const string prefix = nameof(PersistedSettings.StartPageGamesOverviewGrid) + ".";
            if (!propertyName.StartsWith(prefix))
            {
                return propertyName == nameof(PersistedSettings.StartPageGamesOverviewGrid);
            }

            return string.IsNullOrEmpty(childPropertyName) ||
                   string.Equals(
                       propertyName.Substring(prefix.Length),
                       childPropertyName,
                       System.StringComparison.Ordinal);
        }
    }
}
