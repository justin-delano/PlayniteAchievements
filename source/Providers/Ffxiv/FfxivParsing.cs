using HtmlAgilityPack;
using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PlayniteAchievements.Providers.Ffxiv
{
    /// <summary>
    /// Pure parsing helpers for the FFXIV provider, isolated from HTTP and Playnite
    /// dependencies so they can be unit tested directly.
    /// </summary>
    internal static class FfxivParsing
    {
        private static readonly Regex LodestoneIdRegex =
            new Regex("/lodestone/character/(\\d+)/", RegexOptions.Compiled);

        /// <summary>
        /// Extracts the Lodestone character id for an exact name + world match from a
        /// Lodestone character search results page. Returns null when there is no
        /// exact match. The Lodestone search is a partial match (e.g. "Mal Reynolds"
        /// also returns "Malynor Reynolds"), so the first result link cannot be used.
        /// </summary>
        public static long? ParseLodestoneCharacterId(string html, string name, string world)
        {
            if (string.IsNullOrWhiteSpace(html) ||
                string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(world))
            {
                return null;
            }

            var wantName = HtmlEntity.DeEntitize(name).Trim();
            var wantWorld = world.Trim();

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var entries = doc.DocumentNode.SelectNodes("//a[contains(@class, 'entry__link')]");
            if (entries == null)
            {
                return null;
            }

            foreach (var entry in entries)
            {
                var href = entry.GetAttributeValue("href", string.Empty);
                var idMatch = LodestoneIdRegex.Match(href);
                if (!idMatch.Success)
                {
                    continue;
                }

                var nameNode = entry.SelectSingleNode(".//p[contains(@class, 'entry__name')]");
                var worldNode = entry.SelectSingleNode(".//p[contains(@class, 'entry__world')]");

                var entryName = HtmlEntity.DeEntitize(nameNode?.InnerText ?? string.Empty).Trim();
                var entryWorldText = HtmlEntity.DeEntitize(worldNode?.InnerText ?? string.Empty);
                // entry__world reads like "Gilgamesh [Aether]"; keep the world.
                var entryWorld = entryWorldText.Split('[')[0].Replace(' ', ' ').Trim();

                if (string.Equals(entryName, wantName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(entryWorld, wantWorld, StringComparison.OrdinalIgnoreCase) &&
                    long.TryParse(idMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                {
                    return id;
                }
            }

            return null;
        }

        /// <summary>
        /// Parses an FFXIV Collect ownership string such as "98%" into a 0-100 value.
        /// </summary>
        public static double? ParseOwnedPercent(string owned)
        {
            if (string.IsNullOrWhiteSpace(owned))
            {
                return null;
            }

            var trimmed = owned.Trim().TrimEnd('%').Trim();
            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return Math.Max(0, Math.Min(100, value));
            }

            return null;
        }

        /// <summary>
        /// Rewrites the FFXIV Collect icon URL from webp to png. WPF on .NET
        /// Framework 4.6.2 cannot decode webp.
        /// </summary>
        public static string NormalizeIconUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return url;
            }

            return url.Replace("format=webp", "format=png");
        }
    }
}
