using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.PSN
{
    internal static class PsnSuffixRetryHelper
    {
        internal static List<string> BuildSuffixCandidates(string normalizedGameId)
        {
            var normalized = normalizedGameId ?? string.Empty;
            var isPs5 = normalized.StartsWith("PPSA", StringComparison.OrdinalIgnoreCase);
            var primarySuffix = isPs5 ? string.Empty : "?npServiceName=trophy";
            var alternateSuffix = string.IsNullOrEmpty(primarySuffix) ? "?npServiceName=trophy" : string.Empty;

            var suffixes = new List<string> { primarySuffix };
            if (!string.Equals(primarySuffix, alternateSuffix, StringComparison.Ordinal))
            {
                suffixes.Add(alternateSuffix);
            }

            return suffixes;
        }

        internal static async Task<string> GetStringWithSuffixRetryAsync(
            Func<string, Task<string>> fetchBySuffixAsync,
            IReadOnlyList<string> suffixCandidates)
        {
            if (fetchBySuffixAsync == null)
            {
                throw new ArgumentNullException(nameof(fetchBySuffixAsync));
            }

            if (suffixCandidates == null || suffixCandidates.Count == 0)
            {
                throw new ArgumentException("At least one suffix candidate is required.", nameof(suffixCandidates));
            }

            Exception lastException = null;
            for (var i = 0; i < suffixCandidates.Count; i++)
            {
                var suffix = suffixCandidates[i] ?? string.Empty;
                try
                {
                    return await fetchBySuffixAsync(suffix).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                }
            }

            throw lastException ?? new InvalidOperationException("Unable to fetch data for any suffix candidate.");
        }
    }
}
