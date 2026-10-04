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

        public WorkshopGameMatcher(
            Func<IEnumerable<GameAchievementData>> getCachedGames,
            Func<IEnumerable<Game>> getLibraryGames)
        {
            _getCachedGames = getCachedGames ?? (() => Enumerable.Empty<GameAchievementData>());
            _getLibraryGames = getLibraryGames ?? (() => Enumerable.Empty<Game>());
        }

        public WorkshopGameMatch Match(IReadOnlyList<PortableGameKey> keys)
        {
            if (keys == null || keys.Count == 0)
            {
                return null;
            }

            var providerKeys = keys.Where(key => key != null && !string.IsNullOrWhiteSpace(key.ProviderKey)).ToList();
            if (providerKeys.Count > 0)
            {
                var cached = SafeEnumerate(_getCachedGames).Where(data => data?.PlayniteGameId != null && data.PlayniteGameId != Guid.Empty).ToList();
                foreach (var key in providerKeys)
                {
                    var hit = cached.FirstOrDefault(data => MatchesProvider(data, key));
                    if (hit != null)
                    {
                        return new WorkshopGameMatch(hit.PlayniteGameId.Value, hit.GameName, WorkshopGameMatchConfidence.Provider);
                    }
                }
            }

            foreach (var key in keys.Where(key => key != null && !string.IsNullOrWhiteSpace(key.Name)))
            {
                var match = MatchByName(key.Name, key.Platform);
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

            return SafeEnumerate(_getLibraryGames)
                .Where(game => game != null && names.Contains(Normalize(game.Name)))
                .OrderBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool MatchesProvider(GameAchievementData data, PortableGameKey key)
        {
            if (!string.Equals(data.ProviderKey, key.ProviderKey, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (key.ProviderGameId is int id && id > 0 && data.AppId == id)
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(key.ProviderGameKey) &&
                   string.Equals(data.ProviderGameKey, key.ProviderGameKey, StringComparison.OrdinalIgnoreCase);
        }

        private WorkshopGameMatch MatchByName(string name, string platform)
        {
            var normalized = Normalize(name);
            if (normalized.Length == 0)
            {
                return null;
            }

            var byName = SafeEnumerate(_getLibraryGames)
                .Where(game => game != null && Normalize(game.Name) == normalized)
                .ToList();
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
