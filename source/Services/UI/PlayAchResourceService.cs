using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Settings;

namespace PlayniteAchievements.Services.UI
{
    internal static class PlayAchResourceService
    {
        private sealed class TokenDefinition
        {
            public TokenDefinition(
                string resourceKey,
                string displayName,
                ResourceOverrideValueKind kind,
                string playniteResourceKey,
                bool isUserVisible,
                bool allowsOverride,
                params string[] fallbackPlayniteResourceKeys)
            {
                ResourceKey = resourceKey;
                DisplayName = displayName;
                Kind = kind;
                PlayniteResourceKey = playniteResourceKey;
                IsUserVisible = isUserVisible;
                AllowsOverride = allowsOverride;
                FallbackPlayniteResourceKeys = fallbackPlayniteResourceKeys ?? Array.Empty<string>();
            }

            public string ResourceKey { get; }
            public string DisplayName { get; }
            public ResourceOverrideValueKind Kind { get; }
            public string PlayniteResourceKey { get; }
            public bool IsUserVisible { get; }
            public bool AllowsOverride { get; }
            public IReadOnlyList<string> FallbackPlayniteResourceKeys { get; }
        }

        private sealed class AliasDefinition
        {
            public AliasDefinition(string resourceKey, string sourceResourceKey, byte? alpha)
            {
                ResourceKey = resourceKey;
                SourceResourceKey = sourceResourceKey;
                Alpha = alpha;
            }

            public string ResourceKey { get; }
            public string SourceResourceKey { get; }
            public byte? Alpha { get; }
        }

        private static readonly TokenDefinition[] Tokens =
        {
            Brush("PlayAch.Brush.Text", "Text", "TextBrush"),
            Brush("PlayAch.Brush.Text.Secondary", "Secondary text", "TextBrushDarker", "TextBrush"),
            Brush("PlayAch.Brush.Text.Tertiary", "Tertiary text", "TextBrushDark", "TextBrushDarker", "TextBrush"),
            Brush("PlayAch.Brush.Surface", "Surface", "ControlBackgroundBrush"),
            Brush("PlayAch.Brush.Panel", "Panel", "PanelBackgroundBrush", "ControlBackgroundBrush"),
            Brush("PlayAch.Brush.GridSurface", "Grid surface", "GridItemBackgroundBrush", "ControlBackgroundBrush"),
            Brush("PlayAch.Brush.Border", "Border", "NormalBorderBrush"),
            Brush("PlayAch.Brush.ControlBorder", "Control border", "NormalBrush"),
            Brush("PlayAch.Brush.Glyph", "Glyph", "GlyphBrush"),
            Brush("PlayAch.Brush.Accent", "Accent", "HighlightGlyphBrush"),
            Brush("PlayAch.Brush.Selection", "Selection", "SelectionLightBrush", "GlyphBrush"),
            Brush("PlayAch.Brush.ControlSurface", "Control surface", "ButtonBackgroundBrush", "ControlBackgroundBrush"),
            Brush("PlayAch.Brush.PopupSurface", "Popup surface", "PopupBackgroundBrush", "PanelBackgroundBrush", "ControlBackgroundBrush"),
            Brush("PlayAch.Brush.PopupBorder", "Popup border", "PopupBorderBrush", "NormalBorderBrush"),
            Brush("PlayAch.Brush.StrongSurface", "Strong surface", "NormalBrushDark", "NormalBrush"),

            FontSize("PlayAch.FontSize.Caption", "Caption size", "FontSizeSmall"),
            FontSize("PlayAch.FontSize.Body", "Body size", "FontSize"),
            FontSize("PlayAch.FontSize.Title", "Title size", "FontSizeLarge"),

            FontFamily("PlayAch.FontFamily.Body", "Body font", "FontFamily"),
            StaticFontFamily("PlayAch.FontFamily.Icon", "Icon font", "FontIcoFont")
        };

