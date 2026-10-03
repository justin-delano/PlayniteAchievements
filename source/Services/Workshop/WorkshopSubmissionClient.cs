using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PlayniteAchievements.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Services.Workshop
{
    /// <summary>What the user fills in before sharing; mirrors the Workshop submission form.</summary>
    public sealed class WorkshopSubmission
    {
        public WorkshopItemKind Kind { get; set; }
        public string Name { get; set; }
        public string Author { get; set; }
        public string Description { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
        public string License { get; set; } = "CC-BY-4.0";
        /// <summary>The id of an item this user already shared, for an update or removal.</summary>
        public string ExistingId { get; set; }
        public bool Remove { get; set; }
        public string Readme { get; set; }
    }

    public sealed class WorkshopSubmissionReceipt
    {
        public int IssueNumber { get; set; }
        public string IssueUrl { get; set; }
    }

    public sealed class WorkshopSubmissionStatus
    {
        /// <summary>validating, needs-changes, in-review, published, closed.</summary>
        public string State { get; set; }
        public string Message { get; set; }
        public string PullRequestUrl { get; set; }
        public string IssueUrl { get; set; }
    }

    /// <summary>
    /// Talks to the Workshop's submission service (the Cloudflare Worker): uploads a package to
    /// temporary storage through the presigned URLs the service hands out, then asks it to open
    /// the submission. The extension never holds a token for the repository.
    /// </summary>
    public sealed class WorkshopSubmissionClient
    {
        public const string DefaultServiceUrl = "https://pa-workshop.playniteachievements-workshop-worker.workers.dev";

        private static readonly TimeSpan Timeout = TimeSpan.FromHours(6);

        private readonly Func<string> _getServiceUrl;
        private readonly HttpClient _http;

        public WorkshopSubmissionClient(Func<string> getServiceUrl, HttpMessageHandler handler = null)
        {
            _getServiceUrl = getServiceUrl;
            _http = handler == null ? HttpClientFactory.Create(Timeout) : HttpClientFactory.Create(handler, Timeout);
        }

        public string ServiceUrl
        {
            get
            {
                var configured = _getServiceUrl?.Invoke();
                var url = string.IsNullOrWhiteSpace(configured) ? DefaultServiceUrl : configured.Trim();
                return url.TrimEnd('/');
            }
        }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(ServiceUrl);

        /// <summary>
        /// Uploads a file and returns the storage key the service knows it by. Large files go up
        /// in parts; <paramref name="progress"/> receives bytes sent.
        /// </summary>
        public async Task<string> UploadAsync(string path, string contentType, IProgress<long> progress, CancellationToken cancel)
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                throw new FileNotFoundException("File to upload not found.", path);
            }

            var begin = await PostJsonAsync("/v1/uploads", new
            {
                fileName = info.Name,
                sizeBytes = info.Length,
                sha256 = Sha256Hex(path),
                contentType
            }, cancel).ConfigureAwait(false);

            var key = begin.Value<string>("key") ?? throw new InvalidOperationException("The Workshop service returned no upload key.");
            var multipart = begin.Value<bool?>("multipart") ?? false;

            if (!multipart)
            {
                using (var stream = File.OpenRead(path))
                {
                    await PutAsync(begin.Value<string>("url"), new ProgressStream(stream, progress, 0), info.Length, contentType, cancel).ConfigureAwait(false);
                }

                await PostJsonAsync("/v1/uploads/complete", new { key }, cancel).ConfigureAwait(false);
                return key;
            }

            var uploadId = begin.Value<string>("uploadId");
            var partSize = begin.Value<long?>("partSize") ?? (32L * 1024 * 1024);
            var parts = (begin["parts"] as JArray) ?? new JArray();
            var etags = new List<object>();
            long sent = 0;
            using (var stream = File.OpenRead(path))
            {
                foreach (var part in parts)
                {
                    cancel.ThrowIfCancellationRequested();
                    var partNumber = part.Value<int>("partNumber");
                    var url = part.Value<string>("url");
                    var length = Math.Min(partSize, info.Length - sent);
                    if (length <= 0)
                    {
                        break;
                    }

                    stream.Seek(sent, SeekOrigin.Begin);
                    var etag = await PutAsync(url, new ProgressStream(new SubStream(stream, length), progress, sent), length, contentType, cancel).ConfigureAwait(false);
                    etags.Add(new { partNumber, etag });
                    sent += length;
                }
            }

            await PostJsonAsync("/v1/uploads/complete", new { key, uploadId, parts = etags }, cancel).ConfigureAwait(false);
            return key;
        }

        public async Task<WorkshopSubmissionReceipt> SubmitAsync(
            WorkshopSubmission submission,
            string submitterHash,
            string packageKey,
            string previewKey,
            string pluginVersion,
            CancellationToken cancel)
        {
            if (submission == null)
            {
                throw new ArgumentNullException(nameof(submission));
            }

            var response = await PostJsonAsync("/v1/submissions", new
            {
                kind = KindLabel(submission.Kind),
                name = submission.Name,
                author = submission.Author,
                description = submission.Description,
                tags = submission.Tags,
                license = submission.License,
                existingId = submission.ExistingId,
                remove = submission.Remove,
                readme = submission.Readme,
                submitterHash,
                packageKey,
                previewKey,
                pluginVersion
            }, cancel).ConfigureAwait(false);

            return new WorkshopSubmissionReceipt
            {
                IssueNumber = response.Value<int?>("issueNumber") ?? 0,
                IssueUrl = response.Value<string>("issueUrl")
            };
        }

        public async Task<WorkshopSubmissionStatus> GetStatusAsync(int issueNumber, CancellationToken cancel)
        {
            using (var response = await _http.GetAsync($"{ServiceUrl}/v1/submissions/{issueNumber}", cancel).ConfigureAwait(false))
            {
                var json = await ReadJsonOrThrowAsync(response).ConfigureAwait(false);
                return new WorkshopSubmissionStatus
                {
                    State = json.Value<string>("state"),
                    Message = json.Value<string>("message"),
                    PullRequestUrl = json.Value<string>("pullRequestUrl"),
                    IssueUrl = json.Value<string>("issueUrl")
                };
            }
        }

        /// <summary>The form's dropdown label for a kind; the service and intake parse these.</summary>
        public static string KindLabel(WorkshopItemKind kind)
        {
            switch (kind)
            {
                case WorkshopItemKind.NotificationStyle: return "Notification style";
                case WorkshopItemKind.ScreenshotFrame: return "Screenshot frame";
                case WorkshopItemKind.ShowcasePage: return "Showcase page";
                case WorkshopItemKind.UnlockSounds: return "Unlock sound pack";
                case WorkshopItemKind.Theme: return "Theme";
                default: return "Per-game custom data";
            }
        }

        private async Task<JObject> PostJsonAsync(string path, object body, CancellationToken cancel)
        {
            using (var content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json"))
            using (var response = await _http.PostAsync(ServiceUrl + path, content, cancel).ConfigureAwait(false))
            {
                return await ReadJsonOrThrowAsync(response).ConfigureAwait(false);
            }
        }

        private static async Task<JObject> ReadJsonOrThrowAsync(HttpResponseMessage response)
        {
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            JObject json = null;
            try
            {
                json = string.IsNullOrWhiteSpace(text) ? null : JObject.Parse(text);
            }
            catch (JsonException)
            {
            }

            if (!response.IsSuccessStatusCode)
            {
                var message = json?.Value<string>("error");
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(message)
                    ? $"The Workshop service answered HTTP {(int)response.StatusCode}."
                    : message);
            }

            return json ?? new JObject();
        }

        private async Task<string> PutAsync(string url, Stream body, long length, string contentType, CancellationToken cancel)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new InvalidOperationException("The Workshop service returned no upload URL.");
            }

            using (var content = new StreamContent(body))
            {
                content.Headers.ContentLength = length;
                content.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
                using (var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = content })
                using (var response = await _http.SendAsync(request, cancel).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException($"Uploading to Workshop storage failed with HTTP {(int)response.StatusCode}.");
                    }

                    return response.Headers.ETag?.Tag?.Trim('"') ?? string.Empty;
                }
            }
        }

        public static string Sha256Hex(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        /// <summary>A fixed-length window over a seekable stream, for one multipart part.</summary>
        private sealed class SubStream : Stream
        {
            private readonly Stream _inner;
            private readonly long _length;
            private long _position;

            public SubStream(Stream inner, long length)
            {
                _inner = inner;
                _length = length;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => _length;
            public override long Position { get => _position; set => throw new NotSupportedException(); }

            public override int Read(byte[] buffer, int offset, int count)
            {
                var remaining = _length - _position;
                if (remaining <= 0)
                {
                    return 0;
                }

                var read = _inner.Read(buffer, offset, (int)Math.Min(count, remaining));
                _position += read;
                return read;
            }

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        /// <summary>Reports cumulative bytes read as an upload proceeds.</summary>
        private sealed class ProgressStream : Stream
        {
            private readonly Stream _inner;
            private readonly IProgress<long> _progress;
            private long _reported;

            public ProgressStream(Stream inner, IProgress<long> progress, long baseOffset)
            {
                _inner = inner;
                _progress = progress;
                _reported = baseOffset;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => _inner.Length;
            public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }

            public override int Read(byte[] buffer, int offset, int count)
            {
                var read = _inner.Read(buffer, offset, count);
                if (read > 0)
                {
                    _reported += read;
                    _progress?.Report(_reported);
                }

                return read;
            }

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
