using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.ViewModels.Items;

namespace PlayniteAchievements.Tests.ViewModels
{
    [TestClass]
    public class FriendAchievementSpoilerTests
    {
        [DataTestMethod]
        [DataRow(true, false, true, false, DisplayName = "Spoilers hidden, friend unlocked, user has not: obscured")]
        [DataRow(true, false, false, false, DisplayName = "Spoilers hidden, neither unlocked: obscured")]
        [DataRow(true, true, false, true, DisplayName = "Spoilers hidden, user unlocked, friend has not: revealed")]
        [DataRow(true, true, true, true, DisplayName = "Spoilers hidden, both unlocked: revealed")]
        [DataRow(false, false, true, true, DisplayName = "Spoilers shown, friend unlock governs: revealed")]
        [DataRow(false, true, false, false, DisplayName = "Spoilers shown, friend unlock governs: obscured")]
        public void UnlockedForVisibility_AppliesOwnUnlockStateWhenHidingSpoilers(
            bool hideFriendSpoilers,
            bool unlockedBySelf,
            bool friendUnlocked,
            bool expected)
        {
            var item = new FriendAchievementDisplayItem
            {
                HideFriendSpoilers = hideFriendSpoilers,
                UnlockedBySelf = unlockedBySelf,
                Unlocked = friendUnlocked
            };

            Assert.AreEqual(expected, item.UnlockedForVisibility);
        }
    }
}
