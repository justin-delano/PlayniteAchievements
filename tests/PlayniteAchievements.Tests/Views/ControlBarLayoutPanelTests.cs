using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Views.Controls;

namespace PlayniteAchievements.Tests.Views
{
    [TestClass]
    public class ControlBarLayoutPanelTests
    {
        [TestMethod]
        public void OneLine_SearchFillsTheRestAndFiltersSitRight()
        {
            RunOnStaThread(() =>
            {
                var (panel, leading, search, items) = Create(wrap: true, withSearch: true);

                Layout(panel, 600);

                Assert.AreEqual(30, panel.DesiredSize.Height);
                Assert.AreEqual(new Rect(40, 0, 360, 30), LayoutRect(search));
                Assert.AreEqual(new Rect(400, 0, 200, 30), LayoutRect(items));
                Assert.AreEqual(0, LayoutRect(leading).X);
            });
        }

        [TestMethod]
        public void Narrow_WithWrap_MovesFiltersBelowTheSearchBox()
        {
            RunOnStaThread(() =>
            {
                var (panel, _, search, items) = Create(wrap: true, withSearch: true);

                Layout(panel, 300);

                Assert.AreEqual(30 + 4 + 30, panel.DesiredSize.Height);
                Assert.AreEqual(new Rect(40, 0, 260, 30), LayoutRect(search));
                Assert.AreEqual(new Rect(0, 34, 300, 30), LayoutRect(items));
            });
        }

        [TestMethod]
        public void Narrow_WithoutWrap_StaysOnOneLine()
        {
            RunOnStaThread(() =>
            {
                var (panel, _, search, items) = Create(wrap: false, withSearch: true);

                Layout(panel, 300);

                Assert.AreEqual(30, panel.DesiredSize.Height);
                Assert.AreEqual(0, LayoutRect(items).Y);
                Assert.AreEqual(0, LayoutRect(search).Y);
            });
        }

        [TestMethod]
        public void NoSearch_Wrapped_FiltersStartAtTheTopWithNoGap()
        {
            RunOnStaThread(() =>
            {
                var (panel, _, _, items) = Create(wrap: true, withSearch: false, leadingWidth: 0);

                Layout(panel, 150);

                Assert.AreEqual(0, LayoutRect(items).Y);
                Assert.AreEqual(150, LayoutRect(items).Width);
            });
        }

        private static (ControlBarLayoutPanel, FrameworkElement, ContentControl, FrameworkElement) Create(
            bool wrap,
            bool withSearch,
            double leadingWidth = 40)
        {
            var leading = new Border { Width = leadingWidth, Height = leadingWidth > 0 ? 30 : 0 };
            var search = new ContentControl
            {
                Content = withSearch ? new Border { Height = 30 } : null
            };
            var items = new Border { MinWidth = 0, Height = 30, Child = new Border { Width = 200 } };
            var panel = new ControlBarLayoutPanel { Wrap = wrap, SearchMinWidth = 100 };
            panel.Children.Add(leading);
            panel.Children.Add(search);
            panel.Children.Add(items);
            return (panel, leading, search, items);
        }

        private static void Layout(FrameworkElement panel, double width)
        {
            panel.Measure(new Size(width, double.PositiveInfinity));
            panel.Arrange(new Rect(0, 0, width, panel.DesiredSize.Height));
        }

        private static Rect LayoutRect(FrameworkElement element)
        {
            var offset = element.TranslatePoint(new Point(0, 0), (UIElement)element.Parent);
            return new Rect(offset, element.RenderSize);
        }

        private static void RunOnStaThread(Action action)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null)
            {
                throw new AssertFailedException(failure.Message, failure);
            }
        }
    }
}
