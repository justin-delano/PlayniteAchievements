using System;
using System.Collections.Generic;
using System.Linq;

namespace PlayniteAchievements.Services.Database
{
    internal static class SqlNadoCacheBehavior
    {
        public static List<long> ComputeStaleDefinitionIds(
            IDictionary<string, long> existingDefinitionIdsByApiName,
            IEnumerable<string> incomingApiNames)
        {
            var staleIds = new List<long>();
            if (existingDefinitionIdsByApiName == null || existingDefinitionIdsByApiName.Count == 0)
            {
                return staleIds;
            }

            var desired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (incomingApiNames != null)
            {
                foreach (var apiName in incomingApiNames)
                {
                    var normalized = apiName?.Trim();
                    if (!string.IsNullOrWhiteSpace(normalized))
                    {
                        desired.Add(normalized);
                    }
                }
            }

            foreach (var pair in existingDefinitionIdsByApiName)
            {
                var apiName = pair.Key?.Trim();
                if (string.IsNullOrWhiteSpace(apiName) || desired.Contains(apiName))
                {
                    continue;
                }

                if (pair.Value > 0)
                {
                    staleIds.Add(pair.Value);
                }
            }

            return staleIds;
        }

        public static bool ShouldMarkLegacyImportDone(
            int parseFailedCount,
            int dbWriteFailedCount,
            int remainingFileCount)
        {
            return parseFailedCount <= 0 &&
                   dbWriteFailedCount <= 0 &&
                   remainingFileCount <= 0;
        }
    }
}
