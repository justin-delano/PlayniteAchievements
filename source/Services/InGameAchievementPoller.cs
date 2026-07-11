using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Models;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Friends;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Providers;
using PlayniteAchievements.Services.Achievements;
using PlayniteAchievements.Services.Cache;
using PlayniteAchievements.Services.Friends;
using PlayniteAchievements.Services.Hydration;
using PlayniteAchievements.Services.Refresh;

namespace PlayniteAchievements.Services
{
    internal sealed class InGameAchievementPoller : IDisposable
    {
        private const int StartupDelaySeconds = 20;

        private sealed class FriendPollTarget
        {
            public string ProviderKey { get; set; }
            public FriendIdentity Friend { get; set; }
            public int AppId { get; set; }
            public string ProviderGameKey { get; set; }
            public Guid? PlayniteGameId { get; set; }
            public string GameName { get; set; }
        }

        private readonly IPlayniteAPI _api;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly ILogger _logger;
        private readonly ICacheManager _cacheManager;
        private readonly IFriendCacheManager _friendCache;
        private readonly RefreshRuntime _refreshRuntime;
        private readonly IReadOnlyList<IDataProvider> _providers;
        private readonly Func<RefreshRequest, RefreshExecutionPolicy, Task> _executeRefreshAsync;
        private readonly Action<AchievementUnlockedEventArgs> _notifyUnlocked;
        private readonly AchievementUnlockDiffer _differ;
        private readonly object _stateLock = new object();
        private readonly SemaphoreSlim _tickSemaphore = new SemaphoreSlim(1, 1);
        private readonly Dictionary<string, HashSet<string>> _toastedFriendKeys =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<FriendAchievementRow>> _friendBaselines =
            new Dictionary<string, List<FriendAchievementRow>>(StringComparer.OrdinalIgnoreCase);

        private CancellationTokenSource _cts;
        private Game _currentGame;
        private DateTime _sessionStartUtc;
        private int _friendCursor;
        private int _tickCount;
        private Task _loopTask;

        public InGameAchievementPoller(
            IPlayniteAPI api,
            PlayniteAchievementsSettings settings,
            ILogger logger,
            ICacheManager cacheManager,
            RefreshRuntime refreshRuntime,
            IReadOnlyList<IDataProvider> providers,
            Func<RefreshRequest, RefreshExecutionPolicy, Task> executeRefreshAsync,
            Action<AchievementUnlockedEventArgs> notifyUnlocked,
            AchievementUnlockDiffer differ = null)
        {
            _api = api;
            _settings = settings;
            _logger = logger;
            _cacheManager = cacheManager;
            _friendCache = cacheManager as IFriendCacheManager;
            _refreshRuntime = refreshRuntime;
            _providers = providers ?? Array.Empty<IDataProvider>();
            _executeRefreshAsync = executeRefreshAsync ?? throw new ArgumentNullException(nameof(executeRefreshAsync));
            _notifyUnlocked = notifyUnlocked;
            _differ = differ ?? new AchievementUnlockDiffer();
        }

        public Game CurrentGame
        {
            get
            {
                lock (_stateLock)
                {
                    return _currentGame;
                }
            }
        }

        public void Start(Game game)
        {
            if (game == null || game.Id == Guid.Empty)
            {
                return;
            }

            CancellationTokenSource oldCts = null;
            lock (_stateLock)
            {
                oldCts = _cts;
                _cts = null;
                _loopTask = null;
                _currentGame = game;
                _sessionStartUtc = DateTime.UtcNow;
                _friendCursor = 0;
                _tickCount = 0;
                _toastedFriendKeys.Clear();
                _friendBaselines.Clear();

                if (!ShouldPollGame(game, logReason: true))
                {
                    _currentGame = null;
                    return;
                }

                _cts = new CancellationTokenSource();
                var token = _cts.Token;
                _loopTask = Task.Run(() => PollLoopAsync(game, token), token);
            }

            oldCts?.Cancel();
            oldCts?.Dispose();
        }

        public void Stop()
        {
            CancellationTokenSource cts;
            lock (_stateLock)
            {
                cts = _cts;
                ClearStateLocked();
            }

            cts?.Cancel();
            cts?.Dispose();
        }

