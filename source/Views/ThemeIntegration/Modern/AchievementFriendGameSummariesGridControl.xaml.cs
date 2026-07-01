using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Models.ThemeIntegration;
using PlayniteAchievements.Services.Friends;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.Views.ThemeIntegration.Base;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace PlayniteAchievements.Views.ThemeIntegration.Modern
{
    public partial class AchievementFriendGameSummariesGridControl : ThemeControlBase
    {
        public static readonly DependencyProperty DisplayItemsProperty =
            DependencyProperty.Register(
                nameof(DisplayItems),
                typeof(IEnumerable<GameSummaryItem>),
                typeof(AchievementFriendGameSummariesGridControl),
                new PropertyMetadata(null));

        public IEnumerable<GameSummaryItem> DisplayItems
        {
            get => (IEnumerable<GameSummaryItem>)GetValue(DisplayItemsProperty);
            private set => SetValue(DisplayItemsProperty, value);
        }

        public static readonly DependencyProperty ColumnSettingsKeyProperty =
            DependencyProperty.Register(
                nameof(ColumnSettingsKey),
                typeof(string),
                typeof(AchievementFriendGameSummariesGridControl),
                new PropertyMetadata("FriendsOverviewGameSummaries"));

        public string ColumnSettingsKey
        {
            get => (string)GetValue(ColumnSettingsKeyProperty);
            private set => SetValue(ColumnSettingsKeyProperty, value);
        }

        public AchievementFriendGameSummariesGridControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        protected override bool EnableAutomaticThemeDataUpdates => true;
        protected override bool UsesThemeBindings => true;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            LoadData();
        }

        protected override void OnThemeDataUpdated()
        {
            LoadData();
        }

        protected override bool ShouldHandleThemeDataChange(string propertyName)
        {
            return string.IsNullOrWhiteSpace(propertyName) ||
                   propertyName == nameof(ModernThemeBindings.DynamicFriendGameSummaries) ||
                   propertyName == nameof(ModernThemeBindings.DynamicFriendScopeUserKey);
        }

        protected override bool ShouldHandleSettingsDataChange(string propertyName)
        {
            return propertyName == nameof(PersistedSettings.ShowFriendsOverviewGameSummariesGridColumnHeaders) ||
                   propertyName == nameof(PersistedSettings.FriendsOverviewGameSummariesGridRowHeight);
        }

        private void LoadData()
        {
            var theme = EffectiveTheme;
            DisplayItems = (theme?.DynamicFriendGameSummaryRows ?? new System.Collections.ObjectModel.ObservableCollection<FriendGameSummaryItem>())
                .Cast<GameSummaryItem>()
                .ToList();
            ColumnSettingsKey = FriendOverviewProjection.IsAllScope(theme?.DynamicFriendScopeUserKey)
                ? "FriendsOverviewGameSummaries"
                : "FriendsOverviewSelectedFriendGameSummaries";
        }
    }
}
