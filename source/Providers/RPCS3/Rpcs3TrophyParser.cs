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
            logger?.Debug($"[RPCS3] ParseTrophyDefinitions - Starting parse of '{tropconfPath ?? "(null)"}'");
            var trophies = new List<Rpcs3Trophy>();

            if (string.IsNullOrWhiteSpace(tropconfPath))
            {
                logger?.Debug("[RPCS3] ParseTrophyDefinitions - Path is null or empty");
                return trophies;
            }

            if (!File.Exists(tropconfPath))
            {
                logger?.Debug($"[RPCS3] ParseTrophyDefinitions - File not found at '{tropconfPath}'");
                return trophies;
            }

            try
            {
                logger?.Debug($"[RPCS3] ParseTrophyDefinitions - Loading XML document from '{tropconfPath}'");
                var doc = XDocument.Load(tropconfPath);
                var trophyElements = doc.Descendants("trophy");
                var elementCount = doc.Descendants("trophy").Count();
                logger?.Debug($"[RPCS3] ParseTrophyDefinitions - Found {elementCount} trophy elements in XML");

                foreach (var trophyElement in trophyElements)
                {
                    try
                    {
                        var idAttr = trophyElement.Attribute("id")?.Value;
                        var ttypeAttr = trophyElement.Attribute("ttype")?.Value;
                        var hiddenAttr = trophyElement.Attribute("hidden")?.Value;
                        var nameElement = trophyElement.Element("name")?.Value;
                        var detailElement = trophyElement.Element("detail")?.Value;
                        var gidAttr = trophyElement.Attribute("gid")?.Value;

                        logger?.Debug($"[RPCS3] ParseTrophyDefinitions - Trophy element: id='{idAttr}', ttype='{ttypeAttr}', hidden='{hiddenAttr}', name='{nameElement}', gid='{gidAttr}'");

                        var trophy = new Rpcs3Trophy
                        {
                            Id = ParseIntAttribute(trophyElement, "id", -1),
                            TrophyType = ttypeAttr?.Trim() ?? "B",
                            Hidden = string.Equals(hiddenAttr, "yes", StringComparison.OrdinalIgnoreCase),
                            Name = nameElement?.Trim() ?? string.Empty,
                            Description = detailElement?.Trim() ?? string.Empty,
                            GroupId = gidAttr?.Trim() ?? "0"
                        };

                        if (trophy.Id >= 0)
                        {
                            trophies.Add(trophy);
                            logger?.Debug($"[RPCS3] ParseTrophyDefinitions - Added trophy Id={trophy.Id}, Name='{trophy.Name}', Type='{trophy.TrophyType}', Hidden={trophy.Hidden}");
                        }
                        else
                        {
                            logger?.Debug($"[RPCS3] ParseTrophyDefinitions - Skipped trophy with invalid Id={idAttr}");
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.Debug(ex, $"[RPCS3] ParseTrophyDefinitions - Failed to parse trophy element");
                    }
                }

                logger?.Info($"[RPCS3] ParseTrophyDefinitions - Parsed {trophies.Count} trophy definitions from '{tropconfPath}'");

                // Log trophy type distribution
                var bronzeCount = trophies.Count(t => t.TrophyType?.ToUpperInvariant() == "B");
                var silverCount = trophies.Count(t => t.TrophyType?.ToUpperInvariant() == "S");
                var goldCount = trophies.Count(t => t.TrophyType?.ToUpperInvariant() == "G");
                var platinumCount = trophies.Count(t => t.TrophyType?.ToUpperInvariant() == "P");
                var hiddenCount = trophies.Count(t => t.Hidden);
                logger?.Debug($"[RPCS3] ParseTrophyDefinitions - Trophy distribution: Bronze={bronzeCount}, Silver={silverCount}, Gold={goldCount}, Platinum={platinumCount}, Hidden={hiddenCount}");
            }
            catch (Exception ex)
            {
                logger?.Error(ex, $"[RPCS3] ParseTrophyDefinitions - Failed to parse TROPCONF.SFM at '{tropconfPath}'");
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
            logger?.Debug($"[RPCS3] ParseTrophyUnlockData - Starting parse of '{tropusrPath ?? "(null)"}'");

            if (string.IsNullOrWhiteSpace(tropusrPath))
            {
                logger?.Debug("[RPCS3] ParseTrophyUnlockData - Path is null or empty");
                return;
            }

            if (!File.Exists(tropusrPath))
            {
                logger?.Debug($"[RPCS3] ParseTrophyUnlockData - File not found at '{tropusrPath}'");
                return;
            }

            if (trophies == null || trophies.Count == 0)
            {
                logger?.Debug($"[RPCS3] ParseTrophyUnlockData - No trophies to update (trophies is null or empty)");
                return;
            }

            logger?.Debug($"[RPCS3] ParseTrophyUnlockData - Will process {trophies.Count} trophies");

            try
            {
                var fileInfo = new FileInfo(tropusrPath);
                logger?.Debug($"[RPCS3] ParseTrophyUnlockData - File size: {fileInfo.Length} bytes");

                var bytes = File.ReadAllBytes(tropusrPath);
                logger?.Debug($"[RPCS3] ParseTrophyUnlockData - Read {bytes.Length} bytes into memory");

                var hexString = BytesToHex(bytes);
                logger?.Debug($"[RPCS3] ParseTrophyUnlockData - Converted to hex string, length: {hexString.Length} characters");

                // Find entries by splitting on magic byte patterns
                var entries = SplitByMagicBytes(hexString, logger);
                logger?.Debug($"[RPCS3] ParseTrophyUnlockData - Split into {entries.Count} entries by magic byte patterns");

                // Take the last N entries (where N = trophy count)
                var relevantEntries = entries
                    .Skip(Math.Max(0, entries.Count - trophies.Count))
                    .Take(trophies.Count)
                    .ToList();

                logger?.Debug($"[RPCS3] ParseTrophyUnlockData - Using {relevantEntries.Count} relevant entries for {trophies.Count} trophies (skipped {Math.Max(0, entries.Count - trophies.Count)} entries)");

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
                        logger?.Debug(ex, $"[RPCS3] ParseTrophyUnlockData - Failed to parse entry for trophy {trophy.Id}");
                    }
                }

                var unlockedCount = trophies.Count(t => t.Unlocked);
                logger?.Info($"[RPCS3] ParseTrophyUnlockData - Parsed unlock data: {unlockedCount}/{trophies.Count} trophies unlocked");

                // Log which trophies are unlocked
                foreach (var trophy in trophies.Where(t => t.Unlocked))
                {
                    logger?.Debug($"[RPCS3] ParseTrophyUnlockData - Unlocked: Id={trophy.Id}, Name='{trophy.Name}', UnlockTime={trophy.UnlockTimeUtc?.ToString("o") ?? "(null)"}");
                }
            }
            catch (Exception ex)
            {
                logger?.Error(ex, $"[RPCS3] ParseTrophyUnlockData - Failed to parse TROPUSR.DAT at '{tropusrPath}'");
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
            logger?.Debug($"[RPCS3] ExtractNpCommId - Starting extraction from '{trophyTrpPath ?? "(null)"}'");

            if (string.IsNullOrWhiteSpace(trophyTrpPath))
            {
                logger?.Debug("[RPCS3] ExtractNpCommId - Path is null or empty");
                return null;
            }

            if (!File.Exists(trophyTrpPath))
            {
                logger?.Debug($"[RPCS3] ExtractNpCommId - File not found at '{trophyTrpPath}'");
                return null;
            }

            try
            {
                var fileInfo = new FileInfo(trophyTrpPath);
                logger?.Debug($"[RPCS3] ExtractNpCommId - File size: {fileInfo.Length} bytes");

                // TROPHY.TRP is a container file that includes XML data
                // Search for npcommid in the raw bytes
                var bytes = File.ReadAllBytes(trophyTrpPath);
                var content = Encoding.UTF8.GetString(bytes);
                logger?.Debug($"[RPCS3] ExtractNpCommId - Read {bytes.Length} bytes, string length: {content.Length}");

                // Look for <npcommid>...</npcommid> pattern
                var tagStart = content.IndexOf("<npcommid>", StringComparison.OrdinalIgnoreCase);
                logger?.Debug($"[RPCS3] ExtractNpCommId - <npcommid> tag search result: index={tagStart}");

                if (tagStart < 0)
                {
                    // Try alternate format without angle brackets (some dumps use different format)
                    logger?.Debug("[RPCS3] ExtractNpCommId - <npcommid> tag not found, trying npcommid= attribute format");
                    var attrStart = content.IndexOf("npcommid=", StringComparison.OrdinalIgnoreCase);
                    logger?.Debug($"[RPCS3] ExtractNpCommId - npcommid= attribute search result: index={attrStart}");

                    if (attrStart >= 0)
                    {
                        // Find the value after the equals sign
                        var valueStart = attrStart + "npcommid=".Length;
                        var quoteStart = content.IndexOf("\"", valueStart);
                        logger?.Debug($"[RPCS3] ExtractNpCommId - First quote at index: {quoteStart}");

                        if (quoteStart >= 0)
                        {
                            var quoteEnd = content.IndexOf("\"", quoteStart + 1);
                            logger?.Debug($"[RPCS3] ExtractNpCommId - Second quote at index: {quoteEnd}");

                            if (quoteEnd > quoteStart)
                            {
                                var npcommid = content.Substring(quoteStart + 1, quoteEnd - quoteStart - 1).Trim();
                                logger?.Debug($"[RPCS3] ExtractNpCommId - Extracted from attribute format: '{npcommid}'");
                                return npcommid;
                            }
                        }
                    }
                    logger?.Debug("[RPCS3] ExtractNpCommId - npcommid not found in either format");
                    return null;
                }

                var tagEnd = content.IndexOf("</npcommid>", tagStart, StringComparison.OrdinalIgnoreCase);
                logger?.Debug($"[RPCS3] ExtractNpCommId - Closing tag at index: {tagEnd}");

                if (tagEnd < 0)
                {
                    logger?.Debug("[RPCS3] ExtractNpCommId - Closing </npcommid> tag not found");
                    return null;
                }

                var npcommidValue = content.Substring(tagStart + "<npcommid>".Length, tagEnd - tagStart - "<npcommid>".Length).Trim();
                logger?.Debug($"[RPCS3] ExtractNpCommId - Extracted from tag format: '{npcommidValue}'");

                if (string.IsNullOrWhiteSpace(npcommidValue))
                {
                    logger?.Debug("[RPCS3] ExtractNpCommId - Extracted value is empty");
                    return null;
                }

                logger?.Info($"[RPCS3] ExtractNpCommId - Successfully extracted npcommid: '{npcommidValue}'");
                return npcommidValue;
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, $"[RPCS3] ExtractNpCommId - Failed to extract npcommid from '{trophyTrpPath}'");
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
        private static List<string> SplitByMagicBytes(string hexString, ILogger logger)
        {
            logger?.Debug($"[RPCS3] SplitByMagicBytes - Starting with hex string length: {hexString.Length}");
            var entries = new List<string>();

            // Find all occurrences of magic byte patterns
            var indices = new List<int>();
            foreach (var pattern in MagicBytePatterns)
            {
                logger?.Debug($"[RPCS3] SplitByMagicBytes - Searching for pattern: '{pattern}'");
                var patternCount = 0;
                var searchIndex = 0;
                while ((searchIndex = hexString.IndexOf(pattern, searchIndex, StringComparison.OrdinalIgnoreCase)) >= 0)
                {
                    if (!indices.Contains(searchIndex))
                    {
                        indices.Add(searchIndex);
                        patternCount++;
                    }
                    searchIndex++;
                }
                logger?.Debug($"[RPCS3] SplitByMagicBytes - Found pattern '{pattern}' {patternCount} times");
            }

            logger?.Debug($"[RPCS3] SplitByMagicBytes - Total unique pattern indices found: {indices.Count}");

            // Sort indices and extract entries
            indices.Sort();

            for (var i = 0; i < indices.Count; i++)
            {
                var entryStart = indices[i];
                var entryEnd = (i + 1 < indices.Count) ? indices[i + 1] : hexString.Length;
                var entryLength = entryEnd - entryStart;
                entries.Add(hexString.Substring(entryStart, entryLength));
            }

            // Log entry length distribution
            if (entries.Count > 0)
            {
                var lengths = entries.Select(e => e.Length).Distinct().OrderBy(l => l).ToList();
                logger?.Debug($"[RPCS3] SplitByMagicBytes - Entry lengths: [{string.Join(", ", lengths)}]");
                logger?.Debug($"[RPCS3] SplitByMagicBytes - First entry (first 100 chars): '{(entries[0].Length > 100 ? entries[0].Substring(0, 100) + "..." : entries[0])}'");
            }

            return entries;
        }

        /// <summary>
        /// Parses a single trophy entry from TROPUSR.DAT.
        /// Entry structure (matching SuccessStory's parsing):
        /// - Offset 0-1: Trophy ID (first 2 hex chars = 1 byte)
        /// - Offset 18-25: Unlock status (8 hex chars, "00000001" = unlocked)
        /// - Offset 44-57: Timestamp (14 hex chars, ticks * 10)
        /// </summary>
        private static void ParseTrophyEntry(string entry, Rpcs3Trophy trophy, ILogger logger)
        {
            logger?.Debug($"[RPCS3] ParseTrophyEntry - Parsing entry for trophy Id={trophy.Id}, entry length={entry?.Length ?? 0}");

            if (string.IsNullOrWhiteSpace(entry))
            {
                logger?.Debug($"[RPCS3] ParseTrophyEntry - Entry is null or empty for trophy Id={trophy.Id}");
                return;
            }

            if (entry.Length < 58)
            {
                logger?.Debug($"[RPCS3] ParseTrophyEntry - Entry too short ({entry.Length} chars) for trophy Id={trophy.Id}, expected at least 58");
                return;
            }

            // Check unlock status at offset 18-25 (8 hex chars)
            // Looking for "00000001" which indicates unlocked
            var unlockArea = entry.Substring(18, 8);
            logger?.Debug($"[RPCS3] ParseTrophyEntry - Trophy Id={trophy.Id}, unlock area (offset 18, 8 chars): '{unlockArea}'");
            trophy.Unlocked = unlockArea.Equals("00000001", StringComparison.OrdinalIgnoreCase);
            logger?.Debug($"[RPCS3] ParseTrophyEntry - Trophy Id={trophy.Id}, Unlocked={trophy.Unlocked}");

            // Parse timestamp at offset 44-57 (14 hex chars)
            if (trophy.Unlocked)
            {
                try
                {
                    var timestampHex = entry.Substring(44, 14);
                    logger?.Debug($"[RPCS3] ParseTrophyEntry - Trophy Id={trophy.Id}, timestamp hex (offset 44, 14 chars): '{timestampHex}'");

                    var timestampTicks = Convert.ToUInt64(timestampHex, 16);
                    logger?.Debug($"[RPCS3] ParseTrophyEntry - Trophy Id={trophy.Id}, raw timestamp ticks: {timestampTicks}");

                    // SuccessStory multiplies by 10 to convert to ticks
                    // The stored value is in units of 100ns, so multiply by 10 to get ticks
                    var ticks = (long)(timestampTicks * 10);
                    logger?.Debug($"[RPCS3] ParseTrophyEntry - Trophy Id={trophy.Id}, adjusted ticks: {ticks}");

                    if (ticks > 0 && ticks < DateTime.MaxValue.Ticks)
                    {
                        trophy.UnlockTimeUtc = new DateTime(ticks, DateTimeKind.Utc);
                        logger?.Debug($"[RPCS3] ParseTrophyEntry - Trophy Id={trophy.Id}, UnlockTimeUtc={trophy.UnlockTimeUtc?.ToString("o") ?? "(null)"}");
                    }
                    else
                    {
                        logger?.Debug($"[RPCS3] ParseTrophyEntry - Trophy Id={trophy.Id}, ticks out of valid range (0 < {ticks} < {DateTime.MaxValue.Ticks})");
                    }
                }
                catch (Exception ex)
                {
                    logger?.Debug(ex, $"[RPCS3] ParseTrophyEntry - Failed to parse timestamp for trophy Id={trophy.Id}");
                }
            }
            else
            {
                logger?.Debug($"[RPCS3] ParseTrophyEntry - Trophy Id={trophy.Id} is locked, skipping timestamp parse");
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
