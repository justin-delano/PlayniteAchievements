using PlayniteAchievements.Models.Friends;
using PlayniteAchievements.Providers.Steam.Models;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.Steam
{
    internal sealed class SteamFriendsProvider : IFriendsProvider
    {
        private readonly SteamHttpClient _steamClient;
        private readonly SteamApiClient _steamApiClient;
        private readonly SteamScanner _scanner;
        private readonly SteamWebApiTokenResolver _tokenResolver;
        private readonly ILogger _logger;
        private readonly object _refreshStateLock = new object();
        private TokenState _preparedRefreshTokenState;

        public SteamFriendsProvider(
            SteamHttpClient steamClient,
            SteamApiClient steamApiClient,
            SteamScanner scanner,
            SteamWebApiTokenResolver tokenResolver,
            ILogger logger)
        {
            _steamClient = steamClient ?? throw new ArgumentNullException(nameof(steamClient));
            _steamApiClient = steamApiClient ?? throw new ArgumentNullException(nameof(steamApiClient));
            _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
            _tokenResolver = tokenResolver ?? throw new ArgumentNullException(nameof(tokenResolver));
            _logger = logger;
        }

        public string ProviderKey => "Steam";

        public async Task<FriendsProviderResult<FriendsRefreshPreparation>> BeginRefreshAsync(CancellationToken cancel)
        {
            EndRefresh();

            var session = await ResolveSteamSessionFromTokenResolverAsync(cancel).ConfigureAwait(false);
            if (!session.IsSuccess)
            {
                return FriendsProviderResult<FriendsRefreshPreparation>.Failed(
                    session.ErrorMessage,
                    authRequired: session.AuthRequired);
            }

            lock (_refreshStateLock)
            {
                _preparedRefreshTokenState = session;
            }

            return FriendsProviderResult<FriendsRefreshPreparation>.FromData(new FriendsRefreshPreparation
            {
                CanRefreshAchievements = !string.IsNullOrWhiteSpace(session.WebApiToken)
            });
        }

        public void EndRefresh()
        {
            lock (_refreshStateLock)
            {
                _preparedRefreshTokenState = null;
            }
        }

        public async Task<FriendsProviderResult<IReadOnlyList<FriendIdentity>>> GetFriendsAsync(CancellationToken cancel)
        {
            var session = await ResolveSteamSessionAsync(cancel).ConfigureAwait(false);
            if (!session.IsSuccess)
            {
                return FriendsProviderResult<IReadOnlyList<FriendIdentity>>.Failed(
                    session.ErrorMessage,
                    authRequired: session.AuthRequired);
            }

            return await GetFriendsFromCommunityPageAsync(session.SteamUserId, cancel).ConfigureAwait(false);
        }

        public async Task<FriendsProviderResult<IReadOnlyList<FriendGameOwnership>>> GetOwnedGamesAsync(
            FriendIdentity friend,
            CancellationToken cancel)
        {
            if (string.IsNullOrWhiteSpace(friend?.ExternalUserId))
            {
                return FriendsProviderResult<IReadOnlyList<FriendGameOwnership>>.Failed("Steam friend id is missing.");
            }

            var gamesResult = await GetOwnedGamesFromWebApiAsync(friend.ExternalUserId, cancel).ConfigureAwait(false);
            if (gamesResult?.Success != true)
            {
                _logger?.Debug(
                    $"Steam owned games API unavailable for friend {friend.ExternalUserId}; falling back to community page. " +
                    $"{gamesResult?.ErrorMessage}");
                gamesResult = await GetOwnedGamesFromCommunityPageAsync(friend.ExternalUserId, cancel).ConfigureAwait(false);
            }

            if (gamesResult?.Success != true)
            {
                return FriendsProviderResult<IReadOnlyList<FriendGameOwnership>>.Failed(
                    gamesResult?.ErrorMessage ??
                    $"Steam owned games are unavailable from the community page for friend {friend.ExternalUserId}.",
                    authRequired: gamesResult?.AuthRequired == true,
                    transientFailure: gamesResult?.TransientFailure == true);
            }

            var result = gamesResult.Games
                .Where(game => game != null && game.AppId > 0)
                .Select(game => new FriendGameOwnership
                {
                    ProviderKey = ProviderKey,
                    ExternalUserId = friend.ExternalUserId,
                    AppId = game.AppId,
                    PlaytimeForeverMinutes = Math.Max(0, game.PlaytimeForever),
                    Playtime2WeeksMinutes = game.Playtime2Weeks.HasValue ? Math.Max(0, game.Playtime2Weeks.Value) : (int?)null,
                    LastPlayedUtc = ToUtc(game.LastPlayedUnixSeconds)
                })
                .ToList();

            return FriendsProviderResult<IReadOnlyList<FriendGameOwnership>>.FromData(result);
        }

        private async Task<OwnedGamesPageResult> GetOwnedGamesFromWebApiAsync(
            string steamId64,
            CancellationToken cancel)
        {
            var session = await ResolveSteamSessionAsync(cancel).ConfigureAwait(false);
            if (!session.IsSuccess)
            {
                return OwnedGamesPageResult.Failed(
                    session.ErrorMessage ?? "Steam web session could not be resolved.",
                    authRequired: session.AuthRequired);
            }

            if (string.IsNullOrWhiteSpace(session.WebApiToken))
            {
                return OwnedGamesPageResult.Failed("Steam web API token could not be resolved.");
            }

            IReadOnlyList<SteamOwnedGame> games;
            try
            {
                games = await _steamApiClient
                    .GetOwnedGamesAsync(session.WebApiToken, steamId64, cancel)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"Steam owned games API request failed for friend {steamId64}.");
                return OwnedGamesPageResult.Failed(
                    $"Steam owned games API request failed for friend {steamId64}.",
                    transientFailure: true);
            }

            if (games == null)
            {
                return OwnedGamesPageResult.Failed(
                    $"Steam owned games API returned no usable response for friend {steamId64}.");
            }

            return OwnedGamesPageResult.FromGames(games);
        }

        private async Task<FriendsProviderResult<IReadOnlyList<FriendIdentity>>> GetFriendsFromCommunityPageAsync(
            string selfSteamId64,
            CancellationToken cancel)
        {
            SteamPageResult page;
            try
            {
                page = await _steamClient.GetFriendsPageAsync(selfSteamId64, cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Steam community friends page request failed.");
                return FriendsProviderResult<IReadOnlyList<FriendIdentity>>.Failed(
                    "Steam community friends page request failed.",
                    transientFailure: true);
            }

            if (!IsUsableCommunityPage(page))
            {
                return FriendsProviderResult<IReadOnlyList<FriendIdentity>>.Failed(
                    "Steam friend list is unavailable from the community page.",
                    authRequired: LooksLoggedOut(page));
            }

            var parsedFriends = SteamCommunityPageParser.ParseFriends(page.Html);
            if (parsedFriends.Count == 0 && !SteamCommunityPageParser.LooksLikeFriendsPayload(page.Html))
            {
                return FriendsProviderResult<IReadOnlyList<FriendIdentity>>.Failed(
                    "Steam community friends page did not contain a friends payload.");
            }

            var now = DateTime.UtcNow;
            var friends = parsedFriends
                .Where(friend => !string.IsNullOrWhiteSpace(friend?.SteamId))
                .Select(friend => new FriendIdentity
                {
                    ProviderKey = ProviderKey,
                    ExternalUserId = friend.SteamId.Trim(),
                    DisplayName = FirstNonEmpty(friend.DisplayName, friend.SteamId),
                    AvatarUrl = friend.AvatarUrl,
                    LastRefreshedUtc = now
                })
                .ToList();

            return FriendsProviderResult<IReadOnlyList<FriendIdentity>>.FromData(friends);
        }

        private async Task<OwnedGamesPageResult> GetOwnedGamesFromCommunityPageAsync(
            string steamId64,
            CancellationToken cancel)
        {
            SteamPageResult page;
            try
            {
                page = await _steamClient.GetOwnedGamesPageAsync(steamId64, cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"Steam community games page request failed for friend {steamId64}.");
                return OwnedGamesPageResult.Failed(
                    $"Steam community games page request failed for friend {steamId64}.",
                    transientFailure: true);
            }

            if (page == null || string.IsNullOrWhiteSpace(page.Html))
            {
                return OwnedGamesPageResult.Failed(
                    $"Steam community games page returned no content for friend {steamId64}.",
                    transientFailure: true);
            }

            if (LooksLoggedOut(page))
            {
                return OwnedGamesPageResult.Failed(
                    $"Steam community games page requires Steam web authentication for friend {steamId64}.",
                    authRequired: true);
            }

            if (SteamHttpClient.LooksPrivateOrRestrictedStatsPayload(page.Html, page.FinalUrl))
            {
                return OwnedGamesPageResult.Failed(
                    $"Steam community games page is private or restricted for friend {steamId64}.");
            }

            if (!SteamCommunityPageParser.LooksLikeOwnedGamesPayload(page.Html))
            {
                var profilePageResult = await GetOwnedGamesFromProfileXmlPageAsync(steamId64, cancel).ConfigureAwait(false);
                if (profilePageResult?.Success == true)
                {
                    return profilePageResult;
                }

                return OwnedGamesPageResult.Failed(
                    $"Steam community games page did not contain an owned-games payload for friend {steamId64}. " +
                    $"GamesPage={BuildPageSignature(page)}; ProfileXml={profilePageResult?.ErrorMessage ?? "not checked"}");
            }

            var games = SteamCommunityPageParser.ParseOwnedGames(page.Html);
            if (games.Count > 0)
            {
                return OwnedGamesPageResult.FromGames(games);
            }

            var fallbackProfilePageResult = await GetOwnedGamesFromProfileXmlPageAsync(steamId64, cancel).ConfigureAwait(false);
            if (fallbackProfilePageResult?.Success == true)
            {
                return fallbackProfilePageResult;
            }

            return OwnedGamesPageResult.Failed(
                $"Steam community games page contained owned-games markers but parsed zero games for friend {steamId64}. " +
                $"GamesPage={BuildPageSignature(page)}; ProfileXml={fallbackProfilePageResult?.ErrorMessage ?? "not checked"}");
        }

        private async Task<OwnedGamesPageResult> GetOwnedGamesFromProfileXmlPageAsync(
            string steamId64,
            CancellationToken cancel)
        {
            SteamPageResult page;
            try
            {
                page = await _steamClient.GetProfileXmlPageAsync(steamId64, cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"Steam profile XML page request failed for friend {steamId64}.");
                return OwnedGamesPageResult.Failed(
                    $"ProfileXml=request failed",
                    transientFailure: true);
            }

            if (page == null || string.IsNullOrWhiteSpace(page.Html))
            {
                return OwnedGamesPageResult.Failed("ProfileXml=empty");
            }

            if (LooksLoggedOut(page))
            {
                return OwnedGamesPageResult.Failed("ProfileXml=auth required", authRequired: true);
            }

            if (!SteamCommunityPageParser.LooksLikeOwnedGamesPayload(page.Html))
            {
                return OwnedGamesPageResult.Failed(BuildPageSignature(page));
            }

            return OwnedGamesPageResult.FromGames(SteamCommunityPageParser.ParseOwnedGames(page.Html));
        }

        public async Task<FriendsProviderResult<FriendGameAchievements>> GetFriendGameAchievementsAsync(
            FriendIdentity friend,
            int appId,
            string gameName,
            CancellationToken cancel)
        {
            if (string.IsNullOrWhiteSpace(friend?.ExternalUserId) || appId <= 0)
            {
                return FriendsProviderResult<FriendGameAchievements>.Failed("Steam friend id or app id is missing.");
            }

            var token = await ResolveTokenAsync(cancel).ConfigureAwait(false);
            if (!token.IsSuccess)
            {
                return FriendsProviderResult<FriendGameAchievements>.Failed(
                    token.ErrorMessage,
                    authRequired: token.AuthRequired);
            }

            AchievementsScrapeResponse scrape;
            try
            {
                scrape = await _scanner.ScrapeAchievementsAsync(
                    friend.ExternalUserId,
                    appId,
                    token.WebApiToken,
                    cancel,
                    includeLocked: true,
                    gameName: gameName).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"Steam friend achievement scrape failed for friend={friend.ExternalUserId}, appId={appId}.");
                return FriendsProviderResult<FriendGameAchievements>.Failed(
                    "Steam friend achievement scrape failed.",
                    transientFailure: true);
            }

            var data = new FriendGameAchievements
            {
                Friend = friend,
                AppId = appId,
                LastUpdatedUtc = DateTime.UtcNow,
                StatsUnavailable = scrape?.StatsUnavailable == true,
                TransientFailure = scrape?.TransientFailure == true,
                DetailCode = scrape?.DetailCode ?? SteamScrapeDetail.None,
                Rows = MapRows(scrape?.Rows)
            };

            if (data.TransientFailure)
            {
                var failed = FriendsProviderResult<FriendGameAchievements>.Failed(
                    scrape?.Detail ?? "Steam scrape returned a transient failure.",
                    transientFailure: true);
                failed.Data = data;
                return failed;
            }

            return FriendsProviderResult<FriendGameAchievements>.FromData(data);
        }

        private static bool IsUsableCommunityPage(SteamPageResult page)
        {
            if (page == null || string.IsNullOrWhiteSpace(page.Html))
            {
                return false;
            }

            if (LooksLoggedOut(page))
            {
                return false;
            }

            return !SteamHttpClient.LooksPrivateOrRestrictedStatsPayload(page.Html, page.FinalUrl);
        }

        private static bool LooksLoggedOut(SteamPageResult page)
        {
            return SteamHttpClient.LooksUnauthenticatedSteamPage(page?.Html, page?.FinalUrl);
        }

        private static string BuildPageSignature(SteamPageResult page)
        {
            var html = page?.Html ?? string.Empty;
            var markers = new List<string>();
            foreach (var marker in new[]
            {
                "<gamesList",
                "webkit-xml-viewer-source-xml",
                "&lt;gamesList",
                "<mostPlayedGames",
                "&lt;mostPlayedGames",
                "rgGames",
                "games_list_rows",
                "gameListRow",
                "gameslistitems_",
                "window.SSR.loaderData",
                "window.SSR.renderContext",
                "OwnedGames",
                "playtime_forever",
                "/app/",
                "/apps/",
                "data-app",
                "appid",
                "TOTAL PLAYED",
                "ACHIEVEMENTS",
                "Sign In",
                "profile_private_info",
                "This profile is private"
            })
            {
                if (html.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    markers.Add(marker);
                }
            }

            return $"Status={(int)(page?.StatusCode ?? 0)}, FinalUrl={page?.FinalUrl}, Len={html.Length}, " +
                   $"Title={ExtractTitle(html)}, Markers={string.Join(",", markers)}";
        }

        private static string ExtractTitle(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            var start = html.IndexOf("<title>", StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return string.Empty;
            }

            start += "<title>".Length;
            var end = html.IndexOf("</title>", start, StringComparison.OrdinalIgnoreCase);
            if (end <= start)
            {
                return string.Empty;
            }

            var title = html.Substring(start, end - start);
            title = string.Join(" ", title.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries));
            return title.Length <= 80 ? title : title.Substring(0, 80);
        }

        private async Task<TokenState> ResolveTokenAsync(CancellationToken cancel)
        {
            var session = await ResolveSteamSessionAsync(cancel).ConfigureAwait(false);
            if (!session.IsSuccess || string.IsNullOrWhiteSpace(session.WebApiToken))
            {
                return new TokenState
                {
                    IsSuccess = false,
                    AuthRequired = true,
                    ErrorMessage = "Steam web API token could not be resolved."
                };
            }

            return session;
        }

        private async Task<TokenState> ResolveSteamSessionAsync(CancellationToken cancel)
        {
            lock (_refreshStateLock)
            {
                if (_preparedRefreshTokenState != null)
                {
                    return _preparedRefreshTokenState;
                }
            }

            return await ResolveSteamSessionFromTokenResolverAsync(cancel).ConfigureAwait(false);
        }

        private async Task<TokenState> ResolveSteamSessionFromTokenResolverAsync(CancellationToken cancel)
        {
            var resolution = await _tokenResolver.ResolveAsync(cancel).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(resolution?.UserId))
            {
                return new TokenState
                {
                    IsSuccess = false,
                    AuthRequired = true,
                    ErrorMessage = "Steam web session could not be resolved."
                };
            }

            return new TokenState
            {
                IsSuccess = true,
                WebApiToken = resolution.Token?.Trim(),
                SteamUserId = resolution.UserId.Trim()
            };
        }

        private static List<FriendAchievementRow> MapRows(IEnumerable<ScrapedAchievement> rows)
        {
            return rows?
                .Where(row => row != null)
                .Select(row => new FriendAchievementRow
                {
                    DisplayName = row.DisplayName,
                    Description = row.Description,
                    IconUrl = row.IconUrl,
                    Unlocked = row.IsUnlocked,
                    UnlockTimeUtc = row.UnlockTimeUtc,
                    ProgressNum = row.ProgressNum,
                    ProgressDenom = row.ProgressDenom
                })
                .ToList() ?? new List<FriendAchievementRow>();
        }

        private static DateTime? ToUtc(long? unixSeconds)
        {
            if (!unixSeconds.HasValue || unixSeconds.Value <= 0)
            {
                return null;
            }

            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(unixSeconds.Value).UtcDateTime;
            }
            catch
            {
                return null;
            }
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return values?.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
        }

        private sealed class TokenState
        {
            public bool IsSuccess { get; set; }
            public bool AuthRequired { get; set; }
            public string ErrorMessage { get; set; }
            public string WebApiToken { get; set; }
            public string SteamUserId { get; set; }
        }

        private sealed class OwnedGamesPageResult
        {
            public bool Success { get; private set; }
            public bool AuthRequired { get; private set; }
            public bool TransientFailure { get; private set; }
            public string ErrorMessage { get; private set; }
            public IReadOnlyList<SteamOwnedGame> Games { get; private set; }

            public static OwnedGamesPageResult FromGames(IReadOnlyList<SteamOwnedGame> games)
            {
                return new OwnedGamesPageResult
                {
                    Success = true,
                    Games = games ?? Array.Empty<SteamOwnedGame>()
                };
            }

            public static OwnedGamesPageResult Failed(
                string message,
                bool authRequired = false,
                bool transientFailure = false)
            {
                return new OwnedGamesPageResult
                {
                    Success = false,
                    ErrorMessage = message,
                    AuthRequired = authRequired,
                    TransientFailure = transientFailure
                };
            }
        }
    }
}
