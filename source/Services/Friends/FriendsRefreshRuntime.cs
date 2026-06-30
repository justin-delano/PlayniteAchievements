using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Friends;
using PlayniteAchievements.Providers;
using PlayniteAchievements.Providers.Steam.Models;
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

        private readonly IFriendCacheManager _friendCache;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly ILogger _logger;

        public FriendsRefreshRuntime(
            IReadOnlyList<IDataProvider> providers,
            IFriendCacheManager friendCache,
            ProviderRegistry providerRegistry,
            PlayniteAchievementsSettings settings,
            ILogger logger)
        {
            _friendCache = friendCache;
            _settings = settings;
            _logger = logger;
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

                var friends = friendsResult.Data ?? Array.Empty<FriendIdentity>();
                payload.FriendSummary.FriendsFetched += friends.Count;

                var writeFriends = _friendCache.SaveFriendList(providerKey, friends);
                if (writeFriends?.Success != true)
                {
                    _logger?.Warn($"Failed to save {providerKey} friend list: {writeFriends?.ErrorMessage}");
                    return payload;
                }

                payload.FriendSummary.FriendsSaved += writeFriends.WrittenCount;
                _logger?.Debug(
                    $"Saved {providerKey} friend list: fetched={friends.Count}, active={writeFriends.WrittenCount}, skipped={writeFriends.SkippedCount}.");

                if (ShouldRefreshOwnership(options.Scope))
                {
                    await RefreshOwnershipAsync(
                        friendsProvider,
                        providerKey,
                        friends,
                        payload,
                        payloadLock,
                        reportProgress,
                        maxDegreeOfParallelism,
                        cancel).ConfigureAwait(false);
                }

                var candidates = _friendCache.LoadFriendRefreshCandidates(providerKey, options) ??
                                 new List<FriendRefreshCandidate>();

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
            RebuildPayload payload,
            object payloadLock,
            Action<string, int, int> reportProgress,
            int maxDegreeOfParallelism,
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
                        i + 1,
                        friends.Count,
                        payload,
                        payloadLock,
                        reportProgress,
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
                                progressCurrent,
                                friends.Count,
                                payload,
                                payloadLock,
                                reportProgress,
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
            int progressCurrent,
            int total,
            RebuildPayload payload,
            object payloadLock,
            Action<string, int, int> reportProgress,
            CancellationToken cancel)
        {
            if (friend == null || string.IsNullOrWhiteSpace(friend.ExternalUserId))
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
            if (candidate?.Friend == null || candidate.AppId <= 0)
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
                candidate.AppId,
                achievements);
            if (writeAchievements?.Success != true)
            {
                _logger?.Warn($"Failed to save friend achievements for {providerKey}/{candidate.Friend.ExternalUserId}/{candidate.AppId}: {writeAchievements?.ErrorMessage}");
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

        private FriendRefreshOptions NormalizeOptions(FriendRefreshOptions options)
        {
            var normalized = options?.Clone() ?? new FriendRefreshOptions();
            if (!normalized.RefreshTtl.HasValue || normalized.RefreshTtl.Value <= TimeSpan.Zero)
            {
                normalized.RefreshTtl = TimeSpan.FromHours(Math.Max(1, _settings?.Persisted?.FriendsOverviewRefreshTtlHours ?? 24));
            }

            return normalized;
        }

        private static bool ShouldRefreshOwnership(FriendRefreshScope scope)
        {
            return scope == FriendRefreshScope.Shared ||
                   scope == FriendRefreshScope.Recent ||
                   scope == FriendRefreshScope.Custom;
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
                LastUpdatedUtc = DateTime.UtcNow,
                StatsUnavailable = true,
                TransientFailure = scrapeResult?.TransientFailure == true,
                DetailCode = SteamScrapeDetail.None
            };
        }
    }
}
