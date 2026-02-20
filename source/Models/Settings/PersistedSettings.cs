using System;
using System.Collections.Generic;
using Playnite.SDK;

using ObservableObject = PlayniteAchievements.Common.ObservableObject;

namespace PlayniteAchievements.Models.Settings
{
    /// <summary>
    /// Persisted user settings for PlayniteAchievements plugin.
    /// These settings are serialized to the plugin settings JSON file.
    /// </summary>
    public class PersistedSettings : ObservableObject
    {
        #region Backing Fields

        private string _steamUserId;
        private string _gogUserId;
        private string _epicAccountId;
        private string _epicAccessToken;
        private string _epicRefreshToken;
        private string _epicTokenType;
        private DateTime? _epicTokenExpiryUtc;
        private DateTime? _epicRefreshTokenExpiryUtc;
        private string _steamApiKey;
        private string _globalLanguage = "english";
        private bool _steamEnabled = true;
        private bool _epicEnabled = true;
        private bool _gogEnabled = true;
        private bool _psnEnabled = true;
        private bool _retroAchievementsEnabled = true;
        private bool _enablePeriodicUpdates = true;
        private int _periodicUpdateHours = 6;
        private bool _enableNotifications = true;
        private bool _notifyPeriodicUpdates = true;
        private bool _notifyOnRebuild = true;
        private int _recentRefreshRecentGamesCount = 10;
        private bool _showHiddenIcon = false;
        private bool _showHiddenTitle = false;
        private bool _showHiddenDescription = false;
        private bool _useCoverImages = true;
        private bool _includeUnplayedGames = true;
        private int _scanDelayMs = 200;
        private int _maxRetryAttempts = 3;

        private string _raUsername;
        private string _raWebApiKey;
        private string _raRarityStats = "casual";
        private int _hashIndexMaxAgeDays = 30;
        private bool _enableArchiveScanning = true;
        private bool _enableDiscHashing = true;
        private bool _enableRaNameFallback = true;
        private Dictionary<string, bool> _dataGridColumnVisibility = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, double> _dataGridColumnWidths = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, double> _sidebarAchievementColumnWidths = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, double> _singleGameColumnWidths = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, bool> _gamesOverviewColumnVisibility = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, double> _gamesOverviewColumnWidths = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private bool _pointsColumnAutoEnabled = false;

        private double _ultraRareThreshold = 5;
        private double _rareThreshold = 10;
        private double _uncommonThreshold = 50;
        private bool _firstTimeSetupCompleted = false;
        private bool _seenThemeMigration = false;

        #endregion

        #region Steam Settings

        public string SteamUserId
        {
            get => _steamUserId;
            set => SetValue(ref _steamUserId, value);
        }

        public string GogUserId
        {
            get => _gogUserId;
            set => SetValue(ref _gogUserId, value);
        }

        public string EpicAccountId
        {
            get => _epicAccountId;
            set => SetValue(ref _epicAccountId, value);
        }

        public string EpicAccessToken
        {
            get => _epicAccessToken;
            set => SetValue(ref _epicAccessToken, value);
        }

        public string EpicRefreshToken
        {
            get => _epicRefreshToken;
            set => SetValue(ref _epicRefreshToken, value);
        }

        public string EpicTokenType
        {
            get => _epicTokenType;
            set => SetValue(ref _epicTokenType, value);
        }

        public DateTime? EpicTokenExpiryUtc
        {
            get => _epicTokenExpiryUtc;
            set => SetValue(ref _epicTokenExpiryUtc, value);
        }

        public DateTime? EpicRefreshTokenExpiryUtc
        {
            get => _epicRefreshTokenExpiryUtc;
            set => SetValue(ref _epicRefreshTokenExpiryUtc, value);
        }

        /// <summary>
        /// Optional Steam Web API key used for owned games, friends and player summaries.
        /// </summary>
        public string SteamApiKey
        {
            get => _steamApiKey;
            set => SetValue(ref _steamApiKey, value ?? string.Empty);
        }

        #endregion

        #region Provider Enable/Disable Settings

