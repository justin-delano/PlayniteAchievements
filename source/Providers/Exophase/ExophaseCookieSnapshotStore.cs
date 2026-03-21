using Newtonsoft.Json;
using Playnite.SDK;
using PlayniteAchievements.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;

namespace PlayniteAchievements.Providers.Exophase
{
    internal sealed class ExophaseCookieSnapshotStore
    {
        private readonly ILogger _logger;
        private readonly string _snapshotPath;

        public ExophaseCookieSnapshotStore(string pluginUserDataPath, ILogger logger)
        {
            if (string.IsNullOrWhiteSpace(pluginUserDataPath))
            {
                throw new ArgumentException("Plugin user data path is required.", nameof(pluginUserDataPath));
            }

            _logger = logger;
            _snapshotPath = Path.Combine(pluginUserDataPath, "exophase", "cookies.json.enc");
        }

        public bool Exists => File.Exists(_snapshotPath);

        public bool Save(IReadOnlyList<HttpCookie> cookies)
        {
            try
            {
                var filteredCookies = FilterExophaseCookies(cookies);
                if (filteredCookies.Count == 0)
                {
                    return false;
                }

                var directory = Path.GetDirectoryName(_snapshotPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var snapshot = new ExophaseCookieSnapshotFile
                {
                    CreatedUtc = DateTime.UtcNow,
                    Cookies = filteredCookies.Select(ToStoredCookie).ToList()
                };

                var json = JsonConvert.SerializeObject(snapshot);
                Encryption.EncryptToFile(_snapshotPath, json, Encoding.UTF8, GetCurrentUserSid());
                return true;
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "[ExophaseAuth] Failed to save encrypted Exophase cookie snapshot.");
                return false;
            }
        }

        public bool TryLoad(out List<HttpCookie> cookies)
        {
            cookies = new List<HttpCookie>();

            try
            {
                if (!File.Exists(_snapshotPath))
                {
                    return false;
                }

                var json = Encryption.DecryptFromFile(_snapshotPath, Encoding.UTF8, GetCurrentUserSid());
                var snapshot = JsonConvert.DeserializeObject<ExophaseCookieSnapshotFile>(json);
                if (snapshot?.Cookies == null || snapshot.Cookies.Count == 0)
                {
                    return false;
                }

                cookies = snapshot.Cookies
                    .Select(ToHttpCookie)
                    .Where(cookie => cookie != null)
                    .ToList();

                return cookies.Count > 0;
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "[ExophaseAuth] Failed to load encrypted Exophase cookie snapshot.");
                cookies = new List<HttpCookie>();
                return false;
            }
        }

        public void Delete()
        {
            try
            {
                if (File.Exists(_snapshotPath))
                {
                    File.Delete(_snapshotPath);
                }
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "[ExophaseAuth] Failed to delete encrypted Exophase cookie snapshot.");
            }
        }

        public static List<HttpCookie> FilterExophaseCookies(IEnumerable<HttpCookie> cookies)
        {
            return (cookies ?? Enumerable.Empty<HttpCookie>())
                .Where(cookie =>
                    cookie != null &&
                    !string.IsNullOrWhiteSpace(cookie.Domain) &&
                    cookie.Domain.IndexOf("exophase.com", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(CloneCookie)
                .OrderBy(cookie => cookie.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(cookie => cookie.Domain, StringComparer.OrdinalIgnoreCase)
                .ThenBy(cookie => cookie.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static HttpCookie CloneCookie(HttpCookie cookie)
        {
            if (cookie == null)
            {
                return null;
            }

            return new HttpCookie
            {
                Name = cookie.Name,
                Value = cookie.Value,
                Domain = cookie.Domain,
                Path = string.IsNullOrWhiteSpace(cookie.Path) ? "/" : cookie.Path,
                Expires = cookie.Expires,
                Secure = cookie.Secure,
                HttpOnly = cookie.HttpOnly,
                SameSite = cookie.SameSite,
                Priority = cookie.Priority
            };
        }

        private static ExophaseStoredCookie ToStoredCookie(HttpCookie cookie)
        {
            return new ExophaseStoredCookie
            {
                Name = cookie.Name,
                Value = cookie.Value,
                Domain = cookie.Domain,
                Path = string.IsNullOrWhiteSpace(cookie.Path) ? "/" : cookie.Path,
                Expires = cookie.Expires,
                Secure = cookie.Secure,
                HttpOnly = cookie.HttpOnly,
                SameSite = cookie.SameSite,
                Priority = cookie.Priority
            };
        }

        private static HttpCookie ToHttpCookie(ExophaseStoredCookie cookie)
        {
            if (cookie == null || string.IsNullOrWhiteSpace(cookie.Name))
            {
                return null;
            }

            return new HttpCookie
            {
                Name = cookie.Name,
                Value = cookie.Value,
                Domain = cookie.Domain,
                Path = string.IsNullOrWhiteSpace(cookie.Path) ? "/" : cookie.Path,
                Expires = cookie.Expires,
                Secure = cookie.Secure,
                HttpOnly = cookie.HttpOnly,
                SameSite = cookie.SameSite,
                Priority = cookie.Priority
            };
        }

        private static string GetCurrentUserSid()
        {
            var sid = WindowsIdentity.GetCurrent()?.User?.Value;
            if (string.IsNullOrWhiteSpace(sid))
            {
                throw new InvalidOperationException("Unable to resolve current Windows SID for Exophase cookie encryption.");
            }

            return sid;
        }

        private sealed class ExophaseCookieSnapshotFile
        {
            public DateTime CreatedUtc { get; set; }

            public List<ExophaseStoredCookie> Cookies { get; set; } = new List<ExophaseStoredCookie>();
        }

        private sealed class ExophaseStoredCookie
        {
            public string Name { get; set; }

            public string Value { get; set; }

            public string Domain { get; set; }

            public string Path { get; set; }

            public DateTime? Expires { get; set; }

            public bool Secure { get; set; }

            public bool HttpOnly { get; set; }

            public CookieSameSite SameSite { get; set; }

            public CookiePriority Priority { get; set; }
        }
    }
}
