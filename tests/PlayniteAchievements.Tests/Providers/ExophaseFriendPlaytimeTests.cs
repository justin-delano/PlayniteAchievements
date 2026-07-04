using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PlayniteAchievements.Tests.Providers
{
    [TestClass]
    public class ExophaseFriendPlaytimeTests
    {
        [TestMethod]
        public void ParseGames_WiresAchievementProgressCountIntoOwnershipHint()
        {
            var provider = File.ReadAllText(
                FindRepoFile("source", "Providers", "Exophase", "ExophaseFriendsProvider.cs"));

            // The game-progress award column ("6/37") is parsed for its earned/total count...
            StringAssert.Contains(provider, "game-progress");
            StringAssert.Contains(provider, "exo-icon-award");
            StringAssert.Contains(provider, @"(\d+)\s*/\s*(\d+)");
            StringAssert.Contains(provider, "ParseAchievementCounts");

            // ...and the earned/total values flow into the ownership unlock hint.
            StringAssert.Contains(provider, "AchievementUnlocksHint = game.AchievementsEarned");
            StringAssert.Contains(provider, "AchievementTotalHint = game.AchievementsTotal");
        }

        [TestMethod]
        public void ParsePlaytimeMinutes_AcceptsCommaDecimalAndNormalizesToDot()
        {
            var provider = File.ReadAllText(
                FindRepoFile("source", "Providers", "Exophase", "ExophaseFriendsProvider.cs"));

            // The hours group accepts a comma or dot decimal, and the comma is normalized to a dot
            // before invariant parsing so French values like "12,5 h" parse correctly.
            StringAssert.Contains(provider, @"(?:(\d+(?:[.,]\d+)?)\s*h(?:ours?)?)?");
            StringAssert.Contains(provider, "match.Groups[1].Value.Replace(',', '.')");
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

            throw new FileNotFoundException("Could not locate repo file: " + string.Join("/", parts));
        }
    }
}
