using HtmlAgilityPack;
using Playnite.SDK;
using Playnite.SDK.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Friends;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Providers.Steam;
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
    internal sealed class ExophaseFriendsProvider : IFriendsProvider, ICurrentUserGameLabelReceiver, ISteamFriendOwnershipSupplementSource
    {
        private const string Provider = "Exophase";
        private readonly ExophaseApiClient _apiClient;
        private readonly ExophasePublicApiClient _publicApiClient;
        private readonly ExophaseSettings _settings;
        private readonly PlayniteAchievementsSettings _globalSettings;
        private readonly IPlayniteAPI _playniteApi;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _webViewGate = new SemaphoreSlim(1, 1);

        // Per-refresh map of (friend username + provider game key) -> the friend's per-game context id,
        // the "#{id}" fragment (master_playerid) on each game's achievements link. Sourced from the
        // games-list API's endpoint_awards (or the profile-page scrape on the fallback path). Used to
        // build the friend-scoped achievement URL. Populated during the ownership pass (which always
        // runs before achievements) and cleared each refresh.
        private readonly Dictionary<string, string> _friendGameContextIds =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Per-refresh memo of each friend's fetched library so the Steam ownership supplement reuses
        // the ownership fetch instead of re-scraping the profile. Cleared each refresh.
        private readonly Dictionary<string, IReadOnlyList<ExophaseFriendGame>> _friendLibraryMemo =
            new Dictionary<string, IReadOnlyList<ExophaseFriendGame>>(StringComparer.OrdinalIgnoreCase);

        // Per-refresh memo of resolved profile identities (playerProfileId + SSR games blob + rendered
        // HTML) so the metadata probe and the ownership pass share one profile-page fetch per friend.
        private readonly Dictionary<string, ExophaseProfileIdentity> _friendIdentityMemo =
            new Dictionary<string, ExophaseProfileIdentity>(StringComparer.OrdinalIgnoreCase);

        // Per-refresh map of provider game key -> Exophase master_id (game-global, same for every
        // friend), sourced from the games-list API. Consumed by the earned-awards endpoint.
        private readonly Dictionary<string, long> _friendGameMasterIds =
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

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
            _publicApiClient = new ExophasePublicApiClient(_apiClient, logger);
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _globalSettings = globalSettings;
            _playniteApi = playniteApi;
            _logger = logger;
        }

        public string ProviderKey => Provider;

        public Task<FriendsProviderResult<FriendsRefreshPreparation>> BeginRefreshAsync(CancellationToken cancel)
        {
            _friendGameContextIds.Clear();
            _friendLibraryMemo.Clear();
            _friendIdentityMemo.Clear();
            _friendGameMasterIds.Clear();
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
            _friendLibraryMemo.Clear();
            _friendIdentityMemo.Clear();
            _friendGameMasterIds.Clear();
            ClearCurrentUserGameIndex();
            _apiClient.EndCookieSession();
        }

        // Receives the current user's cached game labels for this refresh and indexes them by servicing
        // provider label + normalized name, so the ownership merge can resolve a friend's game to a local
        // game using the stored provider label rather than re-deriving platform from Source/Platform.
        public void SetCurrentUserGameLabels(IReadOnlyList<CurrentUserGameLabel> labels)
        {
            ClearCurrentUserGameIndex();
            IndexCurrentUserGameLabels(
                labels,
                _currentUserGameFamilyById,
                _currentUserGameIdsByFamilyName);

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

        private static void IndexCurrentUserGameLabels(
            IReadOnlyList<CurrentUserGameLabel> labels,
            IDictionary<Guid, string> gameFamilyById,
            IDictionary<string, List<Guid>> gameIdsByFamilyName)
        {
            if (labels == null || gameFamilyById == null || gameIdsByFamilyName == null)
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

                gameFamilyById[label.PlayniteGameId] = family;

                var normalizedName = ExophaseGameNameMatcher.NormalizeGameName(label.GameName);
                if (string.IsNullOrWhiteSpace(normalizedName))
                {
                    continue;
                }

                var key = BuildFamilyNameKey(family, normalizedName);
                if (!gameIdsByFamilyName.TryGetValue(key, out var ids))
                {
                    ids = new List<Guid>();
                    gameIdsByFamilyName[key] = ids;
                }

                if (!ids.Contains(label.PlayniteGameId))
                {
                    ids.Add(label.PlayniteGameId);
                }
            }
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
            _logger?.Info($"[ExophaseFriends] GetOwnedGames: friend='{config.ExternalUserId}', " +
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

                // Remember the game-global master_id for the earned-awards endpoint.
                if (game.MasterId > 0)
                {
                    _friendGameMasterIds[key] = game.MasterId;
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
                    LastPlayedUtc = game.LastPlayedUtc,
                    AchievementUnlocksHint = game.AchievementsEarned,
                    AchievementTotalHint = game.AchievementsTotal
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

        public async Task<FriendsProviderResult<IReadOnlyList<FriendGameOwnership>>> GetSteamOwnedGamesAsync(
            string externalUserId,
            IReadOnlyList<CurrentUserGameLabel> currentUserLabels,
            IReadOnlyList<FriendGameOwnership> knownSteamOwnership,
            CancellationToken cancel)
        {
            if (string.IsNullOrWhiteSpace(externalUserId))
            {
                return FriendsProviderResult<IReadOnlyList<FriendGameOwnership>>.Failed("Exophase friend username is missing.");
            }

            var username = externalUserId.Trim();
            var allGames = await FetchOwnedGamesAsync(username, cancel).ConfigureAwait(false);
            var steamLabelsByName = BuildUniqueSteamLabelsByNormalizedName(currentUserLabels);
            var knownSteamGames = BuildKnownSteamOwnershipIndex(knownSteamOwnership);
            var result = new List<FriendGameOwnership>();
            var skippedOtherPlatform = 0;
            var skippedKnownSteam = 0;
            var skippedNoUnlockHint = 0;
            var resolvedFromProfile = 0;
            var resolvedFromLibrary = 0;
            var resolvedFromPage = 0;
            var unresolvedSteamAppIds = 0;
            foreach (var game in allGames)
            {
                cancel.ThrowIfCancellationRequested();
                var gameProviderKey = ExophaseFriendPlatformMatcher.ResolveProviderPlatformKey(game.Platform);
                if (!string.Equals(gameProviderKey, "Steam", StringComparison.OrdinalIgnoreCase))
                {
                    skippedOtherPlatform++;
                    continue;
                }

                var key = ExophaseFriendGameKey.Build(game.Platform, game.Slug);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (IsKnownSteamOwnership(game, knownSteamGames))
                {
                    skippedKnownSteam++;
                    continue;
                }

                if (!HasPositiveAchievementUnlock(game))
                {
                    skippedNoUnlockHint++;
                    continue;
                }

                var appId = Math.Max(0, game.SteamAppId);
                if (appId > 0)
                {
                    resolvedFromProfile++;
                }
                else if (TryResolveSteamAppIdFromLibrary(game.Title, steamLabelsByName, out appId))
                {
                    resolvedFromLibrary++;
                }
                else
                {
                    appId = await ResolveSteamAppIdForSupplementAsync(game, cancel)
                        .ConfigureAwait(false);
                    if (appId > 0)
                    {
                        resolvedFromPage++;
                    }
                }

                if (appId <= 0)
                {
                    unresolvedSteamAppIds++;
                    continue;
                }

                if (knownSteamGames.AppIds.Contains(appId))
                {
                    skippedKnownSteam++;
                    continue;
                }

                result.Add(new FriendGameOwnership
                {
                    ProviderKey = Provider,
                    ExternalUserId = username,
                    AppId = appId,
                    ProviderGameKey = key,
                    ProviderPlatformKey = ExophaseDataProvider.MapSlugToProviderPlatformKey(game.Platform),
                    GameName = game.Title,
                    // Steam games surfaced via Exophase use Steam CDN images, not Exophase ones.
                    IconUrl = SteamImageUrls.Icon(appId),
                    CoverUrl = SteamImageUrls.Cover(appId),
                    PlaytimeForeverMinutes = Math.Max(0, game.PlaytimeMinutes),
                    Playtime2WeeksMinutes = game.RecentPlaytimeMinutes > 0
                        ? (int?)game.RecentPlaytimeMinutes
                        : null,
                    LastPlayedUtc = game.LastPlayedUtc,
                    AchievementUnlocksHint = game.AchievementsEarned,
                    AchievementTotalHint = game.AchievementsTotal
                });
            }

            var deduped = result
                .GroupBy(item => item.AppId)
                .Select(group => group.First())
                .OrderBy(item => item.GameName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _logger?.Info($"[ExophaseFriends] GetSteamOwnedGames: '{username}' parsed {allGames.Count} profile game(s), " +
                $"kept {deduped.Count} extra Steam game(s) for Steam ownership augmentation " +
                $"(skipped {skippedOtherPlatform} on other platforms, " +
                $"skippedKnownSteam={skippedKnownSteam}, skippedNoUnlockHint={skippedNoUnlockHint}, " +
                $"resolvedProfile={resolvedFromProfile}, resolvedLibrary={resolvedFromLibrary}, " +
                $"resolvedPage={resolvedFromPage}, unresolvedSteamAppIds={unresolvedSteamAppIds}).");
            return FriendsProviderResult<IReadOnlyList<FriendGameOwnership>>.FromData(deduped);
        }

        private static KnownSteamOwnershipIndex BuildKnownSteamOwnershipIndex(
            IReadOnlyList<FriendGameOwnership> ownership)
        {
            var index = new KnownSteamOwnershipIndex();
            foreach (var item in ownership ?? Array.Empty<FriendGameOwnership>())
            {
                if (item == null)
                {
                    continue;
                }

                if (item.AppId > 0)
                {
                    index.AppIds.Add(item.AppId);
                }

                var normalizedName = ExophaseGameNameMatcher.NormalizeGameName(item.GameName);
                if (!string.IsNullOrWhiteSpace(normalizedName))
                {
                    index.NormalizedNames.Add(normalizedName);
                }

                var slug = ExophaseGameNameMatcher.NormalizeGameNameForSlug(item.GameName);
                if (!string.IsNullOrWhiteSpace(slug))
                {
                    index.ExophaseSteamSlugs.Add(slug);
                    index.ExophaseSteamSlugs.Add(slug + "-steam");
                }
            }

            return index;
        }

        private static bool IsKnownSteamOwnership(ExophaseFriendGame game, KnownSteamOwnershipIndex knownSteamGames)
        {
            if (game == null || knownSteamGames == null)
            {
                return false;
            }

            if (game.SteamAppId > 0 && knownSteamGames.AppIds.Contains(game.SteamAppId))
            {
                return true;
            }

            var slug = NormalizeSteamSlugKey(game.Slug);
            if (!string.IsNullOrWhiteSpace(slug) &&
                knownSteamGames.ExophaseSteamSlugs.Contains(slug))
            {
                return true;
            }

            var normalizedName = ExophaseGameNameMatcher.NormalizeGameName(game.Title);
            return !string.IsNullOrWhiteSpace(normalizedName) &&
                   knownSteamGames.NormalizedNames.Contains(normalizedName);
        }

        private static bool HasPositiveAchievementUnlock(ExophaseFriendGame game)
        {
            return game?.AchievementsEarned.GetValueOrDefault() > 0;
        }

        private static string NormalizeSteamSlugKey(string slug)
        {
            return string.IsNullOrWhiteSpace(slug)
                ? null
                : slug.Trim().ToLowerInvariant();
        }

        private static Dictionary<string, CurrentUserGameLabel> BuildUniqueSteamLabelsByNormalizedName(
            IReadOnlyList<CurrentUserGameLabel> labels)
        {
            var labelsByName = new Dictionary<string, CurrentUserGameLabel>(StringComparer.OrdinalIgnoreCase);
            var ambiguousNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var label in labels ?? Array.Empty<CurrentUserGameLabel>())
            {
                if (label == null ||
                    label.AppId <= 0 ||
                    !string.Equals(
                        ExophaseFriendPlatformMatcher.ResolveStoredGameFamilyKey(
                            label.ProviderKey,
                            label.ProviderPlatformKey),
                        "Steam",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var normalizedName = ExophaseGameNameMatcher.NormalizeGameName(label.GameName);
                if (string.IsNullOrWhiteSpace(normalizedName) ||
                    ambiguousNames.Contains(normalizedName))
                {
                    continue;
                }

                if (labelsByName.TryGetValue(normalizedName, out var existing) &&
                    existing.AppId != label.AppId)
                {
                    labelsByName.Remove(normalizedName);
                    ambiguousNames.Add(normalizedName);
                    continue;
                }

                labelsByName[normalizedName] = label;
            }

            return labelsByName;
        }

        private static bool TryResolveSteamAppIdFromLibrary(
            string title,
            IReadOnlyDictionary<string, CurrentUserGameLabel> steamLabelsByName,
            out int appId)
        {
            appId = 0;
            var normalizedName = ExophaseGameNameMatcher.NormalizeGameName(title);
            if (string.IsNullOrWhiteSpace(normalizedName) ||
                steamLabelsByName == null ||
                !steamLabelsByName.TryGetValue(normalizedName, out var label))
            {
                return false;
            }

            appId = Math.Max(0, label.AppId);
            return appId > 0;
        }

        private async Task<int> ResolveSteamAppIdForSupplementAsync(
            ExophaseFriendGame game,
            CancellationToken cancel)
        {
            if (game?.SteamAppId > 0)
            {
                return game.SteamAppId;
            }

            if (string.IsNullOrWhiteSpace(game?.Slug))
            {
                return 0;
            }

            var url = ExophaseApiClient.BuildUrlFromSlug(game.Slug, game.Platform);
            if (string.IsNullOrWhiteSpace(url))
            {
                return 0;
            }

            try
            {
                var html = await FetchRenderedHtmlSerializedAsync(url, cancel).ConfigureAwait(false);
                var appId = ExophaseSteamAppIdParser.Extract(html);
                if (appId <= 0)
                {
                    _logger?.Debug($"[ExophaseFriends] Steam app id not found on Exophase game page '{url}'.");
                }

                return appId;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"[ExophaseFriends] Failed to resolve Steam app id from Exophase game page '{url}'.");
                return 0;
            }
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

            // One fetch serves both the schema and the header banner: the achievements page carries the
            // game's banner in its layout, so parse it from the same rendered HTML instead of fetching
            // the game overview page separately.
            var url = ExophaseApiClient.BuildUrlFromSlug(parsed.GameSlug, parsed.PlatformSlug);
            var fetched = await _apiClient
                .FetchAchievementsWithHtmlAsync(url, ExophaseApiClient.MapLanguageToAcceptLanguage("en-US"), cancel, waitForImages: true)
                .ConfigureAwait(false);

            var rows = fetched?.Achievements ?? new List<AchievementDetail>();

            // Reuse the main provider's rarity assignment so friend achievements get the same
            // percentage/rarity tiers instead of defaulting to Common.
            ExophaseDataProvider.ApplyProviderOwnedRarity(
                rows,
                ExophaseDataProvider.MapSlugToProviderPlatformKey(parsed.PlatformSlug));
            var headerImageUrl = string.IsNullOrEmpty(fetched?.Html)
                ? null
                : ExophaseFriendPageParser.ParseGameHeaderImageUrl(fetched.Html);

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

            // The earned-awards JSON endpoint returns the friend's unlocks for one game keyed by the
            // stable award id with exact unix timestamps: locale-proof, no page render, no image
            // warming. Names, descriptions, and icons come from the shared per-game definitions at
            // read time (GetFriendGameDefinitionAsync), not from these rows.
            var ids = await ResolveEarnedEndpointIdsAsync(friend.ExternalUserId, providerGameKey, cancel).ConfigureAwait(false);
            if (ids == null)
            {
                return FriendsProviderResult<FriendGameAchievements>.Failed(
                    $"Could not resolve Exophase player/game ids for '{providerGameKey}'.",
                    transientFailure: true);
            }

            var earned = await _publicApiClient
                .GetEarnedAwardsAsync(ids.Value.MasterPlayerId, ids.Value.MasterId, cancel)
                .ConfigureAwait(false);
            if (earned == null)
            {
                // Fetch failure (Cloudflare or transport); an empty list is a legitimate zero-unlock
                // result and is handled below.
                return FriendsProviderResult<FriendGameAchievements>.Failed(
                    $"Exophase earned-awards fetch failed for '{providerGameKey}'.",
                    transientFailure: true);
            }

            var rows = earned
                .Where(award => award != null && award.EffectiveAwardId > 0)
                .Select(award => new FriendAchievementRow
                {
                    ApiName = ExophaseApiClient.ExophaseStableApiNamePrefix +
                        award.EffectiveAwardId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    IconUrl = ExophasePublicApiClient.NormalizeImageUrl(award.Icons?.Original ?? award.Icons?.Medium),
                    UnlockedIconUrl = ExophasePublicApiClient.NormalizeImageUrl(award.Icons?.Original ?? award.Icons?.Medium),
                    Unlocked = true,
                    UnlockTimeUtc = award.Timestamp > 0
                        ? DateTimeOffset.FromUnixTimeSeconds(award.Timestamp).UtcDateTime
                        : (DateTime?)null
                })
                .ToList();

            _logger?.Debug($"[ExophaseFriends] GetFriendGameAchievements: friend='{friend.ExternalUserId}', " +
                $"gameKey='{providerGameKey}', masterPlayerId={ids.Value.MasterPlayerId}, masterId={ids.Value.MasterId} -> " +
                $"{rows.Count} unlocked award(s).");

            return FriendsProviderResult<FriendGameAchievements>.FromData(new FriendGameAchievements
            {
                Friend = friend,
                ProviderGameKey = providerGameKey,
                LastUpdatedUtc = DateTime.UtcNow,
                StatsUnavailable = false,
                Rows = rows
            });
        }

        /// <summary>
        /// Resolves the (master_playerid, master_id) pair the earned-awards endpoint needs for one
        /// (friend, game). Ownership refreshes populate the per-refresh memos; cache-sourced candidates
        /// (Recent/SelectedGame/Custom scopes) trigger one memoized ownership fetch for the friend.
        /// </summary>
        private async Task<(long MasterPlayerId, long MasterId)?> ResolveEarnedEndpointIdsAsync(
            string username,
            string providerGameKey,
            CancellationToken cancel)
        {
            if (TryGetEarnedEndpointIds(username, providerGameKey, out var resolved))
            {
                return resolved;
            }

            // Ownership was not fetched this refresh (or the game key was filtered out); fetch the
            // friend's library once (memoized per refresh) and index every game's ids, not just the
            // selected platforms, so id resolution is independent of platform filtering.
            var games = await FetchOwnedGamesAsync(username, cancel).ConfigureAwait(false);
            foreach (var game in games ?? (IReadOnlyList<ExophaseFriendGame>)Array.Empty<ExophaseFriendGame>())
            {
                var key = ExophaseFriendGameKey.Build(game.Platform, game.Slug);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(game.ContextId))
                {
                    _friendGameContextIds[BuildContextMapKey(username, key)] = game.ContextId;
                }

                if (game.MasterId > 0)
                {
                    _friendGameMasterIds[key] = game.MasterId;
                }
            }

            return TryGetEarnedEndpointIds(username, providerGameKey, out resolved)
                ? resolved
                : ((long MasterPlayerId, long MasterId)?)null;
        }

        private bool TryGetEarnedEndpointIds(
            string username,
            string providerGameKey,
            out (long MasterPlayerId, long MasterId) ids)
        {
            ids = default((long, long));
            var contextId = GetFriendGameContextId(username, providerGameKey);
            if (string.IsNullOrWhiteSpace(contextId) ||
                !long.TryParse(contextId.Trim(), out var masterPlayerId) ||
                masterPlayerId <= 0)
            {
                return false;
            }

            if (!_friendGameMasterIds.TryGetValue(providerGameKey, out var masterId) || masterId <= 0)
            {
                return false;
            }

            ids = (masterPlayerId, masterId);
            return true;
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
            // The identity fetch renders the same profile page and is memoized per refresh, so the
            // metadata probe and the ownership pass share one fetch per friend.
            var identity = await GetProfileIdentityAsync(username, cancel).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(identity?.Html))
            {
                return ExophaseFriendPageParser.ParseProfile(identity.Html);
            }

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
            // Memoized per refresh: the ownership pass and the Steam supplement pass both need the
            // friend's library; one fetch serves both.
            var memoKey = username?.Trim() ?? string.Empty;
            if (_friendLibraryMemo.TryGetValue(memoKey, out var memoized))
            {
                return memoized;
            }

            var games = await FetchOwnedGamesViaApiAsync(username, cancel).ConfigureAwait(false);
            if (games == null)
            {
                // API path unavailable (unresolvable playerProfileId, Cloudflare, or a mid-pagination
                // failure). Fall back to the legacy profile-page scrape rather than persisting a
                // truncated library.
                _logger?.Warn($"[ExophaseFriends] Games-list API unavailable for '{username}'; falling back to profile-page scrape.");
                games = await FetchOwnedGamesViaHtmlAsync(username, cancel).ConfigureAwait(false);
            }

            _friendLibraryMemo[memoKey] = games;
            return games;
        }

        private async Task<IReadOnlyList<ExophaseFriendGame>> FetchOwnedGamesViaApiAsync(
            string username,
            CancellationToken cancel)
        {
            var identity = await GetProfileIdentityAsync(username, cancel).ConfigureAwait(false);
            if (identity == null || identity.PlayerProfileId <= 0)
            {
                return null;
            }

            var apiGames = await _publicApiClient
                .GetPlayerGamesAsync(identity.PlayerProfileId, cancel)
                .ConfigureAwait(false);
            if (apiGames == null)
            {
                return null;
            }

            // The SSR blob (recent 50 games) is the only source of the game-level Steam appid;
            // index it by master_id so API rows pick their appid up for free.
            var ssrAppIdsByMasterId = new Dictionary<long, int>();
            foreach (var ssrGame in identity.SsrGames ?? new List<ExophasePlayerGame>())
            {
                if (ssrGame == null || ssrGame.MasterId <= 0)
                {
                    continue;
                }

                var ssrAppId = ExophasePublicApiClient.GetSteamAppId(ssrGame);
                if (ssrAppId > 0)
                {
                    ssrAppIdsByMasterId[ssrGame.MasterId] = ssrAppId;
                }
            }

            var games = new List<ExophaseFriendGame>();
            var skippedNoAwards = 0;
            foreach (var apiGame in apiGames)
            {
                cancel.ThrowIfCancellationRequested();
                var mapped = MapApiGameToFriendGame(apiGame, ssrAppIdsByMasterId);
                if (mapped == null)
                {
                    skippedNoAwards++;
                    continue;
                }

                games.Add(mapped);
            }

            var deduped = games
                .GroupBy(game => (game.Platform ?? string.Empty) + "|" + game.Slug, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            _logger?.Info($"[ExophaseFriends] Games-list API: '{username}' (profile {identity.PlayerProfileId}) -> " +
                $"{apiGames.Count} game(s), kept {deduped.Count} with achievements " +
                $"(skipped {skippedNoAwards} without an awards endpoint).");
            return deduped;
        }

        private ExophaseFriendGame MapApiGameToFriendGame(
            ExophasePlayerGame apiGame,
            IReadOnlyDictionary<long, int> ssrAppIdsByMasterId)
        {
            // Rows without an awards endpoint are demos/tools with no achievements.
            if (apiGame?.Meta == null || string.IsNullOrWhiteSpace(apiGame.Meta.EndpointAwards))
            {
                return null;
            }

            var slug = ExophasePublicApiClient.GetGameSlug(apiGame);
            var platform = apiGame.Meta.EnvironmentSlug?.Trim();
            if (string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(platform))
            {
                return null;
            }

            var playtimeMinutes = ExophasePublicApiClient.GetPlaytimeMinutes(apiGame);
            if (playtimeMinutes <= 0 && !string.IsNullOrWhiteSpace(apiGame.Playtime))
            {
                playtimeMinutes = ExophaseFriendPageParser.ParsePlaytimeMinutes(apiGame.Playtime);
            }

            var appId = 0;
            if (ssrAppIdsByMasterId != null && apiGame.MasterId > 0)
            {
                ssrAppIdsByMasterId.TryGetValue(apiGame.MasterId, out appId);
            }

            return new ExophaseFriendGame
            {
                Slug = slug,
                Title = apiGame.Meta.Title,
                ImageUrl = ExophasePublicApiClient.NormalizeImageUrl(apiGame.ResourceStandard),
                Platform = platform,
                SteamAppId = appId,
                ContextId = ExophasePublicApiClient.GetContextId(apiGame)
                    ?? (apiGame.MasterPlayerId > 0 ? apiGame.MasterPlayerId.ToString() : null),
                MasterId = apiGame.MasterId,
                PlaytimeMinutes = Math.Max(0, playtimeMinutes),
                RecentPlaytimeMinutes = 0,
                LastPlayedUtc = ExophasePublicApiClient.FromUnixSeconds(apiGame.LastPlayedUtc),
                AchievementsEarned = apiGame.EarnedAwards,
                AchievementsTotal = apiGame.TotalAwards
            };
        }

        private async Task<ExophaseProfileIdentity> GetProfileIdentityAsync(
            string username,
            CancellationToken cancel)
        {
            var memoKey = username?.Trim() ?? string.Empty;
            if (memoKey.Length == 0)
            {
                return null;
            }

            if (_friendIdentityMemo.TryGetValue(memoKey, out var memoized))
            {
                return memoized;
            }

            await _webViewGate.WaitAsync(cancel).ConfigureAwait(false);
            ExophaseProfileIdentity identity;
            try
            {
                identity = await _publicApiClient.ResolveProfileIdentityAsync(memoKey, cancel).ConfigureAwait(false);
            }
            finally
            {
                _webViewGate.Release();
            }

            if (identity != null)
            {
                _friendIdentityMemo[memoKey] = identity;
                _logger?.Debug($"[ExophaseFriends] Resolved profile identity for '{memoKey}': " +
                    $"playerProfileId={identity.PlayerProfileId}, ssrGames={identity.SsrGames?.Count ?? 0}.");
            }

            return identity;
        }

        private async Task<IReadOnlyList<ExophaseFriendGame>> FetchOwnedGamesViaHtmlAsync(
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
            return ResolveMappedPlayniteGameId(
                providerGameKey,
                platform,
                title,
                _currentUserGameFamilyById,
                _currentUserGameIdsByFamilyName);
        }

        private Guid? ResolveMappedPlayniteGameId(
            string providerGameKey,
            string platform,
            string title,
            IReadOnlyDictionary<Guid, string> currentUserGameFamilyById,
            IReadOnlyDictionary<string, List<Guid>> currentUserGameIdsByFamilyName)
        {
            var normalizedKey = ExophaseSettings.NormalizeFriendGameMappingKey(providerGameKey);
            if (!string.IsNullOrWhiteSpace(normalizedKey) &&
                _settings.FriendGameMappings?.TryGetValue(normalizedKey, out var manualGameId) == true &&
                manualGameId != Guid.Empty)
            {
                return IsMappedPlayniteGameCompatible(
                        manualGameId,
                        platform,
                        null,
                        currentUserGameFamilyById)
                    ? manualGameId
                    : null;
            }

            var slug = ExophaseFriendGameKey.Parse(providerGameKey).GameSlug;
            var overrideMatch = (_settings.SlugOverrides ?? new Dictionary<Guid, string>())
                .FirstOrDefault(pair => string.Equals(pair.Value, slug, StringComparison.OrdinalIgnoreCase));
            if (overrideMatch.Key != Guid.Empty)
            {
                return IsMappedPlayniteGameCompatible(
                        overrideMatch.Key,
                        platform,
                        slug,
                        currentUserGameFamilyById)
                    ? overrideMatch.Key
                    : null;
            }

            return ResolveAutomaticPlayniteGameId(platform, title, currentUserGameIdsByFamilyName);
        }

        private bool IsMappedPlayniteGameCompatible(Guid playniteGameId, string platform, string localOverrideSlug)
        {
            return IsMappedPlayniteGameCompatible(
                playniteGameId,
                platform,
                localOverrideSlug,
                _currentUserGameFamilyById);
        }

        private static bool IsMappedPlayniteGameCompatible(
            Guid playniteGameId,
            string platform,
            string localOverrideSlug,
            IReadOnlyDictionary<Guid, string> currentUserGameFamilyById)
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
            if (currentUserGameFamilyById != null &&
                currentUserGameFamilyById.TryGetValue(playniteGameId, out var gameProviderKey) &&
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
            return ResolveAutomaticPlayniteGameId(platform, title, _currentUserGameIdsByFamilyName);
        }

        private static Guid? ResolveAutomaticPlayniteGameId(
            string platform,
            string title,
            IReadOnlyDictionary<string, List<Guid>> currentUserGameIdsByFamilyName)
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
            return currentUserGameIdsByFamilyName != null &&
                   currentUserGameIdsByFamilyName.TryGetValue(
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
                .Where(platform => !string.Equals(platform, "steam", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string NormalizePlatformSlug(string platform)
        {
            return ExophaseFriendPlatformMatcher.NormalizePlatformSlug(platform);
        }

        private sealed class KnownSteamOwnershipIndex
        {
            public HashSet<int> AppIds { get; } = new HashSet<int>();
            public HashSet<string> NormalizedNames { get; } =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> ExophaseSteamSlugs { get; } =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class ExophaseFriendGame
        {
            public string Slug { get; set; }
            public string Title { get; set; }
            public string ImageUrl { get; set; }
            public string Platform { get; set; }
            public int SteamAppId { get; set; }

            // Friend's per-game context id from the achievement link fragment
            // (/game/{slug}/achievements/#{ContextId}); scopes the achievement page to this friend.
            // Equals the friend's per-service master_playerid on the API path.
            public string ContextId { get; set; }

            // Exophase's internal game id (game-global, stable). Populated on the API path only;
            // 0 when the row came from the profile-page scrape fallback.
            public long MasterId { get; set; }
            public int PlaytimeMinutes { get; set; }
            public int RecentPlaytimeMinutes { get; set; }
            public DateTime? LastPlayedUtc { get; set; }

            // Earned/total achievement counts from the profile row's game-progress block
            // (rendered as "6/37"). Feed FriendGameOwnership.AchievementUnlocksHint so the
            // refresh gate can skip provider-only games the friend has not unlocked anything in.
            public int? AchievementsEarned { get; set; }
            public int? AchievementsTotal { get; set; }
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
            private static readonly Regex ServiceIconPlatformRegex = new Regex(@"exo-icon-service-([a-z0-9]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private static readonly Regex ImageHostPlatformRegex = new Regex(@"exophase\.com/([a-z0-9]+)/(?:games|awards)/", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private static readonly Regex PlaytimeHoursMinutesRegex = new Regex(@"(?:(\d+(?:[.,]\d+)?)\s*h(?:ours?)?)?\s*(?:(\d+)\s*m(?:in(?:utes?)?)?)?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private static readonly Regex EarnedTotalCountsRegex = new Regex(@"(\d+)\s*/\s*(\d+)", RegexOptions.Compiled);
            private static readonly Regex RecentDateContextRegex = new Regex(
                @"\b(today|yesterday|last\s+24\s+hours?|past\s+24\s+hours?|this\s+week|last\s+week|monday|tuesday|wednesday|thursday|friday|saturday|sunday)\b",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private static readonly Regex LastPlayedLabelRegex = new Regex(@"\b(last\s+played|played\s+on|recently\s+played)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private static readonly Regex TodayWordRegex = new Regex(@"\btoday\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private static readonly Regex YesterdayWordRegex = new Regex(@"\byesterday\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private static readonly Regex WeekdayNameRegex = new Regex(@"\b(monday|tuesday|wednesday|thursday|friday|saturday|sunday)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private static readonly Regex MonthDayDateRegex = new Regex(
                @"\b(?:\d{1,2}\s+)?(?:jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|jun(?:e)?|jul(?:y)?|aug(?:ust)?|sep(?:t(?:ember)?)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?)\.?\s+\d{1,2}(?:,?\s+\d{4})?\b",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private static readonly Regex IsoDateRegex = new Regex(@"\b\d{4}-\d{1,2}-\d{1,2}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private static readonly Regex FourDigitYearRegex = new Regex(@"\b\d{4}\b", RegexOptions.Compiled);
            private static readonly Regex HeadingTagNameRegex = new Regex(@"^h[1-6]$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private static readonly Regex DateHeaderClassRegex = new Regex(@"\b(date|day|activity|header|title)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private static readonly Regex CompactDayWordRegex = new Regex(
                @"^(today|yesterday|monday|tuesday|wednesday|thursday|friday|saturday|sunday)$",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private static readonly Regex CompactMonthDayDateRegex = new Regex(
                @"^(?:jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|jun(?:e)?|jul(?:y)?|aug(?:ust)?|sep(?:t(?:ember)?)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?)\.?\s+\d{1,2}(?:,?\s+\d{4})?$",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private static readonly Regex CompactIsoDateRegex = new Regex(@"^\d{4}-\d{1,2}-\d{1,2}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private static readonly Regex WhitespaceRunRegex = new Regex(@"\s+", RegexOptions.Compiled);

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
                    // The games listing renders a per-game "last played" cell (<div class="lastplayed">July 1, 2026</div>);
                    // prefer it as the recency signal before the generic attribute/heading fallbacks.
                    DateTime? lastPlayedFromCell = null;
                    if (TryParseDateText(
                            container?.SelectSingleNode(".//div[contains(@class, 'lastplayed')]")?.InnerText,
                            out var parsedLastPlayedCell))
                    {
                        lastPlayedFromCell = parsedLastPlayedCell;
                    }

                    var lastPlayedUtc = lastPlayedFromCell ?? ParseLastPlayedUtc(container) ?? recentDateContext;

                    var (achievementsEarned, achievementsTotal) = ParseAchievementCounts(container);

                    result.Games.Add(new ExophaseFriendGame
                    {
                        Slug = slug,
                        Title = title,
                        ImageUrl = image,
                        Platform = platform,
                        SteamAppId = ExophaseSteamAppIdParser.Extract(container?.OuterHtml),
                        ContextId = contextId,
                        PlaytimeMinutes = ParsePlaytimeMinutes(container?.InnerText),
                        RecentPlaytimeMinutes = ParseRecentPlaytimeMinutes(container?.InnerText, recentDateContext.HasValue),
                        LastPlayedUtc = lastPlayedUtc,
                        AchievementsEarned = achievementsEarned,
                        AchievementsTotal = achievementsTotal
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
                for (var i = 0; i < 8 && current?.ParentNode != null; i++)
                {
                    current = current.ParentNode;
                    if (!HasGameRowSignals(current))
                    {
                        continue;
                    }

                    // Do not let a broad list/container node classify this row from a sibling's
                    // service icon, image, playtime, or progress block. Row containers can legitimately
                    // contain multiple links to the same game (image + title), so count distinct slugs.
                    if (CountDistinctGameSlugs(current) <= 1)
                    {
                        return current;
                    }
                }

                return link.ParentNode;
            }

            private static bool HasGameRowSignals(HtmlNode node)
            {
                return node != null &&
                       (node.SelectSingleNode(".//div[contains(@class, 'col-image')]") != null ||
                        node.SelectSingleNode(".//i[contains(@class, 'exo-icon-service-')]") != null ||
                        node.SelectSingleNode(".//div[contains(@class, 'game-progress')]") != null ||
                        node.SelectSingleNode(".//div[contains(@class, 'lastplayed')]") != null);
            }

            private static int CountDistinctGameSlugs(HtmlNode node)
            {
                var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var link in Nodes(node?.SelectNodes(".//a[contains(@href, '/game/')]")))
                {
                    var slug = ExophaseApiClient.ExtractSlugFromUrl(
                        NormalizeUrl(link.GetAttributeValue("href", null)));
                    if (!string.IsNullOrWhiteSpace(slug))
                    {
                        slugs.Add(slug);
                    }
                }

                return slugs.Count;
            }

            private static string DerivePlatform(HtmlNode container, string slug)
            {
                // The slug is row-local and cannot be contaminated by sibling rows. Prefer it over
                // service icons because broad profile containers may contain many platform icons.
                var slugPlatform = ExophaseFriendPlatformMatcher.ExtractPlatformSlugFromGameSlug(slug);
                if (!string.IsNullOrWhiteSpace(slugPlatform))
                {
                    return NormalizePlatformSlug(slugPlatform);
                }

                if (container != null)
                {
                    // The service icon carries the platform: <i class="exo-icon-service-origin ...">.
                    var serviceIcon = container.SelectSingleNode(".//i[contains(@class, 'exo-icon-service-')]");
                    var iconClass = serviceIcon?.GetAttributeValue("class", string.Empty) ?? string.Empty;
                    var iconMatch = ServiceIconPlatformRegex.Match(iconClass);
                    if (iconMatch.Success)
                    {
                        return NormalizePlatformSlug(iconMatch.Groups[1].Value);
                    }

                    // Fall back to the media host path: https://m.exophase.com/{platform}/games/...
                    var imageSrc = ExophaseApiClient.ResolveImageUrl(container) ?? string.Empty;
                    var imageMatch = ImageHostPlatformRegex.Match(imageSrc);
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

            public static int ParsePlaytimeMinutes(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return 0;
                }

                var match = PlaytimeHoursMinutesRegex.Match(text);
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

            // Reads the "earned/total" achievement count from a game row's game-progress block,
            // rendered as an award icon followed by e.g. "6/37". Returns (null, null) when the row
            // has no progress block (some platforms/games omit it) so the caller leaves the hint unset.
            private static (int? Earned, int? Total) ParseAchievementCounts(HtmlNode container)
            {
                if (container == null)
                {
                    return (null, null);
                }

                var progressCol = container.SelectSingleNode(
                    ".//div[contains(@class, 'game-progress')]//i[contains(@class, 'exo-icon-award')]/parent::div")
                    ?? container.SelectSingleNode(".//div[contains(@class, 'game-progress')]//div[contains(@class, 'progress-units-top')]");
                if (progressCol == null)
                {
                    return (null, null);
                }

                var match = EarnedTotalCountsRegex.Match(Clean(progressCol.InnerText) ?? string.Empty);
                if (!match.Success ||
                    !int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var earned) ||
                    !int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var total))
                {
                    return (null, null);
                }

                return (Math.Max(0, earned), Math.Max(0, total));
            }

            private static int ParseRecentPlaytimeMinutes(string text, bool hasRecentDateContext)
            {
                text = Clean(text);
                if (string.IsNullOrWhiteSpace(text))
                {
                    return 0;
                }

                if (!hasRecentDateContext &&
                    !RecentDateContextRegex.IsMatch(text))
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
                       LastPlayedLabelRegex.IsMatch(text);
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
                if (TodayWordRegex.IsMatch(text))
                {
                    dateUtc = DateTime.SpecifyKind(today, DateTimeKind.Utc);
                    return true;
                }

                if (YesterdayWordRegex.IsMatch(text))
                {
                    dateUtc = DateTime.SpecifyKind(today.AddDays(-1), DateTimeKind.Utc);
                    return true;
                }

                var weekdayMatch = WeekdayNameRegex.Match(text);
                if (weekdayMatch.Success &&
                    TryParseWeekday(weekdayMatch.Groups[1].Value, out var weekday))
                {
                    var daysBack = ((int)today.DayOfWeek - (int)weekday + 7) % 7;
                    dateUtc = DateTime.SpecifyKind(today.AddDays(-daysBack), DateTimeKind.Utc);
                    return true;
                }

                var dateMatch = MonthDayDateRegex.Match(text);
                if (!dateMatch.Success)
                {
                    dateMatch = IsoDateRegex.Match(text);
                }

                if (!dateMatch.Success)
                {
                    return false;
                }

                var value = dateMatch.Value;
                if (!FourDigitYearRegex.IsMatch(value))
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
                if (HeadingTagNameRegex.IsMatch(name))
                {
                    return true;
                }

                var className = node?.GetAttributeValue("class", string.Empty) ?? string.Empty;
                if (DateHeaderClassRegex.IsMatch(className))
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

                return CompactDayWordRegex.IsMatch(text) ||
                       CompactMonthDayDateRegex.IsMatch(text) ||
                       CompactIsoDateRegex.IsMatch(text);
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
                    : WhitespaceRunRegex.Replace(WebUtility.HtmlDecode(value), " ").Trim();
            }

            private static string FirstNonEmpty(params string[] values)
            {
                return values?.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            }
        }
    }
}
