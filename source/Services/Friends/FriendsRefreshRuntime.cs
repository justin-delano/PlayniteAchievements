using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Friends;
using PlayniteAchievements.Models.Settings;
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

            var progress = new FriendRefreshProgressSession(reportProgress);
            progress.InitializeProviderTotal(providers.Count);
            var contexts = new List<FriendProviderRefreshContext>();
            var payloadLock = new object();
            try
            {
                for (var i = 0; i < providers.Count; i++)
                {
                    var provider = providers[i];
                    cancel.ThrowIfCancellationRequested();
                    var context = await PrepareProviderRefreshAsync(
                        provider.Friends,
                        options,
                        payload,
                        progress,
                        i,
                        providers.Count,
                        cancel).ConfigureAwait(false);
                    if (context != null)
                    {
                        contexts.Add(context);
                    }
                }

                var activeContexts = contexts
                    .Where(context => context?.CanContinue == true)
                    .ToList();
                progress.InitializeFriendAvatarTotal(activeContexts.Sum(context => CountFriendAvatarDownloads(context.ScopedFriends)));
                foreach (var context in activeContexts)
                {
                    cancel.ThrowIfCancellationRequested();
                    await DownloadFriendAvatarsAsync(
                        context.ProviderKey,
                        context.ScopedFriends,
                        cancel,
                        progress).ConfigureAwait(false);
                }

                foreach (var context in activeContexts)
                {
                    cancel.ThrowIfCancellationRequested();
                    var writeFriends = _friendCache.SaveFriendList(context.ProviderKey, context.Friends);
                    if (writeFriends?.Success != true)
                    {
                        _logger?.Warn($"Failed to save {context.ProviderKey} friend list: {writeFriends?.ErrorMessage}");
                        context.CanContinue = false;
                        continue;
                    }

                    payload.FriendSummary.FriendsSaved += writeFriends.WrittenCount;
                    _logger?.Debug(
                        $"Saved {context.ProviderKey} friend list: fetched={context.Friends.Count}, active={writeFriends.WrittenCount}, skipped={writeFriends.SkippedCount}.");
                }

                activeContexts = activeContexts
                    .Where(context => context.CanContinue)
                    .ToList();
                var logicalFriends = BuildLogicalFriendGroups(activeContexts);
                progress.InitializeFriendLibraryTotal(logicalFriends.Count);
                await RefreshOwnershipByLogicalFriendAsync(
                    logicalFriends,
                    options,
                    payload,
                    payloadLock,
                    progress,
                    cancel).ConfigureAwait(false);

                foreach (var context in activeContexts)
                {
                    if (context.DiscoverUnowned && context.Preparation.CanRefreshAchievements)
                    {
                        await RefreshUnownedDefinitionsAndOwnershipAsync(
                            context.Provider,
                            context.ProviderKey,
                            context.OwnershipSnapshots,
                            options,
                            payload,
                            payloadLock,
                            progress,
                            cancel).ConfigureAwait(false);
                    }
                }

                var achievementWorkItems = LoadAchievementWorkItems(activeContexts, options, payload);
                progress.AddGameChecksTotal(achievementWorkItems.Count);
                await RefreshAchievementWorkItemsAsync(
                    achievementWorkItems,
                    payload,
                    payloadLock,
                    progress,
                    cancel).ConfigureAwait(false);
            }
            finally
            {
                foreach (var context in contexts)
                {
                    try
                    {
                        context?.Provider?.EndRefresh();
                    }
                    catch (Exception ex)
                    {
                        _logger?.Debug(ex, $"Failed to end friend refresh for {context?.ProviderKey}.");
                    }
                }
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

                var discoveredFriends = NormalizeProviderFriendIdentities(providerKey, friendsResult.Data);
                MergeProviderFriendsIntoSettings(providerKey, discoveredFriends);
                var friends = FilterIgnoredFriends(providerKey, discoveredFriends);
                await DownloadFriendAvatarsAsync(providerKey, friends, cancel, progress: null).ConfigureAwait(false);

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

        private async Task<FriendProviderRefreshContext> PrepareProviderRefreshAsync(
            IFriendsProvider friendsProvider,
            FriendRefreshOptions options,
            RebuildPayload payload,
            FriendRefreshProgressSession progress,
            int providerIndex,
            int providerTotal,
            CancellationToken cancel)
        {
            if (friendsProvider == null)
            {
                return null;
            }

            var providerKey = friendsProvider.ProviderKey;
            var context = new FriendProviderRefreshContext
            {
                Provider = friendsProvider,
                ProviderKey = providerKey,
                FullLibraryIds = GetFullLibraryFriendIds(providerKey),
                DiscoverUnowned = ShouldDiscoverUnowned(providerKey, options),
                MaxDegreeOfParallelism = ResolveFriendRefreshParallelism()
            };
            context.OwnershipSnapshots = context.DiscoverUnowned
                ? new List<FriendOwnershipSnapshot>()
                : null;

            progress?.ReportLoadingFriends(providerKey, providerIndex, providerTotal);
            var preparationResult = await friendsProvider.BeginRefreshAsync(cancel).ConfigureAwait(false);
            if (preparationResult?.Success != true)
            {
                _logger?.Debug($"Friends refresh skipped for {providerKey}: {preparationResult?.ErrorMessage ?? "provider unavailable"}");
                MarkAuthFailure(payload, providerKey, preparationResult?.AuthRequired == true);
                progress?.ReportProviderRosterLoaded(providerKey);
                return context;
            }

            context.Preparation = preparationResult.Data ?? new FriendsRefreshPreparation();
            context.CanContinue = true;
            payload.FriendSummary.ProvidersProcessed++;

            if (friendsProvider is ICurrentUserGameLabelReceiver labelReceiver)
            {
                var currentUserLabels = _friendCache.LoadCurrentUserGameLabels() ??
                                        new List<CurrentUserGameLabel>();
                labelReceiver.SetCurrentUserGameLabels(currentUserLabels);
                _logger?.Debug(
                    $"Supplied {currentUserLabels.Count} current-user game label(s) to {providerKey} friend merge.");
            }

            var friendsResult = await friendsProvider.GetFriendsAsync(cancel).ConfigureAwait(false);
            if (friendsResult?.Success != true)
            {
                _logger?.Debug($"Friends refresh skipped for {providerKey}: {friendsResult?.ErrorMessage ?? "friend list unavailable"}");
                MarkAuthFailure(payload, providerKey, friendsResult?.AuthRequired == true);
                context.CanContinue = false;
                progress?.ReportProviderRosterLoaded(providerKey);
                return context;
            }

            var discoveredFriends = NormalizeProviderFriendIdentities(providerKey, friendsResult.Data);
            MergeProviderFriendsIntoSettings(providerKey, discoveredFriends);
            context.Friends = FilterIgnoredFriends(providerKey, discoveredFriends).ToList();
            context.ScopedFriends = ScopeFriendsToSelection(context.Friends, options).ToList();
            payload.FriendSummary.FriendsFetched += context.Friends.Count;
            progress?.ReportProviderRosterLoaded(providerKey);
            return context;
        }

        private async Task RefreshOwnershipByLogicalFriendAsync(
            IReadOnlyList<LogicalFriendRefreshGroup> logicalFriends,
            FriendRefreshOptions options,
            RebuildPayload payload,
            object payloadLock,
            FriendRefreshProgressSession progress,
            CancellationToken cancel)
        {
            if (logicalFriends == null || logicalFriends.Count == 0)
            {
                return;
            }

            var maxDegreeOfParallelism = Math.Max(1, logicalFriends.Max(group =>
                group?.Accounts?.Max(account => account?.Context?.MaxDegreeOfParallelism ?? 1) ?? 1));
            if (maxDegreeOfParallelism <= 1 || logicalFriends.Count == 1)
            {
                for (var i = 0; i < logicalFriends.Count; i++)
                {
                    cancel.ThrowIfCancellationRequested();
                    var shouldContinue = await RefreshLogicalFriendOwnershipAsync(
                        logicalFriends[i],
                        options,
                        payload,
                        payloadLock,
                        cancel).ConfigureAwait(false);
                    progress?.ReportFriendLibraryCompleted(logicalFriends[i]?.DisplayName);
                    if (!shouldContinue)
                    {
                        return;
                    }
                }

                return;
            }

            using (var authCts = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                try
                {
                    await RunBoundedAsync(
                        logicalFriends,
                        maxDegreeOfParallelism,
                        async (group, _, token) =>
                        {
                            var shouldContinue = await RefreshLogicalFriendOwnershipAsync(
                                group,
                                options,
                                payload,
                                payloadLock,
                                token).ConfigureAwait(false);
                            progress?.ReportFriendLibraryCompleted(group?.DisplayName);
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

        private async Task<bool> RefreshLogicalFriendOwnershipAsync(
            LogicalFriendRefreshGroup logicalFriend,
            FriendRefreshOptions options,
            RebuildPayload payload,
            object payloadLock,
            CancellationToken cancel)
        {
            foreach (var account in logicalFriend?.Accounts ?? Enumerable.Empty<FriendAccountRefreshItem>())
            {
                var context = account?.Context;
                var friend = account?.Friend;
                if (context == null || friend == null || !ShouldRefreshOwnership(context.ProviderKey, options))
                {
                    continue;
                }

                var shouldContinue = await RefreshOwnershipItemAsync(
                    context.Provider,
                    context.ProviderKey,
                    friend,
                    options?.Scope ?? FriendRefreshScope.Recent,
                    FriendUsesFullLibrary(context.FullLibraryIds, friend),
                    payload,
                    payloadLock,
                    context.OwnershipSnapshots,
                    cancel).ConfigureAwait(false);
                if (!shouldContinue)
                {
                    return false;
                }
            }

            return true;
        }

        private List<FriendAchievementWorkItem> LoadAchievementWorkItems(
            IReadOnlyList<FriendProviderRefreshContext> contexts,
            FriendRefreshOptions options,
            RebuildPayload payload)
        {
            var workItems = new List<FriendAchievementWorkItem>();
            foreach (var context in contexts ?? Array.Empty<FriendProviderRefreshContext>())
            {
                var candidates = FilterIgnoredCandidates(
                    context.ProviderKey,
                    _friendCache.LoadFriendRefreshCandidates(context.ProviderKey, options) ??
                    new List<FriendRefreshCandidate>());
                payload.FriendSummary.CandidatesLoaded += candidates.Count;
                _logger?.Debug(
                    $"Loaded {context.ProviderKey} friend achievement scrape candidates: candidates={candidates.Count}, scope={options.Scope}.");

                if (!context.Preparation.CanRefreshAchievements)
                {
                    _logger?.Debug($"Skipping {context.ProviderKey} friend achievement scrapes: provider did not prepare achievement auth.");
                    MarkAuthFailure(payload, context.ProviderKey, true);
                    continue;
                }

                workItems.AddRange(candidates.Select(candidate => new FriendAchievementWorkItem
                {
                    Context = context,
                    Candidate = candidate
                }));
            }

            return workItems;
        }

        private async Task RefreshAchievementWorkItemsAsync(
            IReadOnlyList<FriendAchievementWorkItem> workItems,
            RebuildPayload payload,
            object payloadLock,
            FriendRefreshProgressSession progress,
            CancellationToken cancel)
        {
            if (workItems == null || workItems.Count == 0)
            {
                return;
            }

            var maxDegreeOfParallelism = Math.Max(1, workItems.Max(item => item?.Context?.MaxDegreeOfParallelism ?? 1));
            if (maxDegreeOfParallelism <= 1 || workItems.Count == 1)
            {
                var limiter = CreateScanRateLimiter();
                foreach (var item in workItems)
                {
                    cancel.ThrowIfCancellationRequested();
                    var shouldContinue = await RefreshAchievementCandidateAsync(
                        item.Context.Provider,
                        item.Context.ProviderKey,
                        item.Candidate,
                        payload,
                        payloadLock,
                        delayBeforeRequest: true,
                        limiter,
                        cancel).ConfigureAwait(false);
                    progress?.ReportGameCheckCompleted(FormatFriendGameDetail(item.Candidate));
                    if (!shouldContinue)
                    {
                        return;
                    }
                }

                return;
            }

            using (var authCts = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                try
                {
                    await RunBoundedAsync(
                        workItems,
                        maxDegreeOfParallelism,
                        async (item, _, token) =>
                        {
                            var limiter = CreateScanRateLimiter();
                            var shouldContinue = await RefreshAchievementCandidateAsync(
                                item.Context.Provider,
                                item.Context.ProviderKey,
                                item.Candidate,
                                payload,
                                payloadLock,
                                delayBeforeRequest: false,
                                limiter,
                                token).ConfigureAwait(false);
                            progress?.ReportGameCheckCompleted(FormatFriendGameDetail(item.Candidate));
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

        private async Task<RebuildPayload> RefreshProviderAsync(
            IFriendsProvider friendsProvider,
            FriendRefreshOptions options,
            FriendRefreshProgressSession progress,
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

                // Providers that map friend games to the local library by name (Exophase) match on the
                // servicing provider label the plugin stored for each current-user game, rather than
                // re-deriving platform from Playnite Source/Platform strings. Supply that index once per
                // provider refresh, before the ownership pass that performs the mapping.
                if (friendsProvider is ICurrentUserGameLabelReceiver labelReceiver)
                {
                    var currentUserLabels = _friendCache.LoadCurrentUserGameLabels() ??
                                            new List<CurrentUserGameLabel>();
                    labelReceiver.SetCurrentUserGameLabels(currentUserLabels);
                    _logger?.Debug(
                        $"Supplied {currentUserLabels.Count} current-user game label(s) to {providerKey} friend merge.");
                }
                progress?.ReportLoadingFriends();

                var friendsResult = await friendsProvider.GetFriendsAsync(cancel).ConfigureAwait(false);
                if (friendsResult?.Success != true)
                {
                    _logger?.Debug($"Friends refresh skipped for {providerKey}: {friendsResult?.ErrorMessage ?? "friend list unavailable"}");
                    MarkAuthFailure(payload, providerKey, friendsResult?.AuthRequired == true);
                    return payload;
                }

                var discoveredFriends = NormalizeProviderFriendIdentities(providerKey, friendsResult.Data);
                MergeProviderFriendsIntoSettings(providerKey, discoveredFriends);
                var friends = FilterIgnoredFriends(providerKey, discoveredFriends);
                payload.FriendSummary.FriendsFetched += friends.Count;

                // The friend list itself is always saved in full, but per-friend work (avatars,
                // library ownership, unowned schema discovery) is limited to the selected friends
                // when the request targets a specific subset (e.g. a custom refresh).
                var scopedFriends = ScopeFriendsToSelection(friends, options);

                await DownloadFriendAvatarsAsync(providerKey, scopedFriends, cancel, progress).ConfigureAwait(false);

                var writeFriends = _friendCache.SaveFriendList(providerKey, friends);
                if (writeFriends?.Success != true)
                {
                    _logger?.Warn($"Failed to save {providerKey} friend list: {writeFriends?.ErrorMessage}");
                    return payload;
                }

                payload.FriendSummary.FriendsSaved += writeFriends.WrittenCount;
                _logger?.Debug(
                    $"Saved {providerKey} friend list: fetched={friends.Count}, active={writeFriends.WrittenCount}, skipped={writeFriends.SkippedCount}.");

                if (ShouldRefreshOwnership(providerKey, options))
                {
                    await RefreshOwnershipAsync(
                        friendsProvider,
                        providerKey,
                        scopedFriends,
                        options.Scope,
                        fullLibraryIds,
                        payload,
                        payloadLock,
                        progress,
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
                        progress,
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
                    progress,
                    maxDegreeOfParallelism,
                    cancel).ConfigureAwait(false);
            }
            finally
            {
                progress?.CompleteProvider();
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
            FriendRefreshProgressSession progress,
            int maxDegreeOfParallelism,
            List<FriendOwnershipSnapshot> ownershipSnapshots,
            CancellationToken cancel)
        {
            if (friends == null || friends.Count == 0)
            {
                return;
            }

            progress?.ReportFriendLibraries(0, friends.Count);

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
                        payload,
                        payloadLock,
                        ownershipSnapshots,
                        cancel).ConfigureAwait(false);
                    progress?.ReportFriendLibraries(i + 1, friends.Count, GetFriendDisplayName(friends[i]));
                    if (!shouldContinue)
                    {
                        return;
                    }
                }

                return;
            }

            using (var authCts = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                var completed = 0;
                try
                {
                    await RunBoundedAsync(
                        friends,
                        maxDegreeOfParallelism,
                        async (friend, _, token) =>
                        {
                            var shouldContinue = await RefreshOwnershipItemAsync(
                                friendsProvider,
                                providerKey,
                                friend,
                                scope,
                                FriendUsesFullLibrary(fullLibraryIds, friend),
                                payload,
                                payloadLock,
                                ownershipSnapshots,
                                token).ConfigureAwait(false);
                            var progressCompleted = Interlocked.Increment(ref completed);
                            progress?.ReportFriendLibraries(progressCompleted, friends.Count, GetFriendDisplayName(friend));
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
            RebuildPayload payload,
            object payloadLock,
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
            if (scope == FriendRefreshScope.Full &&
                !friendUsesFullLibrary &&
                !RequiresOwnershipMapping(providerKey))
            {
                return true;
            }

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
            FriendRefreshProgressSession progress,
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
            var dueProviderGameKeys = options?.ForceDefinitionRefresh == true
                ? providerGameKeys
                : providerGameKeys
                    .Where(key => IsDefinitionCheckDue(states.TryGetValue(key, out var state) ? state : null, cutoffUtc))
                    .ToList();

            if (dueProviderGameKeys.Count > 0)
            {
                var limiter = CreateScanRateLimiter();
                progress?.AddGameChecksTotal(dueProviderGameKeys.Count);
                for (var i = 0; i < dueProviderGameKeys.Count; i++)
                {
                    cancel.ThrowIfCancellationRequested();
                    var providerGameKey = dueProviderGameKeys[i];
                    var ownershipRows = ownershipByKey[providerGameKey];
                    var sample = ownershipRows.FirstOrDefault(item => item != null);
                    var appId = Math.Max(0, sample?.AppId ?? 0);
                    var gameName = ResolveOwnershipGameName(ownershipRows, providerKey, providerGameKey);
                    progress?.ReportGameCheckActive(gameName);

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

                    await DownloadDefinitionAchievementIconsAsync(definition, cancel, progress).ConfigureAwait(false);

                    var writeDefinition = _friendCache.SaveFriendGameDefinition(providerKey, definition);
                    if (writeDefinition?.Success != true)
                    {
                        _logger?.Warn($"Failed to save friend game definition for {providerKey}/{providerGameKey}: {writeDefinition?.ErrorMessage}");
                    }

                    // Download the achievements-page header banner and store it as the game's local
                    // icon+cover paths, mirroring the Steam owned-game image flow. The URL is never
                    // persisted.
                    await DownloadDefinitionGameImageAsync(providerKey, providerGameKey, appId, definition.IconUrl, definition.GameName, cancel, progress)
                        .ConfigureAwait(false);
                    progress?.ReportGameCheckCompleted(gameName);
                }
            }

            var discoveredProviderGameKeys = new HashSet<string>(providerGameKeys, StringComparer.OrdinalIgnoreCase);
            var providerOnlyProbeLimiter = CreateScanRateLimiter();
            foreach (var snapshot in snapshots)
            {
                var libraryOwnership = new List<FriendGameOwnership>();
                foreach (var item in snapshot.Ownership
                    .Where(item => HasProviderGameIdentity(item) && discoveredProviderGameKeys.Contains(GetProviderGameCacheKey(item))))
                {
                    if (IsPlayniteLibraryFriendGame(providerKey, item))
                    {
                        libraryOwnership.Add(item);
                        continue;
                    }

                    var shouldContinue = await ProbeAndPersistProviderOnlyFriendGameAsync(
                        friendsProvider,
                        providerKey,
                        snapshot.Friend,
                        item,
                        providerOnlyProbeLimiter,
                        payload,
                        payloadLock,
                        progress,
                        cancel).ConfigureAwait(false);
                    if (!shouldContinue)
                    {
                        return;
                    }
                }

                if (libraryOwnership.Count == 0)
                {
                    continue;
                }

                var writeOwnership = _friendCache.SaveFriendOwnership(
                    providerKey,
                    snapshot.Friend.ExternalUserId,
                    libraryOwnership,
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

            await DownloadUnownedGameImagesAsync(providerKey, discoveredProviderGameKeys, ownershipByKey, cancel, progress).ConfigureAwait(false);
        }

        private async Task<bool> ProbeAndPersistProviderOnlyFriendGameAsync(
            IFriendsProvider friendsProvider,
            string providerKey,
            FriendIdentity friend,
            FriendGameOwnership ownership,
            RateLimiter limiter,
            RebuildPayload payload,
            object payloadLock,
            FriendRefreshProgressSession progress,
            CancellationToken cancel)
        {
            if (friendsProvider == null ||
                friend == null ||
                ownership == null ||
                !ShouldGuardProviderOnlyZeroUnlocks(providerKey))
            {
                return true;
            }

            await limiter.DelayBeforeNextAsync(cancel).ConfigureAwait(false);
            var scrapeResult = await limiter.ExecuteWithRetryAsync(
                () => friendsProvider.GetFriendGameAchievementsAsync(
                    friend,
                    ownership.ProviderGameKey,
                    ownership.AppId,
                    ownership.GameName,
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

            if (scrapeResult?.Success != true)
            {
                return true;
            }

            var achievements = scrapeResult.Data ?? new FriendGameAchievements();
            achievements.Friend = achievements.Friend ?? friend;
            achievements.AppId = achievements.AppId > 0 ? achievements.AppId : ownership.AppId;
            achievements.ProviderGameKey = string.IsNullOrWhiteSpace(achievements.ProviderGameKey)
                ? ownership.ProviderGameKey
                : achievements.ProviderGameKey;

            if (!HasAnyUnlockedFriendAchievements(achievements))
            {
                ClearProviderOnlyFriendGame(providerKey, friend.ExternalUserId, ownership.AppId, ownership.ProviderGameKey);
                return true;
            }

            var writeOwnership = _friendCache.SaveFriendOwnership(
                providerKey,
                friend.ExternalUserId,
                new[] { ownership },
                new FriendOwnershipSaveOptions { IncludeProviderOnlyGames = true });
            if (writeOwnership?.Success != true)
            {
                _logger?.Warn($"Failed to save provider-only friend ownership for {providerKey}/{friend.ExternalUserId}: {writeOwnership?.ErrorMessage}");
                return true;
            }

            lock (payloadLock)
            {
                payload.FriendSummary.OwnershipRowsWritten += writeOwnership.WrittenCount;
            }

            var writeAchievements = _friendCache.SaveFriendGameAchievements(
                providerKey,
                friend.ExternalUserId,
                ownership.ProviderGameKey,
                ownership.AppId,
                achievements);
            if (writeAchievements?.Success != true)
            {
                _logger?.Warn($"Failed to save probed provider-only friend achievements for {providerKey}/{friend.ExternalUserId}/{GetProviderGameCacheKey(ownership)}: {writeAchievements?.ErrorMessage}");
                return true;
            }

            lock (payloadLock)
            {
                payload.FriendSummary.CandidatesRefreshed++;
                payload.FriendSummary.AchievementsSaved++;
            }

            return true;
        }

        // Downloads friend avatars into the per-user icon cache and records the local path on each
        // identity so it is persisted (and displayed) instead of a remote URL. Avatars are named
        // stably per friend (provider + user id) rather than by URL hash.
        private async Task DownloadFriendAvatarsAsync(
            string providerKey,
            IReadOnlyList<FriendIdentity> friends,
            CancellationToken cancel,
            FriendRefreshProgressSession progress)
        {
            if (_imageService == null || friends == null || friends.Count == 0)
            {
                return;
            }

            var friendsWithAvatars = friends
                .Where(friend => friend != null && !string.IsNullOrWhiteSpace(friend.AvatarUrl))
                .ToList();
            if (friendsWithAvatars.Count == 0)
            {
                return;
            }

            // Because the avatar filename no longer changes when the source URL changes, compare the
            // incoming URL against the persisted one (single load per provider) so a friend's new
            // avatar is re-downloaded while unchanged avatars reuse the cached file.
            var persistedAvatarUrls = LoadPersistedAvatarUrls(providerKey);

            await Task.WhenAll(friendsWithAvatars
                    .Select(async friend =>
                    {
                        try
                        {
                            await DownloadFriendAvatarAsync(providerKey, friend, persistedAvatarUrls, cancel)
                                .ConfigureAwait(false);
                        }
                        finally
                        {
                            progress?.ReportFriendAvatarCompleted(GetFriendDisplayName(friend));
                        }
                    }))
                .ConfigureAwait(false);
        }

        private Dictionary<string, string> LoadPersistedAvatarUrls(string providerKey)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var persisted = _friendCache?.LoadFriendIdentities(providerKey);
                if (persisted != null)
                {
                    foreach (var identity in persisted)
                    {
                        if (identity != null && !string.IsNullOrWhiteSpace(identity.ExternalUserId))
                        {
                            map[identity.ExternalUserId] = identity.AvatarUrl;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"Failed to load persisted friend avatar URLs for {providerKey}.");
            }

            return map;
        }

        private async Task DownloadFriendAvatarAsync(
            string providerKey,
            FriendIdentity friend,
            Dictionary<string, string> persistedAvatarUrls,
            CancellationToken cancel)
        {
            if (friend == null || string.IsNullOrWhiteSpace(friend.AvatarUrl))
            {
                return;
            }

            try
            {
                var relativePath = FriendImageCachePathBuilder.BuildAvatarRelativePath(providerKey, friend.ExternalUserId);
                var targetPath = _imageService.ResolveCacheRelativePath(relativePath);

                var changed = !persistedAvatarUrls.TryGetValue(friend.ExternalUserId ?? string.Empty, out var previousUrl) ||
                    !string.Equals(previousUrl, friend.AvatarUrl, StringComparison.OrdinalIgnoreCase);

                var path = await _imageService
                    .GetOrDownloadIconToPathAsync(friend.AvatarUrl, targetPath, ImageDecodeSize, cancel, overwriteExistingTarget: changed)
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
            CancellationToken cancel,
            FriendRefreshProgressSession progress)
        {
            if (_imageService == null || definition?.Achievements == null || definition.Achievements.Count == 0)
            {
                return;
            }

            var decodeSize = ResolveAchievementIconDecodeSize();
            var iconAttempts = definition.Achievements
                .Where(achievement => achievement != null)
                .Sum(CountAchievementIconDownloadAttempts);
            if (iconAttempts <= 0)
            {
                return;
            }

            var completed = 0;
            progress?.ReportAchievementImages(0, iconAttempts, definition.GameName);
            void ReportIconCompleted()
            {
                var count = Interlocked.Increment(ref completed);
                progress?.ReportAchievementImages(count, iconAttempts, definition.GameName);
            }

            // Stable, collision-safe filename stems keyed by achievement ApiName, so a game's icons
            // land at friendgames/{provider}/{gameKey}/{stem}.png and are shared across every friend
            // that owns the game (never re-downloaded once present).
            var stems = AchievementIconCachePathBuilder.BuildFileStems(
                definition.Achievements.Where(achievement => achievement != null).Select(achievement => achievement.ApiName));

            await Task.WhenAll(definition.Achievements
                    .Where(achievement => achievement != null)
                    .Select(achievement => DownloadAchievementIconsAsync(
                        definition,
                        achievement,
                        stems,
                        decodeSize,
                        ReportIconCompleted,
                        cancel)))
                .ConfigureAwait(false);
        }

        private async Task DownloadAchievementIconsAsync(
            FriendGameDefinition definition,
            AchievementDetail achievement,
            IReadOnlyDictionary<string, string> stems,
            int decodeSize,
            Action reportIconCompleted,
            CancellationToken cancel)
        {
            var unlockedSource = achievement.UnlockedIconPath;
            var lockedSource = achievement.LockedIconPath;
            var stem = ResolveAchievementStem(stems, achievement.ApiName);

            var unlocked = await DownloadAchievementIconAsync(
                definition, unlockedSource, stem, AchievementIconVariant.Unlocked, decodeSize, reportIconCompleted, cancel).ConfigureAwait(false);
            achievement.UnlockedIconPath = unlocked;

            // Providers like Exophase use the same image for the locked and unlocked states. Reuse the
            // already-downloaded result instead of fetching (and warming up) the identical URL a second
            // time, which otherwise doubles the per-achievement download cost.
            achievement.LockedIconPath = string.Equals(lockedSource, unlockedSource, StringComparison.OrdinalIgnoreCase)
                ? unlocked
                : await DownloadAchievementIconAsync(
                    definition, lockedSource, stem, AchievementIconVariant.Locked, decodeSize, reportIconCompleted, cancel).ConfigureAwait(false);
        }

        private static string ResolveAchievementStem(IReadOnlyDictionary<string, string> stems, string apiName)
        {
            var key = apiName?.Trim();
            return !string.IsNullOrEmpty(key) && stems != null && stems.TryGetValue(key, out var stem)
                ? stem
                : null;
        }

        internal static int CountAchievementIconDownloadAttempts(AchievementDetail achievement)
        {
            if (achievement == null)
            {
                return 0;
            }

            var count = IsHttpImageUrl(achievement.UnlockedIconPath) ? 1 : 0;
            if (IsHttpImageUrl(achievement.LockedIconPath) &&
                !string.Equals(achievement.LockedIconPath, achievement.UnlockedIconPath, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }

            return count;
        }

        private static bool IsHttpImageUrl(string url)
        {
            return !string.IsNullOrWhiteSpace(url) &&
                   url.StartsWith("http", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<string> DownloadAchievementIconAsync(
            FriendGameDefinition definition,
            string url,
            string stem,
            AchievementIconVariant variant,
            int decodeSize,
            Action reportIconCompleted,
            CancellationToken cancel)
        {
            var shouldReport = IsHttpImageUrl(url);
            if (!shouldReport)
            {
                return url;
            }

            try
            {
                var fileName = FriendImageCachePathBuilder.GetAchievementFileName(stem, variant);
                var relativePath = FriendImageCachePathBuilder.BuildGameImageRelativePath(
                    definition.ProviderKey, definition.ProviderGameKey, fileName);
                var targetPath = _imageService.ResolveCacheRelativePath(relativePath);

                var path = await _imageService
                    .GetOrDownloadIconToPathAsync(url, targetPath, decodeSize, cancel)
                    .ConfigureAwait(false);
                return string.IsNullOrWhiteSpace(path) ? url : path;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Failed to cache unowned achievement icon.");
                return url;
            }
            finally
            {
                reportIconCompleted?.Invoke();
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
            CancellationToken cancel,
            FriendRefreshProgressSession progress)
        {
            if (_imageService == null || providerGameKeys == null || providerGameKeys.Count == 0)
            {
                return;
            }

            var imageAttempts = CountUnownedGameImageDownloadAttempts(providerGameKeys, ownershipByKey);
            if (imageAttempts <= 0)
            {
                return;
            }

            var completed = 0;
            progress?.ReportFriendGameImages(0, imageAttempts);
            void ReportImageCompleted(string gameName)
            {
                var count = Interlocked.Increment(ref completed);
                progress?.ReportFriendGameImages(count, imageAttempts, gameName);
            }

            await Task.WhenAll(providerGameKeys.Select(providerGameKey =>
                    DownloadUnownedGameImageAsync(providerKey, providerGameKey, ownershipByKey, ReportImageCompleted, cancel)))
                .ConfigureAwait(false);
        }

        private static int CountUnownedGameImageDownloadAttempts(
            IEnumerable<string> providerGameKeys,
            Dictionary<string, List<FriendGameOwnership>> ownershipByKey)
        {
            if (providerGameKeys == null || ownershipByKey == null)
            {
                return 0;
            }

            var count = 0;
            foreach (var providerGameKey in providerGameKeys)
            {
                if (string.IsNullOrWhiteSpace(providerGameKey) ||
                    !ownershipByKey.TryGetValue(providerGameKey, out var owners))
                {
                    continue;
                }

                var source = owners?.FirstOrDefault(item => item != null);
                if (source == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(source.IconUrl))
                {
                    count++;
                }

                if (!string.IsNullOrWhiteSpace(source.CoverUrl))
                {
                    count++;
                }
            }

            return count;
        }

        // Downloads a friend game's achievements-page header banner and stores it as the game's local
        // icon and cover paths. Only local paths are persisted (never the URL), matching how Steam
        // friend games cache their images. Scoped to unmatched games by SaveProviderGameImagePaths.
        private async Task DownloadDefinitionGameImageAsync(
            string providerKey,
            string providerGameKey,
            int appId,
            string bannerUrl,
            string gameName,
            CancellationToken cancel,
            FriendRefreshProgressSession progress)
        {
            if (_imageService == null || string.IsNullOrWhiteSpace(bannerUrl))
            {
                return;
            }

            var detail = !string.IsNullOrWhiteSpace(gameName) ? gameName : providerGameKey;
            progress?.ReportFriendGameImages(0, 1, detail);
            try
            {
                var localPath = await DownloadGameImageAsync(
                    providerKey, providerGameKey, bannerUrl, FriendImageCachePathBuilder.GameIconFileName, () => progress?.ReportFriendGameImages(1, 1, detail), cancel)
                    .ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(localPath))
                {
                    return;
                }

                _friendCache.SaveProviderGameImagePaths(providerKey, providerGameKey, appId, localPath, localPath);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"Failed to cache friend game header image for {providerKey}/{providerGameKey}.");
            }
        }

        private async Task DownloadUnownedGameImageAsync(
            string providerKey,
            string providerGameKey,
            Dictionary<string, List<FriendGameOwnership>> ownershipByKey,
            Action<string> reportImageCompleted,
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

            var gameKey = source.ProviderGameKey ?? providerGameKey;
            var gameName = !string.IsNullOrWhiteSpace(source.GameName) ? source.GameName : gameKey;
            try
            {
                var iconPath = await DownloadGameImageAsync(
                    providerKey, gameKey, source.IconUrl, FriendImageCachePathBuilder.GameIconFileName, () => reportImageCompleted?.Invoke(gameName), cancel).ConfigureAwait(false);
                var coverPath = await DownloadGameImageAsync(
                    providerKey, gameKey, source.CoverUrl, FriendImageCachePathBuilder.GameCoverFileName, () => reportImageCompleted?.Invoke(gameName), cancel).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(iconPath) && string.IsNullOrWhiteSpace(coverPath))
                {
                    return;
                }

                _friendCache.SaveProviderGameImagePaths(providerKey, gameKey, source.AppId, iconPath, coverPath);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"Failed to cache unowned game images for {providerKey}/{providerGameKey}.");
            }
        }

        // Downloads a friend game's cover/icon art into the stable friendgames/{provider}/{gameKey}/
        // folder. Shared across every friend that owns the game and reused (not re-downloaded) once
        // present, because these images are keyed by game rather than by source URL.
        private async Task<string> DownloadGameImageAsync(
            string providerKey,
            string gameKey,
            string url,
            string fileName,
            Action reportImageCompleted,
            CancellationToken cancel)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            try
            {
                var relativePath = FriendImageCachePathBuilder.BuildGameImageRelativePath(providerKey, gameKey, fileName);
                var targetPath = _imageService.ResolveCacheRelativePath(relativePath);
                return await _imageService
                    .GetOrDownloadIconToPathAsync(url, targetPath, ImageDecodeSize, cancel)
                    .ConfigureAwait(false);
            }
            finally
            {
                reportImageCompleted?.Invoke();
            }
        }

        private async Task RefreshAchievementsAsync(
            IFriendsProvider friendsProvider,
            string providerKey,
            IReadOnlyList<FriendRefreshCandidate> candidates,
            RebuildPayload payload,
            object payloadLock,
            FriendRefreshProgressSession progress,
            int maxDegreeOfParallelism,
            CancellationToken cancel)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return;
            }

            progress?.ReportAchievementGameChecks(0, candidates.Count);

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
                        payload,
                        payloadLock,
                        delayBeforeRequest: true,
                        limiter,
                        cancel).ConfigureAwait(false);
                    progress?.ReportAchievementGameChecks(i + 1, candidates.Count, FormatFriendGameDetail(candidates[i]));
                    if (!shouldContinue)
                    {
                        break;
                    }
                }

                return;
            }

            using (var authCts = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                var completed = 0;
                try
                {
                    await RunBoundedAsync(
                        candidates,
                        maxDegreeOfParallelism,
                        async (candidate, _, token) =>
                        {
                            var limiter = CreateScanRateLimiter();
                            var shouldContinue = await RefreshAchievementCandidateAsync(
                                friendsProvider,
                                providerKey,
                                candidate,
                                payload,
                                payloadLock,
                                delayBeforeRequest: false,
                                limiter,
                                token).ConfigureAwait(false);
                            var progressCompleted = Interlocked.Increment(ref completed);
                            progress?.ReportAchievementGameChecks(progressCompleted, candidates.Count, FormatFriendGameDetail(candidate));
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
            RebuildPayload payload,
            object payloadLock,
            bool delayBeforeRequest,
            RateLimiter limiter,
            CancellationToken cancel)
        {
            if (candidate?.Friend == null || !HasProviderGameIdentity(candidate.AppId, candidate.ProviderGameKey))
            {
                return true;
            }

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
            if (ShouldSkipProviderOnlyZeroUnlocks(providerKey, candidate, scrapeResult, achievements))
            {
                ClearProviderOnlyFriendGame(providerKey, candidate.Friend.ExternalUserId, candidate.AppId, candidate.ProviderGameKey);
                lock (payloadLock)
                {
                    payload.FriendSummary.CandidatesRefreshed++;
                }

                return true;
            }

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

        private static int CountFriendAvatarDownloads(IReadOnlyList<FriendIdentity> friends)
        {
            return (friends ?? Array.Empty<FriendIdentity>())
                .Count(friend => friend != null && !string.IsNullOrWhiteSpace(friend.AvatarUrl));
        }

        private List<LogicalFriendRefreshGroup> BuildLogicalFriendGroups(
            IReadOnlyList<FriendProviderRefreshContext> contexts)
        {
            var groups = new Dictionary<string, LogicalFriendRefreshGroup>(StringComparer.OrdinalIgnoreCase);
            var accountLookup = new Dictionary<string, FriendIdentity>(StringComparer.OrdinalIgnoreCase);
            foreach (var context in contexts ?? Array.Empty<FriendProviderRefreshContext>())
            {
                foreach (var friend in (IEnumerable<FriendIdentity>)context?.ScopedFriends ?? Array.Empty<FriendIdentity>())
                {
                    var accountKey = FriendAccountRef.BuildKey(context.ProviderKey, friend?.ExternalUserId);
                    if (!string.IsNullOrWhiteSpace(accountKey) && !accountLookup.ContainsKey(accountKey))
                    {
                        accountLookup[accountKey] = friend;
                    }
                }
            }

            foreach (var context in contexts ?? Array.Empty<FriendProviderRefreshContext>())
            {
                foreach (var friend in (IEnumerable<FriendIdentity>)context?.ScopedFriends ?? Array.Empty<FriendIdentity>())
                {
                    if (friend == null || string.IsNullOrWhiteSpace(friend.ExternalUserId))
                    {
                        continue;
                    }

                    var mergeGroup = _settings?.Persisted?.GetFriendMergeGroupForAccount(context.ProviderKey, friend.ExternalUserId);
                    var groupKey = !string.IsNullOrWhiteSpace(mergeGroup?.Id)
                        ? "merged|" + mergeGroup.Id
                        : "account|" + FriendAccountRef.BuildKey(context.ProviderKey, friend.ExternalUserId);
                    if (string.IsNullOrWhiteSpace(groupKey))
                    {
                        continue;
                    }

                    if (!groups.TryGetValue(groupKey, out var group))
                    {
                        group = new LogicalFriendRefreshGroup
                        {
                            Key = groupKey,
                            DisplayName = ResolveLogicalFriendDisplayName(mergeGroup, friend, accountLookup)
                        };
                        groups[groupKey] = group;
                    }

                    group.Accounts.Add(new FriendAccountRefreshItem
                    {
                        Context = context,
                        Friend = friend
                    });
                }
            }

            return groups.Values
                .OrderBy(group => group.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private static string ResolveLogicalFriendDisplayName(
            FriendMergeGroup mergeGroup,
            FriendIdentity fallbackFriend,
            IReadOnlyDictionary<string, FriendIdentity> accountLookup)
        {
            if (!string.IsNullOrWhiteSpace(mergeGroup?.Nickname))
            {
                return mergeGroup.Nickname.Trim();
            }

            foreach (var member in mergeGroup?.Members ?? Enumerable.Empty<FriendAccountRef>())
            {
                if (!string.IsNullOrWhiteSpace(member?.Key) &&
                    accountLookup != null &&
                    accountLookup.TryGetValue(member.Key, out var friend) &&
                    !string.IsNullOrWhiteSpace(friend?.DisplayName))
                {
                    return friend.DisplayName.Trim();
                }
            }

            return GetFriendDisplayName(fallbackFriend);
        }

        private static IReadOnlyList<FriendIdentity> NormalizeProviderFriendIdentities(
            string providerKey,
            IReadOnlyList<FriendIdentity> friends)
        {
            return (friends ?? Array.Empty<FriendIdentity>())
                .Where(friend => !string.IsNullOrWhiteSpace(friend?.ExternalUserId))
                .Select(friend => new FriendIdentity
                {
                    ProviderKey = string.IsNullOrWhiteSpace(friend.ProviderKey)
                        ? providerKey
                        : friend.ProviderKey.Trim(),
                    ExternalUserId = friend.ExternalUserId.Trim(),
                    DisplayName = string.IsNullOrWhiteSpace(friend.DisplayName)
                        ? friend.ExternalUserId.Trim()
                        : friend.DisplayName.Trim(),
                    AvatarUrl = string.IsNullOrWhiteSpace(friend.AvatarUrl) ? null : friend.AvatarUrl.Trim(),
                    AvatarPath = string.IsNullOrWhiteSpace(friend.AvatarPath) ? null : friend.AvatarPath.Trim(),
                    LastRefreshedUtc = friend.LastRefreshedUtc
                })
                .GroupBy(friend => friend.ExternalUserId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        private void MergeProviderFriendsIntoSettings(
            string providerKey,
            IReadOnlyList<FriendIdentity> friends)
        {
            var persisted = _settings?.Persisted;
            if (persisted == null || friends == null || friends.Count == 0)
            {
                return;
            }

            var changed = false;
            foreach (var friend in friends)
            {
                if (friend == null || string.IsNullOrWhiteSpace(friend.ExternalUserId))
                {
                    continue;
                }

                friend.ProviderKey = string.IsNullOrWhiteSpace(friend.ProviderKey)
                    ? providerKey
                    : friend.ProviderKey.Trim();

                var existing = persisted.GetFriendSetting(friend.ProviderKey, friend.ExternalUserId);
                var beforeName = existing?.DisplayName;
                var beforeAvatarUrl = existing?.AvatarUrl;
                var beforeAvatarPath = existing?.AvatarPath;
                var beforeRefresh = existing?.LastRefreshedUtc;

                persisted.AddOrUpdateFriend(friend, FriendSettingsSource.AutoDiscovered);
                var updated = persisted.GetFriendSetting(friend.ProviderKey, friend.ExternalUserId);
                changed |= existing == null ||
                           !string.Equals(beforeName, updated?.DisplayName, StringComparison.Ordinal) ||
                           !string.Equals(beforeAvatarUrl, updated?.AvatarUrl, StringComparison.Ordinal) ||
                           !string.Equals(beforeAvatarPath, updated?.AvatarPath, StringComparison.Ordinal) ||
                           beforeRefresh != updated?.LastRefreshedUtc;
            }

            if (!changed)
            {
                return;
            }

            try
            {
                _settings?._plugin?.SavePluginSettings(_settings);
                PlayniteAchievementsPlugin.NotifySettingsSaved();
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"Failed to persist central friend settings after {providerKey} roster merge.");
            }
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
            return _settings?.Persisted?.GetIgnoredFriendIds(providerKey) ??
                   new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private HashSet<string> GetFullLibraryFriendIds(string providerKey)
        {
            if (_fullLibraryIdsResolver != null)
            {
                return _fullLibraryIdsResolver(providerKey) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            return _settings?.Persisted?.GetFullLibraryFriendIds(providerKey) ??
                   new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private static bool FriendUsesFullLibrary(HashSet<string> fullLibraryIds, FriendIdentity friend)
        {
            return friend != null &&
                   fullLibraryIds != null &&
                   fullLibraryIds.Contains(friend.ExternalUserId?.Trim() ?? string.Empty);
        }

        private bool IsPlayniteLibraryFriendGame(string providerKey, FriendGameOwnership ownership)
        {
            if (ownership?.PlayniteGameId.HasValue == true &&
                ownership.PlayniteGameId.Value != Guid.Empty)
            {
                return true;
            }

            return _friendCache?.IsProviderGameMappedToPlayniteLibrary(
                providerKey,
                ownership?.AppId ?? 0,
                ownership?.ProviderGameKey) == true;
        }

        private bool IsPlayniteLibraryFriendGame(string providerKey, FriendRefreshCandidate candidate)
        {
            if (candidate?.PlayniteGameId.HasValue == true &&
                candidate.PlayniteGameId.Value != Guid.Empty)
            {
                return true;
            }

            return _friendCache?.IsProviderGameMappedToPlayniteLibrary(
                providerKey,
                candidate?.AppId ?? 0,
                candidate?.ProviderGameKey) == true;
        }

        private bool ShouldSkipProviderOnlyZeroUnlocks(
            string providerKey,
            FriendRefreshCandidate candidate,
            FriendsProviderResult<FriendGameAchievements> scrapeResult,
            FriendGameAchievements achievements)
        {
            return ShouldGuardProviderOnlyZeroUnlocks(providerKey) &&
                   scrapeResult?.Success == true &&
                   candidate?.Friend != null &&
                   HasProviderGameIdentity(candidate.AppId, candidate.ProviderGameKey) &&
                   !IsPlayniteLibraryFriendGame(providerKey, candidate) &&
                   !HasAnyUnlockedFriendAchievements(achievements);
        }

        private void ClearProviderOnlyFriendGame(string providerKey, string externalUserId, int appId, string providerGameKey)
        {
            var clearResult = _friendCache?.ClearFriendProviderOnlyGame(providerKey, externalUserId, appId, providerGameKey);
            if (clearResult?.Success == false)
            {
                _logger?.Warn($"Failed to clear stale provider-only friend game for {providerKey}/{externalUserId}/{GetProviderGameCacheKey(appId, providerGameKey)}: {clearResult.ErrorMessage}");
            }
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

        private static bool ShouldRefreshOwnership(string providerKey, FriendRefreshOptions options)
        {
            if (options == null)
            {
                return false;
            }

            if (RequiresOwnershipMapping(providerKey))
            {
                return true;
            }

            return options.Scope == FriendRefreshScope.Shared ||
                   options.Scope == FriendRefreshScope.Recent ||
                   options.Scope == FriendRefreshScope.Custom ||
                   (options.Scope == FriendRefreshScope.Full &&
                    options.IncludesProviderOnlyGames());
        }

        private static bool RequiresOwnershipMapping(string providerKey)
        {
            return string.Equals(providerKey, "Exophase", StringComparison.OrdinalIgnoreCase);
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

        private static bool ShouldGuardProviderOnlyZeroUnlocks(string providerKey)
        {
            return string.Equals(providerKey, "Steam", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(providerKey, "Exophase", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasAnyUnlockedFriendAchievements(FriendGameAchievements achievements)
        {
            return achievements?.Rows?.Any(row => row?.Unlocked == true) == true;
        }

        private static string GetFriendDisplayName(FriendIdentity friend)
        {
            if (!string.IsNullOrWhiteSpace(friend?.DisplayName))
            {
                return friend.DisplayName.Trim();
            }

            return friend?.ExternalUserId?.Trim();
        }

        private static string FormatFriendGameDetail(FriendRefreshCandidate candidate)
        {
            if (candidate == null)
            {
                return null;
            }

            var friendName = GetFriendDisplayName(candidate.Friend);
            var gameName = !string.IsNullOrWhiteSpace(candidate.GameName)
                ? candidate.GameName.Trim()
                : GetProviderGameCacheKey(candidate);

            if (string.IsNullOrWhiteSpace(friendName))
            {
                return gameName;
            }

            if (string.IsNullOrWhiteSpace(gameName))
            {
                return friendName;
            }

            return $"{friendName} - {gameName}";
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

        private sealed class FriendProviderRefreshContext
        {
            public IFriendsProvider Provider { get; set; }
            public string ProviderKey { get; set; }
            public FriendsRefreshPreparation Preparation { get; set; } = new FriendsRefreshPreparation();
            public List<FriendIdentity> Friends { get; set; } = new List<FriendIdentity>();
            public List<FriendIdentity> ScopedFriends { get; set; } = new List<FriendIdentity>();
            public HashSet<string> FullLibraryIds { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public bool DiscoverUnowned { get; set; }
            public bool CanContinue { get; set; }
            public int MaxDegreeOfParallelism { get; set; } = 1;
            public List<FriendOwnershipSnapshot> OwnershipSnapshots { get; set; }
        }

        private sealed class LogicalFriendRefreshGroup
        {
            public string Key { get; set; }
            public string DisplayName { get; set; }
            public List<FriendAccountRefreshItem> Accounts { get; } = new List<FriendAccountRefreshItem>();
        }

        private sealed class FriendAccountRefreshItem
        {
            public FriendProviderRefreshContext Context { get; set; }
            public FriendIdentity Friend { get; set; }
        }

        private sealed class FriendAchievementWorkItem
        {
            public FriendProviderRefreshContext Context { get; set; }
            public FriendRefreshCandidate Candidate { get; set; }
        }
    }

    internal sealed class FriendRefreshProgressSession
    {
        internal const int TotalUnits = 10000;
        private const int MaxReportUnits = TotalUnits - 1;
        private const int RosterStart = 0;
        private const int RosterEnd = 1000;
        private const int AvatarStart = 1000;
        private const int AvatarEnd = 1800;
        private const int LibraryStart = 1800;
        private const int LibraryEnd = 4200;
        private const int GameStart = 4200;
        private const int GameEnd = 9900;

        private readonly Action<string, int, int> _reportProgress;
        private readonly object _sync = new object();
        private int _lastReportedUnits;
        private int _providerTotal = 1;
        private int _providersCompleted;
        private int _avatarTotal;
        private int _avatarsCompleted;
        private int _libraryTotal;
        private int _librariesCompleted;
        private int _gameTotal;
        private int _gamesCompleted;

        public FriendRefreshProgressSession(Action<string, int, int> reportProgress)
        {
            _reportProgress = reportProgress;
        }

        public FriendRefreshProgressSession(Action<string, int, int> reportProgress, int providerCount)
            : this(reportProgress)
        {
            InitializeProviderTotal(providerCount);
        }

        public void InitializeProviderTotal(int total)
        {
            lock (_sync)
            {
                _providerTotal = Math.Max(1, total);
                _providersCompleted = 0;
            }
        }

        public void ReportLoadingFriends()
        {
            ReportLoadingFriends(null, 0, _providerTotal);
        }

        public void ReportLoadingFriends(string providerKey, int providerIndex, int providerTotal)
        {
            var total = Math.Max(1, providerTotal);
            var current = Math.Max(0, Math.Min(providerIndex, total - 1));
            var message = Format(
                "LOCPlayAch_FriendsRefresh_Progress_LoadingFriends",
                "Loading friends from {0} ({1}/{2})...",
                string.IsNullOrWhiteSpace(providerKey) ? "provider" : providerKey.Trim(),
                current + 1,
                total);
            ReportCountAt(RosterStart, RosterEnd, current, total, message);
        }

        public void ReportProviderRosterLoaded(string providerKey)
        {
            int current;
            int total;
            lock (_sync)
            {
                total = Math.Max(1, _providerTotal);
                _providersCompleted = Math.Max(0, Math.Min(total, _providersCompleted + 1));
                current = _providersCompleted;
            }

            var message = Format(
                "LOCPlayAch_FriendsRefresh_Progress_LoadingFriends",
                "Loading friends from {0} ({1}/{2})...",
                string.IsNullOrWhiteSpace(providerKey) ? "provider" : providerKey.Trim(),
                current,
                total);
            ReportCountAt(RosterStart, RosterEnd, current, total, message);
        }

        public void InitializeFriendAvatarTotal(int total)
        {
            lock (_sync)
            {
                _avatarTotal = Math.Max(0, total);
                _avatarsCompleted = 0;
            }
        }

        public void ReportFriendAvatarCompleted(string friendName = null)
        {
            int current;
            int total;
            lock (_sync)
            {
                total = Math.Max(1, _avatarTotal);
                _avatarsCompleted = Math.Max(0, Math.Min(total, _avatarsCompleted + 1));
                current = _avatarsCompleted;
            }

            ReportCount(
                AvatarStart,
                AvatarEnd,
                current,
                total,
                friendName,
                "LOCPlayAch_FriendsRefresh_Progress_Avatars",
                "Downloading friend avatars {0}/{1}...",
                "LOCPlayAch_FriendsRefresh_Progress_AvatarsNamed",
                "Downloading friend avatars {0}/{1}: {2}");
        }

        public void ReportFriendAvatars(int completed, int total, string friendName = null)
        {
            ReportCount(
                AvatarStart,
                AvatarEnd,
                completed,
                total,
                friendName,
                "LOCPlayAch_FriendsRefresh_Progress_Avatars",
                "Downloading friend avatars {0}/{1}...",
                "LOCPlayAch_FriendsRefresh_Progress_AvatarsNamed",
                "Downloading friend avatars {0}/{1}: {2}");
        }

        public void InitializeFriendLibraryTotal(int total)
        {
            lock (_sync)
            {
                _libraryTotal = Math.Max(0, total);
                _librariesCompleted = 0;
            }
        }

        public void ReportFriendLibraryCompleted(string friendName = null)
        {
            int current;
            int total;
            lock (_sync)
            {
                total = Math.Max(1, _libraryTotal);
                _librariesCompleted = Math.Max(0, Math.Min(total, _librariesCompleted + 1));
                current = _librariesCompleted;
            }

            ReportCount(
                LibraryStart,
                LibraryEnd,
                current,
                total,
                friendName,
                "LOCPlayAch_FriendsRefresh_Progress_Libraries",
                "Refreshing friend libraries {0}/{1}...",
                "LOCPlayAch_FriendsRefresh_Progress_LibrariesNamed",
                "Refreshing friend libraries {0}/{1}: {2}");
        }

        public void ReportFriendLibraries(int completed, int total, string friendName = null)
        {
            ReportCount(
                LibraryStart,
                LibraryEnd,
                completed,
                total,
                friendName,
                "LOCPlayAch_FriendsRefresh_Progress_Libraries",
                "Refreshing friend libraries {0}/{1}...",
                "LOCPlayAch_FriendsRefresh_Progress_LibrariesNamed",
                "Refreshing friend libraries {0}/{1}: {2}");
        }

        public void AddGameChecksTotal(int total)
        {
            if (total <= 0)
            {
                return;
            }

            lock (_sync)
            {
                _gameTotal = Math.Max(0, _gameTotal + total);
            }
        }

        public void ReportGameCheckActive(string detail = null)
        {
            int current;
            int total;
            lock (_sync)
            {
                current = Math.Max(0, _gamesCompleted);
                total = Math.Max(1, _gameTotal);
            }

            ReportGameChecks(current, total, detail);
        }

        public void ReportGameCheckCompleted(string detail = null)
        {
            int current;
            int total;
            lock (_sync)
            {
                total = Math.Max(1, _gameTotal);
                _gamesCompleted = Math.Max(0, Math.Min(total, _gamesCompleted + 1));
                current = _gamesCompleted;
            }

            ReportGameChecks(current, total, detail);
        }

        public void ReportDiscoveryGameChecks(int completed, int total, string gameName = null)
        {
            ReportGameChecks(completed, total, gameName);
        }

        public void ReportAchievementGameChecks(int completed, int total, string friendGameName = null)
        {
            ReportGameChecks(completed, total, friendGameName);
        }

        public void ReportAchievementImages(int completed, int total, string gameName = null)
        {
            ReportImageProgress(
                completed,
                total,
                gameName,
                "LOCPlayAch_FriendsRefresh_Progress_AchievementImages",
                "Downloading achievement images {0}/{1}...",
                "LOCPlayAch_FriendsRefresh_Progress_AchievementImagesNamed",
                "Downloading achievement images {0}/{1}: {2}");
        }

        public void ReportFriendGameImages(int completed, int total, string gameName = null)
        {
            ReportImageProgress(
                completed,
                total,
                gameName,
                "LOCPlayAch_FriendsRefresh_Progress_GameImages",
                "Downloading friend game images {0}/{1}...",
                "LOCPlayAch_FriendsRefresh_Progress_GameImagesNamed",
                "Downloading friend game images {0}/{1}: {2}");
        }

        public void CompleteProvider()
        {
        }

        private void ReportGameChecks(int completed, int total, string detail)
        {
            ReportCount(
                GameStart,
                GameEnd,
                completed,
                total,
                detail,
                "LOCPlayAch_FriendsRefresh_Progress_GameChecks",
                "Checking friend games {0}/{1}...",
                "LOCPlayAch_FriendsRefresh_Progress_GameChecksNamed",
                "Checking friend games {0}/{1}: {2}");
        }

        private void ReportImageProgress(
            int completed,
            int total,
            string detail,
            string resourceKey,
            string fallback,
            string namedResourceKey,
            string namedFallback)
        {
            total = Math.Max(1, total);
            completed = Math.Max(0, Math.Min(completed, total));
            int gameCompleted;
            int gameTotal;
            lock (_sync)
            {
                gameCompleted = Math.Max(0, _gamesCompleted);
                gameTotal = Math.Max(1, _gameTotal);
            }

            var fractional = completed <= 0
                ? 0d
                : Math.Min(0.85d, completed / (double)total * 0.85d);
            var effectiveCompleted = Math.Min(gameTotal, gameCompleted + fractional);
            var units = GameStart + (int)((GameEnd - GameStart) * effectiveCompleted / gameTotal);
            var message = FormatCount(resourceKey, fallback, namedResourceKey, namedFallback, completed, total, detail);
            ReportAt(units, message);
        }

        private void ReportCount(
            int start,
            int end,
            int completed,
            int total,
            string detail,
            string resourceKey,
            string fallback,
            string namedResourceKey,
            string namedFallback)
        {
            total = Math.Max(1, total);
            completed = Math.Max(0, Math.Min(completed, total));
            var localUnits = start + (int)((long)(end - start) * completed / total);
            var message = FormatCount(resourceKey, fallback, namedResourceKey, namedFallback, completed, total, detail);
            ReportAt(localUnits, message);
        }

        private void ReportCountAt(int start, int end, int completed, int total, string message)
        {
            total = Math.Max(1, total);
            completed = Math.Max(0, Math.Min(completed, total));
            var localUnits = start + (int)((long)(end - start) * completed / total);
            ReportAt(localUnits, message);
        }

        private void ReportAt(int localUnits, string message)
        {
            if (_reportProgress == null)
            {
                return;
            }

            var globalUnits = Math.Max(0, Math.Min(MaxReportUnits, localUnits));
            lock (_sync)
            {
                if (globalUnits < _lastReportedUnits)
                {
                    globalUnits = _lastReportedUnits;
                }
                else
                {
                    _lastReportedUnits = globalUnits;
                }
            }

            _reportProgress(message, globalUnits, TotalUnits);
        }

        private static string FormatCount(
            string resourceKey,
            string fallback,
            string namedResourceKey,
            string namedFallback,
            int current,
            int total,
            string detail)
        {
            if (!string.IsNullOrWhiteSpace(detail))
            {
                return Format(namedResourceKey, namedFallback, current, total, detail.Trim());
            }

            return Format(resourceKey, fallback, current, total);
        }

        private static string Format(string resourceKey, string fallback, params object[] args)
        {
            var format = ResourceProvider.GetString(resourceKey);
            if (string.IsNullOrWhiteSpace(format) ||
                (format.StartsWith("<!", StringComparison.Ordinal) && format.EndsWith("!>", StringComparison.Ordinal)))
            {
                format = fallback;
            }

            return string.Format(format, args ?? Array.Empty<object>());
        }
    }
}
