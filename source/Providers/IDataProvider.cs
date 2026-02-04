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
        bool IsCapable(Game game);

        Task<RebuildPayload> ScanAsync(
            List<Game> gamesToScan,
            Action<ProviderScanUpdate> progressCallback,
            Action<GameAchievementData> onGameScanned,
            CancellationToken cancel);
    }
}
