using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Providers.GOG;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

namespace PlayniteAchievements.Gog.Tests
{
    [TestClass]
    public class GogApiClientTests
    {
        [TestMethod]
        public async Task GetClientIdAsync_UsesPersistentCacheAfterInitialLookup()
        {
            var tempRoot = CreateTempRoot();
            try
            {
                var cache = new GogClientIdCacheStore(tempRoot, logger: null, ttl: TimeSpan.FromDays(30));
                var handler = new StubHttpMessageHandler((request, callCount) =>
                {
                    if (callCount > 1)
                    {
                        throw new InvalidOperationException("Unexpected second network request; expected cache hit.");
                    }

                    return JsonResponse(HttpStatusCode.OK, "{ \"id\": 1207664700, \"client_id\": \"gog-client-1\" }");
                });

                using (var httpClient = new HttpClient(handler))
                {
                    var apiClient = new GogApiClient(httpClient, logger: null, tokenProvider: new StubTokenProvider(), clientIdCacheStore: cache);

                    var first = await apiClient.GetClientIdAsync("1207664700", CancellationToken.None).ConfigureAwait(false);
                    var second = await apiClient.GetClientIdAsync("1207664700", CancellationToken.None).ConfigureAwait(false);

                    Assert.AreEqual("gog-client-1", first);
                    Assert.AreEqual("gog-client-1", second);
                    Assert.AreEqual(1, handler.CallCount);
                }
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }
        }

        [TestMethod]
        public async Task GetClientIdAsync_TransientFailureDoesNotPoisonCache()
        {
            var tempRoot = CreateTempRoot();
            try
            {
                var cache = new GogClientIdCacheStore(tempRoot, logger: null, ttl: TimeSpan.FromDays(30));
                var handler = new StubHttpMessageHandler((request, callCount) =>
                {
                    if (callCount == 1)
                    {
                        return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                        {
                            Content = new StringContent(string.Empty)
                        };
                    }

                    return JsonResponse(HttpStatusCode.OK, "{ \"id\": 1207664701, \"client_id\": \"gog-client-2\" }");
                });

                using (var httpClient = new HttpClient(handler))
                {
                    var apiClient = new GogApiClient(httpClient, logger: null, tokenProvider: new StubTokenProvider(), clientIdCacheStore: cache);

                    var firstError = await Assert.ThrowsExceptionAsync<GogApiHttpException>(
                        () => apiClient.GetClientIdAsync("1207664701", CancellationToken.None)).ConfigureAwait(false);

                    Assert.IsTrue(GogApiClient.IsTransientError(firstError));

                    var recovered = await apiClient.GetClientIdAsync("1207664701", CancellationToken.None).ConfigureAwait(false);
                    Assert.AreEqual("gog-client-2", recovered);
                    Assert.AreEqual(2, handler.CallCount);
                }
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }
        }

        [TestMethod]
        public async Task GetAchievementsAsync_SendsLocaleQueryAndAcceptLanguageHeader()
        {
            var tempRoot = CreateTempRoot();
            try
            {
                var cache = new GogClientIdCacheStore(tempRoot, logger: null, ttl: TimeSpan.FromDays(30));
                Uri capturedUri = null;
                string capturedAcceptLanguage = null;

                var handler = new StubHttpMessageHandler((request, callCount) =>
                {
                    capturedUri = request.RequestUri;
                    if (request.Headers.TryGetValues("Accept-Language", out var languageValues))
                    {
                        capturedAcceptLanguage = string.Join(",", languageValues);
                    }

                    return JsonResponse(
                        HttpStatusCode.OK,
                        "{ \"items\": [ { \"id\": \"1\", \"achievement_key\": \"first_win\", \"name\": \"Erster Sieg\", \"description\": \"Gewinne einmal\", \"visible\": true } ] }");
                });

                using (var httpClient = new HttpClient(handler))
                {
                    var apiClient = new GogApiClient(httpClient, logger: null, tokenProvider: new StubTokenProvider(), clientIdCacheStore: cache);
                    var items = await apiClient.GetAchievementsAsync("client-123", "user-456", "german", CancellationToken.None).ConfigureAwait(false);

                    Assert.AreEqual(1, items.Count);
                    Assert.IsNotNull(capturedUri);
                    StringAssert.Contains(capturedUri.Query, "locale=de-DE");
                    Assert.AreEqual("de-DE", capturedAcceptLanguage);
                }
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }
        }

        private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
        {
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json ?? string.Empty, Encoding.UTF8, "application/json")
            };
        }

        private static string CreateTempRoot()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "PlayniteAchievements.Gog.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
                // Best effort cleanup for test temp files.
            }
        }

        private sealed class StubTokenProvider : IGogTokenProvider
        {
            public string GetAccessToken()
            {
                return "stub-token";
            }
        }

        private sealed class StubHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, int, HttpResponseMessage> _responseFactory;

            public int CallCount { get; private set; }

            public StubHttpMessageHandler(Func<HttpRequestMessage, int, HttpResponseMessage> responseFactory)
            {
                _responseFactory = responseFactory ?? throw new ArgumentNullException(nameof(responseFactory));
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                CallCount++;
                return Task.FromResult(_responseFactory(request, CallCount));
            }
        }
    }
}
