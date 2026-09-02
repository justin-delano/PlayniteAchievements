using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Providers;
using PlayniteAchievements.Services.CustomProviders;

namespace PlayniteAchievements.Services.Tests
{
    [TestClass]
    public class CustomProviderKeysTests
    {
        [TestMethod]
        public void Build_And_TryGetId_RoundTrip()
        {
            var key = CustomProviderKeys.Build(" abc123 ");

            Assert.AreEqual("Custom:abc123", key);
            Assert.IsTrue(CustomProviderKeys.TryGetId(key, out var id));
            Assert.AreEqual("abc123", id);
            Assert.IsTrue(CustomProviderKeys.IsCustomProviderKey("custom:ABC"));
        }

        [TestMethod]
        public void BareCustom_IsNotACustomProviderKey()
        {
            Assert.IsFalse(CustomProviderKeys.TryGetId("Custom", out _));
            Assert.IsFalse(CustomProviderKeys.TryGetId("Custom:", out _));
            Assert.IsFalse(CustomProviderKeys.TryGetId("Steam", out _));
            Assert.IsTrue(CustomProviderKeys.IsBaseKey(" custom "));
            Assert.IsNull(CustomProviderKeys.Build("  "));
        }

        [TestMethod]
        public void IconKey_RoundTrip()
        {
            var iconKey = CustomProviderKeys.BuildIconKey("abc");

            Assert.AreEqual("ProviderIconCustom:abc", iconKey);
            Assert.IsTrue(CustomProviderKeys.TryGetIdFromIconKey(iconKey, out var id));
            Assert.AreEqual("abc", id);
            Assert.IsFalse(CustomProviderKeys.TryGetIdFromIconKey("ProviderIconManual", out _));
        }

        [TestMethod]
        public void ProviderRegistry_ResolvesCustomProviderNameAndVisualsThroughHook()
        {
            var previous = ProviderRegistry.CustomProviderResolver;
            try
            {
                ProviderRegistry.CustomProviderResolver = id =>
                    id == "abc" ? new CustomProviderVisuals("My Shelf", "#123456") : null;

                Assert.AreEqual("My Shelf", ProviderRegistry.GetLocalizedName("Custom:abc"));

                Assert.IsTrue(ProviderRegistry.TryResolveProviderVisuals("Custom:abc", out var iconKey, out var colorHex));
                Assert.AreEqual("ProviderIconCustom:abc", iconKey);
                Assert.AreEqual("#123456", colorHex);

                // An unknown id and the bare key both borrow the Manual provider's icon.
                Assert.IsTrue(ProviderRegistry.TryResolveProviderVisuals("Custom:missing", out var fallbackIconKey, out var fallbackColor));
                Assert.AreEqual(CustomProviderKeys.BaseIconKey, fallbackIconKey);
                Assert.IsFalse(string.IsNullOrWhiteSpace(fallbackColor));

                Assert.IsTrue(ProviderRegistry.TryResolveProviderVisuals("Custom", out var baseIconKey, out _));
                Assert.AreEqual(CustomProviderKeys.BaseIconKey, baseIconKey);
            }
            finally
            {
                ProviderRegistry.CustomProviderResolver = previous;
            }
        }
    }
}
