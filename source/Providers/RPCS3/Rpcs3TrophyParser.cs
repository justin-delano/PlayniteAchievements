using Playnite.SDK;
using PlayniteAchievements.Providers.RPCS3.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace PlayniteAchievements.Providers.RPCS3
{
    /// <summary>
    /// Static class for parsing RPCS3 trophy files.
    /// Handles TROPCONF.SFM (XML definitions) and TROPUSR.DAT (binary unlock data).
    /// </summary>
    internal static class Rpcs3TrophyParser
    {
        // Magic bytes that separate trophy entries in TROPUSR.DAT
        private static readonly string[] MagicBytePatterns = new[]
        {
            "0000000400000050000000",
            "0000000600000060000000"
        };

        /// <summary>
        /// Parses trophy definitions from TROPCONF.SFM XML file.
        /// </summary>
        /// <param name="tropconfPath">Path to TROPCONF.SFM file.</param>
        /// <param name="logger">Logger for error reporting.</param>
        /// <returns>List of trophy definitions, or empty list on error.</returns>
        public static List<Rpcs3Trophy> ParseTrophyDefinitions(string tropconfPath, ILogger logger)
        {
            var trophies = new List<Rpcs3Trophy>();

            if (string.IsNullOrWhiteSpace(tropconfPath) || !File.Exists(tropconfPath))
            {
                logger?.Debug($"[RPCS3] TROPCONF.SFM not found at {tropconfPath}");
                return trophies;
            }

            try
            {
                var doc = XDocument.Load(tropconfPath);
                var trophyElements = doc.Descendants("trophy");

                foreach (var trophyElement in trophyElements)
                {
                    try
                    {
                        var trophy = new Rpcs3Trophy
                        {
                            Id = ParseIntAttribute(trophyElement, "id", -1),
                            TrophyType = trophyElement.Attribute("ttype")?.Value?.Trim() ?? "B",
                            Hidden = string.Equals(trophyElement.Attribute("hidden")?.Value, "yes", StringComparison.OrdinalIgnoreCase),
                            Name = trophyElement.Element("name")?.Value?.Trim() ?? string.Empty,
                            Description = trophyElement.Element("detail")?.Value?.Trim() ?? string.Empty,
                            GroupId = trophyElement.Attribute("gid")?.Value?.Trim() ?? "0"
                        };

                        if (trophy.Id >= 0)
                        {
                            trophies.Add(trophy);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.Debug(ex, $"[RPCS3] Failed to parse trophy element");
                    }
                }

                logger?.Info($"[RPCS3] Parsed {trophies.Count} trophy definitions from {tropconfPath}");
            }
            catch (Exception ex)
            {
                logger?.Error(ex, $"[RPCS3] Failed to parse TROPCONF.SFM at {tropconfPath}");
            }

            return trophies;
        }

        /// <summary>
        /// Parses trophy unlock data from TROPUSR.DAT binary file.
        /// Updates the provided trophy list with unlock status and timestamps.
        /// </summary>
        /// <param name="tropusrPath">Path to TROPUSR.DAT file.</param>
        /// <param name="trophies">List of trophies to update with unlock data.</param>
        /// <param name="logger">Logger for error reporting.</param>
        public static void ParseTrophyUnlockData(string tropusrPath, List<Rpcs3Trophy> trophies, ILogger logger)
        {
            if (string.IsNullOrWhiteSpace(tropusrPath) || !File.Exists(tropusrPath))
            {
                logger?.Debug($"[RPCS3] TROPUSR.DAT not found at {tropusrPath}");
                return;
            }

            if (trophies == null || trophies.Count == 0)
            {
                return;
            }

            try
            {
                var bytes = File.ReadAllBytes(tropusrPath);
                var hexString = BytesToHex(bytes);

                // Find entries by splitting on magic byte patterns
                var entries = SplitByMagicBytes(hexString);

                // Take the last N entries (where N = trophy count)
                var relevantEntries = entries
                    .Skip(Math.Max(0, entries.Count - trophies.Count))
                    .Take(trophies.Count)
                    .ToList();

                logger?.Debug($"[RPCS3] Found {entries.Count} total entries, using {relevantEntries.Count} for {trophies.Count} trophies");

                for (var i = 0; i < trophies.Count && i < relevantEntries.Count; i++)
                {
                    var entry = relevantEntries[i];
                    var trophy = trophies[i];

                    try
                    {
                        // Parse entry: offset 0-1 is trophy ID, offset 18-25 is unlock status, offset 44-57 is timestamp
                        ParseTrophyEntry(entry, trophy, logger);
                    }
                    catch (Exception ex)
                    {
                        logger?.Debug(ex, $"[RPCS3] Failed to parse entry for trophy {trophy.Id}");
                    }
                }

                var unlockedCount = trophies.Count(t => t.Unlocked);
                logger?.Info($"[RPCS3] Parsed unlock data: {unlockedCount}/{trophies.Count} trophies unlocked");
            }
            catch (Exception ex)
            {
                logger?.Error(ex, $"[RPCS3] Failed to parse TROPUSR.DAT at {tropusrPath}");
            }
        }

        /// <summary>
        /// Extracts the NP Comm ID from a TROPHY.TRP file.
        /// The TROPHY.TRP file contains XML data with the npcommid element.
        /// </summary>
        /// <param name="trophyTrpPath">Path to TROPHY.TRP file.</param>
        /// <param name="logger">Logger for error reporting.</param>
        /// <returns>NP Comm ID string, or null if not found.</returns>
        public static string ExtractNpCommId(string trophyTrpPath, ILogger logger)
        {
            if (string.IsNullOrWhiteSpace(trophyTrpPath) || !File.Exists(trophyTrpPath))
            {
                return null;
            }

            try
            {
                // TROPHY.TRP is a container file that includes XML data
                // Search for npcommid in the raw bytes
                var bytes = File.ReadAllBytes(trophyTrpPath);
                var content = Encoding.UTF8.GetString(bytes);

                // Look for <npcommid>...</npcommid> pattern
                var tagStart = content.IndexOf("<npcommid>", StringComparison.OrdinalIgnoreCase);
                if (tagStart < 0)
                {
                    // Try alternate format without angle brackets (some dumps use different format)
                    var attrStart = content.IndexOf("npcommid=", StringComparison.OrdinalIgnoreCase);
                    if (attrStart >= 0)
                    {
                        // Find the value after the equals sign
                        var valueStart = attrStart + "npcommid=".Length;
                        var quoteStart = content.IndexOf("\"", valueStart);
                        if (quoteStart >= 0)
                        {
                            var quoteEnd = content.IndexOf("\"", quoteStart + 1);
                            if (quoteEnd > quoteStart)
                            {
                                return content.Substring(quoteStart + 1, quoteEnd - quoteStart - 1).Trim();
                            }
                        }
                    }
                    return null;
                }

                var tagEnd = content.IndexOf("</npcommid>", tagStart, StringComparison.OrdinalIgnoreCase);
                if (tagEnd < 0)
                {
                    return null;
                }

                var npcommid = content.Substring(tagStart + "<npcommid>".Length, tagEnd - tagStart - "<npcommid>".Length).Trim();
                return string.IsNullOrWhiteSpace(npcommid) ? null : npcommid;
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, $"[RPCS3] Failed to extract npcommid from {trophyTrpPath}");
                return null;
            }
        }

        /// <summary>
        /// Converts a byte array to a hexadecimal string.
        /// </summary>
        private static string BytesToHex(byte[] bytes)
        {
            var hex = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
            {
                hex.Append(b.ToString("x2"));
            }
            return hex.ToString();
        }

        /// <summary>
        /// Splits the hex string by magic byte patterns to get individual trophy entries.
        /// </summary>
        private static List<string> SplitByMagicBytes(string hexString)
        {
            var entries = new List<string>();
            var startIndex = 0;

            // Find all occurrences of magic byte patterns
            var indices = new List<int>();
            foreach (var pattern in MagicBytePatterns)
            {
                var searchIndex = 0;
                while ((searchIndex = hexString.IndexOf(pattern, searchIndex, StringComparison.OrdinalIgnoreCase)) >= 0)
                {
                    if (!indices.Contains(searchIndex))
                    {
                        indices.Add(searchIndex);
                    }
                    searchIndex++;
                }
            }

            // Sort indices and extract entries
            indices.Sort();
            for (var i = 0; i < indices.Count; i++)
            {
                var entryStart = indices[i];
                var entryEnd = (i + 1 < indices.Count) ? indices[i + 1] : hexString.Length;
                entries.Add(hexString.Substring(entryStart, entryEnd - entryStart));
            }

            return entries;
        }

        /// <summary>
        /// Parses a single trophy entry from TROPUSR.DAT.
        /// Entry structure:
        /// - Offset 0-3: Trophy ID (hex, extract first 4 chars as 2 bytes)
        /// - Offset 36-51: Unlock status (00000001 = unlocked)
        /// - Offset 88-103: Timestamp (ticks * 10, convert to DateTime)
        /// </summary>
        private static void ParseTrophyEntry(string entry, Rpcs3Trophy trophy, ILogger logger)
        {
            if (string.IsNullOrWhiteSpace(entry) || entry.Length < 104)
            {
                return;
            }

            // Each character in hex string represents 4 bits
            // Characters 0-3 are the first 2 bytes (trophy ID area)
            // The actual trophy ID is at a different offset based on SuccessStory parsing

            // Check unlock status at offset 36-51 (hex chars 72-103 for bytes 36-51)
            // Looking for "00000001" which indicates unlocked
            if (entry.Length >= 104)
            {
                var unlockArea = entry.Substring(72, 8);
                trophy.Unlocked = unlockArea.Equals("00000001", StringComparison.OrdinalIgnoreCase);
            }

            // Parse timestamp at offset 88-103 (hex chars 176-207 for bytes 88-103)
            if (trophy.Unlocked && entry.Length >= 208)
            {
                try
                {
                    var timestampHex = entry.Substring(176, 16);
                    var timestampTicks = Convert.ToUInt64(timestampHex, 16);

                    // RPCS3 stores timestamps as ticks * 10 (essentially FILETIME format)
                    // FILETIME is 100-nanosecond intervals since January 1, 1601 UTC
                    // But RPCS3 appears to use ticks since DateTime.MinValue (January 1, 0001)
                    // Divide by 10 to get ticks
                    var ticks = (long)(timestampTicks / 10);

                    if (ticks > 0 && ticks < DateTime.MaxValue.Ticks)
                    {
                        trophy.UnlockTimeUtc = new DateTime(ticks, DateTimeKind.Utc);
                    }
                }
                catch (Exception ex)
                {
                    logger?.Debug(ex, $"[RPCS3] Failed to parse timestamp for trophy {trophy.Id}");
                }
            }
        }

        /// <summary>
        /// Parses an integer attribute from an XML element.
        /// </summary>
        private static int ParseIntAttribute(XElement element, string attributeName, int defaultValue)
        {
            var attrValue = element.Attribute(attributeName)?.Value;
            if (string.IsNullOrWhiteSpace(attrValue))
            {
                return defaultValue;
            }

            return int.TryParse(attrValue, out var result) ? result : defaultValue;
        }
    }
}
