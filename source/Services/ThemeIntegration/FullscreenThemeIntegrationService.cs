using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Models.ThemeIntegration;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Services;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;
using RelayCommand = PlayniteAchievements.Common.RelayCommand;

namespace PlayniteAchievements.Services.ThemeIntegration
{
    public sealed class FullscreenThemeIntegrationService : IDisposable
    {
        private readonly ILogger _logger;
        private readonly IPlayniteAPI _api;
        private readonly AchievementManager _achievementService;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly Action<Guid> _openAchievementsForGameId;
        private readonly Action<Guid?> _requestPerGameThemeUpdate;
        private Dispatcher UiDispatcher => _api?.MainView?.UIDispatcher ?? Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        private readonly ICommand _openOverviewCmd;
        private readonly ICommand _openSelectedGameCmd;
        private readonly ICommand _refreshSelectedGameCmd;

        private readonly object _refreshLock = new object();
        private CancellationTokenSource _refreshCts;

        private Window _achievementsWindow;

        private bool _fullscreenInitialized;

        public FullscreenThemeIntegrationService(
            IPlayniteAPI api,
            AchievementManager achievementService,
            PlayniteAchievementsSettings settings,
            Action<Guid> openAchievementsForGameId,
            Action<Guid?> requestPerGameThemeUpdate,
            ILogger logger)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _achievementService = achievementService ?? throw new ArgumentNullException(nameof(achievementService));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _openAchievementsForGameId = openAchievementsForGameId ?? throw new ArgumentNullException(nameof(openAchievementsForGameId));
            _requestPerGameThemeUpdate = requestPerGameThemeUpdate ?? throw new ArgumentNullException(nameof(requestPerGameThemeUpdate));
            _logger = logger;

            _openOverviewCmd = new RelayCommand(_ => OpenOverviewAchievementsWindow());
            _openSelectedGameCmd = new RelayCommand(_ => OpenSelectedGameAchievementsWindow());
            _refreshSelectedGameCmd = new RelayCommand(_ => RefreshSelectedGameAchievements());

            // Command surfaces referenced by themes.
            _settings.OpenFullscreenAchievementWindow = _openSelectedGameCmd;
            _settings.OpenAchievementWindow = _openOverviewCmd;
            _settings.OpenGameAchievementWindow = _openSelectedGameCmd;
            _settings.RefreshSelectedGameCommand = _refreshSelectedGameCmd;
            _settings.RefreshCommand = _refreshSelectedGameCmd;

            _settings.PropertyChanged += Settings_PropertyChanged;
            _achievementService.CacheInvalidated += AchievementService_CacheInvalidated;
        }

        public void Dispose()
        {
            try { _settings.PropertyChanged -= Settings_PropertyChanged; } catch { }
            try { _achievementService.CacheInvalidated -= AchievementService_CacheInvalidated; } catch { }

            lock (_refreshLock)
            {
                try { _refreshCts?.Cancel(); } catch { }
                try { _refreshCts?.Dispose(); } catch { }
                _refreshCts = null;
            }

            CloseOverlayWindowIfOpen();
        }

        public void NotifySelectionChanged(Guid? selectedGameId)
        {
            // Raise PropertyChanged for SelectedGame so themes get notified of selection changes
            // This is important for fullscreen themes that bind to PluginSettings.SelectedGame
            _settings.OnPropertyChanged(nameof(PlayniteAchievementsSettings.SelectedGame));

            if (!selectedGameId.HasValue)
            {
                return;
            }

            EnsureFullscreenInitialized();
        }