        /// <summary>
        /// Global language for achievement text, used by all providers that support localization.
        /// </summary>
        public string GlobalLanguage
        {
            get => _globalLanguage;
            set => SetValue(ref _globalLanguage, value);
        }

        /// <summary>
        /// Enable or disable Steam achievement scanning.
        /// </summary>
        public bool SteamEnabled
        {
            get => _steamEnabled;
            set => SetValue(ref _steamEnabled, value);
        }

        /// <summary>
        /// Enable or disable Epic Games achievement scanning.
        /// </summary>
        public bool EpicEnabled
        {
            get => _epicEnabled;
            set => SetValue(ref _epicEnabled, value);
        }

        /// <summary>
        /// Enable or disable GOG achievement scanning.
        /// </summary>
        public bool GogEnabled
        {
            get => _gogEnabled;
            set => SetValue(ref _gogEnabled, value);
        }

        /// <summary>
        /// Enable or disable PlayStation achievement scanning.
        /// </summary>
        public bool PsnEnabled
        {
            get => _psnEnabled;
            set => SetValue(ref _psnEnabled, value);
        }

        /// <summary>
        /// Enable or disable RetroAchievements scanning.
        /// </summary>
        public bool RetroAchievementsEnabled
        {
            get => _retroAchievementsEnabled;
            set => SetValue(ref _retroAchievementsEnabled, value);
        }

        #endregion

        #region Update and Refresh Settings

        /// <summary>
        /// Enable the background periodic updates.
        /// </summary>
        public bool EnablePeriodicUpdates
        {
            get => _enablePeriodicUpdates;
            set => SetValue(ref _enablePeriodicUpdates, value);
        }

        /// <summary>
        /// Hours between periodic background updates.
        /// </summary>
        public int PeriodicUpdateHours
        {
            get => _periodicUpdateHours;
            set => SetValue(ref _periodicUpdateHours, value);
        }

        /// <summary>
        /// Maximum recent games to refresh when using Recent Refresh.
        /// </summary>
        public int RecentRefreshGamesCount
        {
            get => _recentRefreshRecentGamesCount;
            set => SetValue(ref _recentRefreshRecentGamesCount, Math.Max(1, value));
        }

        #endregion

        #region Notification Settings

        /// <summary>
        /// Enable non-modal notifications (toasts) from the plugin.
        /// </summary>
        public bool EnableNotifications
        {
            get => _enableNotifications;
            set => SetValue(ref _enableNotifications, value);
        }

        /// <summary>
        /// Show lightweight toast when periodic background updates complete.
        /// </summary>
        public bool NotifyPeriodicUpdates
        {
            get => _notifyPeriodicUpdates;
            set => SetValue(ref _notifyPeriodicUpdates, value);
        }

        /// <summary>
        /// Show a toast when a manual or managed rebuild completes or fails.
        /// </summary>
        public bool NotifyOnRebuild
        {
            get => _notifyOnRebuild;
            set => SetValue(ref _notifyOnRebuild, value);
        }

        #endregion

        #region Display Preferences

        /// <summary>
        /// When true, hidden achievement icons are shown before reveal.
        /// </summary>
        public bool ShowHiddenIcon
        {
            get => _showHiddenIcon;
            set => SetValue(ref _showHiddenIcon, value);
        }

        /// <summary>
        /// When true, hidden achievement titles are shown before reveal.
        /// </summary>
        public bool ShowHiddenTitle
        {
            get => _showHiddenTitle;
            set => SetValue(ref _showHiddenTitle, value);
        }

        /// <summary>
        /// When true, hidden achievement descriptions are shown before reveal.
        /// </summary>
        public bool ShowHiddenDescription
        {
            get => _showHiddenDescription;
            set => SetValue(ref _showHiddenDescription, value);
        }

        /// <summary>
        /// When true, use Playnite cover images instead of icons/logos in the games list.
        /// </summary>
        public bool UseCoverImages
        {
            get => _useCoverImages;
            set => SetValue(ref _useCoverImages, value);
        }

        public bool IncludeUnplayedGames
        {
            get => _includeUnplayedGames;
            set => SetValue(ref _includeUnplayedGames, value);
        }

