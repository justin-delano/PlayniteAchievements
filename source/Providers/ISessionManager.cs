using PlayniteAchievements.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers
{
    /// <summary>
    /// Common interface for session managers that handle authentication for game providers.
    /// Auth state is always probed from the source of truth before any data provider work.
    /// </summary>
    public interface ISessionManager
    {
        /// <summary>
        /// Unique key identifying this provider (e.g., "Steam", "GOG", "Epic").
        /// </summary>
        string ProviderKey { get; }

        /// <summary>
        /// Probes the current authentication state from the source of truth.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The authentication probe result.</returns>
        Task<AuthProbeResult> ProbeAuthStateAsync(CancellationToken ct);

        /// <summary>
        /// Performs interactive authentication via WebView or browser.
        /// If forceInteractive is false, first checks if already authenticated.
        /// </summary>
        /// <param name="forceInteractive">If true, clears existing session and forces login.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <param name="progress">Optional progress reporter for auth steps.</param>
        /// <returns>The authentication result.</returns>
        Task<AuthProbeResult> AuthenticateInteractiveAsync(
            bool forceInteractive,
            CancellationToken ct,
            IProgress<AuthProgressStep> progress = null);

        /// <summary>
        /// Clears the current session, removing all stored authentication data.
        /// </summary>
        void ClearSession();
    }
}
