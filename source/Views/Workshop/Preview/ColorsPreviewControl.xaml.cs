using Playnite.SDK;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Providers;
using PlayniteAchievements.Services.UI;
using PlayniteAchievements.Services.Workshop.Preview;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PlayniteAchievements.Views.Workshop.Preview
{
    /// <summary>One read-only color of a previewed color set: a swatch, a label and the value text.</summary>
    public sealed class ColorSwatchRow
    {
        public ColorSwatchRow(string label, string value, bool isColor)
        {
            Label = label;
            Value = value ?? string.Empty;
            Swatch = isColor ? ParseBrush(value) : null;
        }

        public string Label { get; }

        public string Value { get; }

        /// <summary>The value as a brush, or null when it is not a color.</summary>
        public Brush Swatch { get; }

        public bool HasSwatch => Swatch != null;

        private static Brush ParseBrush(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            try
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value.Trim()));
                brush.Freeze();
                return brush;
            }
            catch (FormatException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// The colors of a previewed color set (<see cref="ColorsPreviewModel"/> as the DataContext),
    /// grouped as the settings show them: rarity and badge colors, platform colors, and theme
    /// resource overrides. Read-only; groups the set does not carry are hidden.
    /// </summary>
    public partial class ColorsPreviewControl : UserControl
    {
        public ColorsPreviewControl()
        {
            InitializeComponent();
            DataContextChanged += (sender, args) => Rebuild();
        }

        private void Rebuild()
        {
            var colors = (DataContext as ColorsPreviewModel)?.Colors;
            Fill(RarityGroup, RarityList, BuildRarityRows(colors?.RarityColors));
            Fill(ProviderGroup, ProviderList, BuildProviderRows(colors?.ProviderColorOverrides));
            Fill(ResourceGroup, ResourceList, BuildResourceRows(colors?.ResourceOverrides));
        }

        private static void Fill(FrameworkElement group, ItemsControl list, IReadOnlyList<ColorSwatchRow> rows)
        {
            list.ItemsSource = rows;
            group.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static IReadOnlyList<ColorSwatchRow> BuildRarityRows(RarityColorSettings rarity)
        {
            if (rarity == null)
            {
                return Array.Empty<ColorSwatchRow>();
            }

            ColorSwatchRow Row(string key, string value) => new ColorSwatchRow(ResourceProvider.GetString(key), value, isColor: true);

            return new[]
            {
                Row("LOCPlayAch_Rarity_Common", rarity.Common),
                Row("LOCPlayAch_Rarity_Uncommon", rarity.Uncommon),
                Row("LOCPlayAch_Rarity_Rare", rarity.Rare),
                Row("LOCPlayAch_Rarity_UltraRare", rarity.UltraRare),
                Row("LOCPlayAch_Settings_Appearance_GradientStart", rarity.CompletedStart),
                Row("LOCPlayAch_Settings_Appearance_GradientEnd", rarity.CompletedEnd),
                Row("LOCPlayAch_Trophy_Bronze", rarity.TrophyBronze),
                Row("LOCPlayAch_Trophy_Silver", rarity.TrophySilver),
                Row("LOCPlayAch_Trophy_Gold", rarity.TrophyGold),
                Row("LOCPlayAch_Trophy_Platinum", rarity.TrophyPlatinum)
            };
        }

        private static IReadOnlyList<ColorSwatchRow> BuildProviderRows(IReadOnlyDictionary<string, string> overrides)
        {
            return (overrides ?? new Dictionary<string, string>())
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .Select(pair => new ColorSwatchRow(ProviderRegistry.GetLocalizedName(pair.Key), pair.Value, isColor: true))
                .OrderBy(row => row.Label, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private static IReadOnlyList<ColorSwatchRow> BuildResourceRows(IReadOnlyDictionary<string, ResourceOverrideSetting> overrides)
        {
            if (overrides == null || overrides.Count == 0)
            {
                return Array.Empty<ColorSwatchRow>();
            }

            // Listed in the order the settings list the resources; a key the plugin no longer
            // knows keeps its raw name at the end.
            var descriptors = PlayAchResourceService.ResourceDescriptors;
            var rows = new List<ColorSwatchRow>();
            foreach (var descriptor in descriptors)
            {
                if (overrides.TryGetValue(descriptor.ResourceKey, out var setting) && TryValue(setting, out var value))
                {
                    var label = ResourceProvider.GetString(descriptor.DisplayName);
                    rows.Add(new ColorSwatchRow(
                        string.IsNullOrWhiteSpace(label) ? descriptor.ResourceKey : label,
                        value,
                        descriptor.ValueKind == ResourceOverrideValueKind.Brush));
                }
            }

            var known = new HashSet<string>(descriptors.Select(descriptor => descriptor.ResourceKey), StringComparer.OrdinalIgnoreCase);
            foreach (var pair in overrides.Where(pair => !known.Contains(pair.Key)))
            {
                if (TryValue(pair.Value, out var value))
                {
                    rows.Add(new ColorSwatchRow(pair.Key, value, isColor: true));
                }
            }

            return rows;
        }

        private static bool TryValue(ResourceOverrideSetting setting, out string value)
        {
            switch (setting?.Mode)
            {
                case ResourceOverrideMode.Transparent:
                    value = PlayAchResourceService.TransparentValue;
                    return true;
                case ResourceOverrideMode.Custom when !string.IsNullOrWhiteSpace(setting.CustomValue):
                    value = setting.CustomValue.Trim();
                    return true;
                default:
                    value = null;
                    return false;
            }
        }
    }
}