        /// <summary>
        /// Base delay in milliseconds for retry/backoff after transient errors.
        /// Default is 200ms. Higher values are safer for strict APIs but slower after failures.
        /// Set to 0 for fastest retry behavior.
        /// </summary>
        public int ScanDelayMs
        {
            get => _scanDelayMs;
            set => SetValue(ref _scanDelayMs, Math.Max(0, value));
        }

        /// <summary>
        /// Maximum retry attempts when encountering rate limit or transient errors.
        /// Default is 3. Each retry uses exponential backoff with jitter.
        /// </summary>
        public int MaxRetryAttempts
        {
            get => _maxRetryAttempts;
            set => SetValue(ref _maxRetryAttempts, Math.Max(0, Math.Min(value, 10)));
        }

        #endregion

        #region Theme Integration Settings

        #endregion

        #region RetroAchievements Settings

        public string RaUsername
        {
            get => _raUsername;
            set => SetValue(ref _raUsername, value ?? string.Empty);
        }

        public string RaWebApiKey
        {
            get => _raWebApiKey;
            set => SetValue(ref _raWebApiKey, value ?? string.Empty);
        }

        public string RaRarityStats
        {
            get => _raRarityStats;
            set
            {
                var mode = (value ?? string.Empty).Trim();
                if (string.Equals(mode, "hardcore", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(mode, "combined", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(mode, "casual", StringComparison.OrdinalIgnoreCase))
                {
                    SetValue(ref _raRarityStats, mode.ToLowerInvariant());
                }
                else
                {
                    SetValue(ref _raRarityStats, "casual");
                }
            }
        }

        public int HashIndexMaxAgeDays
        {
            get => _hashIndexMaxAgeDays;
            set => SetValue(ref _hashIndexMaxAgeDays, Math.Max(1, value));
        }

        public bool EnableArchiveScanning
        {
            get => _enableArchiveScanning;
            set => SetValue(ref _enableArchiveScanning, value);
        }

        public bool EnableDiscHashing
        {
            get => _enableDiscHashing;
            set => SetValue(ref _enableDiscHashing, value);
        }

        /// <summary>
        /// Enable name-based fallback for RetroAchievements when hash matching fails.
        /// </summary>
        public bool EnableRaNameFallback
        {
            get => _enableRaNameFallback;
            set => SetValue(ref _enableRaNameFallback, value);
        }

        /// <summary>
        /// Persisted visibility state for shared achievement DataGrid columns.
        /// Key is a stable column identifier, value indicates whether the column is visible.
        /// </summary>
        public Dictionary<string, bool> DataGridColumnVisibility
        {
            get => _dataGridColumnVisibility;
            set
            {
                var normalized = value != null
                    ? new Dictionary<string, bool>(value, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                SetValue(ref _dataGridColumnVisibility, normalized);
            }
        }

        /// <summary>
        /// Legacy shared width storage for achievement DataGrid columns.
        /// New installs use scope-specific maps; this remains for backward compatibility fallback.
        /// </summary>
        public Dictionary<string, double> DataGridColumnWidths
        {
            get => _dataGridColumnWidths;
            set
            {
                var normalized = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                if (value != null)
                {
                    foreach (var pair in value)
                    {
                        if (!string.IsNullOrWhiteSpace(pair.Key) &&
                            !double.IsNaN(pair.Value) &&
                            !double.IsInfinity(pair.Value) &&
                            pair.Value > 0)
                        {
                            normalized[pair.Key] = pair.Value;
                        }
                    }
                }

                SetValue(ref _dataGridColumnWidths, normalized);
            }
        }

        /// <summary>
        /// Persisted widths for sidebar achievement columns (recent + selected game panels).
        /// Key is a stable column identifier, value is pixel width.
        /// </summary>
        public Dictionary<string, double> SidebarAchievementColumnWidths
        {
            get => _sidebarAchievementColumnWidths;
            set
            {
                var normalized = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                if (value != null)
                {
                    foreach (var pair in value)
                    {
                        if (!string.IsNullOrWhiteSpace(pair.Key) &&
                            !double.IsNaN(pair.Value) &&
                            !double.IsInfinity(pair.Value) &&
                            pair.Value > 0)
                        {
                            normalized[pair.Key] = pair.Value;
                        }
                    }
                }

                SetValue(ref _sidebarAchievementColumnWidths, normalized);
            }
        }

        /// <summary>
        /// Persisted widths for the single-game achievement columns.
        /// Key is a stable column identifier, value is pixel width.
        /// </summary>
        public Dictionary<string, double> SingleGameColumnWidths
        {
            get => _singleGameColumnWidths;
            set
            {
                var normalized = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                if (value != null)
                {
                    foreach (var pair in value)
                    {
                        if (!string.IsNullOrWhiteSpace(pair.Key) &&
                            !double.IsNaN(pair.Value) &&
                            !double.IsInfinity(pair.Value) &&
                            pair.Value > 0)
                        {
                            normalized[pair.Key] = pair.Value;
                        }
                    }
                }

                SetValue(ref _singleGameColumnWidths, normalized);
            }
        }

        /// <summary>
        /// Persisted visibility state for games overview columns in the sidebar.
        /// Key is a stable column identifier, value indicates whether the column is visible.
        /// </summary>
        public Dictionary<string, bool> GamesOverviewColumnVisibility
        {
            get => _gamesOverviewColumnVisibility;
            set
            {
                var normalized = value != null
                    ? new Dictionary<string, bool>(value, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                SetValue(ref _gamesOverviewColumnVisibility, normalized);
            }
        }

        /// <summary>
        /// Persisted widths for games overview columns in the sidebar.
        /// Key is a stable column identifier, value is pixel width.
        /// </summary>
        public Dictionary<string, double> GamesOverviewColumnWidths
        {
            get => _gamesOverviewColumnWidths;
            set
            {
                var normalized = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                if (value != null)
                {
                    foreach (var pair in value)
                    {
                        if (!string.IsNullOrWhiteSpace(pair.Key) &&
                            !double.IsNaN(pair.Value) &&
                            !double.IsInfinity(pair.Value) &&
                            pair.Value > 0)
                        {
                            normalized[pair.Key] = pair.Value;
                        }
                    }
                }

                SetValue(ref _gamesOverviewColumnWidths, normalized);
            }
        }

        /// <summary>
        /// Tracks whether the Points column was auto-enabled once due to Epic achievement data.
        /// Prevents future refreshes from repeatedly overriding user visibility preferences.
        /// </summary>
        public bool PointsColumnAutoEnabled
        {
            get => _pointsColumnAutoEnabled;
            set => SetValue(ref _pointsColumnAutoEnabled, value);
        }

        #endregion

        #region Rarity Threshold Settings

        /// <summary>
        /// Percentage threshold for ultra-rare achievements (≤ this value = ultra-rare).
        /// </summary>
        public double UltraRareThreshold
        {
            get => _ultraRareThreshold;
            set => SetValue(ref _ultraRareThreshold, Math.Max(0.1, Math.Min(value, RareThreshold - 0.1)));
        }

        /// <summary>
        /// Percentage threshold for rare achievements (≤ this value and > ultra-rare = rare).
        /// </summary>
        public double RareThreshold
        {
            get => _rareThreshold;
            set => SetValue(ref _rareThreshold, Math.Max(UltraRareThreshold + 0.1, Math.Min(value, UncommonThreshold - 0.1)));
        }

        /// <summary>
        /// Percentage threshold for uncommon achievements (≤ this value and > rare = uncommon).
        /// Anything above this is common.
        /// </summary>
        public double UncommonThreshold
        {
            get => _uncommonThreshold;
            set => SetValue(ref _uncommonThreshold, Math.Max(RareThreshold + 0.1, Math.Min(value, 99.9)));
        }

        /// <summary>
        /// Indicates whether the user has completed the first-time setup flow.
        /// When false, the sidebar shows a landing page guiding users through initial configuration.
        /// </summary>
        public bool FirstTimeSetupCompleted
        {
            get => _firstTimeSetupCompleted;
            set => SetValue(ref _firstTimeSetupCompleted, value);
        }

        /// <summary>
        /// Indicates whether the user has seen the theme migration landing page.
        /// When false, the sidebar always shows the landing page to promote theme migration.
        /// </summary>
        public bool SeenThemeMigration
        {
            get => _seenThemeMigration;
            set => SetValue(ref _seenThemeMigration, value);
        }

        #endregion

        #region Clone Method

        /// <summary>
        /// Creates a deep copy of this PersistedSettings instance.
        /// </summary>
        public PersistedSettings Clone()
        {
            return new PersistedSettings
            {
                SteamUserId = this.SteamUserId,
                GogUserId = this.GogUserId,
                EpicAccountId = this.EpicAccountId,
                EpicAccessToken = this.EpicAccessToken,
                EpicRefreshToken = this.EpicRefreshToken,
                EpicTokenType = this.EpicTokenType,
                EpicTokenExpiryUtc = this.EpicTokenExpiryUtc,
                EpicRefreshTokenExpiryUtc = this.EpicRefreshTokenExpiryUtc,
                SteamApiKey = this.SteamApiKey,
                GlobalLanguage = this.GlobalLanguage,
                SteamEnabled = this.SteamEnabled,
                EpicEnabled = this.EpicEnabled,
                GogEnabled = this.GogEnabled,
                PsnEnabled = this.PsnEnabled,
                RetroAchievementsEnabled = this.RetroAchievementsEnabled,
                EnablePeriodicUpdates = this.EnablePeriodicUpdates,
                PeriodicUpdateHours = this.PeriodicUpdateHours,
                EnableNotifications = this.EnableNotifications,
                NotifyPeriodicUpdates = this.NotifyPeriodicUpdates,
                NotifyOnRebuild = this.NotifyOnRebuild,
                RecentRefreshGamesCount = this.RecentRefreshGamesCount,
                ShowHiddenIcon = this.ShowHiddenIcon,
                ShowHiddenTitle = this.ShowHiddenTitle,
                ShowHiddenDescription = this.ShowHiddenDescription,
                UseCoverImages = this.UseCoverImages,
                IncludeUnplayedGames = this.IncludeUnplayedGames,
                ScanDelayMs = this.ScanDelayMs,
                MaxRetryAttempts = this.MaxRetryAttempts,
                RaUsername = this.RaUsername,
                RaWebApiKey = this.RaWebApiKey,
                RaRarityStats = this.RaRarityStats,
                HashIndexMaxAgeDays = this.HashIndexMaxAgeDays,
                EnableArchiveScanning = this.EnableArchiveScanning,
                EnableDiscHashing = this.EnableDiscHashing,
                EnableRaNameFallback = this.EnableRaNameFallback,
                DataGridColumnVisibility = this.DataGridColumnVisibility != null
                    ? new Dictionary<string, bool>(this.DataGridColumnVisibility, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
                DataGridColumnWidths = this.DataGridColumnWidths != null
                    ? new Dictionary<string, double>(this.DataGridColumnWidths, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
                SidebarAchievementColumnWidths = this.SidebarAchievementColumnWidths != null
                    ? new Dictionary<string, double>(this.SidebarAchievementColumnWidths, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
                SingleGameColumnWidths = this.SingleGameColumnWidths != null
                    ? new Dictionary<string, double>(this.SingleGameColumnWidths, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
                GamesOverviewColumnVisibility = this.GamesOverviewColumnVisibility != null
                    ? new Dictionary<string, bool>(this.GamesOverviewColumnVisibility, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
                GamesOverviewColumnWidths = this.GamesOverviewColumnWidths != null
                    ? new Dictionary<string, double>(this.GamesOverviewColumnWidths, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
                PointsColumnAutoEnabled = this.PointsColumnAutoEnabled,
                UltraRareThreshold = this.UltraRareThreshold,
                RareThreshold = this.RareThreshold,
                UncommonThreshold = this.UncommonThreshold,
                FirstTimeSetupCompleted = this.FirstTimeSetupCompleted,
                SeenThemeMigration = this.SeenThemeMigration
            };
        }

        #endregion
    }
}
