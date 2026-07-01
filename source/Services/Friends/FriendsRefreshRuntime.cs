using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Friends;
using PlayniteAchievements.Providers;
using PlayniteAchievements.Providers.Steam.Models;
using PlayniteAchievements.Services.Images;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Services.Friends
{
    internal sealed class FriendsRefreshRuntime
    {
        private const int FriendRefreshParallelism = 4;
        private static readonly TimeSpan DefaultDefinitionTtl = TimeSpan.FromDays(7);

        // Steam library cover art (~600x900) is small enough to cache at full resolution.
        private const int ImageDecodeSize = 0;

        // Matches AchievementIconService.OptimizedDecodeSize so unowned achievement icons decode identically.
        private const int OptimizedAchievementIconDecodeSize = 128;

        private readonly IFriendCacheManager _friendCache;
        private readonly ProviderRegistry _providerRegistry;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly DiskImageService _imageService;
        private readonly ILogger _logger;

        // Resolves the per-friend full-library opt-in id set for a provider. Defaults to the
        // reflection-based lookup of Steam provider settings; overridable for testing.
        private readonly Func<string, HashSet<string>> _fullLibraryIdsResolver;

        public FriendsRefreshRuntime(
            IFriendCacheManager friendCache,
            ProviderRegistry providerRegistry,
            PlayniteAchievementsSettings settings,
            DiskImageService imageService,
            ILogger logger,
            Func<string, HashSet<string>> fullLibraryIdsResolver = null)
        {
            _friendCache = friendCache;
            _providerRegistry = providerRegistry;
            _settings = settings;
            _imageService = imageService;
            _logger = logger;
            _fullLibraryIdsResolver = fullLibraryIdsResolver;
        }

        public async Task<RebuildPayload> RefreshAsync(
            IReadOnlyList<IDataProvider> providerScope,
            FriendRefreshOptions options,
            Action<string, int, int> reportProgress,
            CancellationToken cancel = default)
        {
            var payload = new RebuildPayload();
            if (_friendCache == null)
            {
                return payload;
            }

            options = NormalizeOptions(options);
            var providers = (providerScope ?? Array.Empty<IDataProvider>())
                .Where(provider => provider?.Friends != null)
                .ToList();

            foreach (var provider in providers)
            {
                cancel.ThrowIfCancellationRequested();
                var providerPayload = await RefreshProviderAsync(
                    provider.Friends,
                    options,
                    reportProgress,
                    cancel).ConfigureAwait(false);
                Merge(payload, providerPayload);
            }

            return payload;
        }

        /// <summary>
        /// Refreshes only the friend roster (the list of friends and their avatars) for each
        /// friend-capable provider, without fetching ownership, game definitions, or achievements.
        /// Used by the in-settings "refresh friends list" action. Returns the number of active friends
        /// saved across providers.
        /// </summary>
        public async Task<int> RefreshFriendRosterAsync(
            IReadOnlyList<IDataProvider> providerScope,
            CancellationToken cancel = default)
        {
            if (_friendCache == null)
            {
                return 0;
            }

            var providers = (providerScope ?? Array.Empty<IDataProvider>())
                .Where(provider => provider?.Friends != null)
                .ToList();

            var saved = 0;
            foreach (var provider in providers)
            {
                cancel.ThrowIfCancellationRequested();
                saved += await RefreshProviderRosterAsync(provider.Friends, cancel).ConfigureAwait(false);
            }

            return saved;
        }

        private async Task<int> RefreshProviderRosterAsync(
            IFriendsProvider friendsProvider,
            CancellationToken cancel)
        {
            if (friendsProvider == null)
            {
                return 0;
            }

            var providerKey = friendsProvider.ProviderKey;
            try
            {
                var preparationResult = await friendsProvider.BeginRefreshAsync(cancel).ConfigureAwait(false);
                if (preparationResult?.Success != true)
                {
                    _logger?.Debug($"Friend roster refresh skipped for {providerKey}: {preparationResult?.ErrorMessage ?? "provider unavailable"}");
                    return 0;
                }

                var friendsResult = await friendsProvider.GetFriendsAsync(cancel).ConfigureAwait(false);
                if (friendsResult?.Success != true)
                {
                    _logger?.Debug($"Friend roster refresh skipped for {providerKey}: {friendsResult?.ErrorMessage ?? "friend list unavailable"}");
                    return 0;
                }

                var friends = FilterIgnoredFriends(providerKey, friendsResult.Data ?? Array.Empty<FriendIdentity>());
                await DownloadFriendAvatarsAsync(providerKey, friends, cancel).ConfigureAwait(false);

                var writeFriends = _friendCache.SaveFriendList(providerKey, friends);
                if (writeFriends?.Success != true)
                {
                    _logger?.Warn($"Failed to save {providerKey} friend list: {writeFriends?.ErrorMessage}");
                    return 0;
                }

                _logger?.Debug($"Refreshed {providerKey} friend roster: active={writeFriends.WrittenCount}.");
                return writeFriends.WrittenCount;
            }
            finally
            {
                friendsProvider.EndRefresh();
            }
        }

        private async Task<RebuildPayload> RefreshProviderAsync(
            IFriendsProvider friendsProvider,
            FriendRefreshOptions options,
            Action<string, int, int> reportProgress,
            CancellationToken cancel)
        {
            var payload = new RebuildPayload();
            if (friendsProvider == null)
            {
                return payload;
            }

            var providerKey = friendsProvider.ProviderKey;
            var maxDegreeOfParallelism = ResolveFriendRefreshParallelism();
            var payloadLock = new object();
            var fullLibraryIds = GetFullLibraryFriendIds(providerKey);
            var discoverUnowned = ShouldDiscoverUnowned(providerKey, options);
            var ownershipSnapshots = discoverUnowned
                ? new List<FriendOwnershipSnapshot>()
                : null;
            try
            {
                var preparationResult = await friendsProvider.BeginRefreshAsync(cancel).ConfigureAwait(false);
                if (preparationResult?.Success != true)
                {
                    _logger?.Debug($"Friends refresh skipped for {providerKey}: {preparationResult?.ErrorMessage ?? "provider unavailable"}");
                    MarkAuthFailure(payload, providerKey, preparationResult?.AuthRequired == true);
                    return payload;
                }

                var preparation = preparationResult.Data ?? new FriendsRefreshPreparation();
                payload.FriendSummary.ProvidersProcessed++;
                Report(reportProgress, Format("LOCPlayAch_FriendsRefresh_Progress_Friends", "Refreshing {0} friends...", providerKey), 0, 2);

                var friendsResult = await friendsProvider.GetFriendsAsync(cancel).ConfigureAwait(false);
                if (friendsResult?.Success != true)
                {
                    _logger?.Debug($"Friends refresh skipped for {providerKey}: {friendsResult?.ErrorMessage ?? "friend list unavailable"}");
                    MarkAuthFailure(payload, providerKey, friendsResult?.AuthRequired == true);
                    return payload;
                }

                var friends = FilterIgnoredFriends(providerKey, friendsResult.Data ?? Array.Empty<FriendIdentity>());
                payload.FriendSummary.FriendsFetched += friends.Count;

                // The friend list itself is always saved in full, but per-friend work (avatars,
                // library ownership, unowned schema discovery) is limited to the selected friends
                // when the request targets a specific subset (e.g. a custom refresh).
                var scopedFriends = ScopeFriendsToSelection(friends, options);

                await DownloadFriendAvatarsAsync(providerKey, scopedFriends, cancel).ConfigureAwait(false);

                var writeFriends = _friendCache.SaveFriendList(providerKey, friends);
                if (writeFriends?.Success != true)
                {
                    _logger?.Warn($"Failed to save {providerKey} friend list: {writeFriends?.ErrorMessage}");
                    return payload;
                }

                payload.FriendSummary.FriendsSaved += writeFriends.WrittenCount;
                _logger?.Debug(
                    $"Saved {providerKey} friend list: fetched={friends.Count}, active={writeFriends.WrittenCount}, skipped={writeFriends.SkippedCount}.");

                if (ShouldRefreshOwnership(options))
                {
                    await RefreshOwnershipAsync(
                        friendsProvider,
                        providerKey,
                        scopedFriends,
                        options.Scope,
                        fullLibraryIds,
                        payload,
                        payloadLock,
                        reportProgress,
                        maxDegreeOfParallelism,
                        ownershipSnapshots,
                        cancel).ConfigureAwait(false);
                }

                if (discoverUnowned && preparation.CanRefreshAchievements)
                {
                    await RefreshUnownedDefinitionsAndOwnershipAsync(
                        friendsProvider,
                        providerKey,
                        ownershipSnapshots,
                        options,
                        payload,
                        payloadLock,
                        reportProgress,
                        cancel).ConfigureAwait(false);
                }

                var candidates = FilterIgnoredCandidates(
                    providerKey,
                    _friendCache.LoadFriendRefreshCandidates(providerKey, options) ??
                    new List<FriendRefreshCandidate>());

                payload.FriendSummary.CandidatesLoaded += candidates.Count;
                _logger?.Debug(
                    $"Loaded {providerKey} friend achievement scrape candidates: candidates={candidates.Count}, scope={options.Scope}.");

                if (!preparation.CanRefreshAchievements)
                {
                    _logger?.Debug($"Skipping {providerKey} friend achievement scrapes: provider did not prepare achievement auth.");
                    MarkAuthFailure(payload, providerKey, true);
                    return payload;
                }

                await RefreshAchievementsAsync(
                    friendsProvider,
                    providerKey,
                    candidates,
                    payload,
                    payloadLock,
                    reportProgress,
                    maxDegreeOfParallelism,
                    cancel).ConfigureAwait(false);
            }
            finally
            {
                friendsProvider.EndRefresh();
            }

            return payload;
        }

        private async Task RefreshOwnershipAsync(
            IFriendsProvider friendsProvider,
            string providerKey,
            IReadOnlyList<FriendIdentity> friends,
            FriendRefreshScope scope,
            HashSet<string> fullLibraryIds,
            RebuildPayload payload,
            object payloadLock,
            Action<string, int, int> reportProgress,
            int maxDegreeOfParallelism,
            List<FriendOwnershipSnapshot> ownershipSnapshots,
            CancellationToken cancel)
        {
            if (friends == null || friends.Count == 0)
            {
                return;
            }

            if (maxDegreeOfParallelism <= 1 || friends.Count == 1)
            {
                for (var i = 0; i < friends.Count; i++)
                {
                    cancel.ThrowIfCancellationRequested();
                    var shouldContinue = await RefreshOwnershipItemAsync(
                        friendsProvider,
                        providerKey,
                        friends[i],
                        scope,
                        FriendUsesFullLibrary(fullLibraryIds, friends[i]),
                        i + 1,
                        friends.Count,
                        payload,
                        payloadLock,
                        reportProgress,
                        ownershipSnapshots,
                        cancel).ConfigureAwait(false);
                    if (!shouldContinue)
                    {
                        return;
                    }
                }

                return;
            }

            using (var authCts = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                var started = 0;
                try
                {
                    await RunBoundedAsync(
                        friends,
                        maxDegreeOfParallelism,
                        async (friend, _, token) =>
                        {
                            var progressCurrent = Interlocked.Increment(ref started);
                            var shouldContinue = await RefreshOwnershipItemAsync(
                                friendsProvider,
                                providerKey,
                                friend,
                                scope,
                                FriendUsesFullLibrary(fullLibraryIds, friend),
                                progressCurrent,
                                friends.Count,
                                payload,
                                payloadLock,
                                reportProgress,
                                ownershipSnapshots,
                                token).ConfigureAwait(false);
                            if (!shouldContinue)
                            {
                                authCts.Cancel();
                            }
                        },
                        authCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    cancel.ThrowIfCancellationRequested();
                }
            }
        }

        private async Task<bool> RefreshOwnershipItemAsync(
            IFriendsProvider friendsProvider,
            string providerKey,
            FriendIdentity friend,
            FriendRefreshScope scope,
            bool friendUsesFullLibrary,
            int progressCurrent,
            int total,
            RebuildPayload payload,
            object payloadLock,
            Action<string, int, int> reportProgress,
            List<FriendOwnershipSnapshot> ownershipSnapshots,
            CancellationToken cancel)
        {
            if (friend == null || string.IsNullOrWhiteSpace(friend.ExternalUserId))
            {
                return true;
            }

            // In the Full scope, shared-library friends need no ownership fetch: Full already covers the
            // entire owned library. Only full-library friends require ownership here (to discover the
            // games they own that the current user does not).
            if (scope == FriendRefreshScope.Full && !friendUsesFullLibrary)
            {
                return true;
            }

            Report(
                reportProgress,
                Format(
                    "LOCPlayAch_FriendsRefresh_Progress_Ownership",
                    "Refreshing friend libraries {0}/{1}...",
                    progressCurrent,
                    total),
                Math.Max(0, progressCurrent - 1),
                total + 1);

            var limiter = CreateScanRateLimiter();
            var ownershipResult = await limiter.ExecuteWithRetryAsync(
                () => friendsProvider.GetOwnedGamesAsync(friend, cancel),
                IsTransientError,
                cancel).ConfigureAwait(false);
            if (ownershipResult?.Success != true)
            {
                _logger?.Debug($"Friend ownership unavailable for {providerKey}/{friend.ExternalUserId}: {ownershipResult?.ErrorMessage}");
                if (ownershipResult?.AuthRequired == true)
                {
                    lock (payloadLock)
                    {
                        MarkAuthFailure(payload, providerKey, true);
                    }

                    return false;
                }

                return true;
            }

            lock (payloadLock)
            {
                payload.FriendSummary.OwnershipPagesRefreshed++;
            }

            var ownedGames = ownershipResult.Data ?? Array.Empty<FriendGameOwnership>();
            // Only full-library friends feed unowned discovery: their ownership snapshot is what
            // RefreshUnownedDefinitionsAndOwnershipAsync expands into provider-only rows. Shared-library
            // friends still have their (shared) ownership saved below, but never contribute unowned data.
            if (ownershipSnapshots != null && friendUsesFullLibrary)
            {
                lock (ownershipSnapshots)
                {
                    ownershipSnapshots.Add(new FriendOwnershipSnapshot
                    {
                        Friend = friend,
                        Ownership = ownedGames
                            .Where(item => HasProviderGameIdentity(item))
                            .ToList()
                    });
                }
            }

            var writeOwnership = _friendCache.SaveFriendOwnership(
                providerKey,
                friend.ExternalUserId,
                ownedGames);
            if (writeOwnership?.Success != true)
            {
                _logger?.Warn($"Failed to save friend ownership for {providerKey}/{friend.ExternalUserId}: {writeOwnership?.ErrorMessage}");
                return true;
            }

            lock (payloadLock)
            {
                payload.FriendSummary.OwnershipRowsWritten += writeOwnership.WrittenCount;
            }

            _logger?.Debug(
                $"Saved friend ownership for {providerKey}/{friend.ExternalUserId}: " +
                $"fetched={ownedGames.Count}, shared={writeOwnership.WrittenCount}, skippedUnshared={writeOwnership.SkippedCount}.");
            return true;
        }

        private async Task RefreshUnownedDefinitionsAndOwnershipAsync(
            IFriendsProvider friendsProvider,
            string providerKey,
            IReadOnlyList<FriendOwnershipSnapshot> ownershipSnapshots,
            FriendRefreshOptions options,
            RebuildPayload payload,
            object payloadLock,
            Action<string, int, int> reportProgress,
            CancellationToken cancel)
        {
            var snapshots = ownershipSnapshots?
                .Where(snapshot => snapshot?.Friend != null && snapshot.Ownership?.Count > 0)
                .ToList();
            if (snapshots == null || snapshots.Count == 0)
            {
                return;
            }

            var requestedProviderGameKeys = options?.ProviderGameKeys?
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Select(key => key.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var requestedAppIds = options?.ProviderAppIds?
                .Where(id => id > 0)
                .Select(id => id.ToString())
                .Distinct()
                .ToList();
            var requestedKeys = new List<string>();
            if (requestedProviderGameKeys?.Count > 0)
            {
                requestedKeys.AddRange(requestedProviderGameKeys);
            }

            if (requestedAppIds?.Count > 0)
            {
                requestedKeys.AddRange(requestedAppIds);
            }

            var requestedKeySet = requestedKeys.Count > 0
                ? new HashSet<string>(requestedKeys, StringComparer.OrdinalIgnoreCase)
                : null;

            var ownershipByKey = snapshots
                .SelectMany(snapshot => snapshot.Ownership)
                .Where(item => HasProviderGameIdentity(item) &&
                               (requestedKeySet == null || requestedKeySet.Contains(GetProviderGameCacheKey(item))))
                .GroupBy(GetProviderGameCacheKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList());
            if (ownershipByKey.Count == 0)
            {
                return;
            }

            var providerGameKeys = ownershipByKey.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToList();
            var states = _friendCache.LoadFriendGameDefinitionStates(providerKey, providerGameKeys) ??
                         new Dictionary<string, FriendGameDefinitionState>(StringComparer.OrdinalIgnoreCase);
            var definitionTtl = options.DefinitionTtl.GetValueOrDefault(DefaultDefinitionTtl);
            if (definitionTtl <= TimeSpan.Zero)
            {
                definitionTtl = DefaultDefinitionTtl;
            }

            var cutoffUtc = DateTime.UtcNow - definitionTtl;
            var dueProviderGameKeys = providerGameKeys
                .Where(key => IsDefinitionCheckDue(states.TryGetValue(key, out var state) ? state : null, cutoffUtc))
                .ToList();

            if (dueProviderGameKeys.Count > 0)
            {
                var limiter = CreateScanRateLimiter();
                for (var i = 0; i < dueProviderGameKeys.Count; i++)
                {
                    cancel.ThrowIfCancellationRequested();
                    var providerGameKey = dueProviderGameKeys[i];
                    var ownershipRows = ownershipByKey[providerGameKey];
                    var sample = ownershipRows.FirstOrDefault(item => item != null);
                    var appId = Math.Max(0, sample?.AppId ?? 0);
                    var gameName = ResolveOwnershipGameName(ownershipRows, providerKey, providerGameKey);
                    Report(
                        reportProgress,
                        Format(
                            "LOCPlayAch_FriendsRefresh_Progress_Definitions",
                            "Checking friend game definitions {0}/{1}...",
                            i + 1,
                            dueProviderGameKeys.Count),
                        i,
                        dueProviderGameKeys.Count + 1);

                    await limiter.DelayBeforeNextAsync(cancel).ConfigureAwait(false);
                    var definitionResult = await limiter.ExecuteWithRetryAsync(
                        () => friendsProvider.GetFriendGameDefinitionAsync(providerGameKey, appId, gameName, cancel),
                        IsTransientError,
                        cancel).ConfigureAwait(false);

                    if (definitionResult?.AuthRequired == true)
                    {
                        lock (payloadLock)
                        {
                            MarkAuthFailure(payload, providerKey, true);
                        }

                        return;
                    }

                    var definition = definitionResult?.Data ?? new FriendGameDefinition
                    {
                        ProviderKey = providerKey,
                        AppId = appId,
                        ProviderGameKey = providerGameKey,
                        GameName = gameName,
                        Status = definitionResult?.TransientFailure == true
                            ? FriendGameDefinitionStatus.Transient
                            : FriendGameDefinitionStatus.Unavailable,
                        LastCheckedUtc = DateTime.UtcNow
                    };

                    definition.ProviderKey = providerKey;
                    definition.AppId = appId;
                    definition.ProviderGameKey = providerGameKey;
                    if (string.IsNullOrWhiteSpace(definition.GameName))
                    {
                        definition.GameName = gameName;
                    }

                    await DownloadDefinitionAchievementIconsAsync(definition, cancel).ConfigureAwait(false);

                    var writeDefinition = _friendCache.SaveFriendGameDefinition(providerKey, definition);
                    if (writeDefinition?.Success != true)
                    {
                        _logger?.Warn($"Failed to save friend game definition for {providerKey}/{providerGameKey}: {writeDefinition?.ErrorMessage}");
                    }
                }
            }

            var discoveredProviderGameKeys = new HashSet<string>(providerGameKeys, StringComparer.OrdinalIgnoreCase);
            foreach (var snapshot in snapshots)
            {
                var providerOnlyOwnership = snapshot.Ownership
                    .Where(item => HasProviderGameIdentity(item) && discoveredProviderGameKeys.Contains(GetProviderGameCacheKey(item)))
                    .ToList();
                if (providerOnlyOwnership.Count == 0)
                {
                    continue;
                }

                var writeOwnership = _friendCache.SaveFriendOwnership(
                    providerKey,
                    snapshot.Friend.ExternalUserId,
                    providerOnlyOwnership,
                    new FriendOwnershipSaveOptions { IncludeProviderOnlyGames = true });
                if (writeOwnership?.Success != true)
                {
                    _logger?.Warn($"Failed to save provider-only friend ownership for {providerKey}/{snapshot.Friend.ExternalUserId}: {writeOwnership?.ErrorMessage}");
                    continue;
                }

                lock (payloadLock)
                {
                    payload.FriendSummary.OwnershipRowsWritten += writeOwnership.WrittenCount;
                }
            }

            await DownloadUnownedGameImagesAsync(providerKey, discoveredProviderGameKeys, ownershipByKey, cancel).ConfigureAwait(false);
        }

        // Downloads friend avatars into the per-user icon cache and records the local path on each
        // identity so it is persisted (and displayed) instead of a remote URL.
        private async Task DownloadFriendAvatarsAsync(
            string providerKey,
            IReadOnlyList<FriendIdentity> friends,
            CancellationToken cancel)
        {
            if (_imageService == null || friends == null || friends.Count == 0)
            {
                return;
            }

            await Task.WhenAll(friends.Select(friend => DownloadFriendAvatarAsync(providerKey, friend, cancel)))
                .ConfigureAwait(false);
        }

        private async Task DownloadFriendAvatarAsync(
            string providerKey,
            FriendIdentity friend,
            CancellationToken cancel)
        {
            if (friend == null || string.IsNullOrWhiteSpace(friend.AvatarUrl))
            {
                return;
            }

            try
            {
                var path = await _imageService
                    .GetOrDownloadIconAsync(friend.AvatarUrl, ImageDecodeSize, cancel, FriendImageCacheFolders.Avatars)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    friend.AvatarPath = path;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"Failed to cache friend avatar for {providerKey}/{friend.ExternalUserId}.");
            }
        }

        // Downloads the unlocked/locked achievement icons for an unowned game definition into the
        // shared friend-games cache and rewrites the paths to local files, so they are stored and
        // displayed from disk like owned-game achievement icons (instead of remote URLs).
        private async Task DownloadDefinitionAchievementIconsAsync(
            FriendGameDefinition definition,
            CancellationToken cancel)
        {
            if (_imageService == null || definition?.Achievements == null || definition.Achievements.Count == 0)
            {
                return;
            }

            var decodeSize = ResolveAchievementIconDecodeSize();
            await Task.WhenAll(definition.Achievements
                    .Where(achievement => achievement != null)
                    .Select(achievement => DownloadAchievementIconsAsync(achievement, decodeSize, cancel)))
                .ConfigureAwait(false);
        }

        private async Task DownloadAchievementIconsAsync(
            AchievementDetail achievement,
            int decodeSize,
            CancellationToken cancel)
        {
            var unlockedSource = achievement.UnlockedIconPath;
            var lockedSource = achievement.LockedIconPath;

            var unlocked = await DownloadAchievementIconAsync(unlockedSource, decodeSize, cancel).ConfigureAwait(false);
            achievement.UnlockedIconPath = unlocked;

            // Providers like Exophase use the same image for the locked and unlocked states. Reuse the
            // already-downloaded result instead of fetching (and warming up) the identical URL a second
            // time, which otherwise doubles the per-achievement download cost.
            achievement.LockedIconPath = string.Equals(lockedSource, unlockedSource, StringComparison.OrdinalIgnoreCase)
                ? unlocked
                : await DownloadAchievementIconAsync(lockedSource, decodeSize, cancel).ConfigureAwait(false);
        }

        private async Task<string> DownloadAchievementIconAsync(string url, int decodeSize, CancellationToken cancel)
        {
            if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            try
            {
                var path = await _imageService
                    .GetOrDownloadIconAsync(url, decodeSize, cancel, FriendImageCacheFolders.Games)
                    .ConfigureAwait(false);
                return string.IsNullOrWhiteSpace(path) ? url : path;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Failed to cache unowned achievement icon.");
                return url;
            }
        }

        // Mirrors AchievementIconService: optimized 128px icons unless the user preserves full resolution.
        private int ResolveAchievementIconDecodeSize()
        {
            return _settings?.Persisted?.PreserveAchievementIconResolution == true
                ? 0
                : OptimizedAchievementIconDecodeSize;
        }

        // Downloads cover/icon art for provider-only (unowned) games into the shared friend-games cache
        // and persists the local paths so the summaries grid can render them like owned games.
        private async Task DownloadUnownedGameImagesAsync(
            string providerKey,
            HashSet<string> providerGameKeys,
            Dictionary<string, List<FriendGameOwnership>> ownershipByKey,
            CancellationToken cancel)
        {
            if (_imageService == null || providerGameKeys == null || providerGameKeys.Count == 0)
            {
                return;
            }

            await Task.WhenAll(providerGameKeys.Select(providerGameKey =>
                    DownloadUnownedGameImageAsync(providerKey, providerGameKey, ownershipByKey, cancel)))
                .ConfigureAwait(false);
        }

        private async Task DownloadUnownedGameImageAsync(
            string providerKey,
            string providerGameKey,
            Dictionary<string, List<FriendGameOwnership>> ownershipByKey,
            CancellationToken cancel)
        {
            if (string.IsNullOrWhiteSpace(providerGameKey) ||
                ownershipByKey == null ||
                !ownershipByKey.TryGetValue(providerGameKey, out var owners))
            {
                return;
            }

            var source = owners?.FirstOrDefault(item => item != null);
            if (source == null)
            {
                return;
            }

            try
            {
                var iconPath = string.IsNullOrWhiteSpace(source.IconUrl)
                    ? null
                    : await _imageService
                        .GetOrDownloadIconAsync(source.IconUrl, ImageDecodeSize, cancel, FriendImageCacheFolders.Games)
                        .ConfigureAwait(false);
                var coverPath = string.IsNullOrWhiteSpace(source.CoverUrl)
                    ? null
                    : await _imageService
                        .GetOrDownloadIconAsync(source.CoverUrl, ImageDecodeSize, cancel, FriendImageCacheFolders.Games)
                        .ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(iconPath) && string.IsNullOrWhiteSpace(coverPath))
                {
                    return;
                }

                _friendCache.SaveProviderGameImagePaths(providerKey, source.ProviderGameKey ?? providerGameKey, source.AppId, iconPath, coverPath);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"Failed to cache unowned game images for {providerKey}/{providerGameKey}.");
            }
        }

        private async Task RefreshAchievementsAsync(
            IFriendsProvider friendsProvider,
            string providerKey,
            IReadOnlyList<FriendRefreshCandidate> candidates,
            RebuildPayload payload,
            object payloadLock,
            Action<string, int, int> reportProgress,
            int maxDegreeOfParallelism,
            CancellationToken cancel)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return;
            }

            if (maxDegreeOfParallelism <= 1 || candidates.Count == 1)
            {
                var limiter = CreateScanRateLimiter();
                for (var i = 0; i < candidates.Count; i++)
                {
                    cancel.ThrowIfCancellationRequested();
                    var shouldContinue = await RefreshAchievementCandidateAsync(
                        friendsProvider,
                        providerKey,
                        candidates[i],
                        i + 1,
                        candidates.Count,
                        payload,
                        payloadLock,
                        reportProgress,
                        delayBeforeRequest: true,
                        limiter,
                        cancel).ConfigureAwait(false);
                    if (!shouldContinue)
                    {
                        break;
                    }
                }

                return;
            }

            using (var authCts = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                var started = 0;
                try
                {
                    await RunBoundedAsync(
                        candidates,
                        maxDegreeOfParallelism,
                        async (candidate, _, token) =>
                        {
                            var progressCurrent = Interlocked.Increment(ref started);
                            var limiter = CreateScanRateLimiter();
                            var shouldContinue = await RefreshAchievementCandidateAsync(
                                friendsProvider,
                                providerKey,
                                candidate,
                                progressCurrent,
                                candidates.Count,
                                payload,
                                payloadLock,
                                reportProgress,
                                delayBeforeRequest: false,
                                limiter,
                                token).ConfigureAwait(false);
                            if (!shouldContinue)
                            {
                                authCts.Cancel();
                            }
                        },
                        authCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    cancel.ThrowIfCancellationRequested();
                }
            }
        }

        private async Task<bool> RefreshAchievementCandidateAsync(
            IFriendsProvider friendsProvider,
            string providerKey,
            FriendRefreshCandidate candidate,
            int progressCurrent,
            int total,
            RebuildPayload payload,
            object payloadLock,
            Action<string, int, int> reportProgress,
            bool delayBeforeRequest,
            RateLimiter limiter,
            CancellationToken cancel)
        {
            if (candidate?.Friend == null || !HasProviderGameIdentity(candidate.AppId, candidate.ProviderGameKey))
            {
                return true;
            }

            Report(
                reportProgress,
                Format(
                    "LOCPlayAch_FriendsRefresh_Progress_Achievements",
                    "Refreshing friend achievements {0}/{1}...",
                    progressCurrent,
                    total),
                Math.Max(0, progressCurrent - 1),
                total + 1);

            if (delayBeforeRequest)
            {
                await limiter.DelayBeforeNextAsync(cancel).ConfigureAwait(false);
            }

            var scrapeResult = await limiter.ExecuteWithRetryAsync(
                () => friendsProvider.GetFriendGameAchievementsAsync(
                    candidate.Friend,
                    candidate.ProviderGameKey,
                    candidate.AppId,
                    candidate.GameName,
                    cancel),
                IsTransientError,
                cancel).ConfigureAwait(false);

            if (scrapeResult?.AuthRequired == true)
            {
                lock (payloadLock)
                {
                    MarkAuthFailure(payload, providerKey, true);
                }

                return false;
            }

            var achievements = scrapeResult?.Data ?? CreateFailureResult(candidate, scrapeResult);
            var writeAchievements = _friendCache.SaveFriendGameAchievements(
                providerKey,
                candidate.Friend.ExternalUserId,
                candidate.ProviderGameKey,
                candidate.AppId,
                achievements);
            if (writeAchievements?.Success != true)
            {
                _logger?.Warn($"Failed to save friend achievements for {providerKey}/{candidate.Friend.ExternalUserId}/{GetProviderGameCacheKey(candidate)}: {writeAchievements?.ErrorMessage}");
                return true;
            }

            lock (payloadLock)
            {
                payload.FriendSummary.CandidatesRefreshed++;
                payload.FriendSummary.AchievementsSaved++;
            }

            return true;
        }

        private int ResolveFriendRefreshParallelism()
        {
            return (_settings?.Persisted?.EnableParallelProviderRefresh ?? true)
                ? FriendRefreshParallelism
                : 1;
        }

        private IReadOnlyList<FriendIdentity> FilterIgnoredFriends(
            string providerKey,
            IReadOnlyList<FriendIdentity> friends)
        {
            var ignoredIds = GetIgnoredFriendIds(providerKey);
            if (ignoredIds.Count == 0)
            {
                return friends ?? Array.Empty<FriendIdentity>();
            }

            return (friends ?? Array.Empty<FriendIdentity>())
                .Where(friend => !ignoredIds.Contains(friend?.ExternalUserId ?? string.Empty))
                .ToList();
        }

        // Restricts per-friend work to the requested subset when the options carry a friend
        // selection (e.g. a custom refresh of specific friends); otherwise returns all friends.
        private static IReadOnlyList<FriendIdentity> ScopeFriendsToSelection(
            IReadOnlyList<FriendIdentity> friends,
            FriendRefreshOptions options)
        {
            var all = friends ?? Array.Empty<FriendIdentity>();
            var selection = options?.FriendExternalUserIds;
            if (selection == null || selection.Count == 0)
            {
                return all;
            }

            var selected = new HashSet<string>(
                selection.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()),
                StringComparer.OrdinalIgnoreCase);
            if (selected.Count == 0)
            {
                return all;
            }

            return all
                .Where(friend => friend != null && selected.Contains(friend.ExternalUserId?.Trim() ?? string.Empty))
                .ToList();
        }

        private List<FriendRefreshCandidate> FilterIgnoredCandidates(
            string providerKey,
            List<FriendRefreshCandidate> candidates)
        {
            var ignoredIds = GetIgnoredFriendIds(providerKey);
            if (ignoredIds.Count == 0)
            {
                return candidates ?? new List<FriendRefreshCandidate>();
            }

            return (candidates ?? new List<FriendRefreshCandidate>())
                .Where(candidate => !ignoredIds.Contains(candidate?.Friend?.ExternalUserId ?? string.Empty))
                .ToList();
        }

        private HashSet<string> GetIgnoredFriendIds(string providerKey)
        {
            return GetSteamFriendIdSet(providerKey, "GetIgnoredSteamIds", "Failed to load Steam ignored friend ids.");
        }

        // Steam friends who opted into the full library scope. Resolved via reflection for the same
        // layering reason as GetIgnoredFriendIds (this service does not reference the Steam provider).
        private HashSet<string> GetFullLibraryFriendIds(string providerKey)
        {
            if (_fullLibraryIdsResolver != null)
            {
                return _fullLibraryIdsResolver(providerKey) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            if (string.Equals(providerKey, "Exophase", StringComparison.OrdinalIgnoreCase))
            {
                return GetProviderFriendIdSet(
                    "PlayniteAchievements.Providers.Exophase.ExophaseSettings",
                    "GetFullLibraryFriendIds",
                    "Failed to load Exophase full-library friend ids.");
            }

            return GetSteamFriendIdSet(providerKey, "GetFullLibrarySteamIds", "Failed to load Steam full-library friend ids.");
        }

        private HashSet<string> GetProviderFriendIdSet(string settingsTypeName, string methodName, string logMessage)
        {
            try
            {
                var settingsType = typeof(ProviderRegistry).Assembly.GetType(settingsTypeName);
                if (settingsType == null)
                {
                    return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }

                var settings = ResolveProviderSettings(settingsType);
                var getIds = settingsType.GetMethod(methodName);
                var ids = getIds?.Invoke(settings, null) as IEnumerable<string>;
                return new HashSet<string>(ids ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, logMessage);
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private HashSet<string> GetSteamFriendIdSet(string providerKey, string methodName, string logMessage)
        {
            if (!string.Equals(providerKey, "Steam", StringComparison.OrdinalIgnoreCase))
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                var steamSettingsType = typeof(ProviderRegistry).Assembly.GetType(
                    "PlayniteAchievements.Providers.Steam.SteamSettings");
                if (steamSettingsType == null)
                {
                    return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }

                var settings = ResolveProviderSettings(steamSettingsType);
                var getIds = steamSettingsType.GetMethod(methodName);
                var ids = getIds?.Invoke(settings, null) as IEnumerable<string>;
                return new HashSet<string>(ids ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, logMessage);
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private object ResolveProviderSettings(Type settingsType)
        {
            if (settingsType == null)
            {
                return null;
            }

            if (_providerRegistry != null)
            {
                var getSettings = typeof(ProviderRegistry).GetMethod("GetSettings");
                return getSettings?.MakeGenericMethod(settingsType).Invoke(_providerRegistry, null);
            }

            var settingsMethod = typeof(ProviderRegistry).GetMethod(nameof(ProviderRegistry.Settings));
            return settingsMethod?.MakeGenericMethod(settingsType).Invoke(null, null);
        }

        private static bool FriendUsesFullLibrary(HashSet<string> fullLibraryIds, FriendIdentity friend)
        {
            return friend != null &&
                   fullLibraryIds != null &&
                   fullLibraryIds.Contains(friend.ExternalUserId?.Trim() ?? string.Empty);
        }

        private RateLimiter CreateScanRateLimiter()
        {
            return new RateLimiter(
                Math.Max(0, _settings?.Persisted?.ScanDelayMs ?? 200),
                Math.Max(0, _settings?.Persisted?.MaxRetryAttempts ?? 3));
        }

        private static async Task RunBoundedAsync<T>(
            IReadOnlyList<T> items,
            int maxDegreeOfParallelism,
            Func<T, int, CancellationToken, Task> body,
            CancellationToken cancel)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            if (items == null || items.Count == 0)
            {
                return;
            }

            var degree = Math.Max(1, Math.Min(maxDegreeOfParallelism, items.Count));
            if (degree == 1)
            {
                for (var i = 0; i < items.Count; i++)
                {
                    cancel.ThrowIfCancellationRequested();
                    await body(items[i], i, cancel).ConfigureAwait(false);
                }

                return;
            }

            var nextIndex = -1;
            var workers = Enumerable.Range(0, degree)
                .Select(_ => WorkerAsync())
                .ToArray();

            await Task.WhenAll(workers).ConfigureAwait(false);

            async Task WorkerAsync()
            {
                while (true)
                {
                    cancel.ThrowIfCancellationRequested();
                    var index = Interlocked.Increment(ref nextIndex);
                    if (index >= items.Count)
                    {
                        return;
                    }

                    await body(items[index], index, cancel).ConfigureAwait(false);
                }
            }
        }

        private static bool IsTransientError(Exception ex)
        {
            if (ex == null || ex is OperationCanceledException)
            {
                return false;
            }

            var message = ex.Message ?? string.Empty;
            if (message.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("temporarily", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("429", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("502", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("503", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("504", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (message.IndexOf("connection", StringComparison.OrdinalIgnoreCase) >= 0 &&
                 message.IndexOf("reset", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return true;
            }

            return ex.InnerException != null &&
                   !ReferenceEquals(ex.InnerException, ex) &&
                   IsTransientError(ex.InnerException);
        }

        private static bool IsDefinitionCheckDue(FriendGameDefinitionState state, DateTime cutoffUtc)
        {
            if (state == null || !state.LastCheckedUtc.HasValue)
            {
                return true;
            }

            if (state.Status != FriendGameDefinitionStatus.Ok)
            {
                return true;
            }

            return state.LastCheckedUtc.Value < cutoffUtc;
        }

        private static string ResolveOwnershipGameName(
            IReadOnlyList<FriendGameOwnership> ownership,
            string providerKey,
            string providerGameKey)
        {
            var name = ownership?
                .Select(item => item?.GameName)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            return !string.IsNullOrWhiteSpace(name)
                ? name.Trim()
                : $"{providerKey} Game {providerGameKey}";
        }

        private static bool HasProviderGameIdentity(FriendGameOwnership ownership)
        {
            return ownership != null && HasProviderGameIdentity(ownership.AppId, ownership.ProviderGameKey);
        }

        private static bool HasProviderGameIdentity(int appId, string providerGameKey)
        {
            return appId > 0 || !string.IsNullOrWhiteSpace(providerGameKey);
        }

        private static string GetProviderGameCacheKey(FriendGameOwnership ownership)
        {
            return ownership == null ? null : GetProviderGameCacheKey(ownership.AppId, ownership.ProviderGameKey);
        }

        private static string GetProviderGameCacheKey(FriendRefreshCandidate candidate)
        {
            return candidate == null ? null : GetProviderGameCacheKey(candidate.AppId, candidate.ProviderGameKey);
        }

        private static string GetProviderGameCacheKey(int appId, string providerGameKey)
        {
            return !string.IsNullOrWhiteSpace(providerGameKey)
                ? providerGameKey.Trim()
                : (appId > 0 ? appId.ToString() : null);
        }

        private FriendRefreshOptions NormalizeOptions(FriendRefreshOptions options)
        {
            var normalized = options?.Clone() ?? new FriendRefreshOptions();
            if (!normalized.RefreshTtl.HasValue || normalized.RefreshTtl.Value <= TimeSpan.Zero)
            {
                normalized.RefreshTtl = TimeSpan.FromHours(Math.Max(1, _settings?.Persisted?.FriendsOverviewRefreshTtlHours ?? 24));
            }

            if (!normalized.DefinitionTtl.HasValue || normalized.DefinitionTtl.Value <= TimeSpan.Zero)
            {
                normalized.DefinitionTtl = DefaultDefinitionTtl;
            }

            return normalized;
        }

        private static bool ShouldRefreshOwnership(FriendRefreshOptions options)
        {
            if (options == null)
            {
                return false;
            }

            return options.Scope == FriendRefreshScope.Shared ||
                   options.Scope == FriendRefreshScope.Recent ||
                   options.Scope == FriendRefreshScope.Custom ||
                   (options.Scope == FriendRefreshScope.Full &&
                    options.IncludesProviderOnlyGames());
        }

        private static bool ShouldDiscoverUnowned(string providerKey, FriendRefreshOptions options)
        {
            if (options?.IncludesProviderOnlyGames() != true)
            {
                return false;
            }

            if (!string.Equals(providerKey, "Steam", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(providerKey, "Exophase", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private static void MarkAuthFailure(RebuildPayload payload, string providerKey, bool authRequired)
        {
            if (!authRequired || payload == null)
            {
                return;
            }

            payload.AuthRequired = true;
            if (!string.IsNullOrWhiteSpace(providerKey) &&
                !payload.FailedProviderKeys.Contains(providerKey, StringComparer.OrdinalIgnoreCase))
            {
                payload.FailedProviderKeys.Add(providerKey);
            }
        }

        private static void Merge(RebuildPayload target, RebuildPayload source)
        {
            if (target == null || source == null)
            {
                return;
            }

            target.AuthRequired |= source.AuthRequired;
            foreach (var key in source.FailedProviderKeys ?? Enumerable.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(key) &&
                    !target.FailedProviderKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                {
                    target.FailedProviderKeys.Add(key);
                }
            }

            target.FriendSummary.ProvidersProcessed += source.FriendSummary?.ProvidersProcessed ?? 0;
            target.FriendSummary.FriendsFetched += source.FriendSummary?.FriendsFetched ?? 0;
            target.FriendSummary.FriendsSaved += source.FriendSummary?.FriendsSaved ?? 0;
            target.FriendSummary.OwnershipPagesRefreshed += source.FriendSummary?.OwnershipPagesRefreshed ?? 0;
            target.FriendSummary.OwnershipRowsWritten += source.FriendSummary?.OwnershipRowsWritten ?? 0;
            target.FriendSummary.CandidatesLoaded += source.FriendSummary?.CandidatesLoaded ?? 0;
            target.FriendSummary.CandidatesRefreshed += source.FriendSummary?.CandidatesRefreshed ?? 0;
            target.FriendSummary.AchievementsSaved += source.FriendSummary?.AchievementsSaved ?? 0;
        }

        private static void Report(Action<string, int, int> reportProgress, string message, int current, int total)
        {
            reportProgress?.Invoke(message, current, total);
        }

        private static string Format(string resourceKey, string fallback, params object[] args)
        {
            var format = ResourceProvider.GetString(resourceKey);
            if (string.IsNullOrWhiteSpace(format))
            {
                format = fallback;
            }

            return string.Format(format, args ?? Array.Empty<object>());
        }

        private static FriendGameAchievements CreateFailureResult(
            FriendRefreshCandidate candidate,
            FriendsProviderResult<FriendGameAchievements> scrapeResult)
        {
            return new FriendGameAchievements
            {
                Friend = candidate.Friend,
                AppId = candidate.AppId,
                ProviderGameKey = candidate.ProviderGameKey,
                LastUpdatedUtc = DateTime.UtcNow,
                StatsUnavailable = true,
                TransientFailure = scrapeResult?.TransientFailure == true,
                DetailCode = SteamScrapeDetail.None
            };
        }

        private sealed class FriendOwnershipSnapshot
        {
            public FriendIdentity Friend { get; set; }
            public List<FriendGameOwnership> Ownership { get; set; } = new List<FriendGameOwnership>();
        }
    }
}
