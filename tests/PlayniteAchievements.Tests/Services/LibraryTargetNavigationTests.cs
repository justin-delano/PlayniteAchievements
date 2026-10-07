using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Library;
using System;

namespace PlayniteAchievements.Services.Tests
{
    [TestClass]
    public class LibraryTargetNavigationTests
    {
        private static readonly Guid GameId = new Guid("8f0c1f8e-2b7a-4c55-9a43-1f2d3e4c5b6a");

        [TestMethod]
        public void Colors_OpensDisplayColorsPage()
        {
            var navigation = LibraryTargetNavigation.Resolve(LibraryTargetKeys.Colors);

            Assert.AreEqual(LibraryTargetDestination.Settings, navigation.Destination);
            Assert.AreEqual(SettingsNavigationRequest.DisplayTab, navigation.Settings.TabKey);
            Assert.AreEqual(SettingsNavigationRequest.ColorsPage, navigation.Settings.PageKey);
            Assert.IsNull(navigation.Settings.IsFrame);
            Assert.IsFalse(navigation.Settings.ShowSounds);
        }

        [TestMethod]
        public void Sounds_OpensNotificationsBehaviorPageAtTheSoundPicker()
        {
            var navigation = LibraryTargetNavigation.Resolve(LibraryTargetKeys.Sounds);

            Assert.AreEqual(LibraryTargetDestination.Settings, navigation.Destination);
            Assert.AreEqual(SettingsNavigationRequest.NotificationsTab, navigation.Settings.TabKey);
            Assert.AreEqual(SettingsNavigationRequest.BehaviorPage, navigation.Settings.PageKey);
            Assert.IsTrue(navigation.Settings.ShowSounds);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void GlobalScope_OpensAppearanceOnTheDefaultPlatform(bool isFrame)
        {
            var navigation = LibraryTargetNavigation.Resolve(LibraryTargetKeys.NotificationScope(isFrame, null, Guid.Empty));

            Assert.AreEqual(LibraryTargetDestination.Settings, navigation.Destination);
            Assert.AreEqual(SettingsNavigationRequest.NotificationsTab, navigation.Settings.TabKey);
            Assert.AreEqual(SettingsNavigationRequest.AppearancePage, navigation.Settings.PageKey);
            Assert.IsNull(navigation.Settings.ProviderKey);
            Assert.AreEqual(isFrame, navigation.Settings.IsFrame);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ProviderScope_OpensAppearanceOnThatPlatform(bool isFrame)
        {
            var navigation = LibraryTargetNavigation.Resolve(LibraryTargetKeys.NotificationScope(isFrame, "Steam", Guid.Empty));

            Assert.AreEqual(LibraryTargetDestination.Settings, navigation.Destination);
            Assert.AreEqual(SettingsNavigationRequest.AppearancePage, navigation.Settings.PageKey);
            Assert.AreEqual("Steam", navigation.Settings.ProviderKey);
            Assert.AreEqual(isFrame, navigation.Settings.IsFrame);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void GameScope_OpensManageAchievementsOnThatSurface(bool isFrame)
        {
            var navigation = LibraryTargetNavigation.Resolve(LibraryTargetKeys.NotificationScope(isFrame, null, GameId));

            Assert.AreEqual(LibraryTargetDestination.ManageAchievements, navigation.Destination);
            Assert.AreEqual(GameId, navigation.GameId);
            Assert.AreEqual(isFrame, navigation.IsFrame);
            Assert.IsNull(navigation.Settings);
        }

        [TestMethod]
        public void ShowcasePage_OpensThatPage()
        {
            var navigation = LibraryTargetNavigation.Resolve(LibraryTargetKeys.Showcase("page-1"));

            Assert.AreEqual(LibraryTargetDestination.Showcase, navigation.Destination);
            Assert.AreEqual("page-1", navigation.ShowcasePageId);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("unknown")]
        [DataRow("toast:provider:")]
        public void UnknownKeys_GoNowhere(string key)
        {
            Assert.AreEqual(LibraryTargetDestination.None, LibraryTargetNavigation.Resolve(key).Destination);
        }

        [TestMethod]
        public void GameDataKey_GoesNowhere()
        {
            // Game data is opened from the game data list, not from an item's uses.
            Assert.AreEqual(LibraryTargetDestination.None, LibraryTargetNavigation.Resolve(LibraryTargetKeys.GameData(GameId)).Destination);
        }
    }
}
