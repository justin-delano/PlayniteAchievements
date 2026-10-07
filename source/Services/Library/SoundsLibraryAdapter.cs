using System;
using System.Collections.Generic;
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
    /// <remarks>
    /// The settings-backed form targets the global pack under <see cref="LibraryTargetKeys.Sounds"/>;
    /// the scope form targets a platform's or game's pack, which an apply gives its own copy of.
    /// </remarks>
    public sealed class SoundsLibraryAdapter : ISettingsLibraryAdapter, ILibraryAdapter<UnlockSoundScope>
    {
        private static readonly string[] AtomicPaths = { JsonThreeWayMerge.AnySegment };

        private readonly UnlockSoundPortableStore _store;
        private readonly Func<IEnumerable<UnlockSoundSettings>> _alsoReferenced;

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
            return Project(GlobalScope(target), ownedKeys);
        }

        public JObject Project(UnlockSoundScope target, IEnumerable<string> ownedKeys = null)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            return ProjectSounds(target.EffectiveSounds, TiersOf(ownedKeys));
        }

        public IReadOnlyCollection<string> OwnedKeys(string packagePath)
        {
            return _store.Inspect(packagePath).Select(tier => tier.ToString()).ToList();
        }

        public void ApplyReplace(string packagePath, PersistedSettings target)
        {
            ApplyReplace(packagePath, GlobalScope(target));
        }

        public void ApplyReplace(string packagePath, UnlockSoundScope target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var sounds = target.EffectiveSounds?.Clone() ?? UnlockSoundSettings.CreateDefault();
            _store.Import(packagePath, sounds);
            target.Write(sounds);
            Prune(sounds);
        }

        public void ApplyMerged(string packagePath, PersistedSettings target, JToken baseline, out int keptEdits)
        {
            ApplyMerged(packagePath, GlobalScope(target), baseline, out keptEdits);
        }

        public void ApplyMerged(string packagePath, UnlockSoundScope target, JToken baseline, out int keptEdits)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var sounds = target.EffectiveSounds?.Clone() ?? UnlockSoundSettings.CreateDefault();
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

            target.Write(sounds);
            Prune(sounds);
        }

        private static UnlockSoundScope GlobalScope(PersistedSettings target)
        {
            return UnlockSoundScope.ForSettings(target ?? throw new ArgumentNullException(nameof(target)), null);
        }

        /// <summary>The projection of <paramref name="sounds"/> over <paramref name="tiers"/>, or over every tier when null.</summary>
        public static JObject ProjectSounds(UnlockSoundSettings sounds, IEnumerable<UnlockSoundTier> tiers)
        {
            var result = new JObject();
            var wanted = tiers == null ? UnlockSoundTierExtensions.All : tiers.Distinct().ToArray();
            foreach (var tier in UnlockSoundTierExtensions.All.Where(wanted.Contains))
            {
                result[tier.ToString()] = LibraryFileTokens.FileValue(sounds?.GetPath(tier));
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
    }
}
