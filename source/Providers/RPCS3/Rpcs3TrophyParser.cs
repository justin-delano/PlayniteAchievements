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
        /// <param name="language">Target language code (e.g., "en", "fr") for localized names.</param>
        /// <param name="logger">Logger for error reporting.</param>
        /// <returns>List of trophy definitions, or empty list on error.</returns>
        public static List<Rpcs3Trophy> ParseTrophyDefinitions(string tropconfPath, string language, ILogger logger)
        {
            var trophies = new List<Rpcs3Trophy>();

            if (string.IsNullOrWhiteSpace(tropconfPath) || !File.Exists(tropconfPath))
            {
                return trophies;
            }

            try
            {
                var doc = XDocument.Load(tropconfPath);
                var groupNames = BuildGroupNamesDictionary(doc);

                foreach (var trophyElement in doc.Descendants("trophy"))
                {
                    try
                    {
                        var trophy = ParseTrophyElement(trophyElement, groupNames, language);
                        if (trophy.Id >= 0)
                        {
                            trophies.Add(trophy);
                        }
                    }
                    catch
                    {
                        // Skip malformed trophy elements
                    }
                }

                logger?.Info($"[RPCS3] Parsed {trophies.Count} trophy definitions from '{Path.GetFileName(Path.GetDirectoryName(tropconfPath))}'");
            }
            catch (Exception ex)
            {
                logger?.Error(ex, $"[RPCS3] Failed to parse TROPCONF.SFM at '{tropconfPath}'");
            }

            return trophies;
        }

        /// <summary>
        /// Parses a single trophy element from XML.
        /// </summary>
        private static Rpcs3Trophy ParseTrophyElement(XElement trophyElement, Dictionary<string, string> groupNames, string language)
        {
            var gidAttr = trophyElement.Attribute("gid")?.Value;
            var groupId = gidAttr?.Trim() ?? "0";

            return new Rpcs3Trophy
            {
                Id = ParseIntAttribute(trophyElement, "id", -1),
                TrophyType = trophyElement.Attribute("ttype")?.Value?.Trim() ?? "B",
                Hidden = string.Equals(trophyElement.Attribute("hidden")?.Value, "yes", StringComparison.OrdinalIgnoreCase),
                Name = GetLocalizedElement(trophyElement, "name", language)?.Trim() ?? string.Empty,
                Description = GetLocalizedElement(trophyElement, "detail", language)?.Trim() ?? string.Empty,
                GroupId = groupId,
                GroupName = groupNames.TryGetValue(groupId, out var name) ? name : null
            };
        }

        /// <summary>
        /// Builds a dictionary mapping group IDs to group names from TROPCONF.SFM.
        /// Used for DLC trophy categorization.
        /// </summary>
        private static Dictionary<string, string> BuildGroupNamesDictionary(XDocument doc)
        {
            var groupNames = new Dictionary<string, string>();

            foreach (var groupElement in doc.Descendants("group"))
            {
                var groupId = groupElement.Attribute("id")?.Value;
                var groupName = groupElement.Element("name")?.Value?.Trim();

                if (!string.IsNullOrWhiteSpace(groupId) && !string.IsNullOrWhiteSpace(groupName))
                {
                    groupNames[groupId] = groupName;
                }
            }

            return groupNames;
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
            if (string.IsNullOrWhiteSpace(tropusrPath) || !File.Exists(tropusrPath) || trophies == null || trophies.Count == 0)
            {
                return;
            }

            try
            {
                var bytes = File.ReadAllBytes(tropusrPath);
                var hexString = BytesToHex(bytes);
                var entries = hexString.Split(MagicBytePatterns, StringSplitOptions.None).ToList();

                // Take the last N entries (where N = trophy count) - matching SuccessStory behavior
                var relevantEntries = entries.Count >= trophies.Count
                    ? entries.Skip(entries.Count - trophies.Count).Take(trophies.Count).ToList()
                    : new List<string>();

                var trophyById = trophies.ToDictionary(t => t.Id, t => t);

                foreach (var entry in relevantEntries)
                {
                    if (entry.Length < 58) continue;

                    try
                    {
                        var trophyId = (int)long.Parse(entry.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);

                        if (trophyById.TryGetValue(trophyId, out var trophy))
                        {
                            ParseTrophyEntry(entry, trophy);
                        }
                    }
                    catch
                    {
                        // Skip malformed entries
                    }
                }

                var unlockedCount = trophies.Count(t => t.Unlocked);
                logger?.Info($"[RPCS3] Parsed unlock data: {unlockedCount}/{trophies.Count} trophies unlocked");
            }
            catch (Exception ex)
            {
                logger?.Error(ex, $"[RPCS3] Failed to parse TROPUSR.DAT at '{tropusrPath}'");
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
                var bytes = File.ReadAllBytes(trophyTrpPath);
                var content = Encoding.UTF8.GetString(bytes);

                // Look for <npcommid>...</npcommid> pattern
                var tagStart = content.IndexOf("<npcommid>", StringComparison.OrdinalIgnoreCase);

                if (tagStart < 0)
                {
                    // Try alternate format: npcommid="..."
                    var attrStart = content.IndexOf("npcommid=", StringComparison.OrdinalIgnoreCase);
                    if (attrStart >= 0)
                    {
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
                if (tagEnd < 0) return null;

                return content.Substring(tagStart + "<npcommid>".Length, tagEnd - tagStart - "<npcommid>".Length).Trim();
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, $"[RPCS3] Failed to extract npcommid from '{trophyTrpPath}'");
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
        /// Parses a single trophy entry from TROPUSR.DAT.
        /// Entry structure (matching SuccessStory's parsing):
        /// - Offset 0-1: Trophy ID (first 2 hex chars = 1 byte)
        /// - Offset 18-25: Unlock status (8 hex chars, "00000001" = unlocked)
        /// - Offset 44-57: Timestamp (14 hex chars, ticks * 10)
        /// </summary>
        private static void ParseTrophyEntry(string entry, Rpcs3Trophy trophy)
        {
            if (entry.Length < 58) return;

            // Check unlock status at offset 18-25 (8 hex chars)
            var unlockArea = entry.Substring(18, 8);
            trophy.Unlocked = unlockArea.Equals("00000001", StringComparison.OrdinalIgnoreCase);

            // Parse timestamp at offset 44-57 (14 hex chars)
            if (trophy.Unlocked)
            {
                try
                {
                    var timestampHex = entry.Substring(44, 14);
                    var timestampTicks = Convert.ToUInt64(timestampHex, 16);
                    var ticks = (long)(timestampTicks * 10);

                    if (ticks > 0 && ticks < DateTime.MaxValue.Ticks)
                    {
                        trophy.UnlockTimeUtc = new DateTime(ticks, DateTimeKind.Utc);
                    }
                }
                catch
                {
                    // Keep trophy as unlocked but without timestamp
                }
            }
        }

        /// <summary>
        /// Parses an integer attribute from an XML element.
        /// </summary>
        private static int ParseIntAttribute(XElement element, string attributeName, int defaultValue)
        {
            var attrValue = element.Attribute(attributeName)?.Value;
            return int.TryParse(attrValue, out var result) ? result : defaultValue;
        }

        /// <summary>
        /// Gets a localized element value from a trophy element.
        /// Tries to find an element with matching lang attribute, falls back to element without lang.
        /// </summary>
        private static string GetLocalizedElement(XElement trophyElement, string elementName, string language)
        {
            if (string.IsNullOrWhiteSpace(language))
            {
                return trophyElement.Element(elementName)?.Value;
            }

            // Try to find element with matching lang attribute
            var localizedElement = trophyElement.Elements(elementName)
                .FirstOrDefault(e => string.Equals(e.Attribute("lang")?.Value, language, StringComparison.OrdinalIgnoreCase));

            if (localizedElement != null)
            {
                return localizedElement.Value;
            }

            // Fall back to element without lang attribute (default language)
            return trophyElement.Elements(elementName)
                .FirstOrDefault(e => e.Attribute("lang") == null)?.Value
                ?? trophyElement.Element(elementName)?.Value;
        }

        /// <summary>
        /// Parses trophy definitions from a TROPHY.TRP file.
        /// Used for pre-launch trophy detection before RPCS3 creates cache files.
        /// </summary>
        /// <param name="trophyTrpPath">Path to TROPHY.TRP file.</param>
        /// <param name="language">Target language code (e.g., "en", "fr") for localized names.</param>
        /// <param name="logger">Logger for error reporting.</param>
        /// <returns>List of trophy definitions (all Unlocked = false), or empty list on error.</returns>
        public static List<Rpcs3Trophy> ParseTrophyDefinitionsFromTrp(string trophyTrpPath, string language, ILogger logger)
        {
            var trophies = new List<Rpcs3Trophy>();

            if (string.IsNullOrWhiteSpace(trophyTrpPath) || !File.Exists(trophyTrpPath))
            {
                return trophies;
            }

            try
            {
                var bytes = File.ReadAllBytes(trophyTrpPath);
                var content = Encoding.UTF8.GetString(bytes);

                // Search for <trophyconf> start and end tags to extract XML content
                var tagStart = content.IndexOf("<trophyconf>", StringComparison.OrdinalIgnoreCase);
                if (tagStart < 0) return trophies;

                var tagEnd = content.IndexOf("</trophyconf>", tagStart, StringComparison.OrdinalIgnoreCase);
                if (tagEnd < 0) return trophies;

                var xmlContent = content.Substring(tagStart, tagEnd - tagStart + "</trophyconf>".Length);
                var doc = XDocument.Parse(xmlContent);
                var groupNames = BuildGroupNamesDictionary(doc);

                foreach (var trophyElement in doc.Descendants("trophy"))
                {
                    try
                    {
                        var trophy = ParseTrophyElement(trophyElement, groupNames, language);
                        trophy.Unlocked = false; // Pre-launch: all locked
                        trophy.UnlockTimeUtc = null;

                        if (trophy.Id >= 0)
                        {
                            trophies.Add(trophy);
                        }
                    }
                    catch
                    {
                        // Skip malformed trophy elements
                    }
                }

                logger?.Info($"[RPCS3] Parsed {trophies.Count} trophy definitions from TROPHY.TRP (pre-launch)");
            }
            catch (Exception ex)
            {
                logger?.Error(ex, $"[RPCS3] Failed to parse TROPHY.TRP at '{trophyTrpPath}'");
            }

            return trophies;
        }

        /// <summary>
        /// Maps a global language setting to PS3 locale code.
        /// </summary>
        /// <param name="globalLanguage">The global language setting (e.g., "english", "french").</param>
        /// <returns>PS3 locale code (e.g., "en", "fr"), or null for default.</returns>
        public static string MapGlobalLanguageToPs3Locale(string globalLanguage)
        {
            if (string.IsNullOrWhiteSpace(globalLanguage))
            {
                return null;
            }

            return globalLanguage.Trim().ToLowerInvariant() switch
            {
                "english" => "en",
                "french" => "fr",
                "spanish" => "es",
                "german" => "de",
                "italian" => "it",
                "japanese" => "ja",
                "dutch" => "nl",
                "portuguese" => "pt",
                "russian" => "ru",
                "korean" => "ko",
                "chinese" => "zh",
                "polish" => "pl",
                "danish" => "da",
                "finnish" => "fi",
                "norwegian" => "no",
                "swedish" => "sv",
                "turkish" => "tr",
                "czech" => "cs",
                "hungarian" => "hu",
                "greek" => "el",
                "brazilian" => "pt-br",
                "latam" => "es-419",
                _ => null
            };
        }
    }
}
