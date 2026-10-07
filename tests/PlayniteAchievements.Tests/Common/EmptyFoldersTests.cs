using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Common;
using System;
using System.IO;

namespace PlayniteAchievements.Common.Tests
{
    [TestClass]
    public class EmptyFoldersTests
    {
        private string _root;

        [TestInitialize]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "PlayAchEmptyFolders_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
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
        public void RemoveUpTo_RemovesTheEmptyChainUpToTheRoot()
        {
            var store = Path.Combine(_root, "custom_templates");
            var scope = Path.Combine(store, "games", "x");
            Directory.CreateDirectory(scope);

            EmptyFolders.RemoveUpTo(scope, store);

            Assert.IsFalse(Directory.Exists(store));
            Assert.IsTrue(Directory.Exists(_root), "the walk stops at the root");
        }

        [TestMethod]
        public void RemoveUpTo_StopsAtAFolderThatStillHoldsSomething()
        {
            var store = Path.Combine(_root, "custom_templates");
            var scope = Path.Combine(store, "games", "x");
            Directory.CreateDirectory(scope);
            File.WriteAllText(Path.Combine(store, "games", "kept.xaml"), "<x/>");

            EmptyFolders.RemoveUpTo(scope, store);

            Assert.IsFalse(Directory.Exists(scope));
            Assert.IsTrue(File.Exists(Path.Combine(store, "games", "kept.xaml")));
        }

        [TestMethod]
        public void RemoveUpTo_SkipsAMissingFolder_AndLeavesFoldersOutsideTheRoot()
        {
            var store = Path.Combine(_root, "showcase");
            Directory.CreateDirectory(store);
            var outside = Path.Combine(_root, "other");
            Directory.CreateDirectory(outside);

            EmptyFolders.RemoveUpTo(Path.Combine(store, "images"), store);
            EmptyFolders.RemoveUpTo(outside, store);

            Assert.IsFalse(Directory.Exists(store));
            Assert.IsTrue(Directory.Exists(outside));
        }
    }
}
