using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Playnite;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using PlayniteAchievements.Providers.Xenia;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services;
using System;
using System.Collections.Generic;
using System.IO;

namespace PlayniteAchievements.Providers.Tests
{
    [TestClass]
    public class XeniaScannerTests
    {
        [TestMethod]
        public void ResolveTitleId_ManualOverrideBeatsCacheAndUpdatesCache()
        {
            var tempDir = CreateTempDirectory();
            var gameId = Guid.NewGuid();
            var previousPlugin = PlayniteAchievementsPlugin.Instance;

            try
            {
                var store = new GameCustomDataStore(tempDir);
                store.Save(gameId, new GameCustomDataFile
                {
                    PlayniteGameId = gameId,
                    XeniaTitleIdOverride = "0x4d5307e6"
                });

                PlayniteAchievementsPlugin.Instance = new PlayniteAchievementsPlugin
                {
                    GameCustomDataStore = store
                };

                var cacheDir = Path.Combine(tempDir, "xenia");
                Directory.CreateDirectory(cacheDir);
                File.WriteAllText(
                    Path.Combine(cacheDir, "titleID_cache.json"),
                    JsonConvert.SerializeObject(new List<KeyValuePair<Guid, string>>
                    {
                        new KeyValuePair<Guid, string>(gameId, "11111111")
                    }));

                var scanner = new XeniaScanner(
                    logger: null,
                    playniteApi: new FakePlayniteApi(),
                    providerSettings: new XeniaSettings { AccountPath = tempDir },
                    pluginUserDataPath: tempDir);

                var resolved = scanner.ResolveTitleID(new Game
                {
                    Id = gameId,
                    Name = "Test Game"
                }, out var titleId);

                Assert.IsTrue(resolved);
                Assert.AreEqual("4D5307E6", titleId);
                Assert.IsTrue(scanner.TryGetCachedTitleId(gameId, out var cachedTitleId));
                Assert.AreEqual("4D5307E6", cachedTitleId);
            }
            finally
            {
                PlayniteAchievementsPlugin.Instance = previousPlugin;
                DeleteDirectory(tempDir);
            }
        }

        private static string CreateTempDirectory()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "PlayniteAchievementsTests",
                nameof(XeniaScannerTests),
                Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(path);
            return path;
        }

        private static void DeleteDirectory(string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }

        private sealed class FakePlayniteApi : IPlayniteAPI
        {
            public IMainViewAPI MainView => null;
            public IGameDatabaseAPI Database => null;
            public IDialogsFactory Dialogs => null;
            public IPlaynitePathsAPI Paths => null;
            public INotificationsAPI Notifications => null;
            public IPlayniteInfoAPI ApplicationInfo => null;
            public IWebViewFactory WebViews => null;
            public IResourceProvider Resources => null;
            public IUriHandlerAPI UriHandler => null;
            public IPlayniteSettingsAPI ApplicationSettings => null;
            public IAddons Addons => null;
            public IEmulationAPI Emulation => null;

            public string ExpandGameVariables(Game game, string source) => source;
            public string ExpandGameVariables(Game game, string source, string fallbackValue) => source ?? fallbackValue;
            public GameAction ExpandGameVariables(Game game, GameAction source) => source;
            public void StartGame(Guid id) { }
            public void InstallGame(Guid id) { }
            public void UninstallGame(Guid id) { }
            public void AddCustomElementSupport(Plugin plugin, AddCustomElementSupportArgs args) { }
            public void AddSettingsSupport(Plugin plugin, AddSettingsSupportArgs args) { }
            public void AddConvertersSupport(Plugin plugin, AddConvertersSupportArgs args) { }
        }
    }
}
