using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Sound;

namespace PlayniteAchievements.Services.Library
{
    /// <summary>
    /// The unlock sounds target: one property per tier the item carries, each the hash of the
    /// tier's file as <c>{"file":"sha256:..."}</c>, because every import copies the files to a
    /// new managed folder. Tiers a pack does not carry are not the item's and stay out of its
    /// projection. A merge imports the package into a copy of the settings and takes only the
    /// tiers the merge gives to the package; the managed folders nothing points into any more
    /// are then removed.
    /// </summary>
    public sealed class SoundsLibraryAdapter : ISettingsLibraryAdapter
    {
        private const string FileProperty = "file";
        private const string HashPrefix = "sha256:";
        private const string MissingPrefix = "missing:";

        private static readonly string[] AtomicPaths = { JsonThreeWayMerge.AnySegment };
        private static readonly ConcurrentDictionary<string, CachedHash> HashCache =
            new ConcurrentDictionary<string, CachedHash>(StringComparer.OrdinalIgnoreCase);

        private readonly UnlockSoundPortableStore _store;
        private readonly Func<IEnumerable<UnlockSoundSettings>> _alsoReferenced;

        private sealed class CachedHash
        {
            public CachedHash(long length, DateTime writeUtc, string hash)
            {
                Length = length;
                WriteUtc = writeUtc;
                Hash = hash;
            }

            public long Length { get; }

            public DateTime WriteUtc { get; }

            public string Hash { get; }
        }

        /// <param name="store">The portable store that imports packages into managed storage.</param>
        /// <param name="alsoReferenced">
        /// Other sound settings whose files must survive a prune, such as the settings edit
        /// snapshot that a Cancel restores; null when there are none.
        /// </param>
        public SoundsLibraryAdapter(UnlockSoundPortableStore store, Func<IEnumerable<UnlockSoundSettings>> alsoReferenced = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _alsoReferenced = alsoReferenced;
        }

        public LibraryItemKind Kind => LibraryItemKind.Sounds;

        public string TargetKey => LibraryTargetKeys.Sounds;

        public JObject Project(PersistedSettings target, IEnumerable<string> ownedKeys = null)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            return ProjectSounds(target.UnlockSounds, TiersOf(ownedKeys));
        }

        public IReadOnlyCollection<string> OwnedKeys(string packagePath)
        {
            return _store.Inspect(packagePath).Select(tier => tier.ToString()).ToList();
        }

        public void ApplyReplace(string packagePath, PersistedSettings target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var sounds = target.UnlockSounds ?? UnlockSoundSettings.CreateDefault();
            _store.Import(packagePath, sounds);
            target.UnlockSounds = sounds;
            Prune(sounds);
        }

        public void ApplyMerged(string packagePath, PersistedSettings target, JToken baseline, out int keptEdits)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var sounds = target.UnlockSounds ?? UnlockSoundSettings.CreateDefault();
            var incomingSounds = sounds.Clone();
            var carried = _store.Import(packagePath, incomingSounds);
            var incoming = ProjectSounds(incomingSounds, carried);

            var tiers = new HashSet<UnlockSoundTier>(carried);
            foreach (var tier in TiersOf((baseline as JObject)?.Properties().Select(property => property.Name)) ?? Enumerable.Empty<UnlockSoundTier>())
            {
                tiers.Add(tier);
            }

            var current = ProjectSounds(sounds, tiers);
            var merged = JsonThreeWayMerge.Merge(baseline, current, incoming, AtomicPaths, out keptEdits) as JObject ?? new JObject();

            foreach (var tier in tiers)
            {
                var name = tier.ToString();
                var mergedValue = merged.GetValue(name, StringComparison.OrdinalIgnoreCase);
                if (JsonThreeWayMerge.SameValue(mergedValue, current.GetValue(name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (JsonThreeWayMerge.SameValue(mergedValue, null))
                {
                    // The new version dropped a tier the user left as it was.
                    sounds.SetPath(tier, null);
                }
                else if (JsonThreeWayMerge.SameValue(mergedValue, incoming.GetValue(name, StringComparison.OrdinalIgnoreCase)))
                {
                    sounds.SetPath(tier, incomingSounds.GetPath(tier));
                }
            }

            target.UnlockSounds = sounds;
            Prune(sounds);
        }

        /// <summary>The projection of <paramref name="sounds"/> over <paramref name="tiers"/>, or over every tier when null.</summary>
        public static JObject ProjectSounds(UnlockSoundSettings sounds, IEnumerable<UnlockSoundTier> tiers)
        {
            var result = new JObject();
            var wanted = tiers == null ? UnlockSoundTierExtensions.All : tiers.Distinct().ToArray();
            foreach (var tier in UnlockSoundTierExtensions.All.Where(wanted.Contains))
            {
                var path = sounds?.GetPath(tier);
                result[tier.ToString()] = string.IsNullOrWhiteSpace(path)
                    ? null
                    : new JObject { [FileProperty] = FileToken(path) };
            }

            return result;
        }

        private void Prune(UnlockSoundSettings sounds)
        {
            var keep = new List<UnlockSoundSettings> { sounds };
            if (_alsoReferenced != null)
            {
                keep.AddRange((_alsoReferenced() ?? Enumerable.Empty<UnlockSoundSettings>()).Where(other => other != null));
            }

            _store.PruneUnreferenced(keep);
        }

        private static IReadOnlyList<UnlockSoundTier> TiersOf(IEnumerable<string> names)
        {
            if (names == null)
            {
                return null;
            }

            var tiers = new List<UnlockSoundTier>();
            foreach (var name in names)
            {
                if (Enum.TryParse(name, ignoreCase: true, out UnlockSoundTier tier) && !tiers.Contains(tier))
                {
                    tiers.Add(tier);
                }
            }

            return tiers;
        }

        /// <summary>The content hash of a sound file, cached by path, length and write time.</summary>
        private static string FileToken(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    return MissingPrefix + path.Trim().ToLowerInvariant();
                }

                if (HashCache.TryGetValue(info.FullName, out var cached)
                    && cached.Length == info.Length
                    && cached.WriteUtc == info.LastWriteTimeUtc)
                {
                    return cached.Hash;
                }

                var hash = HashPrefix + LibraryStore.HashFile(info.FullName);
                HashCache[info.FullName] = new CachedHash(info.Length, info.LastWriteTimeUtc, hash);
                return hash;
            }
            catch (Exception)
            {
                return MissingPrefix + path.Trim().ToLowerInvariant();
            }
        }
    }
}
