using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Services.UI;

namespace PlayniteAchievements.Tests.Services.UI
{
    /// <summary>
    /// Covers <see cref="ToastWindowPlacer.TryMeasureCardPhysical"/> against a real window: with the
    /// slide host given, a placement pass that lands mid-animation must read the card's resting rect,
    /// whatever scale or translate the entrance has the host at.
    /// </summary>
    [TestClass]
    public class ToastCardMeasurementTests
    {
        [TestMethod]
        public void TryMeasureCardPhysical_WithSlideHost_IgnoresTheHostZoomAndSlide()
        {
            RunOnSta(() =>
            {
                var scale = new ScaleTransform(1d, 1d);
                var slide = new TranslateTransform();
                var transforms = new TransformGroup();
                transforms.Children.Add(scale);
                transforms.Children.Add(slide);

                var card = new Border { Width = 200, Height = 100, Margin = new Thickness(10, 20, 0, 0) };
                var host = new Grid
                {
                    RenderTransform = transforms,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                };
                host.Children.Add(card);

                var window = new Window
                {
                    WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    SizeToContent = SizeToContent.WidthAndHeight,
                    Left = -10000,
                    Top = -10000,
                    Content = host,
                };

                try
                {
                    window.Show();
                    window.UpdateLayout();

                    Assert.IsTrue(ToastWindowPlacer.TryMeasureCardPhysical(
                        window, card, 1d, 0d, 0d,
                        out var restX, out var restY, out var restW, out var restH, host));

                    scale.ScaleX = 0.85;
                    scale.ScaleY = 0.85;
                    slide.Y = 37;
                    window.UpdateLayout();

                    Assert.IsTrue(ToastWindowPlacer.TryMeasureCardPhysical(
                        window, card, 1d, 0d, slide.Y,
                        out var x, out var y, out var w, out var h, host));

                    Assert.AreEqual(restX, x);
                    Assert.AreEqual(restY, y);
                    Assert.AreEqual(restW, w);
                    Assert.AreEqual(restH, h);

                    // Without the host, the measurement sees the zoom: the card reads smaller.
                    Assert.IsTrue(ToastWindowPlacer.TryMeasureCardPhysical(
                        window, card, 1d, 0d, slide.Y,
                        out _, out _, out var scaledW, out _));
                    Assert.IsTrue(scaledW < restW);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        private static void RunOnSta(Action action)
        {
            Exception exception = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (exception != null)
            {
                ExceptionDispatchInfo.Capture(exception).Throw();
            }
        }
    }
}
