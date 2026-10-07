using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.ViewModels.Showcase.Widgets;

namespace PlayniteAchievements.Models.Tests
{
    [TestClass]
    public class CompletionFrameOptionTests
    {
        private static readonly string[] GameSummarySurfaces =
        {
            GridOptionKeys.GameSummaries.Overview,
            GridOptionKeys.GameSummaries.StartPage,
            GridOptionKeys.GameSummaries.ViewAchievements,
            GridOptionKeys.GameSummaries.FriendsOverview,
            GridOptionKeys.GameSummaries.FriendsOverviewSelectedFriend,
            GridOptionKeys.GameSummaries.ViewFriendsAchievements,
            GridOptionKeys.GameSummaries.ViewFriendsAchievementsSelectedFriend,
            GridOptionKeys.GameSummaries.DesktopTheme,
            ShowcaseGridSurfaces.ForInstance(ShowcaseGridSurfaces.GameSummaries, "instance-1")
        };

        [TestMethod]
        public void ShowCompletionFrame_DefaultsOffOnEveryGameSummarySurface()
        {
            var settings = new PersistedSettings();

            foreach (var surface in GameSummarySurfaces)
            {
                Assert.IsFalse(
                    settings.GridOptions.GetGameSummaries(surface).ShowCompletionFrame,
                    surface);
            }

            Assert.IsFalse(new GameSummaryGridOptions().ShowCompletionFrame);
        }

        [TestMethod]
        public void ShowCompletionFrame_IsCopiedByClone()
        {
            var options = new GameSummaryGridOptions { ShowCompletionFrame = true };

            Assert.IsTrue(options.Clone().ShowCompletionFrame);
        }

        [TestMethod]
        public void ShowCompletionFrame_SurvivesSettingsCloneAndCopyFrom()
        {
            var source = new PersistedSettings();
            source.GridOptions.GetGameSummaries(GridOptionKeys.GameSummaries.FriendsOverview).ShowCompletionFrame = true;

            var clone = source.Clone();
            var copy = new PersistedSettings();
            copy.CopyFrom(source);

            Assert.IsTrue(clone.FriendsOverviewGameSummariesShowCompletionFrame);
            Assert.IsTrue(copy.FriendsOverviewGameSummariesShowCompletionFrame);
            Assert.IsFalse(copy.OverviewGameSummariesShowCompletionFrame);
        }

        [TestMethod]
        public void ShowCompletionFrame_SeedsShowcaseSurfaceFromDonor()
        {
            var settings = new PersistedSettings();
            settings.GridOptions.GetGameSummaries(GridOptionKeys.GameSummaries.StartPage).ShowCompletionFrame = true;
            var surface = ShowcaseGridSurfaces.ForInstance(ShowcaseGridSurfaces.GameSummaries, "seeded");

            settings.GridOptions.SeedGameSummariesFrom(surface, GridOptionKeys.GameSummaries.StartPage);

            Assert.IsTrue(settings.GridOptions.GetGameSummaries(surface).ShowCompletionFrame);
        }

        [TestMethod]
        public void ShowCompletionFrame_EditOnEachFixedSurfaceRaisesItsFlatName()
        {
            var expected = new Dictionary<string, string>
            {
                [GridOptionKeys.GameSummaries.Overview] = nameof(PersistedSettings.OverviewGameSummariesShowCompletionFrame),
                [GridOptionKeys.GameSummaries.StartPage] = nameof(PersistedSettings.StartPageGameSummariesShowCompletionFrame),
                [GridOptionKeys.GameSummaries.ViewAchievements] = nameof(PersistedSettings.ViewAchievementsGameSummariesShowCompletionFrame),
                [GridOptionKeys.GameSummaries.FriendsOverview] = nameof(PersistedSettings.FriendsOverviewGameSummariesShowCompletionFrame),
                [GridOptionKeys.GameSummaries.FriendsOverviewSelectedFriend] = nameof(PersistedSettings.FriendsOverviewSelectedFriendGameSummariesShowCompletionFrame),
                [GridOptionKeys.GameSummaries.ViewFriendsAchievements] = nameof(PersistedSettings.ViewFriendsAchievementsGameSummariesShowCompletionFrame),
                [GridOptionKeys.GameSummaries.ViewFriendsAchievementsSelectedFriend] = nameof(PersistedSettings.ViewFriendsAchievementsSelectedFriendGameSummariesShowCompletionFrame),
                [GridOptionKeys.GameSummaries.DesktopTheme] = nameof(PersistedSettings.DesktopThemeGameSummariesShowCompletionFrame)
            };

            foreach (var pair in expected)
            {
                var settings = new PersistedSettings();
                var raised = new List<string>();
                settings.PropertyChanged += (sender, e) => raised.Add(e.PropertyName);

                settings.GridOptions.GetGameSummaries(pair.Key).ShowCompletionFrame = true;

                CollectionAssert.Contains(raised, pair.Value, pair.Key);
            }
        }

        [TestMethod]
        public void GameMosaicTileLayout_EqualityIncludesShowCompletionFrame()
        {
            var off = CreateLayout(showCompletionFrame: false);

            Assert.AreEqual(off, CreateLayout(showCompletionFrame: false));
            Assert.AreNotEqual(off, CreateLayout(showCompletionFrame: true));
            Assert.IsTrue(CreateLayout(showCompletionFrame: true).Equals(CreateLayout(showCompletionFrame: true)));
        }

        private static GameMosaicTileLayout CreateLayout(bool showCompletionFrame) =>
            new GameMosaicTileLayout(
                pinnable: false,
                pinCollectionId: null,
                coverWidth: 56,
                coverHeight: 78,
                decodePixel: 156,
                useCovers: true,
                showCompletionGlow: true,
                spacing: 6,
                showRarityBar: false,
                showCompletionFrame: showCompletionFrame);
    }
}