        public void CloseOverlayWindowIfOpen()
        {
            try
            {
                var dispatcher = UiDispatcher;
                if (dispatcher == null || dispatcher.CheckAccess())
                {
                    if (_achievementsWindow != null && _achievementsWindow.IsVisible)
                    {
                        _achievementsWindow.Close();
                    }
                }
                else
                {
                    dispatcher.Invoke(() =>
                    {
                        if (_achievementsWindow != null && _achievementsWindow.IsVisible)
                        {
                            _achievementsWindow.Close();
                        }
                    }, DispatcherPriority.Send);
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Failed to close fullscreen overlay achievements window.");
            }
        }

        private bool IsFullscreen()
        {
            try
            {
                return _api?.ApplicationInfo?.Mode == ApplicationMode.Fullscreen;
            }
            catch
            {
                return false;
            }
        }

        private void EnsureFullscreenInitialized()
        {
            if (!IsFullscreen() || _fullscreenInitialized)
            {
                return;
            }

            _fullscreenInitialized = true;
            RequestRefresh();
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e == null)
            {
                return;
            }

            if (e.PropertyName == $"{nameof(PlayniteAchievementsSettings.Persisted)}.{nameof(PersistedSettings.UltraRareThreshold)}" ||
                e.PropertyName == nameof(PersistedSettings.UltraRareThreshold) ||
                e.PropertyName == $"{nameof(PlayniteAchievementsSettings.Persisted)}.{nameof(PersistedSettings.RareThreshold)}" ||
                e.PropertyName == nameof(PersistedSettings.RareThreshold) ||
                e.PropertyName == $"{nameof(PlayniteAchievementsSettings.Persisted)}.{nameof(PersistedSettings.UncommonThreshold)}" ||
                e.PropertyName == nameof(PersistedSettings.UncommonThreshold))
            {
                try
                {
                    RarityHelper.Configure(
                        _settings.Persisted.UltraRareThreshold,
                        _settings.Persisted.RareThreshold,
                        _settings.Persisted.UncommonThreshold);
                }
                catch
                {
                }

                if (IsFullscreen() && _fullscreenInitialized)
                {
                    RequestRefresh();
                }
            }
        }

        private void AchievementService_CacheInvalidated(object sender, EventArgs e)
        {
            if (IsFullscreen() && _fullscreenInitialized)
            {
                RequestRefresh();
            }

            try
            {
                var id = GetSingleSelectedGameId();
                if (id.HasValue)
                {
                    _requestPerGameThemeUpdate(id);
                }
            }
            catch
            {
            }
        }

        private Guid? GetSingleSelectedGameId()
        {
            try
            {
                var selected = _api?.MainView?.SelectedGames?
                    .Where(g => g != null)
                    .Take(2)
                    .ToList();

                if (selected == null || selected.Count != 1)
                {
                    return null;
                }

                return selected[0].Id;
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to resolve selected game for fullscreen achievement window.");
                return null;
            }
        }

        private void OpenOverviewAchievementsWindow()
        {
            // Cancel any pending async refresh to prevent it from overwriting our data
            lock (_refreshLock)
            {
                try { _refreshCts?.Cancel(); } catch { }
                try { _refreshCts?.Dispose(); } catch { }
                _refreshCts = null;
            }

            // Mark as initialized to prevent other code paths from triggering refresh
            if (!_fullscreenInitialized)
            {
                _fullscreenInitialized = true;
            }

            // Immediately populate all-games data on UI thread before opening window
            PopulateAllGamesDataSync();

            ShowAchievementsWindow(styleKey: "AchievementsWindow", preselectGameId: null);
        }

        private void PopulateAllGamesDataSync()
        {
            try
            {
                // Get all achievement data synchronously
                var allData = _achievementService.GetAllGameAchievementData() ?? new List<GameAchievementData>();
                var ids = allData
                    .Where(d => d?.PlayniteGameId != null && d.NoAchievements == false && (d.Achievements?.Count ?? 0) > 0)
                    .Select(d => d.PlayniteGameId.Value)
                    .Distinct()
                    .ToList();

                // Build game info map synchronously
                var info = BuildGameInfoMapOnUiThread(ids);

                // Build snapshot synchronously
                var snapshot = BuildSnapshot(allData, info, CancellationToken.None);

                // Apply compatibility surface directly.
                ApplyCompatibilitySurface(snapshot);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to populate all-games data synchronously.");
            }
        }

        private void OpenSelectedGameAchievementsWindow()
        {
            var id = GetSingleSelectedGameId();
            if (!id.HasValue)
            {
                return;
            }

            EnsureFullscreenInitialized();

            _requestPerGameThemeUpdate(id);

            try
            {
                if (_achievementService.GetGameAchievementData(id.Value) == null)
                {
                    var scanTask = _achievementService.StartManagedSingleGameScanAsync(id.Value);
                    _ = scanTask?.ContinueWith(_ =>
                    {
                        try { _requestPerGameThemeUpdate(id); } catch { }
                        try { if (IsFullscreen() && _fullscreenInitialized) RequestRefresh(); } catch { }
                    });
                }
            }
            catch
            {
            }

            ShowAchievementsWindow(styleKey: "GameAchievementsWindow", preselectGameId: id);
        }

