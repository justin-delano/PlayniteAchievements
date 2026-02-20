using PlayniteAchievements.Models;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers
{
    public interface IDataProvider
    {
        string ProviderName { get; }
        string ProviderKey { get; }
        string ProviderIconKey { get; }
        string ProviderColorHex { get; }
        bool IsCapable(Game game);

        /// <summary>
        /// Gets whether this provider has valid authentication credentials configured.
        /// This property checks credentials ONLY - it does not consider whether the provider is enabled.
        /// The enabled state is managed separately by ProviderRegistry.
        /// Each provider implements its own validation logic appropriate to its auth requirements.
        /// </summary>
        bool IsAuthenticated { get; }

        Task<RebuildPayload> RefreshAsync(
            List<Game> gamesToRefresh,
            Action<ProviderRefreshUpdate> progressCallback,
            Func<GameAchievementData, Task> OnGameRefreshed,
            CancellationToken cancel);
    }
}
