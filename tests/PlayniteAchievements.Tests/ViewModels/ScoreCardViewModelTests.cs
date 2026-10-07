using System.Linq;
using System.Windows;
using System.Windows.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Achievements.Scoring;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.ViewModels;

namespace PlayniteAchievements.Tests.ViewModels
{
    [TestClass]
    public class ScoreCardViewModelTests
    {
        // Level 14, roughly half way through it: the fifth level of Bronze IV, so the segmented
        // bar has four solid cells, one part-filled and five empty.
        private const int MidRankScore = 5371;

        // Points in one full pass of the ladder; one more point is Mastery 1, Bronze V.
        private const int PassLength = 1040480;

        [TestMethod]
        public void Labels_UseCollectionAndPrestigeScoreText()
        {
            var collection = new ScoreCardViewModel(ScoreCardType.Collection);
            var prestige = new ScoreCardViewModel(ScoreCardType.Prestige);

            Assert.AreEqual("Collection Score", collection.Label);
            Assert.AreEqual("Prestige Score", prestige.Label);
        }

        [TestMethod]
        public void Apply_FormatsPointsTierAndLevel()
        {
            var card = new ScoreCardViewModel(ScoreCardType.Collection);

            card.Apply(12345, 42, 67, "Gold3", useUniformRarityBadges: false);

            Assert.AreEqual("12,345", card.ScoreText);
            Assert.AreEqual("12,345 pts", card.PointsText);
            Assert.AreEqual("Lv 42", card.LevelText);
            Assert.AreEqual("Gold III", card.TierText);
            Assert.AreEqual(67, card.LevelProgress);
        }

        [TestMethod]
        public void BadgeIconKey_TracksUniformRarityBadgeSetting()
        {
            var card = new ScoreCardViewModel(ScoreCardType.Prestige);

            card.Apply(12345, 42, 67, "Gold5", useUniformRarityBadges: false);
            Assert.AreEqual("ScoreBadgeGoldPentagon", card.BadgeIconKey);

            card.RefreshBadgeStyle(useUniformRarityBadges: true);
            Assert.AreEqual("ScoreBadgeGoldHexagon", card.BadgeIconKey);
        }

        [TestMethod]
        public void CaptionText_PairsTheLevelWithWhatTheBarIsFilling()
        {
            const int score = 315;
            var snapshot = AchievementLevelCalculator.CalculateModern(score);
            var card = new ScoreCardViewModel(ScoreCardType.Collection);

            card.ApplyFromScore(score, useUniformRarityBadges: false);

            StringAssert.Contains(card.CaptionText, card.LevelText);
            StringAssert.Contains(card.CaptionText, card.PointsUntilNextLevelText);
            StringAssert.Contains(
                card.PointsUntilNextLevelText,
                snapshot.PointsUntilNextLevel.ToString("N0"));
            StringAssert.Contains(
                card.PointsUntilNextLevelText,
                $"Lv {snapshot.DisplayLevel + 1}");
        }

        [TestMethod]
        public void TooltipText_StatesTheLadderRatherThanRepeatingTheCard()
        {
            var snapshot = AchievementLevelCalculator.CalculateModern(MidRankScore);
            var card = new ScoreCardViewModel(ScoreCardType.Collection);

            card.ApplyFromScore(MidRankScore, useUniformRarityBadges: false);

            Assert.AreEqual("Level", card.TooltipLevelLabel);
            Assert.AreEqual($"{snapshot.DisplayLevel}/250", card.TooltipLevelValueText);
            Assert.AreEqual("Level in tier", card.TooltipRankPositionLabel);
            Assert.AreEqual(
                $"{snapshot.LevelsCompletedInRank + 1}/{snapshot.LevelsInRank}",
                card.TooltipRankPositionValueText);
            Assert.AreEqual(
                AchievementRankPresentation.FormatRank(snapshot.NextRank),
                card.TooltipNextRankLabel);
            StringAssert.Contains(
                card.TooltipNextRankValueText,
                snapshot.PointsUntilNextRank.ToString("N0"));
        }

        [TestMethod]
        public void CompactTierText_FoldsTheShortLabelIntoTheTier()
        {
            var collection = new ScoreCardViewModel(ScoreCardType.Collection);
            var prestige = new ScoreCardViewModel(ScoreCardType.Prestige);

            collection.Apply(12345, 42, 67, "Gold3", useUniformRarityBadges: false);
            prestige.Apply(12345, 42, 67, "Silver2", useUniformRarityBadges: false);

            Assert.AreEqual("Collection · Gold III", collection.CompactTierText);
            Assert.AreEqual("Prestige · Silver II", prestige.CompactTierText);
        }