        private void OpenGameAchievementsWindow(Guid gameId)
        {
            EnsureFullscreenInitialized();
            _requestPerGameThemeUpdate(gameId);

            try
            {
                if (_achievementService.GetGameAchievementData(gameId) == null)
                {
                    var scanTask = _achievementService.StartManagedSingleGameScanAsync(gameId);
                    _ = scanTask?.ContinueWith(_ =>
                    {
                        try { _requestPerGameThemeUpdate(gameId); } catch { }
                        try { if (IsFullscreen() && _fullscreenInitialized) RequestRefresh(); } catch { }
                    });
                }
            }
            catch
            {
            }

            ShowAchievementsWindow(styleKey: "GameAchievementsWindow", preselectGameId: gameId);
        }

        private void RefreshSelectedGameAchievements()
        {
            var id = GetSingleSelectedGameId();
            if (!id.HasValue)
            {
                return;
            }

            EnsureFullscreenInitialized();

            var scanTask = _achievementService.StartManagedSingleGameScanAsync(id.Value);
            _ = scanTask?.ContinueWith(_ =>
            {
                try { _requestPerGameThemeUpdate(id); } catch { }
                try { if (IsFullscreen() && _fullscreenInitialized) RequestRefresh(); } catch { }
            });
        }

        private void ShowAchievementsWindow(string styleKey, Guid? preselectGameId)
        {
            if (string.IsNullOrWhiteSpace(styleKey))
            {
                return;
            }

            try
            {
                var dispatcher = UiDispatcher;
                if (dispatcher == null)
                {
                    return;
                }

                if (dispatcher.CheckAccess())
                {
                    OpenOverlayWindowOnUiThread(styleKey, preselectGameId);
                }
                else
                {
                    dispatcher.Invoke(() => OpenOverlayWindowOnUiThread(styleKey, preselectGameId), DispatcherPriority.Send);
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"[FullscreenTheme] Failed to open overlay window (styleKey={styleKey}).");
            }
        }

        private void OpenOverlayWindowOnUiThread(string styleKey, Guid? preselectGameId)
        {
            if (preselectGameId.HasValue)
            {
                try { _api?.MainView?.SelectGame(preselectGameId.Value); } catch { }
                try { _requestPerGameThemeUpdate(preselectGameId.Value); } catch { }
            }

            try
            {
                if (_achievementsWindow != null && _achievementsWindow.IsVisible)
                {
                    _achievementsWindow.Close();
                }
            }
            catch
            {
            }

            // Match SuccessStoryFullscreenHelper behavior as closely as possible (Aniki ReMake expects this).
            var window = _api.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMinimizeButton = false
            });

            var parent = _api.Dialogs.GetCurrentAppWindow();
            if (parent != null)
            {
                window.Owner = parent;
            }
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            window.Title = "Achievements";

            window.Height = parent != null && parent.Height > 0 ? parent.Height : SystemParameters.PrimaryScreenHeight;
            window.Width = parent != null && parent.Width > 0 ? parent.Width : SystemParameters.PrimaryScreenWidth;

            var xamlString = $@"
<Viewbox Stretch=""Uniform""
         xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
         xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
         xmlns:pbeh=""clr-namespace:Playnite.Behaviors;assembly=Playnite"">
    <Grid Width=""1920"" Height=""1080"">
        <ContentControl x:Name=""AchievementsWindow""
                        Focusable=""False""
                        Style=""{{DynamicResource {styleKey}}}"" />
    </Grid>
