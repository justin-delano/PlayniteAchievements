using HtmlAgilityPack;
using Playnite.SDK;
using Playnite.SDK.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Friends;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.Exophase
{
    internal sealed class ExophaseFriendsProvider : IFriendsProvider
    {
        private const string Provider = "Exophase";
        private readonly ExophaseApiClient _apiClient;
        private readonly ExophaseSettings _settings;
        private readonly PlayniteAchievementsSettings _globalSettings;
        private readonly IPlayniteAPI _playniteApi;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _webViewGate = new SemaphoreSlim(1, 1);

        // Per-refresh map of (friend username + provider game key) -> the friend's per-game context id,
        // parsed from the profile games page as the "#{id}" fragment on each game's achievements link.
        // Used to build the friend-scoped achievement URL. Populated during the ownership pass (which
        // always runs before achievements) and cleared each refresh.
        private readonly Dictionary<string, string> _friendGameContextIds =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public ExophaseFriendsProvider(
            ExophaseApiClient apiClient,
            ExophaseSettings settings,
            PlayniteAchievementsSettings globalSettings,
            IPlayniteAPI playniteApi,
            ILogger logger)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _globalSettings = globalSettings;
            _playniteApi = playniteApi;
            _logger = logger;
        }

        public string ProviderKey => Provider;

        public Task<FriendsProviderResult<FriendsRefreshPreparation>> BeginRefreshAsync(CancellationToken cancel)
        {
            _friendGameContextIds.Clear();

            // Load and validate the encrypted cookie snapshot once for this refresh; every per-friend
            // and per-game fetch reuses the cached cookies instead of decrypting the file per call.
            // Exophase still fetches public profile/owned-games/definition pages without critical
            // cookies, so achievements remain enabled regardless (the single log line surfaces any
            // missing criticals), matching the prior warn-and-continue behavior.
            var hasCriticalCookies = _apiClient.BeginCookieSession();
            _logger?.Info($"[ExophaseFriends] BeginRefresh: cookie session prepared (allCriticalCookiesPresent={hasCriticalCookies}).");

            return Task.FromResult(FriendsProviderResult<FriendsRefreshPreparation>.FromData(new FriendsRefreshPreparation
            {
                CanRefreshAchievements = true
            }));
        }

        public void EndRefresh()
        {
            _friendGameContextIds.Clear();
            _apiClient.EndCookieSession();
        }

        public async Task<FriendsProviderResult<IReadOnlyList<FriendIdentity>>> GetFriendsAsync(CancellationToken cancel)
        {
            var now = DateTime.UtcNow;
            var configs = GetConfiguredFriends(includeIgnored: false);
            var identities = new List<FriendIdentity>();
            foreach (var friend in configs)
            {
                cancel.ThrowIfCancellationRequested();
                var updated = await RefreshProfileMetadataIfNeededAsync(friend, cancel).ConfigureAwait(false);
                identities.Add(new FriendIdentity
                {
                    ProviderKey = Provider,
                    ExternalUserId = friend.ExternalUserId.Trim(),
                    DisplayName = string.IsNullOrWhiteSpace(updated?.DisplayName) ? friend.ExternalUserId.Trim() : updated.DisplayName.Trim(),
                    AvatarUrl = updated?.AvatarUrl,
                    AvatarPath = updated?.AvatarPath,
                    LastRefreshedUtc = now
                });
            }

            return FriendsProviderResult<IReadOnlyList<FriendIdentity>>.FromData(identities);
        }

        public async Task<FriendsProviderResult<IReadOnlyList<FriendGameOwnership>>> GetOwnedGamesAsync(
            FriendIdentity friend,
            CancellationToken cancel)
        {
            var friendId = friend?.ExternalUserId;
            var config = GetConfiguredFriend(friendId);
            if (config == null)
            {
                _logger?.Warn($"[ExophaseFriends] GetOwnedGames: no saved friend configuration for '{friendId}'; returning 0 games. " +
                    "The friend may have been removed from Friends settings or the username no longer matches.");
                return FriendsProviderResult<IReadOnlyList<FriendGameOwnership>>.FromData(Array.Empty<FriendGameOwnership>());
            }

            var platforms = NormalizePlatforms(config.SelectedPlatforms);
            _logger?.Info($"[ExophaseFriends] GetOwnedGames: friend='{config.ExternalUserId}', scope={config.LibraryScope}, " +
                $"platforms=[{string.Join(", ", platforms)}] (count={platforms.Count}).");
            if (platforms.Count == 0)
            {
                _logger?.Warn($"[ExophaseFriends] GetOwnedGames: friend '{config.ExternalUserId}' has no platforms selected, " +
                    "so there is nothing to fetch (fetched will be 0). Select at least one platform for this friend in Exophase settings.");
                return FriendsProviderResult<IReadOnlyList<FriendGameOwnership>>.FromData(Array.Empty<FriendGameOwnership>());
            }

            // Exophase lists every platform's games together on a single profile page: there is no
            // per-platform page and the ?environment= filter is ignored. Fetch the profile once (with
            // scroll to load the lazily-appended entries) and filter to the friend's selected platforms.
            var allGames = await FetchOwnedGamesAsync(config.ExternalUserId, cancel).ConfigureAwait(false);
            var selectedPlatforms = new HashSet<string>(platforms, StringComparer.OrdinalIgnoreCase);

            var result = new List<FriendGameOwnership>();
            var skippedOtherPlatform = 0;
            foreach (var game in allGames)
            {
                cancel.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(game.Platform) || !selectedPlatforms.Contains(game.Platform))
                {
                    skippedOtherPlatform++;
                    continue;
                }

                var key = ExophaseFriendGameKey.Build(game.Platform, game.Slug);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                // Remember the friend's per-game context id so the achievement pass can build the
                // friend-scoped achievement URL for this exact (friend, game) pair.
                if (!string.IsNullOrWhiteSpace(game.ContextId))
                {
                    _friendGameContextIds[BuildContextMapKey(config.ExternalUserId, key)] = game.ContextId;
                }

                result.Add(new FriendGameOwnership
                {
                    ProviderKey = Provider,
                    ExternalUserId = config.ExternalUserId,
                    ProviderGameKey = key,
                    ProviderPlatformKey = ExophaseDataProvider.MapSlugToProviderPlatformKey(game.Platform),
                    PlayniteGameId = ResolveMappedPlayniteGameId(key, game.Platform, game.Title),
                    GameName = game.Title,
                    IconUrl = game.ImageUrl,
                    CoverUrl = game.ImageUrl,
                    PlaytimeForeverMinutes = Math.Max(0, game.PlaytimeMinutes)
                });
            }

            var deduped = result
                .GroupBy(item => item.ProviderGameKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(item => item.GameName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _logger?.Info($"[ExophaseFriends] GetOwnedGames: '{config.ExternalUserId}' parsed {allGames.Count} profile game(s), " +
                $"kept {deduped.Count} on selected platform(s) [{string.Join(", ", platforms)}] " +
                $"(skipped {skippedOtherPlatform} on other platforms).");
            return FriendsProviderResult<IReadOnlyList<FriendGameOwnership>>.FromData(deduped);
        }

        public async Task<FriendsProviderResult<FriendGameDefinition>> GetFriendGameDefinitionAsync(
            string providerGameKey,
            int appId,
            string gameName,
            CancellationToken cancel)
        {
            var parsed = ExophaseFriendGameKey.Parse(providerGameKey);
            if (string.IsNullOrWhiteSpace(parsed.GameSlug))
            {
                return FriendsProviderResult<FriendGameDefinition>.Failed("Exophase friend game key is missing a slug.");
            }

            var url = ExophaseApiClient.BuildUrlFromSlug(parsed.GameSlug);
            var achievements = await _apiClient
                .FetchAchievementsAsync(url, ExophaseApiClient.MapLanguageToAcceptLanguage("en-US"), cancel, waitForImages: true)
                .ConfigureAwait(false);

            var rows = achievements ?? new List<AchievementDetail>();

            // Reuse the main provider's rarity assignment so friend achievements get the same
            // percentage/rarity tiers instead of defaulting to Common.
            ExophaseDataProvider.ApplyProviderOwnedRarity(
                rows,
                ExophaseDataProvider.MapSlugToProviderPlatformKey(parsed.PlatformSlug));
            var headerImageUrl = await FetchGameHeaderImageAsync(parsed.GameSlug, cancel).ConfigureAwait(false);

            return FriendsProviderResult<FriendGameDefinition>.FromData(new FriendGameDefinition
            {
                ProviderKey = Provider,
                ProviderGameKey = providerGameKey,
                ProviderPlatformKey = ExophaseDataProvider.MapSlugToProviderPlatformKey(parsed.PlatformSlug),
                GameName = gameName,
                IconUrl = headerImageUrl,
                Status = rows.Count > 0 ? FriendGameDefinitionStatus.Ok : FriendGameDefinitionStatus.NoAchievements,
                LastCheckedUtc = DateTime.UtcNow,
                Achievements = rows
            });
        }

        public async Task<FriendsProviderResult<FriendGameAchievements>> GetFriendGameAchievementsAsync(
            FriendIdentity friend,
            string providerGameKey,
            int appId,
            string gameName,
            CancellationToken cancel)
        {
            var parsed = ExophaseFriendGameKey.Parse(providerGameKey);
            if (string.IsNullOrWhiteSpace(friend?.ExternalUserId) ||
                string.IsNullOrWhiteSpace(parsed.PlatformSlug) ||
                string.IsNullOrWhiteSpace(parsed.GameSlug))
            {
                return FriendsProviderResult<FriendGameAchievements>.Failed("Exophase friend id or game key is missing.");
            }

            // The friend's games page links each game to /game/{slug}/achievements/#{contextId}, which
            // renders that friend's unlock state. Reuse the main provider's achievement fetch+parse
            // against that friend-scoped URL: FetchAchievementsAsync waits for the JS-loaded unlock data
            // and ParseAchievementsHtml yields names, descriptions, full-size icons AND unlock times for
            // the friend in one pass (no separate awards scrape).
            var contextId = GetFriendGameContextId(friend.ExternalUserId, providerGameKey);
            var achievementUrl = BuildFriendAchievementUrl(parsed.GameSlug, contextId);
            var achievements = await _apiClient
                .FetchAchievementsAsync(achievementUrl, ExophaseApiClient.MapLanguageToAcceptLanguage("en-US"), cancel, waitForImages: true)
                .ConfigureAwait(false) ?? new List<AchievementDetail>();

            // Reuse the main provider's rarity assignment (percentage -> rarity tier) so friend
            // achievements are not all reported as Common.
            ExophaseDataProvider.ApplyProviderOwnedRarity(
                achievements,
                ExophaseDataProvider.MapSlugToProviderPlatformKey(parsed.PlatformSlug));

            var rows = achievements
                .Where(achievement => achievement != null)
                .Select(achievement => new FriendAchievementRow
                {
                    DisplayName = achievement.DisplayName,
                    Description = achievement.Description,
                    IconUrl = achievement.Unlocked ? achievement.UnlockedIconPath : achievement.LockedIconPath,
                    Unlocked = achievement.Unlocked,
                    UnlockTimeUtc = achievement.UnlockTimeUtc,
                    ProgressNum = achievement.ProgressNum,
                    ProgressDenom = achievement.ProgressDenom
                })
                .ToList();

            _logger?.Debug($"[ExophaseFriends] GetFriendGameAchievements: friend='{friend.ExternalUserId}', " +
                $"gameKey='{providerGameKey}', contextId='{contextId ?? "(none)"}' -> " +
                $"{achievements.Count} achievement(s), {rows.Count(row => row.Unlocked)} unlocked.");

            return FriendsProviderResult<FriendGameAchievements>.FromData(new FriendGameAchievements
            {
                Friend = friend,
                ProviderGameKey = providerGameKey,
                LastUpdatedUtc = DateTime.UtcNow,
                StatsUnavailable = achievements.Count == 0,
                Rows = rows
            });
        }

        private List<FriendSettingsEntry> GetConfiguredFriends(bool includeIgnored)
        {
            var central = _globalSettings?.Persisted?.GetFriendSettings(Provider, includeIgnored)
                ?.Where(friend => !string.IsNullOrWhiteSpace(friend?.ExternalUserId))
                .ToList();
            if (central?.Count > 0)
            {
                return central;
            }

            return (_settings.Friends ?? new List<ExophaseFriendSettings>())
                .Where(friend => !string.IsNullOrWhiteSpace(friend?.Username))
                .Select(friend => new FriendSettingsEntry
                {
                    ProviderKey = Provider,
                    ExternalUserId = friend.Username.Trim(),
                    DisplayName = string.IsNullOrWhiteSpace(friend.DisplayName) ? friend.Username.Trim() : friend.DisplayName.Trim(),
                    AvatarUrl = friend.AvatarUrl,
                    AvatarPath = friend.AvatarPath,
                    Source = FriendSettingsSource.Manual,
                    LibraryScope = friend.LibraryScope,
                    SelectedPlatforms = FriendSettingsEntry.NormalizePlatformList(friend.SelectedPlatforms),
                    AddedUtc = friend.AddedUtc == default(DateTime) ? DateTime.UtcNow : friend.AddedUtc,
                    LastRefreshedUtc = friend.LastRefreshedUtc,
                    LastProbedUtc = friend.LastProbedUtc,
                    LastProbeStatus = friend.LastProbeStatus,
                    LastError = friend.LastError
                })
                .ToList();
        }

        private FriendSettingsEntry GetConfiguredFriend(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return null;
            }

            return GetConfiguredFriends(includeIgnored: false)
                .FirstOrDefault(friend => string.Equals(friend.ExternalUserId, username.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private async Task<FriendSettingsEntry> RefreshProfileMetadataIfNeededAsync(
            FriendSettingsEntry friend,
            CancellationToken cancel)
        {
            if (friend == null || string.IsNullOrWhiteSpace(friend.ExternalUserId))
            {
                return friend;
            }

            var live = _globalSettings?.Persisted?.GetFriendSetting(Provider, friend.ExternalUserId) ?? friend;
            if (!ShouldProbeProfile(live))
            {
                return live;
            }

            try
            {
                var metadata = await FetchProfileMetadataAsync(friend.ExternalUserId, cancel).ConfigureAwait(false);
                live.LastProbedUtc = DateTime.UtcNow;
                live.LastProbeStatus = "ok";
                live.LastError = null;
                if (!string.IsNullOrWhiteSpace(metadata?.DisplayName))
                {
                    live.DisplayName = metadata.DisplayName;
                }

                if (!string.IsNullOrWhiteSpace(metadata?.AvatarUrl))
                {
                    live.AvatarUrl = metadata.AvatarUrl;
                }

                return live;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                live.LastProbedUtc = DateTime.UtcNow;
                live.LastProbeStatus = "failed";
                live.LastError = ex.Message;
                _logger?.Debug(ex, $"[ExophaseFriends] Failed to probe profile metadata for '{friend.ExternalUserId}'.");
                return live;
            }
        }

        private static bool ShouldProbeProfile(FriendSettingsEntry friend)
        {
            if (friend == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(friend.AvatarUrl) && string.IsNullOrWhiteSpace(friend.AvatarPath))
            {
                return true;
            }

            if (!friend.LastProbedUtc.HasValue)
            {
                return true;
            }

            return DateTime.UtcNow - friend.LastProbedUtc.Value > TimeSpan.FromDays(7);
        }

        private async Task<ExophaseProfileMetadata> FetchProfileMetadataAsync(
            string username,
            CancellationToken cancel)
        {
            var url = BuildProfileUrl(username);
            var html = await FetchRenderedHtmlSerializedAsync(url, cancel, scrollToLoad: false).ConfigureAwait(false);
            return ExophaseFriendPageParser.ParseProfile(html);
        }

        private async Task<string> FetchGameHeaderImageAsync(string gameSlug, CancellationToken cancel)
        {
            if (string.IsNullOrWhiteSpace(gameSlug))
            {
                return null;
            }

            try
            {
                var url = ExophaseApiClient.BuildUrlFromSlug(gameSlug);
                var html = await FetchRenderedHtmlSerializedAsync(url, cancel, scrollToLoad: false).ConfigureAwait(false);
                return ExophaseFriendPageParser.ParseGameHeaderImageUrl(html);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"[ExophaseFriends] Failed to fetch game header image for '{gameSlug}'.");
                return null;
            }
        }

        private async Task<IReadOnlyList<ExophaseFriendGame>> FetchOwnedGamesAsync(
            string username,
            CancellationToken cancel)
        {
            var url = BuildProfileUrl(username);
            var html = await FetchRenderedHtmlSerializedAsync(url, cancel, scrollToLoad: true).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(html))
            {
                _logger?.Warn($"[ExophaseFriends] Profile fetch: empty or blocked HTML from {url}. " +
                    "The page may require login, have been rate-limited, or failed to render.");
                return Array.Empty<ExophaseFriendGame>();
            }

            var page = ExophaseFriendPageParser.ParseGames(html);
            var deduped = page.Games
                .Where(game => !string.IsNullOrWhiteSpace(game?.Slug))
                .GroupBy(game => (game.Platform ?? string.Empty) + "|" + game.Slug, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            _logger?.Debug($"[ExophaseFriends] Profile fetch: {url} -> htmlLength={html.Length}, " +
                $"parsedGames={page.Games.Count}, uniqueGames={deduped.Count}.");
            return deduped;
        }

        private async Task<string> FetchRenderedHtmlSerializedAsync(string url, CancellationToken cancel, bool scrollToLoad = false)
        {
            await _webViewGate.WaitAsync(cancel).ConfigureAwait(false);
            try
            {
                return await _apiClient.FetchRenderedHtmlAsync(url, cancel, scrollToLoad: scrollToLoad).ConfigureAwait(false);
            }
            finally
            {
                _webViewGate.Release();
            }
        }

        private Guid? ResolveMappedPlayniteGameId(string providerGameKey, string platform, string title)
        {
            var normalizedKey = ExophaseSettings.NormalizeFriendGameMappingKey(providerGameKey);
            if (!string.IsNullOrWhiteSpace(normalizedKey) &&
                _settings.FriendGameMappings?.TryGetValue(normalizedKey, out var manualGameId) == true &&
                manualGameId != Guid.Empty)
            {
                return manualGameId;
            }

            var slug = ExophaseFriendGameKey.Parse(providerGameKey).GameSlug;
            var overrideMatch = (_settings.SlugOverrides ?? new Dictionary<Guid, string>())
                .FirstOrDefault(pair => string.Equals(pair.Value, slug, StringComparison.OrdinalIgnoreCase));
            if (overrideMatch.Key != Guid.Empty)
            {
                return overrideMatch.Key;
            }

            return ResolveAutomaticPlayniteGameId(platform, title);
        }

        private Guid? ResolveAutomaticPlayniteGameId(string platform, string title)
        {
            if (string.IsNullOrWhiteSpace(title) || _playniteApi?.Database?.Games == null)
            {
                return null;
            }

            var normalizedTitle = NormalizeTitle(title);
            if (string.IsNullOrWhiteSpace(normalizedTitle))
            {
                return null;
            }

            var candidates = _playniteApi.Database.Games
                .Where(game => game != null && IsPlatformCompatible(game, platform))
                .Select(game => new
                {
                    Game = game,
                    Score = TitleSimilarity(normalizedTitle, NormalizeTitle(game.Name))
                })
                .Where(item => item.Score >= 0.92)
                .OrderByDescending(item => item.Score)
                .Take(3)
                .ToList();

            if (candidates.Count == 1 ||
                (candidates.Count > 1 && candidates[0].Score >= 0.98 && candidates[0].Score - candidates[1].Score >= 0.05))
            {
                return candidates[0].Game.Id;
            }

            return null;
        }

        private static bool IsPlatformCompatible(Game game, string platform)
        {
            if (game == null || string.IsNullOrWhiteSpace(platform))
            {
                return true;
            }

            var token = platform.Trim().ToLowerInvariant();
            var source = game.Source?.Name ?? string.Empty;
            var platforms = string.Join(" ", game.Platforms?.Select(item => item?.Name) ?? Enumerable.Empty<string>());
            var haystack = (source + " " + platforms).ToLowerInvariant();

            if (token == "steam") return haystack.Contains("steam");
            if (token == "gog") return haystack.Contains("gog");
            if (token == "epic") return haystack.Contains("epic");
            if (token == "psn" || token == "ps3" || token == "ps4" || token == "ps5" || token == "vita")
            {
                return haystack.Contains("playstation") || haystack.Contains("psn") || haystack.Contains(token);
            }

            if (token.StartsWith("xbox", StringComparison.Ordinal))
            {
                return haystack.Contains("xbox");
            }

            if (token == "android" || token == "apple")
            {
                return haystack.Contains(token) || haystack.Contains("ios") || haystack.Contains("mobile");
            }

            if (token == "ubisoft" || token == "uplay")
            {
                return haystack.Contains("ubisoft") || haystack.Contains("uplay");
            }

            return true;
        }

        private static double TitleSimilarity(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return 0;
            }

            if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            var distance = LevenshteinDistance(left, right);
            var max = Math.Max(left.Length, right.Length);
            return max <= 0 ? 0 : 1d - ((double)distance / max);
        }

        private static int LevenshteinDistance(string left, string right)
        {
            var costs = new int[right.Length + 1];
            for (var j = 0; j < costs.Length; j++)
            {
                costs[j] = j;
            }

            for (var i = 1; i <= left.Length; i++)
            {
                costs[0] = i;
                var previous = i - 1;
                for (var j = 1; j <= right.Length; j++)
                {
                    var current = costs[j];
                    costs[j] = Math.Min(
                        Math.Min(costs[j] + 1, costs[j - 1] + 1),
                        previous + (left[i - 1] == right[j - 1] ? 0 : 1));
                    previous = current;
                }
            }

            return costs[right.Length];
        }

        private static string BuildProfileUrl(string username)
        {
            // The base profile page lists every platform's games on one page (the ?environment= filter
            // is ignored by Exophase), so no query string is appended.
            return $"https://www.exophase.com/user/{Uri.EscapeDataString(username.Trim())}/";
        }

        private static string BuildContextMapKey(string username, string providerGameKey)
        {
            return (username?.Trim() ?? string.Empty) + "|" + (providerGameKey?.Trim() ?? string.Empty);
        }

        private string GetFriendGameContextId(string username, string providerGameKey)
        {
            return _friendGameContextIds.TryGetValue(BuildContextMapKey(username, providerGameKey), out var contextId)
                ? contextId
                : null;
        }

        private static string BuildFriendAchievementUrl(string gameSlug, string contextId)
        {
            // Mirrors the games-page link: /game/{slug}/achievements/#{contextId} renders the friend's
            // unlock state. Without a context id this falls back to the plain (viewer-scoped) page.
            var url = ExophaseApiClient.BuildUrlFromSlug(gameSlug);
            return string.IsNullOrWhiteSpace(contextId)
                ? url
                : url + "#" + contextId.Trim();
        }

        private static List<string> NormalizePlatforms(IEnumerable<string> platforms)
        {
            return (platforms ?? Enumerable.Empty<string>())
                .Where(platform => !string.IsNullOrWhiteSpace(platform))
                .Select(platform => platform.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string NormalizeTitle(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var lower = WebUtility.HtmlDecode(value).ToLowerInvariant();
            lower = Regex.Replace(lower, @"[^\p{L}\p{Nd}]+", " ");
            lower = Regex.Replace(lower, @"\b(the|a|an|edition|remastered|remaster|complete|definitive)\b", " ");
            return Regex.Replace(lower, @"\s+", " ").Trim();
        }

        private sealed class ExophaseFriendGame
        {
            public string Slug { get; set; }
            public string Title { get; set; }
            public string ImageUrl { get; set; }
            public string Platform { get; set; }

            // Friend's per-game context id from the games-page achievement link fragment
            // (/game/{slug}/achievements/#{ContextId}); scopes the achievement page to this friend.
            public string ContextId { get; set; }
            public int PlaytimeMinutes { get; set; }
        }

        private sealed class ParsedGamesPage
        {
            public List<ExophaseFriendGame> Games { get; set; } = new List<ExophaseFriendGame>();
        }

        private sealed class ExophaseProfileMetadata
        {
            public string DisplayName { get; set; }
            public string AvatarUrl { get; set; }
        }

        private static class ExophaseFriendGameKey
        {
            public static string Build(string platformSlug, string gameSlug)
            {
                if (string.IsNullOrWhiteSpace(platformSlug) || string.IsNullOrWhiteSpace(gameSlug))
                {
                    return null;
                }

                return platformSlug.Trim().ToLowerInvariant() + "|" + gameSlug.Trim().ToLowerInvariant();
            }

            public static (string PlatformSlug, string GameSlug) Parse(string key)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    return (null, null);
                }

                var parts = key.Split(new[] { '|' }, 2);
                return parts.Length == 2
                    ? (parts[0].Trim().ToLowerInvariant(), parts[1].Trim().ToLowerInvariant())
                    : (null, key.Trim().ToLowerInvariant());
            }
        }

        private static class ExophaseFriendPageParser
        {
            public static ExophaseProfileMetadata ParseProfile(string html)
            {
                var doc = LoadDocument(html);
                if (doc?.DocumentNode == null)
                {
                    return new ExophaseProfileMetadata();
                }

                var header = doc.DocumentNode.SelectSingleNode("//section[contains(@class, 'section-profile-header')]")
                    ?? doc.DocumentNode;
                return new ExophaseProfileMetadata
                {
                    DisplayName = FirstNonEmpty(
                        Clean(header.SelectSingleNode(".//div[contains(@class, 'column-username')]//h2")?.InnerText),
                        Clean(header.SelectSingleNode(".//h2")?.InnerText)),
                    AvatarUrl = NormalizeUrl(FirstNonEmpty(
                        header.SelectSingleNode(".//div[contains(@class, 'avatar')]//img")?.GetAttributeValue("src", null),
                        header.SelectSingleNode(".//img[contains(@src, '/forums/data/avatars/')]")?.GetAttributeValue("src", null)))
                };
            }

            public static string ParseGameHeaderImageUrl(string html)
            {
                var doc = LoadDocument(html);
                if (doc?.DocumentNode == null)
                {
                    return null;
                }

                return NormalizeUrl(FirstNonEmpty(
                    doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'col-game-information')]//a[contains(@class, 'image')]//img")?.GetAttributeValue("src", null),
                    doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'feature-header')]//img")?.GetAttributeValue("src", null),
                    doc.DocumentNode.SelectSingleNode("//a[contains(@class, 'image')]//img")?.GetAttributeValue("src", null)));
            }

            public static ParsedGamesPage ParseGames(string html)
            {
                var result = new ParsedGamesPage();
                var doc = LoadDocument(html);
                if (doc?.DocumentNode == null)
                {
                    return result;
                }

                // Each game row exposes its title as <h3><a href=".../game/{slug}/achievements/#id">Title</a>.
                // Prefer the heading anchors so award-icon and navigation links are not mistaken for games;
                // fall back to any /game/ anchor if the markup changes.
                var links = doc.DocumentNode.SelectNodes("//h3/a[contains(@href, '/game/')]")
                    ?? doc.DocumentNode.SelectNodes("//a[contains(@href, '/game/')]");

                foreach (var link in Nodes(links))
                {
                    var rawHref = link.GetAttributeValue("href", null);
                    var href = NormalizeUrl(rawHref);
                    var slug = ExophaseApiClient.ExtractSlugFromUrl(href);
                    if (string.IsNullOrWhiteSpace(slug))
                    {
                        continue;
                    }

                    var contextId = ExtractFragmentId(rawHref);
                    var container = FindGameContainer(link);
                    var platform = DerivePlatform(container, slug);
                    var title = FirstNonEmpty(
                        Clean(link.InnerText),
                        Clean(link.GetAttributeValue("title", null)),
                        Clean(container?.SelectSingleNode(".//div[contains(@class, 'col-image')]//img")?.GetAttributeValue("alt", null)),
                        SlugToTitle(slug, platform));
                    var image = NormalizeUrl(FirstNonEmpty(
                        container?.SelectSingleNode(".//div[contains(@class, 'col-image')]//img")?.GetAttributeValue("src", null),
                        container?.SelectSingleNode(".//img")?.GetAttributeValue("src", null),
                        container?.SelectSingleNode(".//img")?.GetAttributeValue("data-src", null)));

                    result.Games.Add(new ExophaseFriendGame
                    {
                        Slug = slug,
                        Title = title,
                        ImageUrl = image,
                        Platform = platform,
                        ContextId = contextId,
                        PlaytimeMinutes = ParsePlaytimeMinutes(container?.InnerText)
                    });
                }

                return result;
            }

            private static HtmlDocument LoadDocument(string html)
            {
                if (string.IsNullOrWhiteSpace(html))
                {
                    return null;
                }

                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                return doc;
            }

            private static IEnumerable<HtmlNode> Nodes(HtmlNodeCollection nodes)
            {
                return nodes ?? Enumerable.Empty<HtmlNode>();
            }

            private static string ExtractFragmentId(string href)
            {
                if (string.IsNullOrWhiteSpace(href))
                {
                    return null;
                }

                var hashIndex = href.IndexOf('#');
                if (hashIndex < 0 || hashIndex >= href.Length - 1)
                {
                    return null;
                }

                var fragment = href.Substring(hashIndex + 1).Trim();
                return string.IsNullOrWhiteSpace(fragment) ? null : fragment;
            }

            private static HtmlNode FindGameContainer(HtmlNode link)
            {
                var current = link;
                for (var i = 0; i < 6 && current?.ParentNode != null; i++)
                {
                    current = current.ParentNode;
                    if (current.SelectSingleNode(".//div[contains(@class, 'col-image')]") != null ||
                        current.SelectSingleNode(".//i[contains(@class, 'exo-icon-service-')]") != null)
                    {
                        return current;
                    }
                }

                return link.ParentNode;
            }

            private static string DerivePlatform(HtmlNode container, string slug)
            {
                if (container != null)
                {
                    // The service icon carries the platform: <i class="exo-icon-service-origin ...">.
                    var serviceIcon = container.SelectSingleNode(".//i[contains(@class, 'exo-icon-service-')]");
                    var iconClass = serviceIcon?.GetAttributeValue("class", string.Empty) ?? string.Empty;
                    var iconMatch = Regex.Match(iconClass, @"exo-icon-service-([a-z0-9]+)", RegexOptions.IgnoreCase);
                    if (iconMatch.Success)
                    {
                        return iconMatch.Groups[1].Value.ToLowerInvariant();
                    }

                    // Fall back to the media host path: https://m.exophase.com/{platform}/games/...
                    var imageSrc = container.SelectSingleNode(".//img")?.GetAttributeValue("src", null) ?? string.Empty;
                    var imageMatch = Regex.Match(imageSrc, @"exophase\.com/([a-z0-9]+)/(?:games|awards)/", RegexOptions.IgnoreCase);
                    if (imageMatch.Success)
                    {
                        return imageMatch.Groups[1].Value.ToLowerInvariant();
                    }
                }

                // Last resort: the platform suffix embedded in the slug (e.g. dead-space-3-origin -> origin).
                var lastDash = (slug ?? string.Empty).LastIndexOf('-');
                if (lastDash > 0 && lastDash < slug.Length - 1)
                {
                    return slug.Substring(lastDash + 1).ToLowerInvariant();
                }

                return null;
            }

            private static string SlugToTitle(string slug, string platform)
            {
                var value = slug ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(platform) &&
                    value.EndsWith("-" + platform, StringComparison.OrdinalIgnoreCase))
                {
                    value = value.Substring(0, value.Length - platform.Length - 1);
                }

                return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(value.Replace('-', ' '));
            }

            private static int ParsePlaytimeMinutes(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return 0;
                }

                var match = Regex.Match(text, @"(?:(\d+(?:\.\d+)?)\s*h(?:ours?)?)?\s*(?:(\d+)\s*m(?:in(?:utes?)?)?)?", RegexOptions.IgnoreCase);
                if (!match.Success)
                {
                    return 0;
                }

                var total = 0;
                if (double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var hours))
                {
                    total += (int)Math.Round(hours * 60);
                }

                if (int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes))
                {
                    total += minutes;
                }

                return Math.Max(0, total);
            }

            private static string NormalizeUrl(string url)
            {
                if (string.IsNullOrWhiteSpace(url))
                {
                    return null;
                }

                url = WebUtility.HtmlDecode(url.Trim());
                if (url.StartsWith("//", StringComparison.Ordinal))
                {
                    return "https:" + url;
                }

                if (url.StartsWith("/", StringComparison.Ordinal))
                {
                    return "https://www.exophase.com" + url;
                }

                return url;
            }

            private static string Clean(string value)
            {
                return string.IsNullOrWhiteSpace(value)
                    ? null
                    : Regex.Replace(WebUtility.HtmlDecode(value), @"\s+", " ").Trim();
            }

            private static string FirstNonEmpty(params string[] values)
            {
                return values?.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            }
        }
    }
}
