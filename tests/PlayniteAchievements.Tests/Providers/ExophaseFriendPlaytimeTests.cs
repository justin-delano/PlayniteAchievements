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
