using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;

namespace PlayniteAchievements.Services.Workshop
{
    /// <summary>
    /// Looks for newer versions of the library's Workshop items and of the Workshop game data
    /// applied to games on a slow clock: a few minutes after startup, then hourly. One index
    /// fetch per tick, nothing when neither holds a Workshop item, and a single Playnite
    /// notification that opens the Library page; the same item version is never
    /// announced twice in a session. The first tick also resolves the system proxy for the
    /// Workshop host off the UI thread, which is the slow step .NET Framework otherwise runs
    /// synchronously inside the first request.
    /// </summary>
    public sealed class WorkshopUpdateChecker : IDisposable
    {
        public const string NotificationId = "PlayAch.WorkshopUpdates";
        public static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(3);
        public static readonly TimeSpan Period = TimeSpan.FromHours(1);

        private readonly PlayniteAchievementsPlugin _plugin;
        private readonly ILogger _logger;
        private readonly HashSet<string> _announced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Timer _timer;
        private int _running;

        public WorkshopUpdateChecker(PlayniteAchievementsPlugin plugin, ILogger logger = null)
        {
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            _logger = logger;
        }

        public void Start()
        {
            if (_timer != null)
            {
                return;
            }

            _timer = new Timer(_ => Tick(), null, InitialDelay, Period);
        }

        public void Stop()
        {
            _timer?.Dispose();
            _timer = null;
        }

        public void Dispose() => Stop();

        /// <summary>
        /// Resolves the proxy for <paramref name="url"/> so the result is cached before any UI
        /// thread request needs it. WPAD lookups can take ten seconds when they time out.
        /// </summary>
        public static void WarmProxy(string url)
        {
            try
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    WebRequest.DefaultWebProxy?.GetProxy(uri);
                }
            }
            catch
            {
                // Best effort: a failed warm-up only means the first request pays the cost.
            }
        }

        /// <summary>The index items that are newer than the Workshop items in the library.</summary>
        public static IReadOnlyList<WorkshopItem> FindUpdates(WorkshopIndexFile index, IEnumerable<Library.LibraryItem> library)
        {
            var workshopItems = (library ?? Enumerable.Empty<Library.LibraryItem>())
                .Where(item => item != null && item.IsWorkshop && !string.IsNullOrWhiteSpace(item.WorkshopItemId))
                .ToList();
            if (index?.Items == null || workshopItems.Count == 0)
            {
                return Array.Empty<WorkshopItem>();
            }

            return index.Items
                .Where(item => item != null && workshopItems.Any(owned =>
                    string.Equals(owned.WorkshopItemId, item.Id, StringComparison.OrdinalIgnoreCase)
                    && WorkshopIdentityStore.IsNewer(item.Version, owned.Version)))
                .ToList();
        }

        private async void Tick()
        {
            if (Interlocked.Exchange(ref _running, 1) == 1)
            {
                return;
            }

            try
            {
                var client = _plugin.WorkshopClient;
                WarmProxy(client.IndexUrl);

                var library = _plugin.LibraryStore.Items;
                var gameData = _plugin.GameDataLinks.All.Values.ToList();
                if (!library.Any(item => item.IsWorkshop) && gameData.Count == 0)
                {
                    return;
                }

                var index = await client.FetchIndexAsync(CancellationToken.None).ConfigureAwait(false);
                var updates = FindUpdates(index, library)
                    .Concat(Library.GameDataLinkService.FindUpdates(index, gameData))
                    .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();
                if (updates.Count == 0)
                {
                    return;
                }

                var unannounced = updates.Count(item => _announced.Add(item.Id + "@" + item.Version));
                if (unannounced == 0)
                {
                    return;
                }

                var message = string.Format(ResourceProvider.GetString("LOCPlayAch_Workshop_UpdatesAvailable"), updates.Count);
                var dispatcher = _plugin.PlayniteApi?.MainView?.UIDispatcher;
                void Notify()
                {
                    _plugin.PlayniteApi?.Notifications?.Add(new NotificationMessage(
                        NotificationId,
                        message,
                        NotificationType.Info,
                        () => _plugin.OpenWorkshopSettings(Views.Settings.Workshop.WorkshopSettingsTab.LibraryPageKey)));
                }

                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    await dispatcher.InvokeAsync(Notify);
                }
                else
                {
                    Notify();
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug($"Workshop update check skipped: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }
        }
    }
}
