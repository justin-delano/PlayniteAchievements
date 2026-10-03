using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using PlayniteAchievements.Services.Workshop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Services.Tests
{
    [TestClass]
    [DoNotParallelize]
    public class WorkshopSubmissionClientTests
    {
        [TestMethod]
        public async Task Upload_SingleAndMultipart_FollowTheServiceContract()
        {
            var dir = Path.Combine(Path.GetTempPath(), "PlayniteAchievementsTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var small = Path.Combine(dir, "small.pasounds");
                File.WriteAllBytes(small, new byte[100]);
                var large = Path.Combine(dir, "large.pa");
                File.WriteAllBytes(large, new byte[250]);

                var calls = new List<string>();
                var putBodies = new List<long>();
                var handler = new FakeHandler(async request =>
                {
                    calls.Add(request.Method + " " + request.RequestUri.AbsolutePath);
                    if (request.Method == HttpMethod.Put)
                    {
                        var body = await request.Content.ReadAsByteArrayAsync();
                        putBodies.Add(body.Length);
                        var response = new HttpResponseMessage(HttpStatusCode.OK);
                        response.Headers.ETag = new EntityTagHeaderValue("\"etag-" + putBodies.Count + "\"");
                        return response;
                    }

                    var json = JObject.Parse(await request.Content.ReadAsStringAsync());
                    if (request.RequestUri.AbsolutePath == "/v1/uploads")
                    {
                        var size = json.Value<long>("sizeBytes");
                        return size <= 100
                            ? Json("{\"key\":\"2026-10-03/u/small.pasounds\",\"url\":\"https://r2.invalid/single\",\"multipart\":false}")
                            : Json("{\"key\":\"2026-10-03/u/large.pa\",\"uploadId\":\"up1\",\"partSize\":100,\"multipart\":true,\"parts\":[{\"partNumber\":1,\"url\":\"https://r2.invalid/p1\"},{\"partNumber\":2,\"url\":\"https://r2.invalid/p2\"},{\"partNumber\":3,\"url\":\"https://r2.invalid/p3\"}]}");
                    }

                    if (request.RequestUri.AbsolutePath == "/v1/uploads/complete")
                    {
                        return Json("{\"key\":\"" + json.Value<string>("key") + "\",\"sizeBytes\":1}");
                    }

                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                });

                var client = new WorkshopSubmissionClient(() => "https://service.invalid/", handler);
                Assert.AreEqual("https://service.invalid", client.ServiceUrl);

                var smallKey = await client.UploadAsync(small, "application/zip", null, CancellationToken.None);
                Assert.AreEqual("2026-10-03/u/small.pasounds", smallKey);
                CollectionAssert.AreEqual(new[] { 100L }, putBodies);

                putBodies.Clear();
                long lastProgress = 0;
                var largeKey = await client.UploadAsync(large, "application/zip", new Progress<long>(n => lastProgress = n), CancellationToken.None);
                Assert.AreEqual("2026-10-03/u/large.pa", largeKey);
                CollectionAssert.AreEqual(new[] { 100L, 100L, 50L }, putBodies, "parts are partSize each with a short final part");
                Assert.AreEqual(250, lastProgress);
                CollectionAssert.Contains(calls, "POST /v1/uploads/complete");
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        [TestMethod]
        public async Task Submit_SendsFormLabelsAndHash_AndSurfacesServiceErrors()
        {
            JObject sent = null;
            var handler = new FakeHandler(async request =>
            {
                sent = JObject.Parse(await request.Content.ReadAsStringAsync());
                return Json("{\"issueNumber\":12,\"issueUrl\":\"https://github.com/x/y/issues/12\"}");
            });
            var client = new WorkshopSubmissionClient(() => "https://service.invalid", handler);

            var receipt = await client.SubmitAsync(
                new WorkshopSubmission { Kind = WorkshopItemKind.GameCustomData, Name = "Icons", Author = "Me", Description = "d", License = "CC0-1.0" },
                new string('a', 64),
                "2026-10-03/u/icons.pa",
                null,
                "4.1.0",
                CancellationToken.None);

            Assert.AreEqual(12, receipt.IssueNumber);
            Assert.AreEqual("Per-game custom data", sent.Value<string>("kind"));
            Assert.AreEqual(new string('a', 64), sent.Value<string>("submitterHash"));
            Assert.AreEqual("4.1.0", sent.Value<string>("pluginVersion"));

            var failing = new WorkshopSubmissionClient(() => "https://service.invalid",
                new FakeHandler(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)429)
                {
                    Content = new StringContent("{\"error\":\"Daily submission limit reached (10).\"}", Encoding.UTF8, "application/json")
                })));
            try
            {
                await failing.GetStatusAsync(1, CancellationToken.None);
                Assert.Fail("expected the service error");
            }
            catch (InvalidOperationException ex)
            {
                StringAssert.Contains(ex.Message, "Daily submission limit");
            }
        }

        private static HttpResponseMessage Json(string body) =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        private sealed class FakeHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _respond;

            public FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
            {
                _respond = respond;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                _respond(request);
        }
    }
}