        private static readonly AliasDefinition[] Aliases =
        {
            Alias("PlayAch.Brush.Button.Background", "PlayAch.Brush.ControlSurface"),
            Alias("PlayAch.Brush.Button.Background.Hover", "PlayAch.Brush.Selection", 0x30),
            Alias("PlayAch.Brush.Button.Background.Selected", "PlayAch.Brush.Selection", 0x45),
            Alias("PlayAch.Brush.Button.Background.Pressed", "PlayAch.Brush.StrongSurface", 0x54),
            Alias("PlayAch.Brush.Button.Border", "PlayAch.Brush.ControlBorder"),
            Alias("PlayAch.Brush.Input.Background", "PlayAch.Brush.Surface"),
            Alias("PlayAch.Brush.Input.Border", "PlayAch.Brush.ControlBorder"),
            Alias("PlayAch.Brush.Grid.Background", "PlayAch.Brush.GridSurface"),
            Alias("PlayAch.Brush.Grid.HeaderBackground", "PlayAch.Brush.GridSurface"),
            Alias("PlayAch.Brush.Grid.RowBackground", "PlayAch.Brush.GridSurface"),
            Alias("PlayAch.Brush.Grid.RowHoverBackground", "PlayAch.Brush.Selection", 0x30),
            Alias("PlayAch.Brush.Grid.RowSelectedBackground", "PlayAch.Brush.Selection", 0x45),
            Alias("PlayAch.Brush.Dialog.Background", "PlayAch.Brush.PopupSurface"),
            Alias("PlayAch.Brush.Dialog.Border", "PlayAch.Brush.PopupBorder"),
            Alias("PlayAch.Brush.Chrome.Background", "PlayAch.Brush.ControlSurface", 0x1F),
            Alias("PlayAch.Brush.Chrome.StrongBackground", "PlayAch.Brush.StrongSurface", 0x54)
        };

        public static IReadOnlyList<ResourceOverrideDescriptor> ResourceDescriptors { get; } =
            Tokens
                .Where(token => token.IsUserVisible)
                .Select(token => new ResourceOverrideDescriptor(
                    token.ResourceKey,
                    token.DisplayName,
                    token.Kind,
                    token.PlayniteResourceKey,
                    token.FallbackPlayniteResourceKeys.ToArray()))
                .ToList()
                .AsReadOnly();

        public static void Apply(ResourceDictionary resources, IDictionary<string, ResourceOverrideSetting> overrides)
        {
            if (resources == null)
            {
                return;
            }

            foreach (var token in Tokens)
            {
                resources[token.ResourceKey] = ResolveToken(token, overrides);
            }

            foreach (var alias in Aliases)
            {
                resources[alias.ResourceKey] = ResolveAlias(resources, alias);
            }

            RarityAppearanceHelper.ApplyCompletedGameBrushResource(resources);
        }

        private static TokenDefinition Brush(
            string resourceKey,
            string displayName,
            string playniteResourceKey,
            params string[] fallbackPlayniteResourceKeys)
        {
            return new TokenDefinition(
                resourceKey,
                displayName,
                ResourceOverrideValueKind.Brush,
                playniteResourceKey,
                true,
                true,
                fallbackPlayniteResourceKeys);
        }

        private static TokenDefinition FontSize(
            string resourceKey,
            string displayName,
            string playniteResourceKey)
        {
            return new TokenDefinition(
                resourceKey,
                displayName,
                ResourceOverrideValueKind.FontSize,
                playniteResourceKey,
                true,
                true);
        }

        private static AliasDefinition Alias(string resourceKey, string sourceResourceKey, byte? alpha = null)
        {
            return new AliasDefinition(resourceKey, sourceResourceKey, alpha);
        }

        private static TokenDefinition FontFamily(
            string resourceKey,
            string displayName,
            string playniteResourceKey)
        {
            return new TokenDefinition(
                resourceKey,
                displayName,
                ResourceOverrideValueKind.FontFamily,
                playniteResourceKey,
                true,
                true);
        }

        private static TokenDefinition StaticFontFamily(
            string resourceKey,
            string displayName,
            string playniteResourceKey)
        {
            return new TokenDefinition(
                resourceKey,
                displayName,
                ResourceOverrideValueKind.FontFamily,
                playniteResourceKey,
                false,
                false);
        }

