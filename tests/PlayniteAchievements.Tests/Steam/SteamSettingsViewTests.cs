using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models.Friends;
using PlayniteAchievements.Providers.Steam;

namespace PlayniteAchievements.Tests.Steam
{
    [TestClass]
    public class SteamSettingsViewTests
    {
        [TestMethod]
        public void BuildFriendListItems_IncludesPersistedFullLibraryFriendsMissingFromActiveCache()
        {
            var settings = new SteamSettings();
            settings.SetFullLibraryFriend("333", "Full Library", "full-avatar", enabled: true);

            var rows = SteamFriendListBuilder.BuildItems(
                settings,
                new[]
                {
                    new FriendIdentity
                    {
                        ExternalUserId = "111",
                        DisplayName = "Active Friend",
                        AvatarUrl = "active-avatar",
                        AvatarPath = "active-avatar-path"
                    }
                });

            Assert.AreEqual(2, rows.Count);

            var activeRow = rows.Single(row => row.SteamId == "111");
            Assert.AreEqual("active-avatar-path", activeRow.AvatarUrl);

            var staleFullLibraryRow = rows.Single(row => row.SteamId == "333");
            Assert.AreEqual("Full Library", staleFullLibraryRow.DisplayName);
            Assert.AreEqual("full-avatar", staleFullLibraryRow.AvatarUrl);
            Assert.IsFalse(staleFullLibraryRow.IsIgnored);
            Assert.IsTrue(staleFullLibraryRow.UseFullLibrary);
        }

        [TestMethod]
        public void BuildFriendListItems_IgnoredFriendOverridesActiveCacheRow()
        {
            var settings = new SteamSettings();
            settings.AddIgnoredFriend("111", "Ignored Friend", "ignored-avatar");
            settings.SetFullLibraryFriend("111", "Ignored Friend", "ignored-avatar", enabled: true);

            var rows = SteamFriendListBuilder.BuildItems(
                settings,
                new[]
                {
                    new FriendIdentity
                    {
                        ExternalUserId = "111",
                        DisplayName = "Active Friend",
                        AvatarUrl = "active-avatar"
                    }
                });

            var row = rows.Single();
            Assert.AreEqual("111", row.SteamId);
            Assert.AreEqual("Ignored Friend", row.DisplayName);
            Assert.AreEqual("ignored-avatar", row.AvatarUrl);
            Assert.IsTrue(row.IsIgnored);
            Assert.IsTrue(row.UseFullLibrary);
        }
    }
}
