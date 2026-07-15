using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace PlayniteAchievements.Tests.Views
{
    [TestClass]
    public class GridStyleDefinitionTests
    {
        [TestMethod]
        public void SharedGridStyles_PaintRowStateOnceToAvoidCellSeams()
        {
            var overview = File.ReadAllText(FindRepoFile("source", "Resources", "OverviewStyles.xaml"));
            var achievement = File.ReadAllText(FindRepoFile("source", "Resources", "AchievementGridStyles.xaml"));

            AssertContainsAll(
                ExtractStyleBlock(overview, "GameSummariesGridCellStyle"),
                "TargetType=\"DataGridCell\"",
                "Property=\"Background\" Value=\"Transparent\"");
            AssertContainsNone(
                ExtractStyleBlock(overview, "GameSummariesGridCellStyle"),
                "PlayAch.Brush.Grid.RowHoverBackground",
                "PlayAch.Brush.Grid.RowSelectedBackground");
            AssertContainsAll(
                ExtractStyleBlock(overview, "GameSummariesGridRowBaseStyle"),
                "TargetType=\"DataGridRow\"",
                "Path=ContextMenu.IsOpen",
                "Property=\"IsMouseOver\" Value=\"True\"",
                "Property=\"IsSelected\" Value=\"True\"",
                "Property=\"IsKeyboardFocusWithin\" Value=\"True\"",
                "PlayAch.Brush.Grid.RowHoverBackground",
                "PlayAch.Brush.Grid.RowSelectedBackground");

            AssertContainsAll(
                ExtractStyleBlock(achievement, "AchievementCellStyle"),
                "TargetType=\"DataGridCell\"",
                "Property=\"Background\" Value=\"Transparent\"");
            AssertContainsNone(
                ExtractStyleBlock(achievement, "AchievementCellStyle"),
                "PlayAch.Brush.Grid.RowHoverBackground",
                "PlayAch.Brush.Grid.RowSelectedBackground");
            AssertContainsAll(
                ExtractStyleBlock(achievement, "AchievementRowStyle"),
                "TargetType=\"DataGridRow\"",
                "Path=ContextMenu.IsOpen",
                "Property=\"IsMouseOver\" Value=\"True\"",
                "Property=\"IsSelected\" Value=\"True\"",
                "Property=\"IsKeyboardFocusWithin\" Value=\"True\"",
                "PlayAch.Brush.Grid.RowHoverBackground",
                "PlayAch.Brush.Grid.RowSelectedBackground");
        }

        private static string ExtractStyleBlock(string content, string key)
        {
            var keyText = $"x:Key=\"{key}\"";
            var start = content.IndexOf(keyText, StringComparison.Ordinal);
            Assert.IsTrue(start >= 0, $"Style key not found: {key}");

            start = content.LastIndexOf("<Style", start, StringComparison.Ordinal);
            Assert.IsTrue(start >= 0, $"Style start not found: {key}");

            var next = content.IndexOf("\n    <Style", start + 1, StringComparison.Ordinal);
            return next >= 0 ? content.Substring(start, next - start) : content.Substring(start);
        }

        private static void AssertContainsAll(string content, params string[] expected)
        {
            foreach (var value in expected)
            {
                StringAssert.Contains(content, value);
            }
        }

        private static void AssertContainsNone(string content, params string[] unexpected)
        {
            foreach (var value in unexpected)
            {
                Assert.IsFalse(content.Contains(value), $"Unexpected content found: {value}");
            }
        }

        private static string FindRepoFile(params string[] parts)
        {
            var root = AppDomain.CurrentDomain.BaseDirectory;
            for (var i = 0; i < 8 && !string.IsNullOrEmpty(root); i++)
            {
                var candidate = Path.Combine(root, Path.Combine(parts));
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                root = Directory.GetParent(root)?.FullName;
            }

            Assert.Fail("Could not locate repo file: " + string.Join("/", parts));
            return null;
        }
    }
}
