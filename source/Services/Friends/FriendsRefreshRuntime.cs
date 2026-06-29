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

            if (_settings?.Persisted?.EnableFriendsOverview != true)
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
                        reportProgress,
                        cancel).ConfigureAwait(false);
                }

                var candidates = _friendCache.LoadFriendRefreshCandidates(providerKey, options) ??
                                 new List<FriendRefreshCandidate>();
                var limiter = new RateLimiter(Math.Max(0, _settings?.Persisted?.ScanDelayMs ?? 200), Math.Max(0, _settings?.Persisted?.MaxRetryAttempts ?? 3));

                payload.FriendSummary.CandidatesLoaded += candidates.Count;
                _logger?.Debug(
                    $"Loaded {providerKey} friend achievement scrape candidates: candidates={candidates.Count}, scope={options.Scope}.");

                if (!preparation.CanRefreshAchievements)
                {
                    _logger?.Debug($"Skipping {providerKey} friend achievement scrapes: provider did not prepare achievement auth.");
                    MarkAuthFailure(payload, providerKey, true);
                    return payload;
                }

                for (var i = 0; i < candidates.Count; i++)
                {
                    cancel.ThrowIfCancellationRequested();
                    var candidate = candidates[i];
                    if (candidate?.Friend == null || candidate.AppId <= 0)
                    {
                        continue;
                    }

                    Report(
                        reportProgress,
                        Format(
                            "LOCPlayAch_FriendsRefresh_Progress_Achievements",
                            "Refreshing friend achievements {0}/{1}...",
                            i + 1,
                            candidates.Count),
                        i,
                        candidates.Count + 1);

                    await limiter.DelayBeforeNextAsync(cancel).ConfigureAwait(false);

                    var scrapeResult = await friendsProvider.GetFriendGameAchievementsAsync(
                        candidate.Friend,
                        candidate.AppId,
                        candidate.GameName,
                        cancel).ConfigureAwait(false);

                    if (scrapeResult?.AuthRequired == true)
                    {
                        MarkAuthFailure(payload, providerKey, true);
                        break;
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
                        continue;
                    }

                    payload.FriendSummary.CandidatesRefreshed++;
                    payload.FriendSummary.AchievementsSaved++;
                }
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
            Action<string, int, int> reportProgress,
            CancellationToken cancel)
        {
            for (var i = 0; i < friends.Count; i++)
            {
                cancel.ThrowIfCancellationRequested();
                var friend = friends[i];
                if (friend == null || string.IsNullOrWhiteSpace(friend.ExternalUserId))
                {
                    continue;
                }

                Report(
                    reportProgress,
                    Format(
                        "LOCPlayAch_FriendsRefresh_Progress_Ownership",
                        "Refreshing friend libraries {0}/{1}...",
                        i + 1,
                        friends.Count),
                    i,
                    friends.Count + 1);

                var ownershipResult = await friendsProvider.GetOwnedGamesAsync(friend, cancel).ConfigureAwait(false);
                if (ownershipResult?.Success != true)
                {
                    _logger?.Debug($"Friend ownership unavailable for {providerKey}/{friend.ExternalUserId}: {ownershipResult?.ErrorMessage}");
                    MarkAuthFailure(payload, providerKey, ownershipResult?.AuthRequired == true);
                    if (ownershipResult?.AuthRequired == true)
                    {
                        return;
                    }

                    continue;
                }

                payload.FriendSummary.OwnershipPagesRefreshed++;

                var ownedGames = ownershipResult.Data ?? Array.Empty<FriendGameOwnership>();
                var writeOwnership = _friendCache.SaveFriendOwnership(
                    providerKey,
                    friend.ExternalUserId,
                    ownedGames);
                if (writeOwnership?.Success != true)
                {
                    _logger?.Warn($"Failed to save friend ownership for {providerKey}/{friend.ExternalUserId}: {writeOwnership?.ErrorMessage}");
                    continue;
                }

                payload.FriendSummary.OwnershipRowsWritten += writeOwnership.WrittenCount;
                _logger?.Debug(
                    $"Saved friend ownership for {providerKey}/{friend.ExternalUserId}: " +
                    $"fetched={ownedGames.Count}, shared={writeOwnership.WrittenCount}, skippedUnshared={writeOwnership.SkippedCount}.");
            }
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
