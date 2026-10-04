using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Workshop;
using System;
using System.Collections.Generic;
using System.IO;

namespace PlayniteAchievements.Services.Tests
{
    [TestClass]
    [DoNotParallelize]
    public class WorkshopUndoStoreTests
    {
        [TestMethod]
        public void Snapshot_ThenRestore_PutsOnlyTheSnapshottedSlicesBack()
        {
            WithTemp(dir =>
            {
                var store = new WorkshopUndoStore(dir);
                var persisted = new PersistedSettings();
                persisted.RarityColors = new RarityColorSettings { Common = "#111111" };
                persisted.UnlockSounds.Rare = @"C:\before\rare.wav";
                persisted.NotificationStyle.Toast.HeaderTexts.UnlockHeader = "Before";

                var id = store.Snapshot(persisted, WorkshopSettingsSlices.Colors | WorkshopSettingsSlices.NotificationStyle, "bundles/neon", "Neon");
                Assert.IsNotNull(id);

                // The install changes everything; only colors and the style were snapshotted.
                persisted.RarityColors = new RarityColorSettings { Common = "#222222" };
                persisted.UnlockSounds.Rare = @"C:\after\rare.wav";
                persisted.NotificationStyle.Toast.HeaderTexts.UnlockHeader = "After";

                var restored = new WorkshopUndoStore(dir).Restore(id, persisted);

                Assert.AreEqual(WorkshopSettingsSlices.Colors | WorkshopSettingsSlices.NotificationStyle, restored);
                Assert.AreEqual("#111111", persisted.RarityColors.Common);
                Assert.AreEqual("Before", persisted.NotificationStyle.Toast.HeaderTexts.UnlockHeader);
                Assert.AreEqual(@"C:\after\rare.wav", persisted.UnlockSounds.Rare, "sounds were not part of the snapshot");
            });
        }

        [TestMethod]
        public void List_IsNewestFirst_AndTrimmedToMaxEntries()
        {
            WithTemp(dir =>
            {
                var store = new WorkshopUndoStore(dir);
                var persisted = new PersistedSettings();
                for (var i = 0; i < WorkshopUndoStore.MaxEntries + 3; i++)
                {
                    store.Snapshot(persisted, WorkshopSettingsSlices.Sounds, "sound-packs/p" + i, "Pack " + i);
                }

                var entries = store.List();
                Assert.AreEqual(WorkshopUndoStore.MaxEntries, entries.Count);
                Assert.AreEqual("Pack " + (WorkshopUndoStore.MaxEntries + 2), entries[0].ItemName);
            });
        }

        [TestMethod]
        public void Restore_UnknownSnapshot_ChangesNothing()
        {
            WithTemp(dir =>
            {
                var store = new WorkshopUndoStore(dir);
                var persisted = new PersistedSettings();
                persisted.RarityColors = new RarityColorSettings { Common = "#333333" };

                Assert.AreEqual(WorkshopSettingsSlices.None, store.Restore("missing", persisted));
                Assert.AreEqual("#333333", persisted.RarityColors.Common);
                Assert.IsNull(store.Snapshot(persisted, WorkshopSettingsSlices.None, "x", "x"));
            });
        }

        private static void WithTemp(Action<string> body)
        {
            var dir = Path.Combine(Path.GetTempPath(), "PlayniteAchievementsTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                body(dir);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
}
