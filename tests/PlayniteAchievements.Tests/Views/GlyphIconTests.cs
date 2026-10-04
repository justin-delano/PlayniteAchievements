using System.Windows;
using System.Windows.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Views.Controls;

namespace PlayniteAchievements.Tests.Views
{
    [TestClass]
    [TestCategory("Views")]
    public class GlyphIconTests
    {
        [TestMethod]
        public void TryBuildGeometry_ReturnsAFrozenOutlineInsideTheBox_ForAGlyphTheFontHas()
        {
            var typeface = new Typeface(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

            var built = GlyphIcon.TryBuildGeometry(typeface, 'A', 20, out var geometry);

            Assert.IsTrue(built, "Arial has an outline for A");
            Assert.IsNotNull(geometry);
            Assert.IsTrue(geometry.IsFrozen, "cached geometry must be frozen so it can be shared across elements");
            var bounds = geometry.Bounds;
            Assert.IsFalse(bounds.IsEmpty);
            Assert.IsTrue(bounds.Left >= -0.5 && bounds.Right <= 20.5, $"horizontally inside the 20px box: {bounds}");
            Assert.IsTrue(bounds.Top >= -0.5 && bounds.Bottom <= 20.5, $"vertically inside the 20px box: {bounds}");
        }

        [TestMethod]
        public void TryBuildGeometry_IsFalseForMissingGlyphsAndBadInput()
        {
            var typeface = new Typeface(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

            Assert.IsFalse(GlyphIcon.TryBuildGeometry(typeface, '', 20, out var missing), "private-use code point Arial does not map");
            Assert.IsNull(missing);
            Assert.IsFalse(GlyphIcon.TryBuildGeometry(typeface, 'A', 0, out _));
            Assert.IsFalse(GlyphIcon.TryBuildGeometry(null, 'A', 20, out _));
        }

        [TestMethod]
        public void MeasureOverride_IsASquareOfSize()
        {
            var icon = new GlyphIcon { Size = 18, Glyph = "A", FontFamily = new FontFamily("Arial") };

            icon.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            Assert.AreEqual(18, icon.DesiredSize.Width);
            Assert.AreEqual(18, icon.DesiredSize.Height);
        }
    }
}
