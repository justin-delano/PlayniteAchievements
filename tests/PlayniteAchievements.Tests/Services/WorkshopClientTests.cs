using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Services.Workshop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Services.Tests
{
    [TestClass]
    [DoNotParallelize]
    public class WorkshopClientTests
    {
        private const string SampleIndex = @"{
  ""schemaVersion"": 1,
  ""generated"": ""2026-10-03T12:00:00Z"",
  ""commit"": ""0123456789012345678901234567890123456789"",
  ""repository"": ""justin-delano/PlayniteAchievements-Workshop"",
  ""items"": [
    {
      ""schemaVersion"": 1, ""id"": ""bundles/neon"", ""kind"": ""Bundle"", ""name"": ""Neon"", ""description"": ""Glow."",
      ""author"": ""someone"", ""version"": ""1.2.0"", ""license"": ""CC-BY-4.0"", ""tags"": [""dark""], ""minPluginVersion"": ""4.1.0"",
      ""created"": ""2026-10-01"", ""updated"": ""2026-10-03"",
      ""contents"": { ""parts"": [""Colors"", ""Toast""] },
      ""package"": { ""file"": ""neon-1.2.0.pabundle"", ""formatKind"": ""PlayniteAchievements.Bundle"", ""formatVersion"": 1, ""sizeBytes"": 5, ""sha256"": ""HASH"",
                     ""release"": { ""tag"": ""bundles/neon"", ""url"": ""https://example.invalid/neon-1.2.0.pabundle"" } },
      ""preview"": ""preview.png"",
      ""downloads"": { ""total"": 42, ""current"": 7 },
      ""urls"": { ""package"": ""https://example.invalid/neon-1.2.0.pabundle"", ""preview"": null, ""readme"": null, ""folder"": ""https://github.com/x/y/tree/main/bundles/neon"" }
    },
    { ""id"": """", ""kind"": ""Bundle"" },
    {
      ""id"": ""game-data/steam-440/icons"", ""kind"": ""GameCustomData"", ""name"": ""Icons"", ""version"": ""1.0.0"",
      ""game"": { ""name"": ""Team Fortress 2"", ""keys"": [ { ""providerKey"": ""Steam"", ""providerGameId"": 440 } ] },
      ""package"": { ""file"": ""icons-1.0.0.pa"", ""sha256"": ""x"", ""release"": { ""url"": ""https://example.invalid/icons.pa"" } },
      ""downloads"": { ""total"": 1, ""current"": 1 }
    }
  ]
}";

        [TestMethod]
        public async Task FetchIndex_ParsesItems_AndDropsEntriesWithoutIdOrPackage()
        {
            var handler = new FakeHandler(request => Text(SampleIndex));
            var client = new WorkshopClient(() => "https://example.invalid/index/v1.json", null, null, handler);

            var index = await client.FetchIndexAsync(CancellationToken.None);

            Assert.AreEqual(1, index.SchemaVersion);
            Assert.AreEqual(2, index.Items.Count);
            var theme = index.Items[0];
            Assert.AreEqual(WorkshopItemKind.Bundle, theme.Kind);
            Assert.AreEqual(42, theme.Downloads.Total);
            CollectionAssert.AreEqual(new[] { "Colors", "Toast" }, new List<string>(theme.PartNames));
            var game = index.Items[1];
            Assert.AreEqual(440, game.Game.Keys[0].ProviderGameId);
            Assert.AreEqual("Steam", game.Game.Keys[0].ProviderKey);
            StringAssert.StartsWith(handler.LastUrl, "https://example.invalid/index/v1.json?t=");
        }

        [TestMethod]
        public async Task FetchIndex_RejectsNewerSchema()
        {
            var handler = new FakeHandler(request => Text(SampleIndex.Replace("\"schemaVersion\": 1,\n  \"generated\"", "\"schemaVersion\": 99,\n  \"generated\"")));
            var client = new WorkshopClient(() => null, null, null, handler);
            StringAssert.StartsWith(client.IndexUrl, WorkshopClient.DefaultIndexUrl);

            try
            {
                await client.FetchIndexAsync(CancellationToken.None);
                Assert.Fail("expected rejection");
            }
            catch (InvalidOperationException ex)
            {
                StringAssert.Contains(ex.Message, "newer");
            }
        }

        [TestMethod]
        public async Task DownloadPackage_VerifiesSha256_AndDeletesOnMismatch()
        {
            var bytes = Encoding.ASCII.GetBytes("PK-package");
            string hash;
            using (var sha = SHA256.Create())
            {
                hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            }

            var handler = new FakeHandler(request => Bytes(bytes));
            var client = new WorkshopClient(() => null, null, null, handler);
            var item = new WorkshopItem
            {
                Id = "bundles/neon",
                Package = new WorkshopPackage { Sha256 = hash, SizeBytes = bytes.Length },
                Urls = new WorkshopUrls { Package = "https://example.invalid/neon.pabundle" }
            };

            var dir = Path.Combine(Path.GetTempPath(), "PlayniteAchievementsTests", Guid.NewGuid().ToString("N"));
            try
            {
                var good = Path.Combine(dir, "good.pabundle");
                long reported = 0;
                await client.DownloadPackageAsync(item, good, new Progress<long>(n => reported = n), CancellationToken.None);
                Assert.IsTrue(File.Exists(good));
                CollectionAssert.AreEqual(bytes, File.ReadAllBytes(good));

                item.Package.Sha256 = new string('0', 64);
                var bad = Path.Combine(dir, "bad.pabundle");
                try
                {
                    await client.DownloadPackageAsync(item, bad, null, CancellationToken.None);
                    Assert.Fail("expected checksum failure");
                }
                catch (InvalidOperationException ex)
                {
                    StringAssert.Contains(ex.Message, "checksum");
                }

                Assert.IsFalse(File.Exists(bad), "a mismatched download is deleted");
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        [TestMethod]
        public void VerifyPackage_MatchesTheIndexHash_AndRejectsAMissingOrChangedFile()
        {
            var bytes = Encoding.ASCII.GetBytes("PK-package");
            string hash;
            using (var sha = SHA256.Create())
            {
                hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToUpperInvariant();
            }

            var item = new WorkshopItem { Id = "bundles/neon", Package = new WorkshopPackage { Sha256 = hash } };
            var dir = Path.Combine(Path.GetTempPath(), "PlayniteAchievementsTests", Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "neon.pabundle");
                File.WriteAllBytes(path, bytes);

                Assert.IsTrue(WorkshopClient.VerifyPackage(item, path), "the hash compares case-insensitively");
                Assert.IsFalse(WorkshopClient.VerifyPackage(item, Path.Combine(dir, "missing.pabundle")));

                File.WriteAllBytes(path, Encoding.ASCII.GetBytes("PK-changed"));
                Assert.IsFalse(WorkshopClient.VerifyPackage(item, path));

                item.Package.Sha256 = null;
                Assert.IsTrue(WorkshopClient.VerifyPackage(item, path), "an item listed without a hash accepts any existing file");
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        private static HttpResponseMessage Text(string body) =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        private static HttpResponseMessage Bytes(byte[] body) =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

        private sealed class FakeHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

            public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            {
                _respond = respond;
            }

            public string LastUrl { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastUrl = request.RequestUri.ToString();
                return Task.FromResult(_respond(request));
            }
        }

        [TestMethod]
        public void ReleaseApiUrl_MapsGitHubReleasePages_AndRejectsOtherAddresses()
        {
            Assert.AreEqual(
                "https://api.github.com/repos/justin-delano/PlayniteAchievements-Workshop/releases/tags/bundles/neon",
                WorkshopClient.ReleaseApiUrl("https://github.com/justin-delano/PlayniteAchievements-Workshop/releases/tag/bundles/neon"));
            Assert.AreEqual(
                "https://api.github.com/repos/o/r/releases/tags/colors%2Fset",
                WorkshopClient.ReleaseApiUrl("https://github.com/o/r/releases/tag/colors%2Fset"),
                "percent-encoded tags pass through unchanged");
            Assert.IsNull(WorkshopClient.ReleaseApiUrl("https://gitlab.com/o/r/releases/tag/x"));
            Assert.IsNull(WorkshopClient.ReleaseApiUrl("https://github.com/o/r/releases"));
            Assert.IsNull(WorkshopClient.ReleaseApiUrl("https://github.com/o/r/blob/main/README.md"));
            Assert.IsNull(WorkshopClient.ReleaseApiUrl(null));
            Assert.IsNull(WorkshopClient.ReleaseApiUrl("not a url"));
        }

        [TestMethod]
        public void SumAssetDownloads_AddsEveryAsset_AndIsNullWithoutAnAssetsArray()
        {
            Assert.AreEqual(12L, WorkshopClient.SumAssetDownloads(
                @"{ ""tag_name"": ""bundles/neon"", ""assets"": [ { ""download_count"": 5 }, { ""download_count"": 7 }, { ""name"": ""no-count"" } ] }"));
            Assert.AreEqual(0L, WorkshopClient.SumAssetDownloads(@"{ ""assets"": [] }"));
            Assert.IsNull(WorkshopClient.SumAssetDownloads(@"{ ""message"": ""Not Found"" }"));
            Assert.IsNull(WorkshopClient.SumAssetDownloads(""));
        }

        [TestMethod]
        public void StripImageBlock_RemovesALeadingBlockAndItsBlankLines_LF()
        {
            var readme = "<!-- workshop:images -->\n![Cover](cover.png)\n![Preview](preview.png)\n<!-- /workshop:images -->\n\n# Neon\n\nGlow.\n";

            Assert.AreEqual("# Neon\n\nGlow.\n", WorkshopClient.StripImageBlock(readme));
        }

        [TestMethod]
        public void StripImageBlock_RemovesALeadingBlockAndItsBlankLines_CRLF()
        {
            var readme = "\r\n<!-- workshop:images -->\r\n![Preview](preview.png)\r\n<!-- /workshop:images -->\r\n\r\n\r\n# Neon\r\n\r\nGlow.\r\n";

            Assert.AreEqual("# Neon\r\n\r\nGlow.\r\n", WorkshopClient.StripImageBlock(readme));
        }

        [TestMethod]
        public void StripImageBlock_KeepsTheTextAroundABlockInTheMiddleOneBlankLineApart()
        {
            var lf = "Intro.\n\n<!-- workshop:images -->\n![Cover](cover.png)\n<!-- /workshop:images -->\n\nRest.";
            var crlf = lf.Replace("\n", "\r\n");

            Assert.AreEqual("Intro.\n\nRest.", WorkshopClient.StripImageBlock(lf));
            Assert.AreEqual("Intro.\r\n\r\nRest.", WorkshopClient.StripImageBlock(crlf));
        }

        [TestMethod]
        public void StripImageBlock_LeavesTextWithoutABlockUnchanged()
        {
            const string readme = "# Neon\r\n\r\n<!-- an ordinary comment -->\r\nGlow.\r\n";

            Assert.AreEqual(readme, WorkshopClient.StripImageBlock(readme));
            Assert.IsNull(WorkshopClient.StripImageBlock(null));
            Assert.AreEqual(string.Empty, WorkshopClient.StripImageBlock(string.Empty));
        }
    }
}
