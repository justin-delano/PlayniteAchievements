using PlayniteAchievements.Providers.Steam.Models;
using PlayniteAchievements.Models;
using PlayniteAchievements.Common;
using PlayniteAchievements.Extensions;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Data;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace PlayniteAchievements.Providers.Steam
{
    public sealed class SteamSessionManager
    {
        private static readonly TimeSpan CookieRefreshInterval = TimeSpan.FromHours(6);
        private readonly IPlayniteAPI _api;
        private readonly ILogger _logger;
        private readonly PlayniteAchievementsSettings _settings;
        // Persistence removed - relying on CEF cookies as source of truth.

        private string _selfSteamId64;
        private DateTime _lastCookieRefreshUtc = DateTime.MinValue;

        public SteamSessionManager(IPlayniteAPI api, ILogger logger, string pluginUserDataPath, PlayniteAchievementsSettings settings)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _logger = logger;
            _settings = settings;
            // No longer loading from disk
        }

        public string GetCachedSteamId64() => _selfSteamId64;

        public bool NeedsRefresh
        {
            get
            {
                // Always refresh if we don't have an ID
                if (string.IsNullOrWhiteSpace(_selfSteamId64))
                    return true;

                // Otherwise respect the interval
                return (DateTime.UtcNow - _lastCookieRefreshUtc) >= CookieRefreshInterval;
            }
        }

        public async Task<string> GetUserSteamId64Async(CancellationToken ct)
        {
            // 1. Refresh if interval elapsed or ID missing
            if (NeedsRefresh)
            {
                await RefreshCookiesHeadlessAsync(ct).ConfigureAwait(false);
            }
            
            if (string.IsNullOrWhiteSpace(_selfSteamId64))
            {
                throw new InvalidOperationException("Could not determine SteamID64. Please ensure you are logged into Steam.");
            }
            return _selfSteamId64;
        }

        public async Task<bool> RefreshCookiesHeadlessAsync(CancellationToken ct, bool force = false)
        {
            // If not forced, check if we need to refresh based on time
            if (!force && !NeedsRefresh)
            {
                _logger?.Debug("Cookie refresh skipped - not needed yet.");
                return true;
            }

            // Prevent spam: Always update the timestamp, even if we fail.
            // This stops the "refresh loop" from happening every single second on failure.
            _lastCookieRefreshUtc = DateTime.UtcNow;

            bool success = false;
            string extractedId = null;

            // Use TaskCompletionSource to ensuring we wait for the UI thread logic to actually FINISH.
            var tcs = new TaskCompletionSource<bool>();

            await _api.MainView.UIDispatcher.InvokeAsync(async () =>
            {
                try
                {
                    using (var view = _api.WebViews.CreateOffscreenView())
                    {
                        // Mimic Playnite Steam Library: Check Community primarily.
                        // Navigating to root is safer than /my/profile to avoid redirects confusing the cookie jar.
                        var targetUrl = "https://steamcommunity.com/";
                        
                        _logger?.Debug("Navigating to {targetUrl} to refresh session...");
                        await view.NavigateAndWaitAsync(targetUrl);
                        
                        // Give the browser a moment to commit cookies to storage
                        await Task.Delay(1000);

                        var cookies = view.GetCookies();
                        
                        // Look for the auth cookie
                        var authCookie = cookies?.FirstOrDefault(c => 
                            c.Name.Equals("steamLoginSecure", StringComparison.OrdinalIgnoreCase));

                        if (authCookie != null)
                        {
                            // Try extract ID
                            extractedId = TryExtractSteamId64FromSteamLoginSecure(authCookie.Value);

                            // Success conditions:
                            // A) We extracted a new ID
                            if (!string.IsNullOrEmpty(extractedId))
                            {
                                success = true;
                            }
                            // B) Extraction failed (weird format), but we have a cached ID. 
                            //    Since the cookie EXISTS, we assume the session is valid.
                            else if (!string.IsNullOrEmpty(_selfSteamId64))
                            {
                                extractedId = _selfSteamId64;
                                success = true;
                            }
                        }
                    }
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, "Offscreen navigation failed.");
                    tcs.TrySetResult(false);
                }
            });

            // Wait here until the UI thread is actually done
            await tcs.Task;

            if (success)
            {
                if (!string.IsNullOrEmpty(extractedId))
                {
                    _selfSteamId64 = extractedId;
                    _settings.Persisted.SteamUserId = extractedId;
                }
                
                _logger?.Info("Session refreshed successfully.");
                return true;
            }

            _logger?.Warn("Session check failed (Auth cookie not found). Scraper will continue with cached cookies if available.");
            return false;
        }

        public async Task<(bool Success, string Message)> AuthenticateInteractiveAsync(CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                bool loggedIn = false;
                string extractedId = null;

                await _api.MainView.UIDispatcher.InvokeAsync(async () =>
                {
                    using (var view = _api.WebViews.CreateView(1000, 800))
                    {
                        view.DeleteDomainCookies(".steamcommunity.com");
                        view.DeleteDomainCookies("steamcommunity.com");
                        view.DeleteDomainCookies(".steampowered.com");
                        view.DeleteDomainCookies("steampowered.com");

                        view.Navigate("https://steamcommunity.com/login/home/?goto=" +
                                      Uri.EscapeDataString("https://steamcommunity.com/my/"));

                        view.LoadingChanged += (s, e) =>
                        {
                            if (e.IsLoading) return;
                            var address = view.GetCurrentAddress();
                            if (string.IsNullOrWhiteSpace(address)) return;

                            // Check if we're on a logged-in page: profile pages or /my/ endpoint
                            bool isOnLoggedInPage =
                                address.IndexOf("steamcommunity.com/id/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                address.IndexOf("steamcommunity.com/profiles/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                address.IndexOf("steamcommunity.com/my/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                address.IndexOf("steamcommunity.com/my", StringComparison.OrdinalIgnoreCase) >= 0;

                            if (isOnLoggedInPage)
                            {
                                var cookies = view.GetCookies();
                                var authCookie = cookies?.FirstOrDefault(c =>
                                    c.Name.Equals("steamLoginSecure", StringComparison.OrdinalIgnoreCase));

                                if (authCookie != null)
                                {
                                    // Schedule close on next message pump cycle to avoid reentrancy deadlock
                                    _api.MainView.UIDispatcher.BeginInvoke(new Action(() => view.Close()),
                                        System.Windows.Threading.DispatcherPriority.Background);
                                }
                            }
                        };

                        view.OpenDialog();
                        await Task.Delay(500);

                        var cookies = view.GetCookies();
                        if (cookies != null)
                        {
                            var authCookie = cookies.FirstOrDefault(c => 
                                c.Name.Equals("steamLoginSecure", StringComparison.OrdinalIgnoreCase));

                            if (authCookie != null)
                            {
                                extractedId = TryExtractSteamId64FromSteamLoginSecure(authCookie.Value);
                                loggedIn = !string.IsNullOrWhiteSpace(extractedId);
                            }
                        }
                    }
                });

                if (!loggedIn)
                    return (false, "Steam session cookies not found. Please ensure login completed.");

                _selfSteamId64 = extractedId;
                _lastCookieRefreshUtc = DateTime.UtcNow;
                _settings.Persisted.SteamUserId = extractedId;

                return (true, "Steam authentication saved.");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "AuthenticateInteractiveAsync failed.");
                return (false, ex.Message);
            }
        }

        public async Task<(bool IsLoggedIn, string FinalUrl)> ProbeLoggedInAsync(CancellationToken ct)
        {
            var probeUrl = "https://steamcommunity.com/my/friends";
            var res = await GetSteamPageAsync(probeUrl, ct).ConfigureAwait(false);

            var finalUrl = res.FinalUrl ?? "";
            if (string.IsNullOrWhiteSpace(finalUrl))
                return (false, finalUrl);

            var isLoggedIn = !IsLoginPageUrl(finalUrl);
            if (isLoggedIn)
            {
                await GetUserSteamId64Async(ct);
            }

            return (isLoggedIn, finalUrl);
        }

        private async Task<(string FinalUrl, string Html)> GetSteamPageAsync(string url, CancellationToken ct)
        {
            string finalUrl = url;
            string html = "";
            var tcs = new TaskCompletionSource<(string, string)>();

            await _api.MainView.UIDispatcher.InvokeAsync(async () =>
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    using (var view = _api.WebViews.CreateOffscreenView())
                    {
                        await view.NavigateAndWaitAsync(url);
                        finalUrl = view.GetCurrentAddress();
                        html = await view.GetPageTextAsync();
                        tcs.TrySetResult((finalUrl, html));
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, "Offscreen navigation failed for {url}.");
                    tcs.TrySetResult((finalUrl, ""));
                }
            });

            return await tcs.Task;
        }

        private bool IsLoginPageUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            return url.IndexOf("/login", StringComparison.OrdinalIgnoreCase) >= 0
                || url.IndexOf("openid", StringComparison.OrdinalIgnoreCase) >= 0
                || url.IndexOf("login.steampowered.com", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public void ClearSession()
        {
            ClearSteamCookiesFromCef(_api, _logger);
            _selfSteamId64 = null;
            _lastCookieRefreshUtc = DateTime.MinValue;
        }

        // ---------------------------------------------------------------------
        // Cookie Management (Merged from SteamCookieManager)
        // ---------------------------------------------------------------------

        private static readonly string[] SteamSessionCookieNames = { "steamLoginSecure", "sessionid" };
        private static readonly Uri CommunityBase = new Uri("https://steamcommunity.com/");
        private static readonly Uri StoreBase = new Uri("https://store.steampowered.com/");

        public static bool HasSteamSessionCookies(IPlayniteAPI api, ILogger logger)
        {
            try
            {
                using (var view = api.WebViews.CreateOffscreenView())
                {
                    var cookies = view.GetCookies();
                    if (cookies == null)
                        return false;

                    return cookies.Any(c =>
                        c != null &&
                        !string.IsNullOrWhiteSpace(c.Domain) &&
                        IsSteamDomain(c.Domain) &&
                        SteamSessionCookieNames.Any(n => string.Equals(c.Name, n, StringComparison.OrdinalIgnoreCase)));
                }
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "Failed to check Steam session cookies.");
                return false;
            }
        }

        private static bool IsSteamDomain(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain)) return false;
            var d = domain.Trim().TrimStart('.');
            return d.EndsWith("steamcommunity.com", StringComparison.OrdinalIgnoreCase) ||
                   d.EndsWith("steampowered.com", StringComparison.OrdinalIgnoreCase);
        }

        public static string TryExtractSteamId64FromSteamLoginSecure(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            string decoded;
            try
            {
                decoded = Uri.UnescapeDataString(value);
            }
            catch
            {
                decoded = value;
            }

            var m = Regex.Match(decoded, @"(?<id>\d{17})");
            return m.Success ? m.Groups["id"].Value : null;
        }

        public static Uri GetAddUriForDomain(string cookieDomain)
        {
            var d = (cookieDomain ?? "").Trim().TrimStart('.');
            if (string.IsNullOrWhiteSpace(d))
                return CommunityBase;

            try
            {
                return new Uri("https://" + d);
            }
            catch
            {
                return CommunityBase;
            }
        }

        public void LoadCefCookiesIntoJar(IPlayniteAPI api, CookieContainer cookieJar, ILogger logger)
        {
            try
            {
                using (var view = api.WebViews.CreateOffscreenView())
                {
                    var cookies = view.GetCookies();
                    if (cookies == null)
                        return;

                    var steamCookies = cookies
                        .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Domain))
                        .Where(c => IsSteamDomain(c.Domain))
                        .ToList();

                    // Check if we can extract ID from these cookies while we're at it
                    var authCookie = steamCookies.FirstOrDefault(c => 
                        c.Name.Equals("steamLoginSecure", StringComparison.OrdinalIgnoreCase));
                    if (authCookie != null)
                    {
                        var id = TryExtractSteamId64FromSteamLoginSecure(authCookie.Value);
                        if (!string.IsNullOrWhiteSpace(id))
                        {
                            _selfSteamId64 = id;
                        }
                    }

                    foreach (var c in steamCookies)
                    {
                        try
                        {
                            var domain = c.Domain.TrimStart('.');
                            var path = string.IsNullOrWhiteSpace(c.Path) ? "/" : c.Path;
                            var uri = GetAddUriForDomain(domain);
                            var cookie = new Cookie(c.Name, SanitizeCookieValue(c.Value), path)
                            {
                                Domain = uri.Host,
                                Secure = c.Secure,
                                HttpOnly = c.HttpOnly
                            };

                            if (c.Expires.HasValue && c.Expires.Value > DateTime.MinValue)
                            {
                                var expires = c.Expires.Value;
                                cookie.Expires = expires.Kind == DateTimeKind.Utc ? expires : expires.ToUniversalTime();
                            }
                            cookieJar.Add(uri, cookie);
                        }
                        catch (Exception ex)
                        {
                            logger?.Debug(ex, "Failed to add cookie {c.Name} to jar");
                        }
                    }

                    AddSteamTimezoneCookie(cookieJar, logger);
                }
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "Failed to load cookies from CEF into jar");
            }
        }

        private static void AddSteamTimezoneCookie(CookieContainer cookieJar, ILogger logger)
        {
            try
            {
                var tzCookie = new Cookie(
                    "timezoneOffset",
                    SanitizeCookieValue(SteamTimeParser.GetSteamTimezoneOffsetCookieValue()),
                    "/")
                {
                    Domain = CommunityBase.Host
                };
                cookieJar.Add(CommunityBase, tzCookie);
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "Failed to set timezoneOffset cookie");
            }
        }

        private static string SanitizeCookieValue(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return value.Replace(",", "%2C");
        }

        public static void ClearSteamCookiesFromCef(IPlayniteAPI api, ILogger logger)
        {
            try
            {
                api.MainView.UIDispatcher.Invoke(() =>
                {
                    using (var view = api.WebViews.CreateOffscreenView(new WebViewSettings()))
                    {
                        view.DeleteDomainCookiesRegex(@"(^|\.)steamcommunity\.com$");
                        view.DeleteDomainCookiesRegex(@"(^|\.)steampowered\.com$");
                        view.DeleteDomainCookies("store.steampowered.com");
                        view.DeleteDomainCookies("login.steampowered.com");
                        view.DeleteDomainCookies("help.steampowered.com");
                    }
                });
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "Failed to clear Steam cookies from CEF.");
            }
        }
    }
}