</Viewbox>";

            var content = (FrameworkElement)XamlReader.Parse(xamlString);

            // Set DataContext so theme bindings like {Binding SelectedGame.BackgroundImage} work
            // The theme uses both {Binding SelectedGame.*} for game data and
            // {PluginSettings Plugin=PlayniteAchievements, Path=...} for plugin data
            content.DataContext = _settings;

            window.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    try { window.Close(); } catch { }
                    e.Handled = true;
                }
            };

            window.Closed += (_, __) =>
            {
                if (ReferenceEquals(_achievementsWindow, window))
                {
                    _achievementsWindow = null;
                }
            };

            window.Content = content;
            _achievementsWindow = window;

            window.ShowDialog();
        }

        private void RequestRefresh()
        {
            if (!IsFullscreen())
            {
                return;
            }

            CancellationToken token;
            lock (_refreshLock)
            {
                try { _refreshCts?.Cancel(); } catch { }
                try { _refreshCts?.Dispose(); } catch { }
                _refreshCts = new CancellationTokenSource();
                token = _refreshCts.Token;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(500, token).ConfigureAwait(false);

                    var allData = _achievementService.GetAllGameAchievementData() ?? new List<GameAchievementData>();
                    var ids = allData
                        .Where(d => d?.PlayniteGameId != null && d.NoAchievements == false && (d.Achievements?.Count ?? 0) > 0)
                        .Select(d => d.PlayniteGameId.Value)
                        .Distinct()
                        .ToList();

                    token.ThrowIfCancellationRequested();

                    Dictionary<Guid, GameInfo> info = null;
                    try
                    {
                        var dispatcher = UiDispatcher;
                        if (dispatcher == null)
                        {
                            info = new Dictionary<Guid, GameInfo>();
                        }
                        else if (dispatcher.CheckAccess())
                        {
                            info = BuildGameInfoMapOnUiThread(ids);
                        }
                        else
                        {
                            dispatcher.Invoke(() => { info = BuildGameInfoMapOnUiThread(ids); }, DispatcherPriority.Background);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.Debug(ex, "Failed to build fullscreen game-info map on UI thread.");
                        info = new Dictionary<Guid, GameInfo>();
                    }

                    token.ThrowIfCancellationRequested();

                    var snapshot = BuildSnapshot(allData, info, token);

                    UiDispatcher?.InvokeIfNeeded(() => ApplySnapshot(snapshot), DispatcherPriority.Background);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, "Failed to refresh fullscreen theme integration snapshot.");
                }
            }, token);
        }

        private sealed class Snapshot
        {
            public List<FullscreenAchievementGameItem> All { get; set; } = new List<FullscreenAchievementGameItem>();
            public List<FullscreenAchievementGameItem> Platinum { get; set; } = new List<FullscreenAchievementGameItem>();
            public List<FullscreenAchievementGameItem> PlatinumAscending { get; set; } = new List<FullscreenAchievementGameItem>();

            public int PlatCount { get; set; }
            public int GoldCount { get; set; }
            public int SilverCount { get; set; }
            public int BronzeCount { get; set; }
            public int TotalCount { get; set; }

            public int Score { get; set; }

            public int Level { get; set; }
            public double LevelProgress { get; set; }
            public string Rank { get; set; } = "Bronze1";
        }

        private sealed class GameInfo
        {
            public string Name { get; set; }
            public string Platform { get; set; }
            public string CoverImagePath { get; set; }
        }

        private Dictionary<Guid, GameInfo> BuildGameInfoMapOnUiThread(List<Guid> ids)
        {
            var gameInfo = new Dictionary<Guid, GameInfo>();
            if (ids == null || ids.Count == 0)
            {
                return gameInfo;
            }

            var gamesDb = _api?.Database?.Games;
            if (gamesDb == null)
            {
                return gameInfo;
            }

            foreach (var id in ids)
            {
                var game = gamesDb.Get(id);
                if (game == null)
                {
                    continue;
                }

                var cover = string.Empty;
                if (!string.IsNullOrWhiteSpace(game.CoverImage))
                {
                    cover = _api.Database.GetFullFilePath(game.CoverImage) ?? string.Empty;
                }

                if (string.IsNullOrWhiteSpace(cover) && !string.IsNullOrWhiteSpace(game.Icon))
                {
                    cover = _api.Database.GetFullFilePath(game.Icon) ?? string.Empty;
                }

                gameInfo[id] = new GameInfo
                {
                    Name = game.Name ?? string.Empty,
                    Platform = game.Source?.Name ?? "Unknown",
                    CoverImagePath = cover ?? string.Empty
                };
            }

            return gameInfo;
        }

        private Snapshot BuildSnapshot(List<GameAchievementData> allData, Dictionary<Guid, GameInfo> gameInfo, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            var snapshot = new Snapshot();

            allData ??= new List<GameAchievementData>();
            gameInfo ??= new Dictionary<Guid, GameInfo>();

            var items = new List<FullscreenAchievementGameItem>();
            foreach (var data in allData)
            {
                token.ThrowIfCancellationRequested();

                if (data?.PlayniteGameId == null || data.NoAchievements) continue;
                var total = data.Achievements?.Count ?? 0;
                if (total <= 0) continue;

                var id = data.PlayniteGameId.Value;
                if (!gameInfo.TryGetValue(id, out var info) || info == null)
                {
                    info = new GameInfo
                    {
                        Name = data.GameName ?? string.Empty,
                        Platform = "Unknown",
                        CoverImagePath = string.Empty
                    };
                }

                var unlocked = 0;
                var latestUnlockUtc = DateTime.MinValue;
                for (int i = 0; i < data.Achievements.Count; i++)
                {
                    var a = data.Achievements[i];
                    if (a?.Unlocked != true)
                    {
                        continue;
                    }

                    unlocked++;

                    var t = a.UnlockTimeUtc;
                    if (!t.HasValue)
                    {
                        continue;
                    }

                    var utc = t.Value;
                    if (utc.Kind == DateTimeKind.Unspecified)
                    {
                        utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
                    }

                    if (utc > latestUnlockUtc)
                    {
                        latestUnlockUtc = utc;
                    }
                }

                var progress = unlocked == total ? 100 : (int)Math.Floor(100.0 * unlocked / total);
                var latestUnlockLocal = latestUnlockUtc == DateTime.MinValue ? DateTime.MinValue : latestUnlockUtc.ToLocalTime();

                GetTrophyCounts(data, out var gold, out var silver, out var bronze);

                var openCmd = new RelayCommand(_ => OpenGameAchievementsWindow(id));

                items.Add(new FullscreenAchievementGameItem(
                    id,
                    info.Name,
                    info.Platform,
                    info.CoverImagePath,
                    progress,
                    gold,
                    silver,
                    bronze,
                    latestUnlockLocal,
                    latestUnlockLocal,
                    openCmd));
            }

            items = items
                .OrderByDescending(i => i.LastUnlockDate)
                .ThenByDescending(i => i.Progress)
                .ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            snapshot.All = items;
            snapshot.Platinum = items
                .Where(i => i.Progress >= 100)
                .Where(i => i.LatestUnlocked != DateTime.MinValue)
                .OrderByDescending(i => i.LatestUnlocked)
                .ToList();

            snapshot.PlatinumAscending = snapshot.Platinum
                .OrderBy(i => i.LatestUnlocked)
                .ToList();

            snapshot.PlatCount = snapshot.Platinum.Count;
            snapshot.GoldCount = items.Sum(i => i.GS90Count);
            snapshot.SilverCount = items.Sum(i => i.GS30Count);
            snapshot.BronzeCount = items.Sum(i => i.GS15Count);
            snapshot.TotalCount = snapshot.PlatCount + snapshot.GoldCount + snapshot.SilverCount + snapshot.BronzeCount;

            if (snapshot.TotalCount > 0)
            {
                snapshot.Score = snapshot.PlatCount * 300 + snapshot.GoldCount * 90 + snapshot.SilverCount * 30 + snapshot.BronzeCount * 15;
                ComputeLevel(snapshot.Score, out var level, out var progressPercent);
                snapshot.Level = level;
                snapshot.LevelProgress = progressPercent;
                snapshot.Rank = RankFromLevel(snapshot.Level);
            }

            return snapshot;
        }

        private void ApplySnapshot(Snapshot snapshot)
        {
            snapshot ??= new Snapshot();

            ApplyNativeSurface(snapshot);
            ApplyCompatibilitySurface(snapshot);
        }

        private void ApplyCompatibilitySurface(Snapshot snapshot)
        {
            _settings.OpenAchievementWindow = _openOverviewCmd;
            _settings.OpenGameAchievementWindow = _openSelectedGameCmd;
            _settings.RefreshSelectedGameCommand = _refreshSelectedGameCmd;

            _settings.AllGamesWithAchievements = new ObservableCollection<FullscreenAchievementGameItem>(snapshot.All ?? new List<FullscreenAchievementGameItem>());
            _settings.PlatinumGames = new ObservableCollection<FullscreenAchievementGameItem>(snapshot.Platinum ?? new List<FullscreenAchievementGameItem>());
            _settings.PlatinumGamesAscending = new ObservableCollection<FullscreenAchievementGameItem>(snapshot.PlatinumAscending ?? new List<FullscreenAchievementGameItem>());

            _settings.GSTotal = snapshot.TotalCount > 0 ? snapshot.TotalCount.ToString() : "0";
            _settings.GSPlat = snapshot.TotalCount > 0 ? snapshot.PlatCount.ToString() : "0";
            _settings.GS90 = snapshot.TotalCount > 0 ? snapshot.GoldCount.ToString() : "0";
            _settings.GS30 = snapshot.TotalCount > 0 ? snapshot.SilverCount.ToString() : "0";
            _settings.GS15 = snapshot.TotalCount > 0 ? snapshot.BronzeCount.ToString() : "0";
            _settings.GSScore = snapshot.TotalCount > 0 ? snapshot.Score.ToString("N0") : "0";

            _settings.GSLevel = snapshot.TotalCount > 0 ? snapshot.Level.ToString() : "0";
            _settings.GSLevelProgress = snapshot.TotalCount > 0 ? snapshot.LevelProgress : 0;
            _settings.GSRank = snapshot.TotalCount > 0 && !string.IsNullOrWhiteSpace(snapshot.Rank) ? snapshot.Rank : "Bronze1";
        }

        private void ApplyNativeSurface(Snapshot snapshot)
        {
            _settings.OpenFullscreenAchievementWindow = _openSelectedGameCmd;
            _settings.RefreshCommand = _refreshSelectedGameCmd;

            _settings.FullscreenHasData = snapshot.TotalCount > 0;
            _settings.FullscreenGamesWithAchievements = new ObservableCollection<FullscreenAchievementGameItem>(snapshot.All ?? new List<FullscreenAchievementGameItem>());
            _settings.FullscreenPlatinumGames = new ObservableCollection<FullscreenAchievementGameItem>(snapshot.Platinum ?? new List<FullscreenAchievementGameItem>());

            _settings.FullscreenTotalTrophies = snapshot.TotalCount;
            _settings.FullscreenPlatinumTrophies = snapshot.PlatCount;
            _settings.FullscreenGoldTrophies = snapshot.GoldCount;
            _settings.FullscreenSilverTrophies = snapshot.SilverCount;
            _settings.FullscreenBronzeTrophies = snapshot.BronzeCount;

            _settings.FullscreenLevel = snapshot.Level;
            _settings.FullscreenLevelProgress = snapshot.LevelProgress;
            _settings.FullscreenRank = !string.IsNullOrWhiteSpace(snapshot.Rank) ? snapshot.Rank : "Bronze1";
        }

        private static void GetTrophyCounts(GameAchievementData data, out int gold, out int silver, out int bronze)
        {
            gold = 0;
            silver = 0;
            bronze = 0;

            if (data?.Achievements == null || data.Achievements.Count == 0)
            {
                return;
            }

            foreach (var a in data.Achievements)
            {
                if (a?.Unlocked != true)
                {
                    continue;
                }

                var percent = a.GlobalPercentUnlocked ?? 100;
                var tier = RarityHelper.GetRarityTier(percent);

                if (tier == RarityTier.Uncommon)
                {
                    silver++;
                }
                else if (tier == RarityTier.Common)
                {
                    bronze++;
                }
                else
                {
                    gold++;
                }
            }
        }

        private static void ComputeLevel(int score, out int level, out int levelProgressPercent)
        {
            level = 0;
            levelProgressPercent = 0;

            if (score <= 0)
            {
                return;
            }

            int rangeMin = 0;
            int rangeMax = 100;
            int step = 100;

            while (score > rangeMax)
            {
                level++;
                rangeMin = rangeMax + 1;
                step += 100;
                rangeMax = rangeMin + step - 1;
            }

            int rangeSpan = rangeMax - rangeMin + 1;
            int progress = (int)(((double)(score - rangeMin) / rangeSpan) * 100);
            levelProgressPercent = Math.Max(0, Math.Min(100, progress));
        }

        private static string RankFromLevel(int level)
        {
            if (level <= 0) return "Bronze1";

            if (level <= 3) return "Bronze1";
            if (level <= 7) return "Bronze2";
            if (level <= 12) return "Bronze3";
            if (level <= 21) return "Silver1";
            if (level <= 31) return "Silver2";
            if (level <= 44) return "Silver3";
            if (level <= 59) return "Gold1";
            if (level <= 77) return "Gold2";
            if (level <= 97) return "Gold3";
            if (level <= 119) return "Plat1";
            if (level <= 144) return "Plat2";
            if (level <= 171) return "Plat3";
            return "Plat";
        }
    }
}