        public void RestartCurrentGame()
        {
            var game = CurrentGame;
            if (game != null)
            {
                Start(game);
            }
        }

        private void ClearStateLocked()
        {
            _cts = null;
            _loopTask = null;
            _currentGame = null;
        }

        private async Task PollLoopAsync(Game game, CancellationToken token)
        {
            var interval = GetPollInterval();
            _logger?.Info($"[InGamePolling] Started for {game.Name}; startup delay={StartupDelaySeconds}s, interval={interval.TotalSeconds:F0}s.");

            // Give the game time to launch and settle before the first poll so startup churn
            // (splash screens, initial sync) doesn't trigger a wave of stale unlock toasts.
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(StartupDelaySeconds), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                _logger?.Info($"[InGamePolling] Stopped for {game.Name} during startup delay.");
                return;
            }

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await _tickSemaphore.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        await RunTickAsync(game, token).ConfigureAwait(false);
                    }
                    finally
                    {
                        _tickSemaphore.Release();
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger?.Debug(ex, $"[InGamePolling] Tick failed for {game.Name}.");
                }

                interval = GetPollInterval();
                try
                {
                    await Task.Delay(interval, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
            }

            _logger?.Info($"[InGamePolling] Stopped for {game.Name}.");
        }

        private async Task RunTickAsync(Game game, CancellationToken token)
        {
            if (!ShouldPollGame(game, logReason: false))
            {
                return;
            }

            _tickCount++;
            var tick = _tickCount;
            await RunUserTickAsync(game, token).ConfigureAwait(false);

            if (ShouldRunFriendTick(tick))
            {
                await RunFriendTickAsync(game, token).ConfigureAwait(false);
            }
        }

        private async Task RunUserTickAsync(Game game, CancellationToken token)
        {
            if (_refreshRuntime?.IsRebuilding == true)
            {
                _logger?.Debug("[InGamePolling] User tick skipped: refresh already running.");
                return;
            }

            var interval = GetPollInterval();
            var before = _cacheManager?.LoadGameData(game.Id.ToString());
            var timer = Stopwatch.StartNew();
            await _executeRefreshAsync(
                new RefreshRequest
                {
                    Mode = RefreshModeType.Single,
                    SingleGameId = game.Id
                },
                new RefreshExecutionPolicy
                {
                    ValidateAuthentication = false,
                    UseProgressWindow = false,
                    SwallowExceptions = true,
                    ExternalCancellationToken = token,
                    ErrorLogMessage = "[InGamePolling] Single-game refresh failed."
                }).ConfigureAwait(false);

            timer.Stop();
            var after = _cacheManager?.LoadGameData(game.Id.ToString());
            HydrateForToast(after);
            var unlocks = _differ.DiffUserUnlocks(before, after)
                .Where(a => a?.IsFiltered != true)
                .ToList();
            _logger?.Debug(
                $"[InGamePolling] User tick complete: game={game.Name}, interval={interval.TotalSeconds:F0}s, elapsedMs={timer.ElapsedMilliseconds}, unlocks={unlocks.Count}.");

            var numberByApiName = BuildAchievementNumberMap(after);
            foreach (var achievement in unlocks)
            {
                _notifyUnlocked?.Invoke(CreateUserEventArgs(game, after, achievement, ResolveAchievementNumber(numberByApiName, achievement)));
            }
        }

        /// <summary>
        /// Maps each achievement's ApiName to its 1-based position in the game's provider/custom
        /// sort order (custom order first via the per-game order, provider/source order as
        /// fallback). Used for stable, interpretable screenshot filenames.
        /// </summary>
        private static Dictionary<string, int> BuildAchievementNumberMap(GameAchievementData data)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (data?.Achievements == null || data.Achievements.Count == 0)
            {
                return map;
            }

            var ordered = AchievementOrderHelper.ApplyOrder(
                data.Achievements,
                a => a?.ApiName,
                data.AchievementOrder);
            for (var i = 0; i < ordered.Count; i++)
            {
                var apiName = ordered[i]?.ApiName?.Trim();
                if (!string.IsNullOrWhiteSpace(apiName) && !map.ContainsKey(apiName))
                {
                    map[apiName] = i + 1;
                }
            }

