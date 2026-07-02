using System;
using PlayniteAchievements.Common;

namespace PlayniteAchievements.Providers.Exophase
{
    /// <summary>
    /// Shared game-name normalization and fuzzy-match scoring used by every Exophase
    /// matching path (catalog search, metadata enrichment, and friend-library resolution).
    /// Centralizing this keeps a single normalization/scoring implementation so asymmetric
    /// edition names (e.g. "Titanfall 2 Deluxe Edition" vs "Titanfall 2") match consistently.
    /// </summary>
    internal static class ExophaseGameNameMatcher
    {
        /// <summary>Score for an exact (case-insensitive) match of two normalized names.</summary>
        public const int ExactMatchScore = 100;

        private static readonly string[] EditionSuffixes =
        {
            " - Definitive Edition",
            " - Game of the Year Edition",
            " - Complete Edition",
            " - Collector's Edition",
            " - Deluxe Edition",
            " - Standard Edition",
            " - Ultimate Edition",
            " - Premium Edition",
            " Definitive Edition",
            " Game of the Year Edition",
            " Complete Edition",
            " Collector's Edition",
            " Deluxe Edition",
            " Standard Edition",
            " Ultimate Edition",
            " Premium Edition",
            " (Definitive Edition)",
            " (Game of the Year Edition)",
            " (Complete Edition)",
            " (Collector's Edition)",
            " (Deluxe Edition)",
            " (Standard Edition)",
            " (Ultimate Edition)",
            " (Premium Edition)"
        };

        /// <summary>
        /// Normalizes a game name for matching by trimming and removing a known edition suffix.
        /// Case is preserved; downstream comparisons are case-insensitive.
        /// </summary>
        public static string NormalizeGameName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            var normalized = name.Trim();

            foreach (var suffix in EditionSuffixes)
            {
                if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    normalized = normalized.Substring(0, normalized.Length - suffix.Length);
                    break;
                }
            }

            return normalized.Trim();
        }

        /// <summary>
        /// Normalizes a game name for use in a slug: edition-stripped, lowercased, with
        /// runs of non-alphanumeric characters collapsed to single hyphens.
        /// </summary>
        public static string NormalizeGameNameForSlug(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var normalized = NormalizeGameName(name).ToLowerInvariant();

            var chars = new char[normalized.Length];
            var charIndex = 0;
            var lastWasHyphen = false;

            foreach (var c in normalized)
            {
                if (char.IsLetterOrDigit(c))
                {
                    chars[charIndex++] = c;
                    lastWasHyphen = false;
                }
                else if (!lastWasHyphen)
                {
                    chars[charIndex++] = '-';
                    lastWasHyphen = true;
                }
            }

            if (charIndex > 0 && chars[charIndex - 1] == '-')
            {
                charIndex--;
            }

            return new string(chars, 0, charIndex);
        }

        /// <summary>
        /// Scores how well two already-normalized names match, on a 0-100 scale:
        /// exact = 100, prefix = 80, substring (either direction) = 60, otherwise a
        /// Jaro-Winkler fallback (>= 0.94 -> 70, >= 0.88 -> 40, else 0).
        /// </summary>
        public static int ComputeMatchScore(string normalizedSearch, string normalizedTitle)
        {
            if (string.IsNullOrWhiteSpace(normalizedSearch) || string.IsNullOrWhiteSpace(normalizedTitle))
            {
                return 0;
            }

            if (string.Equals(normalizedTitle, normalizedSearch, StringComparison.OrdinalIgnoreCase))
            {
                return ExactMatchScore;
            }

            if (normalizedTitle.StartsWith(normalizedSearch, StringComparison.OrdinalIgnoreCase))
            {
                return 80;
            }

            if (normalizedTitle.IndexOf(normalizedSearch, StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalizedSearch.IndexOf(normalizedTitle, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 60;
            }

            var similarity = StringSimilarity.JaroWinklerSimilarityIgnoreCase(normalizedSearch, normalizedTitle);
            if (similarity >= 0.94)
            {
                return 70;
            }

            if (similarity >= 0.88)
            {
                return 40;
            }

            return 0;
        }
    }
}
