using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.UI;

namespace PlayniteAchievements.Services.Tests.UI
{
    [TestClass]
    public class ToastMotionStoryboardFactoryTests
    {
        [TestMethod]
        public void Resolve_NoMotionFields_ReturnsNull()
        {
            Assert.IsNull(ToastMotionStoryboardFactory.Resolve(NotificationSurfaceStyle.CreateToastDefault()));
        }

        [TestMethod]
        public void Resolve_EntranceOnly_ExitMirrorsEntrance()
        {
            var surface = NotificationSurfaceStyle.CreateToastDefault();
            surface.EntranceMotion = ToastMotion.Zoom;

            var plan = ToastMotionStoryboardFactory.Resolve(surface);

            Assert.AreEqual(ToastMotion.Zoom, plan.Entrance);
            Assert.AreEqual(ToastMotion.Zoom, plan.Exit);
            Assert.AreEqual(1d, plan.SpeedScale);
        }

        [TestMethod]
        public void Resolve_SpeedOnly_KeepsTheBuiltInSlide()
        {
            var surface = NotificationSurfaceStyle.CreateToastDefault();
            surface.MotionSpeed = ToastMotionSpeed.Quick;

            var plan = ToastMotionStoryboardFactory.Resolve(surface);

            Assert.AreEqual(ToastMotion.Slide, plan.Entrance);
            Assert.AreEqual(ToastMotion.Slide, plan.Exit);
            Assert.AreEqual(0.7, plan.SpeedScale, 1e-9);
        }

        [TestMethod]
        public void DurationMs_ScalesBySpeed_AndNoneIsInstant()
        {
            Assert.AreEqual(168d, ToastMotionStoryboardFactory.DurationMs(ToastMotion.Slide, 240, 0.7));
            Assert.AreEqual(240d, ToastMotionStoryboardFactory.DurationMs(ToastMotion.Fade, 240, 1d));
            Assert.AreEqual(360d, ToastMotionStoryboardFactory.DurationMs(ToastMotion.Zoom, 240, 1.5));
            Assert.AreEqual(0d, ToastMotionStoryboardFactory.DurationMs(ToastMotion.None, 240, 1.5));
        }

        [TestMethod]
        public void Travels_OnlyForSlides()
        {
            Assert.IsTrue(ToastMotionStoryboardFactory.Travels(ToastMotion.Slide));
            Assert.IsTrue(ToastMotionStoryboardFactory.Travels(ToastMotion.SlideSide));
            Assert.IsFalse(ToastMotionStoryboardFactory.Travels(ToastMotion.Fade));
            Assert.IsFalse(ToastMotionStoryboardFactory.Travels(ToastMotion.Zoom));
            Assert.IsFalse(ToastMotionStoryboardFactory.Travels(ToastMotion.None));
        }

        [DataTestMethod]
        [DataRow(ToastScreenCorner.BottomRight, 0d, 100d)]
        [DataRow(ToastScreenCorner.BottomLeft, 0d, 100d)]
        [DataRow(ToastScreenCorner.TopRight, 0d, -100d)]
        [DataRow(ToastScreenCorner.TopLeft, 0d, -100d)]
        public void TravelOffset_Slide_LeavesThroughTheCornerEdge(ToastScreenCorner corner, double x, double y)
        {
            var offset = ToastMotionStoryboardFactory.TravelOffset(ToastMotion.Slide, corner, 100, 400);

            Assert.AreEqual(x, offset.X);
            Assert.AreEqual(y, offset.Y);
        }

        [DataTestMethod]
        [DataRow(ToastScreenCorner.BottomRight, 400d)]
        [DataRow(ToastScreenCorner.TopRight, 400d)]
        [DataRow(ToastScreenCorner.BottomLeft, -400d)]
        [DataRow(ToastScreenCorner.TopLeft, -400d)]
        public void TravelOffset_SlideSide_LeavesThroughTheCornerSide(ToastScreenCorner corner, double x)
        {
            var offset = ToastMotionStoryboardFactory.TravelOffset(ToastMotion.SlideSide, corner, 100, 400);

            Assert.AreEqual(x, offset.X);
            Assert.AreEqual(0d, offset.Y);
        }

