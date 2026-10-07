using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Views.Helpers;

namespace PlayniteAchievements.Tests.Views
{
    [TestClass]
    public class ColumnSizingPlannerTests
    {
        private static readonly string[] Keys = { "A", "B", "C" };
        private static readonly double[] Floors = { 20d, 20d, 20d };

        [TestMethod]
        public void Rescale_SpreadsTheRoundingRemainderAcrossColumns()
        {
            var planned = Plan(new[] { 100d, 100d, 100d }, 302d, protectedKey: null);

            Assert.AreEqual(302d, planned.Values.Sum());
            // Two extra pixels go to two different columns, not both to the first.
            Assert.AreEqual(1d, planned.Values.Max() - planned.Values.Min());

            planned = Plan(new[] { 100d, 100d, 100d }, 303d, protectedKey: null);
            CollectionAssert.AreEqual(new[] { 101d, 101d, 101d }, Keys.Select(k => planned[k]).ToArray());
        }

        [TestMethod]
        public void Rescale_OnePixelAtATimeEndsWhereOneJumpDoes_WhenSeededFromTheSameWidths()
        {
            var seeds = new[] { 100d, 100d, 100d };
            var jump = Plan(seeds, 306d, protectedKey: null);

            Dictionary<string, double> stepwise = null;
            for (var target = 301d; target <= 306d; target++)
            {
                stepwise = Plan(seeds, target, protectedKey: null);
            }

            CollectionAssert.AreEqual(
                Keys.Select(k => jump[k]).ToArray(),
                Keys.Select(k => stepwise[k]).ToArray());
            CollectionAssert.AreEqual(new[] { 102d, 102d, 102d }, Keys.Select(k => jump[k]).ToArray());
        }

        [TestMethod]
        public void Rescale_FractionalTargetNeverPlansMoreThanTheViewport()
        {
            var planned = Plan(new[] { 100d, 100d, 100d }, 300.5d, protectedKey: null);

            Assert.AreEqual(300d, planned.Values.Sum());
        }

        [TestMethod]
        public void RemainderPixels_SkipTheProtectedAndExcludedColumns()
        {
            // A is the column the user just set; B is locked. Only C may take the leftover pixel.
            var result = ColumnSizingPlanner.TryPlan(
                Keys,
                new[] { 121d, 139.4d, 139.4d },
                Floors,
                protectedKey: "A",
                preferredAbsorberKey: null,
                rescaleAll: false,
                targetWidth: 400d,
                excludedAbsorberKeys: new[] { "B" },
                out var planned);

            Assert.IsTrue(result);
            Assert.AreEqual(400d, planned.Values.Sum());
            Assert.AreEqual(121d, planned["A"]);
            Assert.AreEqual(139d, planned["B"]);
            Assert.AreEqual(140d, planned["C"]);
        }

        [TestMethod]
        public void Rescale_HoldsACappedLastColumnAtItsCeilingAndFillsTheTarget()
        {
            // The last column's seed alone would take far more than its 120 pixel cap.
            var ceilings = new[] { double.NaN, double.NaN, 120d };
            var planned = PlanCapped(new[] { 200d, 200d, 177d }, 1200d, ceilings);

            Assert.AreEqual(1200d, planned.Values.Sum());
            Assert.AreEqual(120d, planned["C"]);
            Assert.AreEqual(540d, planned["A"]);
            Assert.AreEqual(540d, planned["B"]);
        }

        [TestMethod]
        public void Rescale_PassesExcessOnWhenAReceivingColumnReachesItsOwnCeiling()
        {
            var ceilings = new[] { double.NaN, 150d, 120d };
            var planned = PlanCapped(new[] { 100d, 100d, 100d }, 900d, ceilings);

            Assert.AreEqual(900d, planned.Values.Sum());
            Assert.AreEqual(150d, planned["B"]);
            Assert.AreEqual(120d, planned["C"]);
            Assert.AreEqual(630d, planned["A"]);
        }

        [TestMethod]
        public void Rescale_FallsShortOnlyWhenEveryColumnIsCapped()
        {
            var ceilings = new[] { 100d, 100d, 100d };
            var planned = PlanCapped(new[] { 50d, 50d, 50d }, 600d, ceilings);

            CollectionAssert.AreEqual(new[] { 100d, 100d, 100d }, Keys.Select(k => planned[k]).ToArray());
        }

        private static Dictionary<string, double> PlanCapped(double[] seeds, double target, double[] ceilings)
        {
            var result = ColumnSizingPlanner.TryPlan(
                Keys,
                seeds,
                Floors,
                protectedKey: null,
                preferredAbsorberKey: null,
                rescaleAll: true,
                targetWidth: target,
                excludedAbsorberKeys: null,
                ceilingWidths: ceilings,
                out var planned);
            Assert.IsTrue(result);
            return planned;
        }

        private static Dictionary<string, double> Plan(double[] seeds, double target, string protectedKey)
        {
            var result = ColumnSizingPlanner.TryPlan(
                Keys,
                seeds,
                Floors,
                protectedKey,
                preferredAbsorberKey: null,
                rescaleAll: true,
                targetWidth: target,
                out var planned);
            Assert.IsTrue(result);
            return planned;
        }
    }
}