        [TestMethod]
        public void TooltipNextLevel_StatesTheLevelTheBarIsFillingToward()
        {
            var snapshot = AchievementLevelCalculator.CalculateModern(MidRankScore);
            var card = new ScoreCardViewModel(ScoreCardType.Collection);

            card.ApplyFromScore(MidRankScore, useUniformRarityBadges: false);

            Assert.IsTrue(card.HasNextLevel);
            Assert.AreEqual($"Lv {snapshot.DisplayLevel + 1}", card.TooltipNextLevelLabel);
            Assert.AreEqual(
                snapshot.PointsUntilNextLevel.ToString("N0") + " pts",
                card.TooltipNextLevelValueText);
        }

        [TestMethod]
        public void Mastery_IsAbsentBeforeTheFirstPassCompletes()
        {
            var card = new ScoreCardViewModel(ScoreCardType.Prestige);

            card.ApplyFromScore(PassLength, useUniformRarityBadges: false);

            Assert.AreEqual(0, card.Mastery);
            Assert.IsFalse(card.HasMastery);
            Assert.AreEqual(card.TierText, card.TooltipTitleText);
            Assert.AreEqual("249/250", card.TooltipLevelValueText);
            // The rank after Master I opens the next pass, so it is named with that mastery.
            Assert.AreEqual("Bronze V · Mastery 1", card.TooltipNextRankLabel);
        }

        [TestMethod]
        public void Mastery_RestartsTheTierAndKeepsTheLevelAndScore()
        {
            var score = (3 * PassLength) + 2801;
            var snapshot = AchievementLevelCalculator.CalculateModern(score);
            var card = new ScoreCardViewModel(ScoreCardType.Collection);

            card.ApplyFromScore(score, useUniformRarityBadges: false);

            Assert.AreEqual(3, card.Mastery);
            Assert.IsTrue(card.HasMastery);
            Assert.AreEqual("Mastery 3", card.MasteryText);
            Assert.AreEqual("Bronze IV", card.TierText);
            Assert.AreEqual(760, card.Level);
            Assert.AreEqual("Lv 760", card.LevelText);
            Assert.AreEqual(score.ToString("N0") + " pts", card.PointsText);
            Assert.AreEqual("10/250", card.TooltipLevelValueText);
            StringAssert.Contains(card.TooltipTitleText, "Mastery 3");
            StringAssert.Contains(card.PointsUntilNextLevelText, "Lv 761");
            Assert.AreEqual(snapshot.Mastery, card.Mastery);
        }

        [TestMethod]
        public void MasteryText_CountsEveryPass()
        {
            var card = new ScoreCardViewModel(ScoreCardType.Collection);

            card.ApplyFromScore(AchievementLevelCalculator.GetScoreForLevel(150 * 250), useUniformRarityBadges: false);

            Assert.AreEqual(150, card.Mastery);
            Assert.AreEqual("Mastery 150", card.MasteryText);
        }

        [TestMethod]
        public void Segments_DrawOneCellPerLevelOfTheCurrentRank()
        {
            var snapshot = AchievementLevelCalculator.CalculateModern(MidRankScore);
            var card = new ScoreCardViewModel(ScoreCardType.Collection);

            card.ApplyFromScore(MidRankScore, useUniformRarityBadges: false);

            // Pin the shipped curve: ten levels per rank, and this score sits five levels in.
            Assert.AreEqual(10, snapshot.LevelsInRank);
            Assert.AreEqual(4, snapshot.LevelsCompletedInRank);
            Assert.AreEqual(snapshot.LevelsInRank, card.Segments.Count);

            var completedFill = card.Segments[0].Fill;
            Assert.AreEqual(SolidColor(card.AccentBrush), GlossColor(completedFill));
            for (var i = 0; i < snapshot.LevelsCompletedInRank; i++)
            {
                Assert.AreSame(completedFill, card.Segments[i].Fill, $"segment {i}");
            }

            Assert.IsInstanceOfType(
                card.Segments[snapshot.LevelsCompletedInRank].Fill,
                typeof(DrawingBrush));

            for (var i = snapshot.LevelsCompletedInRank + 1; i < card.Segments.Count; i++)
            {
                Assert.AreSame(card.AccentTrackBrush, card.Segments[i].Fill, $"segment {i}");
            }
        }

