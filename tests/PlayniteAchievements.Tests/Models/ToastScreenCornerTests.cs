using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models.Settings;

namespace PlayniteAchievements.Models.Tests
{
    [TestClass]
    public class ToastScreenCornerTests
    {
        [TestMethod]
        public void SavedValues_KeepTheirNumbers()
        {
            // The setting is saved numerically; a reordered enum would move every saved position.
            Assert.AreEqual(0, (int)ToastScreenCorner.BottomRight);
            Assert.AreEqual(1, (int)ToastScreenCorner.BottomLeft);
            Assert.AreEqual(2, (int)ToastScreenCorner.TopRight);
            Assert.AreEqual(3, (int)ToastScreenCorner.TopLeft);
            Assert.AreEqual(4, (int)ToastScreenCorner.BottomCenter);
        }

        [DataTestMethod]
        [DataRow(ToastScreenCorner.BottomRight, ToastHorizontalAlignment.Right, true)]
        [DataRow(ToastScreenCorner.BottomLeft, ToastHorizontalAlignment.Left, true)]
        [DataRow(ToastScreenCorner.TopRight, ToastHorizontalAlignment.Right, false)]
        [DataRow(ToastScreenCorner.TopLeft, ToastHorizontalAlignment.Left, false)]
        [DataRow(ToastScreenCorner.BottomCenter, ToastHorizontalAlignment.Center, true)]
        public void Position_MapsToItsAlignment(
            ToastScreenCorner position, ToastHorizontalAlignment horizontal, bool bottom)
        {
            Assert.AreEqual(horizontal, position.Horizontal());
            Assert.AreEqual(bottom, position.IsBottom());
        }
    }
}
