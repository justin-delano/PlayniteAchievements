using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Playnite.SDK.Data;

namespace PlayniteAchievements.Providers.RetroAchievements
{
    /// <summary>
    /// Represents a console mapping configuration for RetroAchievements.
    /// </summary>
    public class ConsoleMapping
    {
        /// <summary>
        /// The RetroAchievements console ID.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// The human-readable console name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Keywords and aliases used for platform matching.
        /// </summary>
        public List<string> Keywords { get; set; }

        /// <summary>
        /// Optional category for grouping (e.g., "nintendo", "sega", "arcade").
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// Higher priority values are checked first during resolution.
        /// Default is 0. Used for specific variants that should match before general ones.
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// If true, requires checking for exclusion patterns before matching.
        /// Used for cases like Cassette Vision which should not match Super Cassette Vision.
        /// </summary>
        public bool RequiresExclusionCheck { get; set; }
    }

    /// <summary>
    /// Root container for console mappings configuration.
    /// </summary>
    public class ConsoleMappingConfig
    {
        /// <summary>
        /// List of all console mappings.
        /// </summary>
        public List<ConsoleMapping> Consoles { get; set; }

        /// <summary>
        /// Platforms that should be excluded from matching (modern platforms not supported by RetroAchievements).
        /// </summary>
        public List<string> ExcludedPlatforms { get; set; }
    }

    /// <summary>
    /// Registry for loading and querying console mappings from JSON.
    /// Provides thread-safe resolution of platform strings to RetroAchievements console IDs.
    /// </summary>
    public sealed class ConsoleMappingRegistry
    {
        private static readonly Lazy<ConsoleMappingRegistry> _instance = new Lazy<ConsoleMappingRegistry>(() => new ConsoleMappingRegistry());

        /// <summary>
        /// Singleton instance of the registry.
        /// </summary>
        public static ConsoleMappingRegistry Instance => _instance.Value;

        private readonly ConsoleMappingConfig _config;
        private readonly List<ConsoleMapping> _sortedConsoles;
        private readonly HashSet<string> _excludedPlatforms;

        private ConsoleMappingRegistry()
        {
            _config = LoadConfiguration();
            _sortedConsoles = SortConsolesByPriority(_config.Consoles);
            _excludedPlatforms = new HashSet<string>(_config.ExcludedPlatforms, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Attempts to resolve a platform string to a RetroAchievements console ID.
        /// </summary>
        /// <param name="value">The platform specification ID or name.</param>
        /// <param name="consoleId">The resolved console ID, or 0 if no match found.</param>
        /// <returns>True if a match was found; otherwise, false.</returns>
        public bool TryResolve(string value, out int consoleId)
        {
            consoleId = 0;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var normalized = Normalize(value);
            var tokens = Tokenize(value);

            // Check for excluded platforms first
            if (IsExcluded(normalized, tokens))
            {
                return false;
            }

            // Try to match against console keywords, in priority order
            foreach (var console in _sortedConsoles)
            {
                if (MatchesConsole(normalized, tokens, console))
                {
                    consoleId = console.Id;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets all registered console mappings.
        /// </summary>
        public IReadOnlyList<ConsoleMapping> GetAllConsoles() => _sortedConsoles;

        /// <summary>
        /// Gets a console mapping by ID.
        /// </summary>
        public ConsoleMapping GetConsoleById(int id)
        {
            return _config.Consoles.FirstOrDefault(c => c.Id == id);
        }

        /// <summary>
        /// Gets all excluded platform keywords.
        /// </summary>
        public IReadOnlyCollection<string> GetExcludedPlatforms() => _excludedPlatforms;

        private ConsoleMappingConfig LoadConfiguration()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "PlayniteAchievements.Providers.RetroAchievements.PlatformMapping.console-mappings.json";

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException(
                        $"Failed to load console mappings from embedded resource: {resourceName}");
                }

                using (var reader = new StreamReader(stream))
                {
                    var json = reader.ReadToEnd();
                    var config = Serialization.FromJson<ConsoleMappingConfig>(json);

                    if (config?.Consoles == null || config.Consoles.Count == 0)
                    {
                        throw new InvalidOperationException("Console mappings loaded but no consoles found.");
                    }

                    return config;
                }
            }
        }

        private List<ConsoleMapping> SortConsolesByPriority(List<ConsoleMapping> consoles)
        {
            return consoles.OrderByDescending(c => c.Priority).ToList();
        }

        private bool IsExcluded(string normalized, IReadOnlyList<string> tokens)
        {
            foreach (var excluded in _excludedPlatforms)
            {
                var normalizedExcluded = Normalize(excluded);
                if (string.IsNullOrWhiteSpace(normalizedExcluded))
                {
                    continue;
                }

                if (string.Equals(normalized, normalizedExcluded, StringComparison.Ordinal))
                {
                    return true;
                }

                if (ContainsToken(tokens, normalizedExcluded))
                {
                    return true;
                }

                if (normalizedExcluded.Length >= 4 && normalized.Contains(normalizedExcluded))
                {
                    return true;
                }
            }

            return false;
        }

        private bool MatchesConsole(string normalized, IReadOnlyList<string> tokens, ConsoleMapping console)
        {
            if (console.Keywords == null || console.Keywords.Count == 0)
            {
                return false;
            }

            foreach (var keyword in console.Keywords)
            {
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    continue;
                }

                var normalizedKeyword = Normalize(keyword);
                if (string.IsNullOrWhiteSpace(normalizedKeyword))
                {
                    continue;
                }

                var isMatch =
                    string.Equals(normalized, normalizedKeyword, StringComparison.Ordinal) ||
                    ContainsToken(tokens, normalizedKeyword) ||
                    (normalizedKeyword.Length >= 4 && normalized.Contains(normalizedKeyword));

                if (isMatch)
                {
                    // For consoles requiring exclusion checks (e.g., Cassette Vision),
                    // verify we're not actually matching a more specific variant
                    if (console.RequiresExclusionCheck)
                    {
                        var moreSpecificConsoles = _sortedConsoles
                            .Where(c => c.Id != console.Id && c.Priority > console.Priority)
                            .ToList();

                        foreach (var specific in moreSpecificConsoles)
                        {
                            if (MatchesConsole(normalized, tokens, specific))
                            {
                                return false; // Let the more specific console handle it
                            }
                        }
                    }

                    return true;
                }
            }

            return false;
        }

        private static bool ContainsToken(IReadOnlyList<string> tokens, string keyword)
        {
            if (tokens == null || tokens.Count == 0 || string.IsNullOrWhiteSpace(keyword))
            {
                return false;
            }

            for (var i = 0; i < tokens.Count; i++)
            {
                if (string.Equals(tokens[i], keyword, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<string> Tokenize(string input)
        {
            var tokens = new List<string>();
            if (string.IsNullOrWhiteSpace(input))
            {
                return tokens;
            }

            var normalized = input.Trim().ToLowerInvariant();
            var current = new StringBuilder(normalized.Length);

            foreach (var c in normalized)
            {
                if (char.IsLetterOrDigit(c))
                {
                    current.Append(c);
                }
                else if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }

            if (current.Length > 0)
            {
                tokens.Add(current.ToString());
            }

            return tokens;
        }

        private string Normalize(string input)
        {
            var s = input.Trim().ToLowerInvariant();
            var chars = new List<char>(s.Length);

            foreach (var c in s)
            {
                if (char.IsLetterOrDigit(c))
                {
                    chars.Add(c);
                }
            }

            return new string(chars.ToArray());
        }
    }
}
