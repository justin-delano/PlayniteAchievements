using Playnite.SDK.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PlayniteAchievements.Services.Workshop
{
    /// <summary>How confidently a Workshop game-data item was matched to a library game.</summary>
    public enum WorkshopGameMatchConfidence
    {
        None,
        /// <summary>A provider key plus provider game id matched the cache.</summary>
        Provider,
        /// <summary>Only the name (and platform, when both sides had one) matched, uniquely.</summary>
        Name
    }

    public sealed class WorkshopGameMatch
    {
        public WorkshopGameMatch(Guid playniteGameId, string gameName, WorkshopGameMatchConfidence confidence)
        {
            PlayniteGameId = playniteGameId;
            GameName = gameName;
            Confidence = confidence;
        }

        public Guid PlayniteGameId { get; }

        public string GameName { get; }

        public WorkshopGameMatchConfidence Confidence { get; }
    }

    /// <summary>
    /// Finds the library game a shared .pa file belongs to from its <see cref="PortableGameKey"/>
    /// list: first by the servicing provider's identity against cached game data, then by a
    /// unique normalized-name match against the Playnite library. The caller decides what to do
    /// with a name-only match or no match (ask the user).
    /// </summary>
    public sealed class WorkshopGameMatcher
    {
        private readonly Func<IEnumerable<GameAchievementData>> _getCachedGames;
        private readonly Func<IEnumerable<Game>> _getLibraryGames;
        private readonly object _sync = new object();
        private Snapshot _snapshot;

        public WorkshopGameMatcher(
            Func<IEnumerable<GameAchievementData>> getCachedGames,
            Func<IEnumerable<Game>> getLibraryGames)
        {
            _getCachedGames = getCachedGames ?? (() => Enumerable.Empty<GameAchievementData>());
            _getLibraryGames = getLibraryGames ?? (() => Enumerable.Empty<Game>());
        }

        /// <summary>
        /// Drops the indexed snapshot, so the next lookup reads the achievement cache and the
        /// library again. Between resets every lookup uses one snapshot: reading the whole cache
        /// for each item is what stalled the Workshop list.
        /// </summary>
        public void Reset()
        {
            lock (_sync)
            {
                _snapshot = null;
            }
        }

        public WorkshopGameMatch Match(IReadOnlyList<PortableGameKey> keys)
        {
            if (keys == null || keys.Count == 0)
            {
                return null;
            }

            var snapshot = GetSnapshot();
            foreach (var key in keys.Where(key => key != null && !string.IsNullOrWhiteSpace(key.ProviderKey)))
            {
                var hit = snapshot.FindByProvider(key);
                if (hit != null)
                {
                    return hit;
                }
            }

            foreach (var key in keys.Where(key => key != null && !string.IsNullOrWhiteSpace(key.Name)))
            {
                var match = MatchByName(snapshot, key.Name, key.Platform);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        /// <summary>Library games whose normalized name equals the key's, for a pick list.</summary>
        public IReadOnlyList<Game> Candidates(IReadOnlyList<PortableGameKey> keys)
        {
            var names = new HashSet<string>(
                (keys ?? Array.Empty<PortableGameKey>())
                    .Where(key => key != null && !string.IsNullOrWhiteSpace(key.Name))
                    .Select(key => Normalize(key.Name)),
                StringComparer.Ordinal);
            if (names.Count == 0)
            {
                return Array.Empty<Game>();
            }

            var snapshot = GetSnapshot();
            return names
                .SelectMany(name => snapshot.GamesNamed(name))
                .Distinct()
                .OrderBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private Snapshot GetSnapshot()
        {
            lock (_sync)
            {
                return _snapshot ?? (_snapshot = Snapshot.Build(SafeEnumerate(_getCachedGames), SafeEnumerate(_getLibraryGames)));
            }
        }

        private static WorkshopGameMatch MatchByName(Snapshot snapshot, string name, string platform)
        {
            var normalized = Normalize(name);
            if (normalized.Length == 0)
            {
                return null;
            }

            var byName = snapshot.GamesNamed(normalized);
            if (byName.Count == 0)
            {
                return null;
            }

            // Several library entries share the name (editions, platforms): prefer the one on the
            // same platform; otherwise the match is ambiguous and the user must pick.
            if (byName.Count > 1 && !string.IsNullOrWhiteSpace(platform))
            {
                byName = byName
                    .Where(game => game.Platforms != null &&
                                   game.Platforms.Any(p => string.Equals(p?.Name, platform, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }

            return byName.Count == 1
                ? new WorkshopGameMatch(byName[0].Id, byName[0].Name, WorkshopGameMatchConfidence.Name)
                : null;
        }

        /// <summary>One read of the achievement cache and the library, indexed for the lookups above.</summary>
        private sealed class Snapshot
        {
            private readonly Dictionary<string, GameAchievementData> _byAppId = new Dictionary<string, GameAchievementData>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, GameAchievementData> _byProviderGameKey = new Dictionary<string, GameAchievementData>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, List<Game>> _byName = new Dictionary<string, List<Game>>(StringComparer.Ordinal);

            public static Snapshot Build(IEnumerable<GameAchievementData> cached, IEnumerable<Game> library)
            {
                var snapshot = new Snapshot();
                foreach (var data in cached)
                {
                    if (data?.PlayniteGameId == null || data.PlayniteGameId == Guid.Empty || string.IsNullOrWhiteSpace(data.ProviderKey))
                    {
                        continue;
                    }

                    // First entry wins, as the linear scan this replaces did.
                    if (data.AppId > 0)
                    {
                        var key = data.ProviderKey + "|" + data.AppId;
                        if (!snapshot._byAppId.ContainsKey(key))
                        {
                            snapshot._byAppId[key] = data;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(data.ProviderGameKey))
                    {
                        var key = data.ProviderKey + "|" + data.ProviderGameKey;
                        if (!snapshot._byProviderGameKey.ContainsKey(key))
                        {
                            snapshot._byProviderGameKey[key] = data;
                        }
                    }
                }

                foreach (var game in library)
                {
                    var normalized = game == null ? string.Empty : Normalize(game.Name);
                    if (normalized.Length == 0)
                    {
                        continue;
                    }

                    if (!snapshot._byName.TryGetValue(normalized, out var list))
                    {
                        list = new List<Game>();
                        snapshot._byName[normalized] = list;
                    }

                    list.Add(game);
                }

                return snapshot;
            }

            public WorkshopGameMatch FindByProvider(PortableGameKey key)
            {
                GameAchievementData hit = null;
                if (key.ProviderGameId is int id && id > 0)
                {
                    _byAppId.TryGetValue(key.ProviderKey + "|" + id, out hit);
                }

                if (hit == null && !string.IsNullOrWhiteSpace(key.ProviderGameKey))
                {
                    _byProviderGameKey.TryGetValue(key.ProviderKey + "|" + key.ProviderGameKey, out hit);
                }

                return hit == null
                    ? null
                    : new WorkshopGameMatch(hit.PlayniteGameId.Value, hit.GameName, WorkshopGameMatchConfidence.Provider);
            }

            public List<Game> GamesNamed(string normalized)
            {
                return _byName.TryGetValue(normalized, out var list) ? list : new List<Game>();
            }
        }
        /// <summary>Lowercase letters and digits only, so punctuation and spacing differences do not matter.</summary>
        public static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            foreach (var ch in value.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch))
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString();
        }

        private static IEnumerable<T> SafeEnumerate<T>(Func<IEnumerable<T>> source)
        {
            try
            {
                return source() ?? Enumerable.Empty<T>();
            }
            catch (Exception)
            {
                return Enumerable.Empty<T>();
            }
        }
    }
}
