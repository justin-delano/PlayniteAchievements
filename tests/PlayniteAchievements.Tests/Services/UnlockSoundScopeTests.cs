using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.GameCustomData;
using PlayniteAchievements.Services.Library;
using PlayniteAchievements.Services.Sound;
using PlayniteAchievements.Services.UI;
using System;
using System.IO;

namespace PlayniteAchievements.Services.Tests
{
    [TestClass]
    [DoNotParallelize]
    public class UnlockSoundScopeTests
    {
        private string _root;
        private GameCustomDataStore _store;

        [TestInitialize]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "PlayAchSoundScope_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _store = new GameCustomDataStore(_root);
        }

        [TestCleanup]
        public void TearDown()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch
            {
            }
        }

        [TestMethod]
        public void Resolve_PrefersTheGameThenThePlatformThenTheGlobalPack()
        {
            var settings = new PersistedSettings();
            settings.UnlockSounds.Rare = "global.wav";
            var gameId = Guid.NewGuid();

            Assert.AreEqual("global.wav", UnlockSoundScope.Resolve(settings, _store, "Steam", gameId).Rare);

            settings.SetProviderUnlockSounds("Steam", new UnlockSoundSettings { Rare = "steam.wav" });
            Assert.AreEqual("steam.wav", UnlockSoundScope.Resolve(settings, _store, "Steam", gameId).Rare);
            Assert.AreEqual("global.wav", UnlockSoundScope.Resolve(settings, _store, "Epic", gameId).Rare);

            UnlockSoundScope.ForGame(_store, gameId, () => settings, () => "Steam")
                .Write(new UnlockSoundSettings { Rare = "game.wav" });
            Assert.AreEqual("game.wav", UnlockSoundScope.Resolve(settings, _store, "Steam", gameId).Rare);
            Assert.AreEqual("steam.wav", UnlockSoundScope.Resolve(settings, _store, "Steam", Guid.NewGuid()).Rare);
        }

        [TestMethod]
        public void OwnedPack_BlankTier_FallsToBundled_NotToTheParentFile()
        {
            var bundled = Path.Combine(_root, "bundled");
            Directory.CreateDirectory(bundled);
            File.WriteAllBytes(Path.Combine(bundled, "rare.mp3"), new byte[] { 1 });
            var globalFile = Path.Combine(_root, "global.wav");
            File.WriteAllBytes(globalFile, new byte[] { 2 });

            var settings = new PersistedSettings();
            settings.UnlockSounds.Rare = globalFile;
            settings.SetProviderUnlockSounds("Steam", new UnlockSoundSettings());
            var resolver = new UnlockSoundResolver(() => settings.UnlockSounds, () => Array.Empty<string>(), bundled, logger: null);

            var resolved = resolver.Resolve(UnlockSoundTier.Rare, UnlockSoundScope.Resolve(settings, _store, "Steam", Guid.Empty));

            Assert.AreEqual(UnlockSoundSource.Default, resolved.Source);
            Assert.AreEqual(Path.Combine(bundled, "rare.mp3"), resolved.Path);
            Assert.AreEqual(globalFile, resolver.Resolve(UnlockSoundTier.Rare).Path, "the global pack keeps its own file");
        }

        [TestMethod]
        public void GameScope_FollowsUntilWritten_AndFollowsAgainAfterANullWrite()
        {
            var settings = new PersistedSettings();
            settings.SetProviderUnlockSounds("Steam", new UnlockSoundSettings { Common = "steam.wav" });
            var gameId = Guid.NewGuid();
            var scope = UnlockSoundScope.ForGame(_store, gameId, () => settings, () => "Steam");

            Assert.IsNull(scope.OwnSounds);
            Assert.AreEqual("steam.wav", scope.EffectiveSounds.Common);

            scope.Write(new UnlockSoundSettings { Common = "game.wav" });
            Assert.AreEqual("game.wav", scope.OwnSounds.Common);

            scope.Write(null);
            Assert.IsNull(scope.OwnSounds);
            Assert.AreEqual("steam.wav", scope.EffectiveSounds.Common);
        }

        [TestMethod]
        public void PlatformScope_FollowsTheGlobalPackUntilWritten()
        {
            var settings = new PersistedSettings();
            settings.UnlockSounds.Common = "global.wav";
            var scope = UnlockSoundScope.ForSettings(settings, "Steam");

            Assert.IsNull(scope.OwnSounds);
            Assert.AreEqual("global.wav", scope.EffectiveSounds.Common);

            scope.Write(new UnlockSoundSettings { Common = "steam.wav" });

            Assert.AreEqual("steam.wav", settings.GetProviderUnlockSounds("Steam").Common);
            Assert.AreEqual("global.wav", settings.UnlockSounds.Common);
        }

        [TestMethod]
        public void SoundsScopeKeys_RoundTrip()
        {
            var gameId = Guid.NewGuid();

            Assert.AreEqual(LibraryTargetKeys.Sounds, LibraryTargetKeys.SoundsScope(null, Guid.Empty));
            Assert.IsTrue(LibraryTargetKeys.TryParseSoundsScope(LibraryTargetKeys.Sounds, out var globalProvider, out var globalGame));
            Assert.IsNull(globalProvider);
            Assert.AreEqual(Guid.Empty, globalGame);

            Assert.IsTrue(LibraryTargetKeys.TryParseSoundsScope(LibraryTargetKeys.SoundsScope("Steam", Guid.Empty), out var provider, out var noGame));
            Assert.AreEqual("Steam", provider);
            Assert.AreEqual(Guid.Empty, noGame);
            Assert.IsFalse(LibraryTargetKeys.IsPerGame(LibraryTargetKeys.SoundsProvider("Steam")));

            var gameKey = LibraryTargetKeys.SoundsScope("Steam", gameId);
            Assert.IsTrue(LibraryTargetKeys.TryParseSoundsScope(gameKey, out var gameProvider, out var parsedGame));
            Assert.IsNull(gameProvider);
            Assert.AreEqual(gameId, parsedGame);
            Assert.IsTrue(LibraryTargetKeys.IsPerGame(gameKey));
            Assert.IsTrue(LibraryTargetKeys.TryGetGameId(gameKey, out var keyGame));
            Assert.AreEqual(gameId, keyGame);

            Assert.IsFalse(LibraryTargetKeys.TryParseSoundsScope("sounds:provider:", out _, out _));
            Assert.IsFalse(LibraryTargetKeys.TryParseSoundsScope("toast:global", out _, out _));
            Assert.IsFalse(LibraryTargetKeys.TryParseNotificationScope(gameKey, out _, out _, out _));
        }
    }
}
