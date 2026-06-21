using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
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
                string playniteResourceKey)
            {
                ResourceKey = resourceKey;
                DisplayName = displayName;
                Kind = kind;
                PlayniteResourceKey = playniteResourceKey;
            }

            public string ResourceKey { get; }
            public string DisplayName { get; }
            public ResourceOverrideValueKind Kind { get; }
            public string PlayniteResourceKey { get; }
        }

        private static readonly TokenDefinition[] Tokens =
        {
            Brush("PlayAch.Brush.Text", "Text", "TextBrush"),
            Brush("PlayAch.Brush.Text.Secondary", "Secondary text", "TextBrushDarker"),
            Brush("PlayAch.Brush.Text.Tertiary", "Tertiary text", "TextBrushDark"),
            Brush("PlayAch.Brush.Surface", "Surface", "ControlBackgroundBrush"),
            Brush("PlayAch.Brush.Panel", "Panel", "PanelBackgroundBrush"),
            Brush("PlayAch.Brush.Border", "Border", "NormalBorderBrush"),
            Brush("PlayAch.Brush.ControlBorder", "Control border", "NormalBrush"),
            Brush("PlayAch.Brush.Glyph", "Glyph", "GlyphBrush"),
            Brush("PlayAch.Brush.Accent", "Accent", "HighlightGlyphBrush"),
            Brush("PlayAch.Brush.Selection", "Selection", "SelectionLightBrush"),

            FontSize("PlayAch.FontSize.Caption", "Caption size", "FontSizeSmall"),
            FontSize("PlayAch.FontSize.Body", "Body size", "FontSize"),
            FontSize("PlayAch.FontSize.Title", "Title size", "FontSizeLarge"),

            FontFamily("PlayAch.FontFamily.Body", "Body font", "FontFamily"),
            FontFamily("PlayAch.FontFamily.Icon", "Icon font", "FontIcoFont")
        };

        public static IReadOnlyList<ResourceOverrideDescriptor> ResourceDescriptors { get; } =
            Tokens
                .Select(token => new ResourceOverrideDescriptor(
                    token.ResourceKey,
                    token.DisplayName,
                    token.Kind,
                    token.PlayniteResourceKey))
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
        }

        private static TokenDefinition Brush(
            string resourceKey,
            string displayName,
            string playniteResourceKey)
        {
            return new TokenDefinition(
                resourceKey,
                displayName,
                ResourceOverrideValueKind.Brush,
                playniteResourceKey);
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
                playniteResourceKey);
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
                playniteResourceKey);
        }

        private static object ResolveToken(TokenDefinition token, IDictionary<string, ResourceOverrideSetting> overrides)
        {
            var setting = GetSetting(token.ResourceKey, overrides);
            if (setting?.Mode == ResourceOverrideMode.Custom &&
                TryParseCustomValue(token, setting.CustomValue, out var customValue))
            {
                return customValue;
            }

            var playniteValue = Application.Current?.TryFindResource(token.PlayniteResourceKey);
            return CoerceValue(token, playniteValue);
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