            return map;
        }

        private static int ResolveAchievementNumber(
            IReadOnlyDictionary<string, int> numberByApiName,
            AchievementDetail achievement)
        {
            var apiName = achievement?.ApiName?.Trim();
            return !string.IsNullOrWhiteSpace(apiName) && numberByApiName.TryGetValue(apiName, out var number)
                ? number
                : 0;
        }

        private async Task RunFriendTickAsync(Game game, CancellationToken token)
        {
            if (_friendCache == null)
            {
                return;
            }

            if (_refreshRuntime?.IsRebuilding == true)
            {
                _logger?.Debug("[InGamePolling] Friend tick skipped: refresh already running.");
                return;
            }

            var roster = LoadFriendRoster(game);
            if (roster.Count == 0)
            {
                _logger?.Debug($"[InGamePolling] Friend tick skipped: no active friends own {game.Name}.");
                return;
            }

            var batch = SelectFriendBatch(roster);
            if (batch.Count == 0)
            {
                return;
            }

            var providerKeys = batch
                .Select(target => target.ProviderKey)
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var friendIds = batch
                .Select(target => target.Friend?.ExternalUserId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var timer = Stopwatch.StartNew();
            var options = RefreshOptions.FromFriend(new FriendCustomRefreshOptions
            {
                ProviderKeys = providerKeys,
                Scope = FriendRefreshScope.SelectedGame,
                PlayniteGameIds = new[] { game.Id },
                FriendExternalUserIds = friendIds,
                ForceDefinitionRefresh = true
            });
            options.RunProvidersInParallelOverride = false;

            await _executeRefreshAsync(
                new RefreshRequest
                {
                    Mode = RefreshModeType.FriendsCustom,
                    Options = options
                },
                new RefreshExecutionPolicy
                {
                    ValidateAuthentication = false,
                    UseProgressWindow = false,
                    SwallowExceptions = true,
                    ExternalCancellationToken = token,
                    ErrorLogMessage = "[InGamePolling] Friend selected-game refresh failed."
                }).ConfigureAwait(false);
            timer.Stop();

            var totalUnlocks = 0;
            foreach (var target in batch)
            {
                totalUnlocks += EmitFriendUnlocks(game, target);
            }

            _logger?.Debug(
                $"[InGamePolling] Friend tick complete: game={game.Name}, elapsedMs={timer.ElapsedMilliseconds}, batch={batch.Count}, roster={roster.Count}, cursor={_friendCursor}, unlocks={totalUnlocks}.");
        }

        private List<FriendPollTarget> LoadFriendRoster(Game game)
        {
            var providers = GetCapableProviderKeys(game);
            var result = new List<FriendPollTarget>();
            foreach (var providerKey in providers)
            {
                var candidates = _friendCache.LoadFriendRefreshCandidates(
                    providerKey,
                    new FriendRefreshOptions
                    {
                        Scope = FriendRefreshScope.SelectedGame,
                        PlayniteGameIds = new[] { game.Id },
                        ForceDefinitionRefresh = true
                    }) ?? new List<FriendRefreshCandidate>();

                foreach (var candidate in candidates)
                {
                    if (candidate?.Friend == null || string.IsNullOrWhiteSpace(candidate.Friend.ExternalUserId))
                    {
                        continue;
                    }

                    result.Add(new FriendPollTarget
                    {
                        ProviderKey = providerKey,
                        Friend = candidate.Friend,
                        AppId = candidate.AppId,
                        ProviderGameKey = candidate.ProviderGameKey,
                        PlayniteGameId = candidate.PlayniteGameId,
                        GameName = candidate.GameName
                    });
                }
            }

            return result
                .GroupBy(target => BuildFriendTargetKey(target), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        private List<FriendPollTarget> SelectFriendBatch(List<FriendPollTarget> roster)
        {
            if (roster == null || roster.Count == 0)
            {
                return new List<FriendPollTarget>();
            }

            var batchSize = Math.Max(0, _settings?.Persisted?.InGameFriendBatchSize ?? 10);
            if (batchSize == 0 || batchSize >= roster.Count)
            {
                _friendCursor = 0;
                return roster.ToList();
            }

            var result = new List<FriendPollTarget>(batchSize);
            var start = _friendCursor % roster.Count;
            for (var i = 0; i < batchSize; i++)
            {
                result.Add(roster[(start + i) % roster.Count]);
            }

            _friendCursor = (start + batchSize) % roster.Count;
            return result;
        }

        private int EmitFriendUnlocks(Game game, FriendPollTarget target)
        {
            var rows = _friendCache.LoadFriendGameAchievements(
                target.ProviderKey,
                target.Friend.ExternalUserId,
                target.AppId,
                target.ProviderGameKey) ?? new List<FriendAchievementRow>();

            var toasted = GetToastedFriendSet(target);
            var timestampRows = rows.Where(row => row?.UnlockTimeUtc.HasValue == true).ToList();
            var fresh = _differ.DiffFriendSessionUnlocks(timestampRows, _sessionStartUtc, toasted).ToList();

            var nullTimestampRows = rows
                .Where(row => row?.Unlocked == true && !row.UnlockTimeUtc.HasValue)
                .ToList();
            var baselineKey = BuildFriendTargetKey(target);
            if (nullTimestampRows.Count > 0)
            {
                if (_friendBaselines.TryGetValue(baselineKey, out var baseline))
                {
                    fresh.AddRange(_differ.DiffFriendBaselineUnlocks(baseline, nullTimestampRows, toasted));
                    _friendBaselines[baselineKey] = rows;
                }
                else
                {
                    _friendBaselines[baselineKey] = rows;
                }
            }

            if (fresh.Count == 0)
            {
                return 0;
            }

            foreach (var row in fresh)
            {
                _notifyUnlocked?.Invoke(CreateFriendEventArgs(game, target, rows, row));
            }

            return fresh.Count;
        }

        private HashSet<string> GetToastedFriendSet(FriendPollTarget target)
        {
            var key = BuildFriendTargetKey(target);
            if (!_toastedFriendKeys.TryGetValue(key, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _toastedFriendKeys[key] = set;
            }

            return set;
        }

        private bool ShouldPollGame(Game game, bool logReason)
        {
            var persisted = _settings?.Persisted;
            if (persisted?.EnableInGamePolling != true)
            {
                if (logReason) _logger?.Debug("[InGamePolling] Disabled in settings.");
                return false;
            }

            if (persisted.FirstTimeSetupCompleted != true || persisted.SeenThemeMigration != true)
            {
                if (logReason) _logger?.Debug("[InGamePolling] Skipped: first-time setup/theme migration is incomplete.");
                return false;
            }

            if (GetCapableProviderKeys(game).Count == 0)
            {
                if (logReason) _logger?.Debug($"[InGamePolling] Skipped: no provider is capable for {game?.Name}.");
                return false;
            }

            return true;
        }

        private bool ShouldRunFriendTick(int tick)
        {
            var persisted = _settings?.Persisted;
            if (persisted?.InGamePollRefreshFriends != true)
            {
                return false;
            }

            var multiplier = Math.Max(1, persisted.InGameFriendRefreshMultiplier);
            return tick > 0 && tick % multiplier == 0;
        }

        private TimeSpan GetPollInterval()
        {
            return TimeSpan.FromSeconds(Math.Max(10, _settings?.Persisted?.InGamePollIntervalSeconds ?? 15));
        }

        private List<string> GetCapableProviderKeys(Game game)
        {
            return _providers
                .Where(provider => provider != null && SafeIsCapable(provider, game))
                .Select(provider => provider.ProviderKey)
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private bool SafeIsCapable(IDataProvider provider, Game game)
        {
            try
            {
                return provider?.IsCapable(game) == true;
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"[InGamePolling] Provider capability check failed for {provider?.ProviderKey}.");
                return false;
            }
        }

        /// <summary>
        /// Applies the read-time custom-data overlay (user category/category-type overrides,
        /// manual capstone, icon overrides, notes) to a freshly loaded cache snapshot so unlock
        /// toasts reflect the same categories and capstone state the user sees elsewhere. The
        /// SQLite cache stores raw provider data; these overrides live in the custom data store.
        /// </summary>
        private void HydrateForToast(GameAchievementData data)
        {
            if (data == null || _api == null)
            {
                return;
            }

            var persisted = _settings?.Persisted;
            if (persisted == null)
            {
                return;
            }

            try
            {
                var hydrator = new GameDataHydrator(
                    _api,
                    persisted,
                    PlayniteAchievementsPlugin.Instance?.GameCustomDataStore);
                hydrator.Hydrate(data);
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "[InGamePolling] Failed to hydrate game data for unlock toast.");
            }
        }

        private static bool IsHardcoreCategory(string categoryType)
        {
            return AchievementCategoryTypeHelper
                .ParseValues(categoryType)
                .Contains(AchievementCategoryTypeHelper.HardcoreCategoryType);
        }

        private static AchievementUnlockedEventArgs CreateUserEventArgs(
            Game game,
            GameAchievementData data,
            AchievementDetail achievement,
            int achievementNumber)
        {
            return new AchievementUnlockedEventArgs
            {
                PlayniteGameId = game?.Id ?? data?.PlayniteGameId ?? Guid.Empty,
                GameName = data?.GameName ?? game?.Name,
                ProviderKey = achievement?.ProviderKey ?? data?.ProviderKey,
                ApiName = achievement?.ApiName,
                DisplayName = achievement?.DisplayName,
                Description = achievement?.Description,
                Category = achievement?.Category,
                IconPath = achievement?.UnlockedIconPath,
                GlobalPercent = achievement?.GlobalPercentUnlocked,
                RarityTier = achievement?.Rarity.ToString(),
                TrophyType = achievement?.TrophyType,
                IsCapstone = achievement?.IsCapstone == true,
                IsHardcore = IsHardcoreCategory(achievement?.CategoryType),
                Points = achievement?.Points,
                ScaledPoints = achievement?.ScaledPoints,
                UnlockTimeUtc = achievement?.UnlockTimeUtc,
                UnlockedCount = data?.UnlockedCount ?? 0,
                TotalCount = data?.AchievementCount ?? 0,
                AchievementNumber = achievementNumber,
                GameCompleted = data?.IsCompleted == true
            };
        }

        private static AchievementUnlockedEventArgs CreateFriendEventArgs(
            Game game,
            FriendPollTarget target,
            IReadOnlyList<FriendAchievementRow> allRows,
            FriendAchievementRow row)
        {
            return new AchievementUnlockedEventArgs
            {
                PlayniteGameId = target?.PlayniteGameId ?? game?.Id ?? Guid.Empty,
                GameName = target?.GameName ?? game?.Name,
                ProviderKey = target?.ProviderKey,
                ApiName = row?.ApiName,
                DisplayName = row?.DisplayName,
                Description = row?.Description,
                Category = row?.Category,
                IconPath = row?.UnlockedIconUrl ?? row?.IconUrl,
                GlobalPercent = row?.GlobalPercentUnlocked,
                RarityTier = row?.Rarity?.ToString(),
                TrophyType = row?.TrophyType,
                IsCapstone = row?.IsCapstone == true,
                IsHardcore = IsHardcoreCategory(row?.CategoryType),
                Points = row?.Points,
                ScaledPoints = row?.ScaledPoints,
                UnlockTimeUtc = row?.UnlockTimeUtc,
                UnlockedCount = allRows?.Count(r => r?.Unlocked == true) ?? 0,
                TotalCount = allRows?.Count ?? 0,
                IsFriendUnlock = true,
                FriendExternalUserId = target?.Friend?.ExternalUserId,
                FriendDisplayName = target?.Friend?.DisplayName,
                FriendAvatarPath = target?.Friend?.AvatarPath,
                FriendAvatarUrl = target?.Friend?.AvatarUrl
            };
        }

        private static string BuildFriendTargetKey(FriendPollTarget target)
        {
            if (target == null)
            {
                return string.Empty;
            }

            var gameKey = !string.IsNullOrWhiteSpace(target.ProviderGameKey)
                ? target.ProviderGameKey.Trim()
                : target.AppId.ToString();
            return $"{target.ProviderKey}|{target.Friend?.ExternalUserId}|{gameKey}";
        }

        public void Dispose()
        {
            Stop();
            _tickSemaphore.Dispose();
        }
    }
}