        [TestMethod]
        public void TravelOffset_InPlaceMotions_AreZero()
        {
            foreach (var motion in new[] { ToastMotion.Fade, ToastMotion.Zoom, ToastMotion.None })
            {
                var offset = ToastMotionStoryboardFactory.TravelOffset(motion, ToastScreenCorner.BottomRight, 100, 400);
                Assert.AreEqual(new Vector(0, 0), offset, motion.ToString());
            }
        }

        [TestMethod]
        public void Build_None_ReturnsNull()
        {
            Assert.IsNull(ToastMotionStoryboardFactory.Build(ToastMotion.None, true, null, 240, new Vector()));
            Assert.IsNull(ToastMotionStoryboardFactory.Build(ToastMotion.Slide, true, null, 0, new Vector(0, 100)));
        }

        [TestMethod]
        public void Build_SlideSideEntrance_AnimatesTranslateXFromOffsetToRest()
        {
            var storyboard = ToastMotionStoryboardFactory.Build(
                ToastMotion.SlideSide, true, null, 240, new Vector(400, 0));

            var child = (DoubleAnimation)storyboard.Children.Single();
            Assert.AreEqual(TranslateTransform.XProperty, LastPathParameter(child));
            Assert.AreEqual(400d, child.From);
            Assert.AreEqual(0d, child.To);
            Assert.IsNull(Storyboard.GetTarget(child));
            Assert.IsInstanceOfType(child.EasingFunction, typeof(BackEase));
        }

        [TestMethod]
        public void Build_SlideExit_AnimatesTranslateYFromRestToOffset()
        {
            var storyboard = ToastMotionStoryboardFactory.Build(
                ToastMotion.Slide, false, ToastMotionFeel.Smooth, 200, new Vector(0, 180));

            var child = (DoubleAnimation)storyboard.Children.Single();
            Assert.AreEqual(TranslateTransform.YProperty, LastPathParameter(child));
            Assert.AreEqual(0d, child.From);
            Assert.AreEqual(180d, child.To);
            Assert.IsInstanceOfType(child.EasingFunction, typeof(CubicEase));
        }

        [TestMethod]
        public void Build_ZoomEntrance_ScalesUpAndFadesIn()
        {
            var storyboard = ToastMotionStoryboardFactory.Build(
                ToastMotion.Zoom, true, ToastMotionFeel.Bouncy, 240, new Vector());

            var children = storyboard.Children.Cast<DoubleAnimation>().ToList();
            var scaleX = children.Single(c => LastPathParameter(c) == ScaleTransform.ScaleXProperty);
            var scaleY = children.Single(c => LastPathParameter(c) == ScaleTransform.ScaleYProperty);
            var opacity = children.Single(c => LastPathParameter(c) == UIElement.OpacityProperty);

            Assert.AreEqual(ToastMotionStoryboardFactory.ZoomFromScale, scaleX.From);
            Assert.AreEqual(1d, scaleX.To);
            Assert.AreEqual(ToastMotionStoryboardFactory.ZoomFromScale, scaleY.From);
            Assert.AreEqual(0d, opacity.From);
            Assert.AreEqual(1d, opacity.To);
            Assert.IsInstanceOfType(opacity.EasingFunction, typeof(CubicEase));
        }

        [TestMethod]
        public void Build_FadeExit_FadesOutWithoutOvershoot()
        {
            var storyboard = ToastMotionStoryboardFactory.Build(
                ToastMotion.Fade, false, ToastMotionFeel.Bouncy, 200, new Vector());

            var child = (DoubleAnimation)storyboard.Children.Single();
            Assert.AreEqual(UIElement.OpacityProperty, LastPathParameter(child));
            Assert.AreEqual(1d, child.From);
            Assert.AreEqual(0d, child.To);
            Assert.IsInstanceOfType(child.EasingFunction, typeof(CubicEase));
        }

        private static object LastPathParameter(Timeline child)
        {
            var path = Storyboard.GetTargetProperty(child);
            return path.PathParameters[path.PathParameters.Count - 1];
        }
    }
}
