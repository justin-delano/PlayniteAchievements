using Newtonsoft.Json;
using Playnite.SDK;
using PlayniteAchievements.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.Ubisoft
{
    /// <summary>
    /// Raised when ubiservices rejects the session (HTTP 401/403), so the refresh asks for a sign-in.
    /// </summary>
    internal sealed class UbisoftAuthException : Exception
    {
        public UbisoftAuthException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Client over public-ubiservices.ubi.com. Calls are made as the Ubisoft Connect desktop client's
    /// application: the entitlement service only accepts a ticket issued to that application, and
    /// <see cref="CreateClientSessionAsync"/> re-issues the web session's ticket for it. Tickets and
    /// session ids are never logged.
    /// </summary>
    internal sealed class UbisoftApiClient : IDisposable
    {
        /// <summary>The Ubisoft Connect desktop client's application id.</summary>
        internal const string ClientAppId = "f68a4bb5-608a-4ff2-8123-be8ef797e0a6";

        private const string SessionsUrl = "https://public-ubiservices.ubi.com/v3/profiles/sessions";
        private const string EntitlementsUrl = "https://api-ubiservices.ubi.com/v1/profiles/me/global/ubiconnect/entitlement/api/entitlements";
        private const string GraphQlUrl = "https://public-ubiservices.ubi.com/v1/profiles/me/uplay/graphql";
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36";
        private const string NotFoundCode = "NOT_FOUND";

        // The Ubisoft Connect client's own query (challenges bundle), trimmed to the fields mapped.
        private const string AchievementsQuery = @"query GetAchievements($spaceId: String!, $productId: Int) {
  game(spaceId: $spaceId) {
    id
    name
    viewer {
      meta {
        id
        achievements(productId: $productId) {
          totalCount
          completedCount
          nodes {
            id
            achievementId
            title
            description
            icon
            viewer { meta { id completionDate isCompleted } }
          }
        }
      }
    }
  }
}";

        private readonly ILogger _logger;
        private readonly HttpClientHandler _handler;
        private readonly HttpClient _http;

        public UbisoftApiClient(ILogger logger)
        {
            _logger = logger;
            _handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                UseCookies = false
            };
            _http = new HttpClient(_handler) { Timeout = TimeSpan.FromSeconds(30) };
        }

        public void Dispose()
        {
            _http?.Dispose();
            _handler?.Dispose();
        }

        /// <summary>
        /// Re-issues a live session's ticket for the desktop client application. The session id and
        /// expiry carry over; nothing is consumed, so the browser's own session is left intact.
        /// </summary>
        public async Task<UbisoftSession> CreateClientSessionAsync(UbisoftSession webSession, CancellationToken ct)
        {
            using (var request = CreateRequest(HttpMethod.Put, SessionsUrl, webSession, locale: null))
            {
                request.Headers.TryAddWithoutValidation("Ubi-RequestedPlatformType", "uplay");
                request.Content = new StringContent("{\"rememberMe\":false}", Encoding.UTF8, "application/json");

                var json = await SendAsync(request, "session", ct).ConfigureAwait(false);
                var session = UbisoftParsing.ParseSession(json);
                if (session == null)
                {
                    throw new UbisoftAuthException("Ubisoft did not return a usable session.");
                }

                return session;
            }
        }

        /// <summary>Product id to space id for every owned game.</summary>
        public async Task<Dictionary<long, string>> GetGameSpacesAsync(UbisoftSession session, CancellationToken ct)
        {
            using (var request = CreateRequest(HttpMethod.Get, EntitlementsUrl, session, locale: "en-US"))
            {
                var json = await SendAsync(request, "entitlements", ct).ConfigureAwait(false);
                var response = UbisoftParsing.Deserialize<UbisoftEntitlementsResponse>(json);
                return UbisoftParsing.IndexGameSpaces(response?.Entitlements);
            }
        }

        /// <summary>
        /// The achievements of one game in the given locale, or null when ubiservices has no game for
        /// the space id.
        /// </summary>
        public async Task<UbisoftGraphGame> GetAchievementsAsync(
            UbisoftSession session,
            string spaceId,
            long productId,
            string locale,
            CancellationToken ct)
        {
            var body = JsonConvert.SerializeObject(new
            {
                operationName = "GetAchievements",
                query = AchievementsQuery,
                variables = new { spaceId, productId }
            });

            using (var request = CreateRequest(HttpMethod.Post, GraphQlUrl, session, locale))
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                var json = await SendAsync(request, "achievements", ct).ConfigureAwait(false);
                var response = UbisoftParsing.Deserialize<UbisoftGraphResponse>(json);

                var errors = response?.Errors?.Where(e => e != null).ToList();
                if (errors != null && errors.Count > 0)
                {
                    if (errors.All(e => string.Equals(e.Extensions?.Code, NotFoundCode, StringComparison.OrdinalIgnoreCase)))
                    {
                        return null;
                    }

                    throw new InvalidOperationException("Ubisoft achievements query returned an error: " + errors[0].Message);
                }

                return response?.Data?.Game;
            }
        }

        private static HttpRequestMessage CreateRequest(HttpMethod method, string url, UbisoftSession session, string locale)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            request.Headers.TryAddWithoutValidation("Ubi-AppId", ClientAppId);
            request.Headers.TryAddWithoutValidation("Authorization", "ubi_v1 t=" + session.Ticket);
            request.Headers.TryAddWithoutValidation("Ubi-SessionId", session.SessionId);
            if (!string.IsNullOrWhiteSpace(locale))
            {
                request.Headers.TryAddWithoutValidation("Ubi-LocaleCode", locale);
            }

            return request;
        }

        private async Task<string> SendAsync(HttpRequestMessage request, string label, CancellationToken ct)
        {
            using (var response = await _http.SendAsync(request, ct).ConfigureAwait(false))
            {
                var body = response.Content == null
                    ? string.Empty
                    : await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    return body;
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                {
                    throw new UbisoftAuthException($"Ubisoft rejected the session ({label}): HTTP {(int)response.StatusCode}.");
                }

                var message = $"Ubisoft {label} request failed with HTTP {(int)response.StatusCode}: {response.ReasonPhrase}";
                _logger?.Debug($"[Ubisoft] {label} request failed with HTTP {(int)response.StatusCode}.");

                // Only retryable statuses surface as HttpRequestException, which the shared
                // classifier treats as transient.
                if (TransientErrorClassifier.IsTransientStatusCode((int)response.StatusCode))
                {
                    throw new HttpRequestException(message);
                }

                throw new InvalidOperationException(message);
            }
        }
    }
}
