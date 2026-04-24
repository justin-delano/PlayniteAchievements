using System;
using System.Collections.Generic;

namespace PlayniteAchievements.Providers.RetroAchievements
{
    internal static class RetroAchievementsSubsetTitleResolver
    {
        internal static string ExtractBaseTitle(string subsetTitle)
        {
            if (string.IsNullOrWhiteSpace(subsetTitle))
            {
                return null;
            }

            // Strip the first bracket-delimited suffix: "[Subset - ...]", "[Bonus]", "[Hub]", etc.
            var bracketStart = subsetTitle.IndexOf('[');
            if (bracketStart > 0)
            {
                return subsetTitle.Substring(0, bracketStart).Trim();
            }

            var parenStart = subsetTitle.IndexOf('(');
            if (parenStart > 0)
            {
                return subsetTitle.Substring(0, parenStart).Trim();
            }

            return null;
        }

        internal static IReadOnlyList<string> ExtractAlternateBaseTitleCandidates(string baseTitle)
        {
            if (string.IsNullOrWhiteSpace(baseTitle) || baseTitle.IndexOf('|') < 0)
            {
                return Array.Empty<string>();
            }

            var candidates = new List<string>();
            foreach (var segment in baseTitle.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var normalized = segment?.Trim();
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    continue;
                }

                var alreadyPresent = false;
                foreach (var existing in candidates)
                {
                    if (string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase))
                    {
                        alreadyPresent = true;
                        break;
                    }
                }

                if (!alreadyPresent)
                {
                    candidates.Add(normalized);
                }
            }

            return candidates;
        }
    }
}
