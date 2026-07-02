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
    internal sealed class ExophaseFriendsProvider : IFriendsProvider, ICurrentUserGameLabelReceiver
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

        // Per-refresh index of the current user's cached games, keyed on the servicing provider label
        // the plugin stored at scan time (see CurrentUserGameLabel). Populated by the refresh runtime via
        // SetCurrentUserGameLabels before the ownership pass and cleared each refresh. Used to auto-map a
        // friend's game to a local game without re-deriving platform from Playnite Source/Platform strings.
        private readonly Dictionary<Guid, string> _currentUserGameFamilyById =
            new Dictionary<Guid, string>();
        private readonly Dictionary<string, List<Guid>> _currentUserGameIdsByFamilyName =
            new Dictionary<string, List<Guid>>(StringComparer.OrdinalIgnoreCase);

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
            ClearCurrentUserGameIndex();

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
            ClearCurrentUserGameIndex();
            _apiClient.EndCookieSession();
        }

        // Receives the current user's cached game labels for this refresh and indexes them by servicing
        // provider label + normalized name, so the ownership merge can resolve a friend's game to a local
        // game using the stored provider label rather than re-deriving platform from Source/Platform.
        public void SetCurrentUserGameLabels(IReadOnlyList<CurrentUserGameLabel> labels)
        {
            ClearCurrentUserGameIndex();
            if (labels == null)
            {
                return;
            }

            foreach (var label in labels)
            {
                if (label == null || label.PlayniteGameId == Guid.Empty)
                {
                    continue;
                }

                var family = ExophaseFriendPlatformMatcher.ResolveStoredGameFamilyKey(
                    label.ProviderKey, label.ProviderPlatformKey);
                if (string.IsNullOrWhiteSpace(family))
                {
                    continue;
                }

                _currentUserGameFamilyById[label.PlayniteGameId] = family;

                var normalizedName = ExophaseGameNameMatcher.NormalizeGameName(label.GameName);
                if (string.IsNullOrWhiteSpace(normalizedName))
                {
                    continue;
                }

                var key = BuildFamilyNameKey(family, normalizedName);
                if (!_currentUserGameIdsByFamilyName.TryGetValue(key, out var ids))
                {
                    ids = new List<Guid>();
                    _currentUserGameIdsByFamilyName[key] = ids;
                }

                if (!ids.Contains(label.PlayniteGameId))
                {
                    ids.Add(label.PlayniteGameId);
                }
            }

            _logger?.Info($"[ExophaseFriends] Indexed {_currentUserGameFamilyById.Count} current-user game(s) by stored provider label for friend merge.");
        }

        private void ClearCurrentUserGameIndex()
        {
            _currentUserGameFamilyById.Clear();
            _currentUserGameIdsByFamilyName.Clear();
        }

        private static string BuildFamilyNameKey(string familyKey, string normalizedName)
        {
            return familyKey + "\n" + normalizedName;
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

            // Compare on the canonical provider platform key rather than the raw scraped token so a
            // friend's game tagged e.g. "ps4"/"ps5"/"vita" matches the coarse "psn" selection, and
            // "xbox-360"/"xbox-one" matches "xbox". Raw-token equality dropped these families.
            var selectedProviderKeys = new HashSet<string>(
                platforms
                    .Select(ExophaseFriendPlatformMatcher.ResolveProviderPlatformKey)
                    .Where(key => !string.IsNullOrWhiteSpace(key)),
                StringComparer.OrdinalIgnoreCase);

            var result = new List<FriendGameOwnership>();
            var skippedOtherPlatform = 0;
            foreach (var game in allGames)
            {
                cancel.ThrowIfCancellationRequested();
                var gameProviderKey = ExophaseFriendPlatformMatcher.ResolveProviderPlatformKey(game.Platform);
                if (string.IsNullOrWhiteSpace(gameProviderKey) || !selectedProviderKeys.Contains(gameProviderKey))
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
                    PlaytimeForeverMinutes = Math.Max(0, game.PlaytimeMinutes),
                    Playtime2WeeksMinutes = game.RecentPlaytimeMinutes > 0
                        ? (int?)game.RecentPlaytimeMinutes
                        : null,
                    LastPlayedUtc = game.LastPlayedUtc
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

            var url = ExophaseApiClient.BuildUrlFromSlug(parsed.GameSlug, parsed.PlatformSlug);
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
            var achievementUrl = BuildFriendAchievementUrl(parsed.GameSlug, parsed.PlatformSlug, contextId);
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
            var urls = new[]
            {
                BuildProfileUrl(username),
                BuildGamesUrl(username)
            }
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var games = new List<ExophaseFriendGame>();
            foreach (var url in urls)
            {
                cancel.ThrowIfCancellationRequested();
                var html = await FetchRenderedHtmlSerializedAsync(url, cancel, scrollToLoad: true).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(html))
                {
                    _logger?.Warn($"[ExophaseFriends] Profile fetch: empty or blocked HTML from {url}. " +
                        "The page may require login, have been rate-limited, or failed to render.");
                    continue;
                }

                var page = ExophaseFriendPageParser.ParseGames(html);
                games.AddRange(page.Games);
                _logger?.Debug($"[ExophaseFriends] Profile fetch: {url} -> htmlLength={html.Length}, " +
                    $"parsedGames={page.Games.Count}.");
            }

            var deduped = games
                .Where(game => !string.IsNullOrWhiteSpace(game?.Slug))
                .GroupBy(game => (game.Platform ?? string.Empty) + "|" + game.Slug, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            _logger?.Debug($"[ExophaseFriends] Profile fetch: '{username}' -> parsedGames={games.Count}, uniqueGames={deduped.Count}.");
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
                return IsMappedPlayniteGameCompatible(manualGameId, platform, localOverrideSlug: null)
                    ? manualGameId
                    : null;
            }

            var slug = ExophaseFriendGameKey.Parse(providerGameKey).GameSlug;
            var overrideMatch = (_settings.SlugOverrides ?? new Dictionary<Guid, string>())
                .FirstOrDefault(pair => string.Equals(pair.Value, slug, StringComparison.OrdinalIgnoreCase));
            if (overrideMatch.Key != Guid.Empty)
            {
                return IsMappedPlayniteGameCompatible(overrideMatch.Key, platform, slug)
                    ? overrideMatch.Key
                    : null;
            }

            return ResolveAutomaticPlayniteGameId(platform, title);
        }

        private bool IsMappedPlayniteGameCompatible(Guid playniteGameId, string platform, string localOverrideSlug)
        {
            if (playniteGameId == Guid.Empty)
            {
                return false;
            }

            var friendProviderKey = ExophaseFriendPlatformMatcher.ResolveProviderPlatformKey(platform);
            if (string.IsNullOrWhiteSpace(friendProviderKey))
            {
                return false;
            }

            // Prefer the servicing provider label the plugin stored for this game at scan time.
            if (_currentUserGameFamilyById.TryGetValue(playniteGameId, out var gameProviderKey) &&
                !string.IsNullOrWhiteSpace(gameProviderKey))
            {
                return string.Equals(gameProviderKey, friendProviderKey, StringComparison.OrdinalIgnoreCase);
            }

            var overridePlatform = ExophaseFriendPlatformMatcher.ExtractPlatformSlugFromGameSlug(localOverrideSlug);
            var overrideProviderKey = ExophaseFriendPlatformMatcher.ResolveProviderPlatformKey(overridePlatform);
            return !string.IsNullOrWhiteSpace(overrideProviderKey) &&
                   string.Equals(overrideProviderKey, friendProviderKey, StringComparison.OrdinalIgnoreCase);
        }

        private Guid? ResolveAutomaticPlayniteGameId(string platform, string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            var friendProviderKey = ExophaseFriendPlatformMatcher.ResolveProviderPlatformKey(platform);
            if (string.IsNullOrWhiteSpace(friendProviderKey))
            {
                return null;
            }

            var normalizedTitle = ExophaseGameNameMatcher.NormalizeGameName(title);
            if (string.IsNullOrWhiteSpace(normalizedTitle))
            {
                return null;
            }

            // Match against the current user's cached games using the servicing provider label the plugin
            // stored at scan time (see SetCurrentUserGameLabels), not a re-derived Source/Platform slug.
            // Auto-map only on a single, exact normalized-name match on the same platform family (edition
            // suffixes stripped on both sides via the shared matcher, so "Titanfall 2 Deluxe Edition" ==
            // the friend's "Titanfall 2"). Ambiguous multi-matches are left to manual mapping
            // (FriendGameMappings / SlugOverrides).
            return _currentUserGameIdsByFamilyName.TryGetValue(
                       BuildFamilyNameKey(friendProviderKey, normalizedTitle), out var ids) &&
                   ids.Count == 1
                ? ids[0]
                : (Guid?)null;
        }

        private static string BuildProfileUrl(string username)
        {
            // The base profile page lists every platform's games on one page (the ?environment= filter
            // is ignored by Exophase), so no query string is appended.
            return $"https://www.exophase.com/user/{Uri.EscapeDataString(username.Trim())}/";
        }

        private static string BuildGamesUrl(string username)
        {
            return $"https://www.exophase.com/user/{Uri.EscapeDataString(username.Trim())}/games/";
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

        private static string BuildFriendAchievementUrl(string gameSlug, string platformSlug, string contextId)
        {
            // Mirrors the games-page link: /game/{slug}/{endpoint}/#{contextId} renders the friend's
            // unlock state. The endpoint (trophies/challenges/achievements) is resolved from the
            // known platform so PSN/Ubisoft games hit the right page regardless of the slug suffix.
            // Without a context id this falls back to the plain (viewer-scoped) page.
            var url = ExophaseApiClient.BuildUrlFromSlug(gameSlug, platformSlug);
            return string.IsNullOrWhiteSpace(contextId)
                ? url
                : url + "#" + contextId.Trim();
        }

        private static List<string> NormalizePlatforms(IEnumerable<string> platforms)
        {
            return (platforms ?? Enumerable.Empty<string>())
                .Where(platform => !string.IsNullOrWhiteSpace(platform))
                .Select(NormalizePlatformSlug)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string NormalizePlatformSlug(string platform)
        {
            return ExophaseFriendPlatformMatcher.NormalizePlatformSlug(platform);
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
            public int RecentPlaytimeMinutes { get; set; }
            public DateTime? LastPlayedUtc { get; set; }
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
                    ExophaseApiClient.ResolveImageUrl(doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'col-game-information')]//a[contains(@class, 'image')]")),
                    ExophaseApiClient.ResolveImageUrl(doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'feature-header')]")),
                    ExophaseApiClient.ResolveImageUrl(doc.DocumentNode.SelectSingleNode("//a[contains(@class, 'image')]"))));
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
                        ExophaseApiClient.ResolveImageUrl(container?.SelectSingleNode(".//div[contains(@class, 'col-image')]")),
                        ExophaseApiClient.ResolveImageUrl(container)));
                    var recentDateContext = ResolveRecentDateContextUtc(container);
                    var lastPlayedUtc = ParseLastPlayedUtc(container) ?? recentDateContext;

                    result.Games.Add(new ExophaseFriendGame
                    {
                        Slug = slug,
                        Title = title,
                        ImageUrl = image,
                        Platform = platform,
                        ContextId = contextId,
                        PlaytimeMinutes = ParsePlaytimeMinutes(container?.InnerText),
                        RecentPlaytimeMinutes = ParseRecentPlaytimeMinutes(container?.InnerText, recentDateContext.HasValue),
                        LastPlayedUtc = lastPlayedUtc
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
                        return NormalizePlatformSlug(iconMatch.Groups[1].Value);
                    }

                    // Fall back to the media host path: https://m.exophase.com/{platform}/games/...
                    var imageSrc = ExophaseApiClient.ResolveImageUrl(container) ?? string.Empty;
                    var imageMatch = Regex.Match(imageSrc, @"exophase\.com/([a-z0-9]+)/(?:games|awards)/", RegexOptions.IgnoreCase);
                    if (imageMatch.Success)
                    {
                        return NormalizePlatformSlug(imageMatch.Groups[1].Value);
                    }
                }

                // Last resort: the platform suffix embedded in the slug (e.g. dead-space-3-origin -> origin).
                var lastDash = (slug ?? string.Empty).LastIndexOf('-');
                if (lastDash > 0 && lastDash < slug.Length - 1)
                {
                    return NormalizePlatformSlug(slug.Substring(lastDash + 1));
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

                var match = Regex.Match(text, @"(?:(\d+(?:[.,]\d+)?)\s*h(?:ours?)?)?\s*(?:(\d+)\s*m(?:in(?:utes?)?)?)?", RegexOptions.IgnoreCase);
                if (!match.Success)
                {
                    return 0;
                }

                var total = 0;
                // Accept a comma decimal (e.g. French "12,5 h") by normalizing it to a dot before
                // parsing with the invariant culture.
                var hoursText = match.Groups[1].Value.Replace(',', '.');
                if (double.TryParse(hoursText, NumberStyles.Float, CultureInfo.InvariantCulture, out var hours))
                {
                    total += (int)Math.Round(hours * 60);
                }

                if (int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes))
                {
                    total += minutes;
                }

                return Math.Max(0, total);
            }

            private static int ParseRecentPlaytimeMinutes(string text, bool hasRecentDateContext)
            {
                text = Clean(text);
                if (string.IsNullOrWhiteSpace(text))
                {
                    return 0;
                }

                if (!hasRecentDateContext &&
                    !Regex.IsMatch(
                        text,
                        @"\b(today|yesterday|last\s+24\s+hours?|past\s+24\s+hours?|this\s+week|last\s+week|monday|tuesday|wednesday|thursday|friday|saturday|sunday)\b",
                        RegexOptions.IgnoreCase))
                {
                    return 0;
                }

                return ParsePlaytimeMinutes(text);
            }

            private static DateTime? ParseLastPlayedUtc(HtmlNode container)
            {
                if (container == null)
                {
                    return null;
                }

                var nodeDate = FindDateTimeInNode(container);
                if (nodeDate.HasValue)
                {
                    return nodeDate.Value;
                }

                var text = Clean(container.InnerText);
                if (LooksLikeLastPlayedText(text) &&
                    TryParseDateText(text, out var parsed))
                {
                    return parsed;
                }

                return null;
            }

            private static bool LooksLikeLastPlayedText(string text)
            {
                return !string.IsNullOrWhiteSpace(text) &&
                       Regex.IsMatch(
                           text,
                           @"\b(last\s+played|played\s+on|recently\s+played)\b",
                           RegexOptions.IgnoreCase);
            }

            private static DateTime? ResolveRecentDateContextUtc(HtmlNode container)
            {
                var current = container;
                for (var depth = 0; depth < 8 && current != null; depth++, current = current.ParentNode)
                {
                    var isCurrentDateHeader = IsLikelyDateHeader(current);
                    var nodeDate = isCurrentDateHeader
                        ? FindDateTimeInNode(current, directOnly: true)
                        : null;
                    if (nodeDate.HasValue)
                    {
                        return nodeDate.Value;
                    }

                    if (TryParseDateText(Clean(current.InnerText), out var inlineDate) &&
                        isCurrentDateHeader)
                    {
                        return inlineDate;
                    }

                    var previous = current.PreviousSibling;
                    for (var i = 0; i < 12 && previous != null; i++, previous = previous.PreviousSibling)
                    {
                        if (string.IsNullOrWhiteSpace(previous.InnerText) &&
                            !previous.HasChildNodes)
                        {
                            continue;
                        }

                        if (!IsLikelyDateHeader(previous))
                        {
                            continue;
                        }

                        nodeDate = FindDateTimeInNode(previous);
                        if (nodeDate.HasValue)
                        {
                            return nodeDate.Value;
                        }

                        if (TryParseDateText(Clean(previous.InnerText), out var siblingDate) &&
                            IsLikelyDateHeader(previous))
                        {
                            return siblingDate;
                        }
                    }
                }

                return null;
            }

            private static DateTime? FindDateTimeInNode(HtmlNode node, bool directOnly = false)
            {
                if (node == null)
                {
                    return null;
                }

                foreach (var candidate in EnumerateDateNodes(node, directOnly))
                {
                    var parsed = ParseDateTimeAttribute(candidate);
                    if (parsed.HasValue)
                    {
                        return parsed.Value;
                    }
                }

                return null;
            }

            private static IEnumerable<HtmlNode> EnumerateDateNodes(HtmlNode node, bool directOnly)
            {
                if (node == null)
                {
                    yield break;
                }

                if (HasDateAttribute(node))
                {
                    yield return node;
                }

                var xpath = directOnly
                    ? "./*[@datetime or @data-date or @data-timestamp or @title]"
                    : ".//*[@datetime or @data-date or @data-timestamp or @title]";
                foreach (var child in Nodes(node.SelectNodes(xpath)))
                {
                    yield return child;
                }
            }

            private static bool HasDateAttribute(HtmlNode node)
            {
                return node?.Attributes["datetime"] != null ||
                       node?.Attributes["data-date"] != null ||
                       node?.Attributes["data-timestamp"] != null ||
                       node?.Attributes["title"] != null;
            }

            private static DateTime? ParseDateTimeAttribute(HtmlNode node)
            {
                var raw = FirstNonEmpty(
                    node?.GetAttributeValue("datetime", null),
                    node?.GetAttributeValue("data-date", null),
                    node?.GetAttributeValue("data-timestamp", null),
                    node?.GetAttributeValue("title", null));
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return null;
                }

                raw = WebUtility.HtmlDecode(raw.Trim());
                if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
                {
                    try
                    {
                        if (unix > 9999999999L)
                        {
                            unix /= 1000;
                        }

                        return DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;
                    }
                    catch
                    {
                        return null;
                    }
                }

                if (DateTimeOffset.TryParse(
                        raw,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var offset))
                {
                    return offset.UtcDateTime;
                }

                return TryParseDateText(raw, out var parsed) ? parsed : (DateTime?)null;
            }

            private static bool TryParseDateText(string text, out DateTime dateUtc)
            {
                dateUtc = default(DateTime);
                text = Clean(text);
                if (string.IsNullOrWhiteSpace(text))
                {
                    return false;
                }

                var today = DateTime.UtcNow.Date;
                if (Regex.IsMatch(text, @"\btoday\b", RegexOptions.IgnoreCase))
                {
                    dateUtc = DateTime.SpecifyKind(today, DateTimeKind.Utc);
                    return true;
                }

                if (Regex.IsMatch(text, @"\byesterday\b", RegexOptions.IgnoreCase))
                {
                    dateUtc = DateTime.SpecifyKind(today.AddDays(-1), DateTimeKind.Utc);
                    return true;
                }

                var weekdayMatch = Regex.Match(
                    text,
                    @"\b(monday|tuesday|wednesday|thursday|friday|saturday|sunday)\b",
                    RegexOptions.IgnoreCase);
                if (weekdayMatch.Success &&
                    TryParseWeekday(weekdayMatch.Groups[1].Value, out var weekday))
                {
                    var daysBack = ((int)today.DayOfWeek - (int)weekday + 7) % 7;
                    dateUtc = DateTime.SpecifyKind(today.AddDays(-daysBack), DateTimeKind.Utc);
                    return true;
                }

                var dateMatch = Regex.Match(
                    text,
                    @"\b(?:\d{1,2}\s+)?(?:jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|jun(?:e)?|jul(?:y)?|aug(?:ust)?|sep(?:t(?:ember)?)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?)\.?\s+\d{1,2}(?:,?\s+\d{4})?\b",
                    RegexOptions.IgnoreCase);
                if (!dateMatch.Success)
                {
                    dateMatch = Regex.Match(text, @"\b\d{4}-\d{1,2}-\d{1,2}\b", RegexOptions.IgnoreCase);
                }

                if (!dateMatch.Success)
                {
                    return false;
                }

                var value = dateMatch.Value;
                if (!Regex.IsMatch(value, @"\b\d{4}\b"))
                {
                    value = value + " " + today.Year.ToString(CultureInfo.InvariantCulture);
                }

                if (!DateTime.TryParse(
                        value,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var parsedDate))
                {
                    return false;
                }

                dateUtc = DateTime.SpecifyKind(parsedDate.Date, DateTimeKind.Utc);
                return true;
            }

            private static bool TryParseWeekday(string value, out DayOfWeek day)
            {
                switch ((value ?? string.Empty).Trim().ToLowerInvariant())
                {
                    case "sunday":
                        day = DayOfWeek.Sunday;
                        return true;
                    case "monday":
                        day = DayOfWeek.Monday;
                        return true;
                    case "tuesday":
                        day = DayOfWeek.Tuesday;
                        return true;
                    case "wednesday":
                        day = DayOfWeek.Wednesday;
                        return true;
                    case "thursday":
                        day = DayOfWeek.Thursday;
                        return true;
                    case "friday":
                        day = DayOfWeek.Friday;
                        return true;
                    case "saturday":
                        day = DayOfWeek.Saturday;
                        return true;
                    default:
                        day = default(DayOfWeek);
                        return false;
                }
            }

            private static bool IsLikelyDateHeader(HtmlNode node)
            {
                var text = Clean(node?.InnerText);
                if (string.IsNullOrWhiteSpace(text))
                {
                    return false;
                }

                if (node.SelectSingleNode(".//a[contains(@href, '/game/')]") != null)
                {
                    return false;
                }

                var name = node?.Name ?? string.Empty;
                if (Regex.IsMatch(name, @"^h[1-6]$", RegexOptions.IgnoreCase))
                {
                    return true;
                }

                var className = node?.GetAttributeValue("class", string.Empty) ?? string.Empty;
                if (Regex.IsMatch(className, @"\b(date|day|activity|header|title)\b", RegexOptions.IgnoreCase))
                {
                    return true;
                }

                return IsCompactDateText(text);
            }

            private static bool IsCompactDateText(string text)
            {
                if (string.IsNullOrWhiteSpace(text) || text.Length > 48)
                {
                    return false;
                }

                return Regex.IsMatch(
                           text,
                           @"^(today|yesterday|monday|tuesday|wednesday|thursday|friday|saturday|sunday)$",
                           RegexOptions.IgnoreCase) ||
                       Regex.IsMatch(
                           text,
                           @"^(?:jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|jun(?:e)?|jul(?:y)?|aug(?:ust)?|sep(?:t(?:ember)?)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?)\.?\s+\d{1,2}(?:,?\s+\d{4})?$",
                           RegexOptions.IgnoreCase) ||
                       Regex.IsMatch(
                           text,
                           @"^\d{4}-\d{1,2}-\d{1,2}$",
                           RegexOptions.IgnoreCase);
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
