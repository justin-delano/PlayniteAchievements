using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;

namespace PlayniteAchievements.Tests.Views
{
    [TestClass]
    public class ShowcaseWpfContractTests
    {
        [TestMethod]
        public void Overview_RegistersAlwaysAvailableThirdTabAndRemovesHeaderScores()
        {
            var xaml = ReadRepoFile("source", "Views", "OverviewControl.xaml");
            var code = ReadRepoFile("source", "Views", "OverviewControl.xaml.cs");

            StringAssert.Contains(xaml, "x:Name=\"ShowcaseSubViewButton\"");
            StringAssert.Contains(xaml, "rootModels:OverviewSubView.Showcase");
            StringAssert.Contains(xaml, "x:Name=\"ShowcaseContentHost\"");
            StringAssert.Contains(
                xaml,
                "Visibility=\"{Binding EnableFriendsFeatures, Converter={StaticResource BoolToVis}}\"");
            Assert.IsFalse(xaml.Contains("<controls:ScoreCardControl"));
            StringAssert.Contains(
                code,
                "_lastSelectedSubView == OverviewSubView.Friends");
            StringAssert.Contains(
                code,
                "ActiveSubView == OverviewSubView.Showcase");
        }

        [TestMethod]
        public void Showcase_ProvidesPageControlsSelectedBlockEditingAndDynamicThemeResources()
        {
            var xaml = ReadRepoFile(
                "source",
                "Views",
                "Showcase",
                "ShowcaseControl.xaml");
            var code = ReadRepoFile(
                "source",
                "Views",
                "Showcase",
                "ShowcaseControl.xaml.cs");
            var widgetXaml = ReadRepoFile(
                "source",
                "Views",
                "Showcase",
                "ShowcaseWidgetControl.xaml");

            StringAssert.Contains(xaml, "x:Name=\"PreviousPageButton\"");
            StringAssert.Contains(xaml, "x:Name=\"PageSelector\"");
            StringAssert.Contains(xaml, "x:Name=\"NextPageButton\"");
            StringAssert.Contains(xaml, "x:Name=\"EditToolsPanel\"");
            StringAssert.Contains(xaml, "x:Name=\"SplitColumnsButton\"");
            StringAssert.Contains(xaml, "x:Name=\"MergeLeftButton\"");
            StringAssert.Contains(
                xaml,
                "Content=\"{DynamicResource LOCPlayAch_Showcase_MergeLeftLabel}\"");
            Assert.IsFalse(xaml.Contains("BoundaryCanvas"));
            StringAssert.Contains(code, "Focusable = EditLayoutButton.IsChecked == true");
            StringAssert.Contains(code, "TrySplit(");
            StringAssert.Contains(code, "TryMergeWithFallback(");
            StringAssert.Contains(code, "FindAdjacentBlocks");
            StringAssert.Contains(code, "OpenMergePicker");
            StringAssert.Contains(code, "ShowcaseWidgetSettingsDialog.Show");
            StringAssert.Contains(code, "Block_DragLeave");
            StringAssert.Contains(code, "LOCPlayAch_Showcase_DropMoveHere");
            StringAssert.Contains(code, "LOCPlayAch_Showcase_DropSwap");
            StringAssert.Contains(code, "LOCPlayAch_Showcase_DropSame");
            Assert.IsFalse(code.Contains("Widget drawer"));
            Assert.IsFalse(code.Contains("UnplaceWidget"));
            StringAssert.Contains(widgetXaml, "{DynamicResource PlayAch.Brush.Surface}");
            StringAssert.Contains(widgetXaml, "{DynamicResource PlayAch.Brush.Border}");
            StringAssert.Contains(widgetXaml, "{DynamicResource PlayAch.Brush.Text}");
            StringAssert.Contains(widgetXaml, "{DynamicResource PlayAch.Brush.Accent}");
            StringAssert.Contains(widgetXaml, "x:Name=\"GlyphText\"");
        }

        [TestMethod]
        public void Showcase_DataAndSettingsFlowsUseSharedRefreshAndSingleEditor()
        {
            var plugin = ReadRepoFile("source", "PlayniteAchievementsPlugin.cs");
            var editor = ReadRepoFile(
                "source",
                "Views",
                "Showcase",
                "ShowcaseWidgetSettingsDialog.cs");
            var localization = ReadRepoFile("source", "Localization", "en_US.xaml");

            StringAssert.Contains(plugin, "Games_ItemCollectionChanged");
            StringAssert.Contains(plugin, "Games_ItemUpdated");
            StringAssert.Contains(plugin, "InvalidateStartPageData();");
            StringAssert.Contains(editor, "PlayniteUiProvider.CreateExtensionWindow");
            StringAssert.Contains(editor, "LOCPlayAch_Showcase_WidgetSettingsTitle");
            StringAssert.Contains(localization, "LOCPlayAch_Showcase_MergeKeepWidget");
            StringAssert.Contains(localization, "LOCPlayAch_Showcase_Stat_CurrentStreak");
        }

