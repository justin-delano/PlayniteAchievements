using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Providers.RetroAchievements;
using PlayniteAchievements.Providers.RetroAchievements.Models;
using System.Collections.Generic;

namespace PlayniteAchievements.Tests.Providers
{
    [TestClass]
    public class RetroAchievementsSubsetImagePlannerTests
    {
        private static IReadOnlyList<(string Label, string IconUrl, string CoverUrl)> Plan(
            params (string CategoryLabel, RaGameInfoUserProgress Info)[] subsets)
        {
            return RetroAchievementsSubsetImagePlanner.BuildSubsetImagePlan(subsets);
        }

        [TestMethod]
        public void BuildSubsetImagePlan_NormalizesRelativeImagePathsToAbsoluteUrls()
        {
            var plan = Plan(("Bonus", new RaGameInfoUserProgress
            {
                ImageIcon = "/Images/085573.png",
                ImageBoxArt = "/Images/051007.png"
            }));

            Assert.AreEqual(1, plan.Count);
            Assert.AreEqual("Bonus", plan[0].Label);
            Assert.AreEqual("https://retroachievements.org/Images/085573.png", plan[0].IconUrl);
            Assert.AreEqual("https://retroachievements.org/Images/051007.png", plan[0].CoverUrl);
        }

        [TestMethod]
        public void BuildSubsetImagePlan_SkipsSubsetsWithoutIcon()
        {
            var plan = Plan(
                ("Bonus", new RaGameInfoUserProgress { ImageBoxArt = "/Images/051007.png" }),
                ("Multi", new RaGameInfoUserProgress { ImageIcon = "/Images/000001.png" }),
                ("Professor Oak Challenge", null));

            Assert.AreEqual(1, plan.Count);
            Assert.AreEqual("Multi", plan[0].Label);
        }

        [TestMethod]
        public void BuildSubsetImagePlan_LeavesCoverNullWhenBoxArtMissing()
        {
            var plan = Plan(("Bonus", new RaGameInfoUserProgress { ImageIcon = "/Images/085573.png" }));

            Assert.AreEqual(1, plan.Count);
            Assert.IsNull(plan[0].CoverUrl);
        }

        [TestMethod]
        public void BuildSubsetImagePlan_DedupesLabelsFirstWinsIgnoringCase()
        {
            var plan = Plan(
                ("Bonus", new RaGameInfoUserProgress { ImageIcon = "/Images/1.png" }),
                ("bonus", new RaGameInfoUserProgress { ImageIcon = "/Images/2.png" }),
                ("Multi", new RaGameInfoUserProgress { ImageIcon = "/Images/3.png" }));

            Assert.AreEqual(2, plan.Count);
            Assert.AreEqual("Bonus", plan[0].Label);
            Assert.AreEqual("https://retroachievements.org/Images/1.png", plan[0].IconUrl);
            Assert.AreEqual("Multi", plan[1].Label);
        }

        [TestMethod]
        public void BuildSubsetImagePlan_TrimsLabelsAndSkipsBlankOrDefaultLabels()
        {
            var plan = Plan(
                ("  Bonus  ", new RaGameInfoUserProgress { ImageIcon = "/Images/1.png" }),
                ("   ", new RaGameInfoUserProgress { ImageIcon = "/Images/2.png" }),
                ("Default", new RaGameInfoUserProgress { ImageIcon = "/Images/3.png" }));

            Assert.AreEqual(1, plan.Count);
            Assert.AreEqual("Bonus", plan[0].Label);
        }

        [TestMethod]
        public void BuildSubsetImagePlan_ReturnsEmptyForNullOrEmptyInput()
        {
            Assert.AreEqual(0, RetroAchievementsSubsetImagePlanner.BuildSubsetImagePlan(null).Count);
            Assert.AreEqual(0, Plan().Count);
        }
    }
}