        [TestMethod]
        public void Segments_StartEmptyOnTheFirstRankOfAMastery()
        {
            var card = new ScoreCardViewModel(ScoreCardType.Prestige);

            card.ApplyFromScore(PassLength + 1, useUniformRarityBadges: false);

            Assert.AreEqual(10, card.Segments.Count);
            Assert.IsTrue(card.Segments.All(segment => ReferenceEquals(segment.Fill, card.AccentTrackBrush)));
        }

        [TestMethod]
        public void Segments_AreEmptyOnAFreshRank()
        {
            // 2801 is the first point of Bronze IV: a rank just entered, nothing filled yet.
            var card = new ScoreCardViewModel(ScoreCardType.Collection);

            card.ApplyFromScore(2801, useUniformRarityBadges: false);

            Assert.AreEqual(10, card.Segments.Count);
            Assert.IsTrue(card.Segments.All(segment => ReferenceEquals(segment.Fill, card.AccentTrackBrush)));
        }

        [TestMethod]
        public void Accents_FollowTheRarityColorSettings()
        {
            ApplyColors(new RarityColorSettings { Rare = "#123456" });
            try
            {
                var card = new ScoreCardViewModel(ScoreCardType.Collection);

                card.Apply(12345, 42, 67, "Gold3", useUniformRarityBadges: false);

                Assert.AreEqual(Color.FromRgb(0x12, 0x34, 0x56), SolidColor(card.AccentBrush));
                Assert.AreEqual(Color.FromArgb(0x3A, 0x12, 0x34, 0x56), SolidColor(card.AccentTrackBrush));
                Assert.AreEqual(Color.FromArgb(0x24, 0x12, 0x34, 0x56), SolidColor(card.AccentBackgroundBrush));
            }
            finally
            {
                ApplyColors(null);
            }
        }

        [TestMethod]
        public void Accents_OfAMasterRankSweepTheCompletedColors()
        {
            ApplyColors(new RarityColorSettings { CompletedStart = "#FF0000", CompletedEnd = "#0000FF" });
            try
            {
                var card = new ScoreCardViewModel(ScoreCardType.Collection);

                card.ApplyFromScore(PassLength, useUniformRarityBadges: false);

                Assert.AreEqual("ScoreBadgeCompletedGame", card.BadgeIconKey);
                var first = GlossColor(card.Segments[0].Fill);
                var beforeLast = GlossColor(card.Segments[card.Segments.Count - 2].Fill);
                Assert.IsTrue(first.R > first.B, "first segment leans to the start color");
                Assert.IsTrue(beforeLast.B > beforeLast.R, "late segments lean to the end color");
            }
            finally
            {
                ApplyColors(null);
            }
        }

        [TestMethod]
        public void RefreshBadgeStyle_RaisesTheBadgeAndAccentsWhenOnlyColorsChanged()
        {
            var card = new ScoreCardViewModel(ScoreCardType.Collection);
            card.Apply(12345, 42, 67, "Gold3", useUniformRarityBadges: false);
            var raised = new System.Collections.Generic.List<string>();
            card.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            card.RefreshBadgeStyle(useUniformRarityBadges: false);

            CollectionAssert.Contains(raised, nameof(ScoreCardViewModel.BadgeIconKey));
            CollectionAssert.Contains(raised, nameof(ScoreCardViewModel.AccentBrush));
        }

        private static void ApplyColors(RarityColorSettings colors)
        {
            var settings = colors == null ? null : new PersistedSettings { RarityColors = colors };
            RarityAppearanceHelper.ApplyBadgeApplicationResources(new ResourceDictionary(), settings);
        }

        private static Color SolidColor(Brush brush)
        {
            Assert.IsInstanceOfType(brush, typeof(SolidColorBrush));
            return ((SolidColorBrush)brush).Color;
        }

        /// <summary>The accent a glossed segment is built around, at the middle of its gradient.</summary>
        private static Color GlossColor(Brush brush)
        {
            Assert.IsInstanceOfType(brush, typeof(LinearGradientBrush));
            return ((LinearGradientBrush)brush).GradientStops.Single(stop => stop.Offset == 0.55).Color;
        }
    }
}
