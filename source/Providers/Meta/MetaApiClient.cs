using Newtonsoft.Json;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.Meta
{
    /// <summary>
    /// Raised when graph.oculus.com rejects the access token (OAuthException code 190) or reports a
    /// logged-out query.
    /// </summary>
    internal sealed class MetaAuthException : Exception
    {
        public MetaAuthException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Client over graph.oculus.com with a user access token. The token travels in the query string,
    /// which is how the meta.com site sends it; the URL is never logged.
    /// </summary>
    internal sealed class MetaApiClient : IDisposable
    {
        private const string BaseUrl = "https://graph.oculus.com";
        private const string UnlockFeedDocId = "7560363024007475";
        private const int UnlockFeedPageSize = 100;
        private const int MaxUnlockFeedPages = 200;
        private const int DefinitionLimit = 2000;
        private const string DefinitionFields =
            "id,api_name,title,description,is_secret,unlocked_image_uri,locked_image_uri";

        private const int TokenRejectedCode = 190;
        private const int LoggedOutQueryCode = 1675002;

        private readonly ILogger _logger;
        private readonly HttpClientHandler _handler;
        private readonly HttpClient _http;

        public MetaApiClient(ILogger logger)
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

        /// <summary>The account behind the token.</summary>
        public async Task<MetaViewer> GetViewerAsync(string accessToken, CancellationToken ct)
        {
            var url = BaseUrl + "/me?fields=id,alias&access_token=" + Uri.EscapeDataString(accessToken ?? string.Empty);
            var json = await GetStringAsync(url, "me", ct).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<MetaViewer>(json);
        }

        /// <summary>
        /// Every unlock in the user's profile feed, following the cursor until the last page.
        /// </summary>
        public async Task<List<MetaUnlock>> GetUnlocksAsync(string accessToken, string userId, CancellationToken ct)
        {
            var unlocks = new List<MetaUnlock>();
            string cursor = null;

            for (var page = 0; page < MaxUnlockFeedPages; page++)
            {
                ct.ThrowIfCancellationRequested();

                var variables = new Dictionary<string, object>
                {
                    ["userID"] = userId,
                    ["count"] = UnlockFeedPageSize,
                    ["enableRevampAchievements"] = true
                };
                if (!string.IsNullOrEmpty(cursor))
                {
                    variables["cursor"] = cursor;
                }

                var url = BaseUrl + "/graphql?doc_id=" + UnlockFeedDocId +
                          "&variables=" + Uri.EscapeDataString(JsonConvert.SerializeObject(variables)) +
                          "&access_token=" + Uri.EscapeDataString(accessToken ?? string.Empty);

                var json = await GetStringAsync(url, "unlock feed", ct).ConfigureAwait(false);
                var response = JsonConvert.DeserializeObject<MetaFeedResponse>(json);
                ThrowOnGraphErrors(response?.Errors);

                var connection = MetaParsing.GetAchievementsConnection(response);
                if (connection == null)
                {
                    _logger?.Warn("[Meta] Unlock feed returned no achievements module.");
                    break;
                }

                unlocks.AddRange(MetaParsing.ReadUnlocks(connection));

                var next = connection.PageInfo;
                if (next == null || !next.HasNextPage || string.IsNullOrEmpty(next.EndCursor) || next.EndCursor == cursor)
                {
                    break;
                }

                cursor = next.EndCursor;
            }

            return unlocks;
        }

        /// <summary>
        /// Every achievement definition of an app. Returns an empty list for an app with none.
        /// </summary>
        public async Task<List<MetaDefinition>> GetDefinitionsAsync(string accessToken, string appId, CancellationToken ct)
        {
            var url = BaseUrl + "/" + Uri.EscapeDataString(appId) + "/achievement_definitions" +
                      "?fields=" + Uri.EscapeDataString(DefinitionFields) +
                      "&limit=" + DefinitionLimit +
                      "&access_token=" + Uri.EscapeDataString(accessToken ?? string.Empty);

            var json = await GetStringAsync(url, "achievement_definitions", ct).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<MetaDefinitionList>(json)?.Data ?? new List<MetaDefinition>();
        }

        private async Task<string> GetStringAsync(string url, string label, CancellationToken ct)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            using (var response = await _http.SendAsync(request, ct).ConfigureAwait(false))
            {
                var body = response.Content == null
                    ? string.Empty
                    : await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    return body;
                }

                var error = TryReadRestError(body);
                if (error != null && IsAuthError(error))
                {
                    throw new MetaAuthException($"Meta rejected the access token ({label}): {error.Message}");
                }

                throw new HttpRequestException(
                    $"Meta {label} request failed with HTTP {(int)response.StatusCode}: {error?.Message ?? response.ReasonPhrase}");
            }
        }

        private static void ThrowOnGraphErrors(List<MetaGraphError> errors)
        {
            if (errors == null || errors.Count == 0)
            {
                return;
            }

            foreach (var error in errors)
            {
                if (error != null && IsAuthError(error))
                {
                    throw new MetaAuthException("Meta rejected the access token (unlock feed): " + error.Message);
                }
            }

            throw new HttpRequestException("Meta unlock feed returned an error: " + errors[0]?.Message);
        }

        private static bool IsAuthError(MetaGraphError error)
        {
            return error.Code == TokenRejectedCode || error.Code == LoggedOutQueryCode;
        }

        private static MetaGraphError TryReadRestError(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<MetaRestErrorEnvelope>(body)?.Error;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