        private static object ResolveToken(TokenDefinition token, IDictionary<string, ResourceOverrideSetting> overrides)
        {
            var setting = GetSetting(token.ResourceKey, overrides);
            if (token.AllowsOverride &&
                setting?.Mode == ResourceOverrideMode.Custom &&
                TryParseCustomValue(token, setting.CustomValue, out var customValue))
            {
                return customValue;
            }

            var playniteValue = FindPlayniteResource(token);
            return CoerceValue(token, playniteValue);
        }

        private static object FindPlayniteResource(TokenDefinition token)
        {
            var value = Application.Current?.TryFindResource(token.PlayniteResourceKey);
            if (value != null)
            {
                return value;
            }

            foreach (var fallbackKey in token.FallbackPlayniteResourceKeys)
            {
                value = Application.Current?.TryFindResource(fallbackKey);
                if (value != null)
                {
                    return value;
                }
            }

            return null;
        }

        private static object ResolveAlias(ResourceDictionary resources, AliasDefinition alias)
        {
            if (!resources.Contains(alias.SourceResourceKey))
            {
                return null;
            }

            var value = resources[alias.SourceResourceKey];
            if (!alias.Alpha.HasValue || !(value is Brush brush))
            {
                return value;
            }

            return CreateBrushWithAlpha(brush, alias.Alpha.Value);
        }

        private static Brush CreateBrushWithAlpha(Brush brush, byte alpha)
        {
            if (brush is SolidColorBrush solid)
            {
                var color = solid.Color;
                color.A = (byte)Math.Round(color.A * (alpha / 255.0));
                return CreateBrush(color);
            }

            var clone = brush.CloneCurrentValue();
            clone.Opacity = clone.Opacity * (alpha / 255.0);
            if (clone.CanFreeze)
            {
                clone.Freeze();
            }

            return clone;
        }

        private static ResourceOverrideSetting GetSetting(
            string resourceKey,
            IDictionary<string, ResourceOverrideSetting> overrides)
        {
            if (overrides == null || string.IsNullOrWhiteSpace(resourceKey))
            {
                return null;
            }

            return overrides.TryGetValue(resourceKey, out var setting) ? setting : null;
        }

        private static bool TryParseCustomValue(TokenDefinition token, string value, out object result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            switch (token.Kind)
            {
                case ResourceOverrideValueKind.Brush:
                    result = CreateBrush(value);
                    return result != null;

                case ResourceOverrideValueKind.FontSize:
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) &&
                        size > 0 &&
                        !double.IsNaN(size) &&
                        !double.IsInfinity(size))
                    {
                        result = size;
                        return true;
                    }
                    return false;

                case ResourceOverrideValueKind.FontFamily:
                    result = new FontFamily(value.Trim());
                    return true;

                default:
                    return false;
            }
        }

        private static object CoerceValue(TokenDefinition token, object value)
        {
            if (value == null)
            {
                return null;
            }

            switch (token.Kind)
            {
                case ResourceOverrideValueKind.Brush:
                    return CoerceBrush(value);

                case ResourceOverrideValueKind.FontSize:
                    return CoerceFontSize(value);

                case ResourceOverrideValueKind.FontFamily:
                    return value is FontFamily ? value : new FontFamily(value.ToString());

                default:
                    return null;
            }
        }

        private static Brush CoerceBrush(object value)
        {
            if (value is Brush brush)
            {
                return brush;
            }

            if (value is Color color)
            {
                return CreateBrush(color);
            }

            return CreateBrush(value.ToString());
        }

        private static object CoerceFontSize(object value)
        {
            if (value is double number)
            {
                return number;
            }

            if (value is float floatValue)
            {
                return (double)floatValue;
            }

            if (value is int intValue)
            {
                return (double)intValue;
            }

            return double.TryParse(
                value.ToString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed)
                ? parsed
                : (object)null;
        }

        private static SolidColorBrush CreateBrush(string color)
        {
            try
            {
                return CreateBrush((Color)ColorConverter.ConvertFromString(color));
            }
            catch
            {
                return null;
            }
        }

        private static SolidColorBrush CreateBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            if (brush.CanFreeze)
            {
                brush.Freeze();
            }

            return brush;
        }
    }
}
