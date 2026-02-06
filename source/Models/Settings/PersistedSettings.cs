using System;
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
        private string _steamApiKey;
        private string _steamLanguage = "english";
        private bool _enablePeriodicUpdates = true;
        private int _periodicUpdateHours = 6;
        private bool _enableNotifications = true;
        private bool _notifyPeriodicUpdates = true;
        private bool _notifyOnRebuild = true;
        private int _quickRefreshRecentGamesCount = 10;
        private bool _hideAchievementsLockedForSelf = false;
        private bool _useCoverImages = false;
        private bool _ignoreUnplayedGames = false;
        private bool _enableDiagnostics = false;
        private int _scanDelayMs = 200;
        private int _maxRetryAttempts = 3;

        private string _raUsername;
        private string _raWebApiKey;
        private string _raRarityStats = "casual";
        private int _hashIndexMaxAgeDays = 30;
        private bool _enableArchiveScanning = true;
        private bool _enableDiscHashing = true;
        private bool _enableRaNameFallback = true;

        private double _ultraRareThreshold = 5;
        private double _rareThreshold = 20;
        private double _uncommonThreshold = 50;
        private bool _firstTimeSetupCompleted = false;

        #endregion

        #region Steam Settings

        public string SteamUserId
        {
            get => _steamUserId;
            set => SetValue(ref _steamUserId, value);
        }

        /// <summary>
        /// Optional Steam Web API key used for owned games, friends and player summaries.
        /// </summary>
        public string SteamApiKey
        {
            get => _steamApiKey;
            set => SetValue(ref _steamApiKey, value ?? string.Empty);
        }

        /// <summary>
        /// Preferred Steam language code for schema/achievement text (e.g. "english", "spanish").
        /// </summary>
        public string SteamLanguage
        {
            get => _steamLanguage;
            set => SetValue(ref _steamLanguage, value);
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
        /// Maximum recent games to scan when using Quick Refresh.
        /// </summary>
        public int QuickRefreshRecentGamesCount
        {
            get => _quickRefreshRecentGamesCount;
            set => SetValue(ref _quickRefreshRecentGamesCount, Math.Max(1, value));
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
        /// When true, achievements not unlocked by the current account will show a locked icon
        /// and their description will be hidden in the feed.
        /// </summary>
        public bool HideAchievementsLockedForSelf
        {
            get => _hideAchievementsLockedForSelf;
            set => SetValue(ref _hideAchievementsLockedForSelf, value);
        }

        /// <summary>
        /// When true, use Playnite cover images instead of icons/logos in the games list.
        /// </summary>
        public bool UseCoverImages
        {
            get => _useCoverImages;
            set => SetValue(ref _useCoverImages, value);
        }

        public bool IgnoreUnplayedGames
        {
            get => _ignoreUnplayedGames;
            set => SetValue(ref _ignoreUnplayedGames, value);
        }

        /// <summary>
        /// Enables debug-level performance tracing logs (timings) for diagnosing UI hangs.
        /// </summary>
        public bool EnableDiagnostics
        {
            get => _enableDiagnostics;
            set => SetValue(ref _enableDiagnostics, value);
        }

        /// <summary>
        /// Delay in milliseconds between scanning each game to avoid rate limiting.
        /// Default is 200ms. When transient failures occur, exponential backoff with jitter is applied.
        /// Set to 0 for fastest scanning (exponential backoff will still apply on errors).
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
                SteamApiKey = this.SteamApiKey,
                SteamLanguage = this.SteamLanguage,
                EnablePeriodicUpdates = this.EnablePeriodicUpdates,
                PeriodicUpdateHours = this.PeriodicUpdateHours,
                EnableNotifications = this.EnableNotifications,
                NotifyPeriodicUpdates = this.NotifyPeriodicUpdates,
                NotifyOnRebuild = this.NotifyOnRebuild,
                QuickRefreshRecentGamesCount = this.QuickRefreshRecentGamesCount,
                HideAchievementsLockedForSelf = this.HideAchievementsLockedForSelf,
                UseCoverImages = this.UseCoverImages,
                IgnoreUnplayedGames = this.IgnoreUnplayedGames,
                EnableDiagnostics = this.EnableDiagnostics,
                ScanDelayMs = this.ScanDelayMs,
                MaxRetryAttempts = this.MaxRetryAttempts,
                RaUsername = this.RaUsername,
                RaWebApiKey = this.RaWebApiKey,
                RaRarityStats = this.RaRarityStats,
                HashIndexMaxAgeDays = this.HashIndexMaxAgeDays,
                EnableArchiveScanning = this.EnableArchiveScanning,
                EnableDiscHashing = this.EnableDiscHashing,
                EnableRaNameFallback = this.EnableRaNameFallback,
                UltraRareThreshold = this.UltraRareThreshold,
                RareThreshold = this.RareThreshold,
                UncommonThreshold = this.UncommonThreshold,
                FirstTimeSetupCompleted = this.FirstTimeSetupCompleted
            };
        }

        #endregion
    }
}