        [TestMethod]
        public void Showcase_LocalizationCoversCatalogAndEveryDynamicSettingValue()
        {
            var localization = ReadRepoFile("source", "Localization", "en_US.xaml");
            foreach (var definition in ShowcaseWidgetCatalog.Definitions)
            {
                AssertLocalizationKey(localization, definition.NameKey);
                AssertLocalizationKey(localization, definition.DescriptionKey);
            }

            AssertEnumKeys<ShowcasePageTemplate>(
                localization,
                "LOCPlayAch_Showcase_Template_",
                value => value != ShowcasePageTemplate.Showcase);
            AssertEnumKeys<ShowcaseScoreMode>(localization, "LOCPlayAch_Showcase_ScoreMode_");
            AssertEnumKeys<ShowcasePieMode>(localization, "LOCPlayAch_Showcase_PieMode_");
            AssertEnumKeys<ShowcasePointsGrouping>(localization, "LOCPlayAch_Showcase_PointsGrouping_");
            AssertEnumKeys<ShowcaseFavoriteGameSource>(localization, "LOCPlayAch_Showcase_FavoriteSource_");
            AssertEnumKeys<ShowcaseMosaicSource>(localization, "LOCPlayAch_Showcase_MosaicSource_");
            AssertEnumKeys<ShowcaseScreenshotVariant>(localization, "LOCPlayAch_Showcase_ScreenshotVariant_");
            AssertEnumKeys<ShowcaseImageFitMode>(localization, "LOCPlayAch_Showcase_ImageFit_");
        }

        [TestMethod]
        public void Showcase_AllReferencedLocalizationKeysExistInEnUs()
        {
            var englishPath = FindRepoFile("source", "Localization", "en_US.xaml");
            var english = File.ReadAllText(englishPath);
            var showcaseDirectory = Path.GetDirectoryName(FindRepoFile(
                "source",
                "Views",
                "Showcase",
                "ShowcaseControl.xaml"));
            var sourceDirectory = Directory.GetParent(
                Directory.GetParent(showcaseDirectory).FullName).FullName;
            var localizationDirectory = Path.Combine(sourceDirectory, "Localization") +
                Path.DirectorySeparatorChar;
            var sourceFiles = Directory.GetFiles(
                    sourceDirectory,
                    "*.*",
                    SearchOption.AllDirectories)
                .Where(path =>
                    !path.StartsWith(localizationDirectory, StringComparison.OrdinalIgnoreCase) &&
                    (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                     path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)));
            var usedKeys = sourceFiles
                .SelectMany(path => Regex.Matches(
                        File.ReadAllText(path),
                        @"LOCPlayAch_Showcase_[A-Za-z0-9_]+")
                    .Cast<Match>()
                    .Select(match => match.Value))
                .Distinct(StringComparer.Ordinal)
                .Where(key => !key.EndsWith("_", StringComparison.Ordinal))
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();

            foreach (var key in usedKeys)
            {
                AssertLocalizationKey(english, key);
            }
        }

        [TestMethod]
        public void RarestMosaic_LoadsTheFullUnlockedSnapshotWithoutDuplicatingAQuery()
        {
            var reader = ReadRepoFile(
                "source",
                "Services",
                "Database",
                "SummaryCacheReader.cs");
            var builder = ReadRepoFile(
                "source",
                "Services",
                "Overview",
                "OverviewDataBuilder.cs");

            StringAssert.Contains(reader, "includeAllVisibleAchievements: requestedRecentLimit == 0");
            StringAssert.Contains(reader, "result.Achievements = mappedAchievements");
            StringAssert.Contains(builder, "snapshot.Achievements = MaterializeAchievements(");
            StringAssert.Contains(builder, "item?.Unlocked == true && item.UnlockTimeUtc.HasValue");
        }

        [TestMethod]
        public void Showcase_PinMenusExcludeFriendOwnedRows()
        {
            var achievements = ReadRepoFile(
                "source",
                "Views",
                "Helpers",
                "AchievementRowOptionsMenuBuilder.cs");
            var games = ReadRepoFile(
                "source",
                "Views",
                "Helpers",
                "GameRowContextMenuBuilder.cs");

            StringAssert.Contains(achievements, "friendOwned");
            StringAssert.Contains(games, "!(data is FriendGameSummaryItem)");
        }

        private static void AssertEnumKeys<T>(
            string localization,
            string prefix,
            Func<T, bool> include = null)
            where T : struct
        {
            foreach (T value in Enum.GetValues(typeof(T)))
            {
                if (include == null || include(value))
                {
                    AssertLocalizationKey(localization, prefix + value);
                }
            }
        }

        private static void AssertLocalizationKey(string localization, string key)
        {
            StringAssert.Contains(localization, $"x:Key=\"{key}\"");
        }

        private static string ReadRepoFile(params string[] parts)
        {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null)
            {
                var path = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
                if (File.Exists(path))
                {
                    return File.ReadAllText(path);
                }

                directory = directory.Parent;
            }

            Assert.Fail("Could not find " + Path.Combine(parts));
            return null;
        }

        private static string FindRepoFile(params string[] parts)
        {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null)
            {
                var path = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
                if (File.Exists(path))
                {
                    return path;
                }

                directory = directory.Parent;
            }

            Assert.Fail("Could not find " + Path.Combine(parts));
            return null;
        }
    }
}
