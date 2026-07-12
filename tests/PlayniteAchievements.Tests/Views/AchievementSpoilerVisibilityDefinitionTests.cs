using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PlayniteAchievements.Tests.Views
{
    [TestClass]
    public class AchievementSpoilerVisibilityDefinitionTests
    {
        [TestMethod]
        public void DisplayItem_GatesVisibilityOnUnlockedForVisibility()
        {
            var code = File.ReadAllText(FindRepoFile("source", "ViewModels", "Items", "AchievementDisplayItem.cs"));

            AssertContainsAll(
                code,
                "public virtual bool UnlockedForVisibility => Unlocked;",
                "public bool CanReveal => !UnlockedForVisibility && (!ShowLockedIcon",
                "public bool IsLockedIconHidden => !UnlockedForVisibility && !ShowLockedIcon && !IsRevealed;",
                "(!UnlockedForVisibility && !ShowLockedIcon && !IsRevealed)",
                "ShowFriendSpoilers = persisted?.ShowFriendSpoilers ?? false");
        }

        [TestMethod]
        public void FriendDisplayItem_UsesOwnUnlockStateWhenHidingSpoilers()
        {
            var code = File.ReadAllText(FindRepoFile("source", "ViewModels", "Items", "FriendAchievementDisplayItem.cs"));

            AssertContainsAll(
                code,
                "public override bool UnlockedForVisibility =>",
                "ShowFriendSpoilers ? base.UnlockedForVisibility : UnlockedBySelf;");
        }

        [TestMethod]
        public void DisplaySettings_AchievementVisibilityIsBasicAndIncludesSpoilerToggle()
        {
            var xaml = File.ReadAllText(FindRepoFile("source", "Views", "Settings", "Display", "DisplayGeneralSection.xaml"));

            var visibilityCardIndex = xaml.IndexOf("LOCPlayAch_Settings_HiddenAchievements", StringComparison.Ordinal);
            var advancedIndex = xaml.IndexOf("LOCPlayAch_Settings_Advanced", StringComparison.Ordinal);
            var suffixToggleIndex = xaml.IndexOf("LOCPlayAch_Settings_ShowHiddenSuffix", StringComparison.Ordinal);
            var spoilerToggleIndex = xaml.IndexOf("LOCPlayAch_Settings_ShowFriendSpoilers", StringComparison.Ordinal);

            Assert.IsTrue(visibilityCardIndex >= 0, "Achievement Visibility card missing.");
            Assert.IsTrue(advancedIndex >= 0, "Advanced expander missing.");
            Assert.IsTrue(suffixToggleIndex >= 0, "Hidden suffix toggle missing.");
            Assert.IsTrue(spoilerToggleIndex >= 0, "Friend spoiler toggle missing.");
            Assert.IsTrue(visibilityCardIndex < advancedIndex, "Achievement Visibility card must precede the Advanced expander.");
            Assert.IsTrue(spoilerToggleIndex > suffixToggleIndex && spoilerToggleIndex < advancedIndex,
                "Friend spoiler toggle must live in the Achievement Visibility card.");
            AssertContainsAll(
                xaml,
                "IsChecked=\"{Binding Persisted.ShowFriendSpoilers}\"",
                "Visibility=\"{Binding Persisted.EnableFriendsFeatures, Converter={StaticResource BoolToVis}}\"");
        }

        private static void AssertContainsAll(string content, params string[] expected)
        {
            var missing = expected
                .Where(value => !content.Contains(value))
                .ToList();

            CollectionAssert.AreEqual(new List<string>(), missing);
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
