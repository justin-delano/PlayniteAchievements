using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Workshop;

namespace PlayniteAchievements.Services.Library
{
    /// <summary>
    /// The colors target: the rarity, completed and trophy colors, the per-provider colors and
    /// the resource overrides, as <see cref="ColorPackPortableStore.Capture"/> reads them. A
    /// color pack owns all three, so the projection is always whole. Each rarity color and each
    /// provider color merges on its own; a resource override (mode and value) merges as one.
    /// </summary>
    public sealed class ColorsLibraryAdapter : ISettingsLibraryAdapter
    {
        private const string RarityColorsKey = nameof(ColorPackFile.RarityColors);
        private const string ProviderColorsKey = nameof(ColorPackFile.ProviderColorOverrides);
        private const string ResourceOverridesKey = nameof(ColorPackFile.ResourceOverrides);

        private static readonly string[] AtomicPaths = { ResourceOverridesKey + "." + JsonThreeWayMerge.AnySegment };

        private static readonly JsonSerializer Serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            Converters = { new StringEnumConverter() }
        });

        private readonly ColorPackPortableStore _store;

        public ColorsLibraryAdapter(ColorPackPortableStore store = null)
        {
            _store = store ?? new ColorPackPortableStore();
        }

        public LibraryItemKind Kind => LibraryItemKind.Colors;

        public string TargetKey => LibraryTargetKeys.Colors;

        public JObject Project(PersistedSettings target, IEnumerable<string> ownedKeys = null)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            return ToProjection(ColorPackPortableStore.Capture(target));
        }

        public IReadOnlyCollection<string> OwnedKeys(string packagePath) => null;

        public void ApplyReplace(string packagePath, PersistedSettings target)
        {
            _store.Import(packagePath, target ?? throw new ArgumentNullException(nameof(target)));
        }

        public void ApplyMerged(string packagePath, PersistedSettings target, JToken baseline, out int keptEdits)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var incoming = ToProjection(_store.Read(packagePath));
            var merged = JsonThreeWayMerge.Merge(baseline, Project(target), incoming, AtomicPaths, out keptEdits);
            ColorPackPortableStore.Apply(FromProjection(merged), target);
        }

        /// <summary>A color pack as its projection, with properties in a stable order.</summary>
        public static JObject ToProjection(ColorPackFile colors)
        {
            var rarity = new JObject();
            if (colors?.RarityColors != null)
            {
                foreach (var property in JObject.FromObject(colors.RarityColors, Serializer).Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    rarity[property.Name] = property.Value;
                }
            }

            var providers = new JObject();
            foreach (var pair in (colors?.ProviderColorOverrides ?? new Dictionary<string, string>())
                     .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                     .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                providers[pair.Key] = pair.Value;
            }

            var resources = new JObject();
            foreach (var pair in (colors?.ResourceOverrides ?? new Dictionary<string, ResourceOverrideSetting>())
                     .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null)
                     .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                resources[pair.Key] = new JObject
                {
                    [nameof(ResourceOverrideSetting.Mode)] = pair.Value.Mode.ToString(),
                    [nameof(ResourceOverrideSetting.CustomValue)] = pair.Value.CustomValue
                };
            }

            return new JObject
            {
                [RarityColorsKey] = rarity,
                [ProviderColorsKey] = providers,
                [ResourceOverridesKey] = resources
            };
        }

        /// <summary>A projection back as a color pack, for <see cref="ColorPackPortableStore.Apply"/> to validate.</summary>
        public static ColorPackFile FromProjection(JToken projection)
        {
            var obj = projection as JObject ?? new JObject();
            var rarity = RarityColorSettings.CreateDefault();
            if (obj.GetValue(RarityColorsKey, StringComparison.OrdinalIgnoreCase) is JObject rarityObject)
            {
                using (var reader = rarityObject.CreateReader())
                {
                    Serializer.Populate(reader, rarity);
                }
            }

            var providers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (obj.GetValue(ProviderColorsKey, StringComparison.OrdinalIgnoreCase) is JObject providerObject)
            {
                foreach (var property in providerObject.Properties())
                {
                    if (property.Value.Type == JTokenType.String)
                    {
                        providers[property.Name] = (string)property.Value;
                    }
                }
            }

            var resources = new Dictionary<string, ResourceOverrideSetting>(StringComparer.OrdinalIgnoreCase);
            if (obj.GetValue(ResourceOverridesKey, StringComparison.OrdinalIgnoreCase) is JObject resourceObject)
            {
                foreach (var property in resourceObject.Properties())
                {
                    if (property.Value is JObject setting)
                    {
                        resources[property.Name] = setting.ToObject<ResourceOverrideSetting>(Serializer);
                    }
                }
            }

            return new ColorPackFile
            {
                Kind = ColorPackFile.ColorsKind,
                Version = ColorPackPortableStore.CurrentVersion,
                RarityColors = rarity,
                ProviderColorOverrides = providers,
                ResourceOverrides = resources
            };
        }
    }
}
