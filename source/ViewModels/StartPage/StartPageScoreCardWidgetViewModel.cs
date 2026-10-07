using System;
using Playnite.SDK;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Achievements.Scoring;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Overview;
using PlayniteAchievements.Services.StartPage;

namespace PlayniteAchievements.ViewModels.StartPage
{
    public sealed class StartPageScoreCardWidgetViewModel : StartPageWidgetViewModelBase
    {
        private readonly ScoreCardType _cardType;

        public StartPageScoreCardWidgetViewModel(
            StartPageWidgetKind widgetKind,
            StartPageDataCoordinator dataCoordinator,
            PlayniteAchievementsSettings settings,
            ILogger logger)
            : base(dataCoordinator, settings, logger)
        {
            _cardType = GetCardType(widgetKind);
            ScoreCard = new ScoreCardViewModel(_cardType);
        }

        public static ScoreCardType GetCardType(StartPageWidgetKind widgetKind)
        {
            switch (widgetKind)
            {
                case StartPageWidgetKind.CollectionScoreCard:
                    return ScoreCardType.Collection;
                case StartPageWidgetKind.PrestigeScoreCard:
                    return ScoreCardType.Prestige;
                case StartPageWidgetKind.GamerscoreScoreCard:
                    return ScoreCardType.Gamerscore;
                case StartPageWidgetKind.EpicXpScoreCard:
                    return ScoreCardType.EpicXp;
                case StartPageWidgetKind.RetroPointsScoreCard:
                    return ScoreCardType.RetroPoints;
                default:
                    throw new ArgumentOutOfRangeException(nameof(widgetKind));
            }
        }

        public ScoreCardViewModel ScoreCard { get; }

        protected override void ApplySnapshot(OverviewDataSnapshot snapshot)
        {
            ScoreCard.ApplyFor(_cardType, snapshot, PersistedSettings?.UseUniformRarityBadges ?? false);
        }

        protected override void OnPersistedSettingsChanged(string propertyName)
        {
            if (string.IsNullOrWhiteSpace(propertyName) ||
                RarityAppearanceHelper.IsAppearanceSettingPropertyName(propertyName))
            {
                ScoreCard.RefreshBadgeStyle(PersistedSettings?.UseUniformRarityBadges ?? false);
            }
        }

        protected override bool ShouldRefreshForPersistedSettingsChanged(string propertyName)
        {
            if (RarityAppearanceHelper.IsAppearanceSettingPropertyName(propertyName))
            {
                return false;
            }

            return base.ShouldRefreshForPersistedSettingsChanged(propertyName);
        }
    }
}
