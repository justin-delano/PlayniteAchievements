using Microsoft.VisualStudio.TestTools.UnitTesting;
using Playnite.SDK.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Workshop;
using System;
using System.Collections.Generic;

namespace PlayniteAchievements.Services.Tests
{
    [TestClass]
    public class WorkshopGameMatcherTests
    {
        private static readonly Guid SteamGame = Guid.NewGuid();
        private static readonly Guid OtherGame = Guid.NewGuid();

        [TestMethod]
        public void Match_PrefersProviderIdentityOverName()
        {
            var matcher = CreateMatcher();
            var match = matcher.Match(new[]
            {
                new PortableGameKey { ProviderKey = "Steam", ProviderGameId = 440, Name = "Something Else" },
                new PortableGameKey { Name = "Team Fortress 2" }
            });

            Assert.IsNotNull(match);
            Assert.AreEqual(SteamGame, match.PlayniteGameId);
            Assert.AreEqual(WorkshopGameMatchConfidence.Provider, match.Confidence);
        }

        [TestMethod]
        public void Match_FallsBackToUniqueNormalizedName()
        {
            var matcher = CreateMatcher();
            var match = matcher.Match(new[] { new PortableGameKey { Name = "team-fortress 2!" } });

            Assert.IsNotNull(match);
            Assert.AreEqual(SteamGame, match.PlayniteGameId);
            Assert.AreEqual(WorkshopGameMatchConfidence.Name, match.Confidence);
        }

        [TestMethod]
        public void Match_AmbiguousName_ReturnsNull_AndListsBothCandidates()
        {
            // Game.Platforms resolves through Playnite's database, so the platform tie-break is
            // not reachable here; without it two same-named entries stay ambiguous.
            var pc = new Game("Hollow Knight") { Id = Guid.NewGuid() };
            var sw = new Game("Hollow Knight") { Id = Guid.NewGuid() };
            var matcher = new WorkshopGameMatcher(() => Array.Empty<GameAchievementData>(), () => new[] { pc, sw });

            Assert.IsNull(matcher.Match(new[] { new PortableGameKey { Name = "Hollow Knight" } }), "two library entries with no platform hint is ambiguous");
            Assert.IsNull(matcher.Match(new[] { new PortableGameKey { Name = "Hollow Knight", Platform = "Nintendo Switch" } }), "no entry carries a platform");
            Assert.AreEqual(2, matcher.Candidates(new[] { new PortableGameKey { Name = "Hollow Knight" } }).Count);
        }

        [TestMethod]
        public void Match_NoKeys_ReturnsNull()
        {
            var matcher = CreateMatcher();
            Assert.IsNull(matcher.Match(null));
            Assert.IsNull(matcher.Match(Array.Empty<PortableGameKey>()));
            Assert.IsNull(matcher.Match(new[] { new PortableGameKey { ProviderKey = "Steam", ProviderGameId = 999 } }));
        }

        private static WorkshopGameMatcher CreateMatcher()
        {
            var cached = new[]
            {
                new GameAchievementData { ProviderKey = "Steam", AppId = 440, GameName = "Team Fortress 2", PlayniteGameId = SteamGame },
                new GameAchievementData { ProviderKey = "RetroAchievements", AppId = 10003, GameName = "Sonic", PlayniteGameId = OtherGame }
            };
            var library = new[]
            {
                new Game("Team Fortress 2") { Id = SteamGame },
                new Game("Sonic the Hedgehog") { Id = OtherGame }
            };
            return new WorkshopGameMatcher(() => cached, () => library);
        }
    }
}
