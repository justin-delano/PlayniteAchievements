using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Providers.RPCS3.Models;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.RPCS3
{
    /// <summary>
    /// Scanner for RPCS3 PlayStation 3 emulator trophy data.
    /// Orchestrates trophy folder discovery and game matching.
    /// </summary>
    internal sealed class Rpcs3Scanner
    {
        private readonly ILogger _logger;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly Rpcs3DataProvider _provider;
        private readonly IPlayniteAPI _playniteApi;
        private readonly string _pluginUserDataPath;

        // Default rarity estimates by trophy type
        private const double PlatinumRarity = 5.0;
        private const double GoldRarity = 15.0;
        private const double SilverRarity = 30.0;
        private const double BronzeRarity = 60.0;

        // Icon copying settings
        private const int MaxCopyAttempts = 5;
        private const int CopyRetryDelayMs = 200;

        public Rpcs3Scanner(ILogger logger, PlayniteAchievementsSettings settings, Rpcs3DataProvider provider = null, IPlayniteAPI playniteApi = null, string pluginUserDataPath = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _provider = provider;
            _playniteApi = playniteApi;
            _pluginUserDataPath = pluginUserDataPath;
        }

        public async Task<RebuildPayload> RefreshAsync(
            List<Game> gamesToRefresh,
            Action<ProviderRefreshUpdate> progressCallback,
            Func<GameAchievementData, Task> OnGameRefreshed,
            CancellationToken cancel)
        {
            _logger?.Debug("[RPCS3] RefreshAsync - Starting refresh");
            var report = progressCallback ?? (_ => { });
            var summary = new RebuildSummary();

            if (gamesToRefresh == null || gamesToRefresh.Count == 0)
            {
                _logger?.Debug("[RPCS3] RefreshAsync - No games to refresh");
                return new RebuildPayload { Summary = summary };
            }

            _logger?.Debug($"[RPCS3] RefreshAsync - Processing {gamesToRefresh.Count} games");

            // Use the provider's cache if available, otherwise build our own
            Dictionary<string, string> trophyFolderCache;
            if (_provider != null)
            {
                _logger?.Debug("[RPCS3] RefreshAsync - Using provider's trophy folder cache");
                trophyFolderCache = _provider.GetOrBuildTrophyFolderCache();
            }
            else
            {
                _logger?.Debug("[RPCS3] RefreshAsync - Building trophy folder cache directly");
                trophyFolderCache = await BuildTrophyFolderCacheAsync(cancel).ConfigureAwait(false);
            }

            if (trophyFolderCache == null || trophyFolderCache.Count == 0)
            {
                _logger?.Warn("[RPCS3] RefreshAsync - No trophy folders found in RPCS3 trophy directory.");
                return new RebuildPayload { Summary = summary };
            }

            _logger?.Info($"[RPCS3] RefreshAsync - Using trophy folder cache with {trophyFolderCache.Count} games.");
            _logger?.Debug($"[RPCS3] RefreshAsync - Cache NPCommIDs: [{string.Join(", ", trophyFolderCache.Keys)}]");

            var providerName = GetProviderName();

            for (var i = 0; i < gamesToRefresh.Count; i++)
            {
                cancel.ThrowIfCancellationRequested();
                var game = gamesToRefresh[i];
                _logger?.Debug($"[RPCS3] RefreshAsync - Processing game {i + 1}/{gamesToRefresh.Count}: '{game?.Name ?? "(null)"}'");
                report(new ProviderRefreshUpdate { CurrentGameName = game?.Name });

                try
                {
                    var data = await FetchGameDataAsync(game, trophyFolderCache, providerName, cancel).ConfigureAwait(false);
                    _logger?.Debug($"[RPCS3] RefreshAsync - FetchGameDataAsync result for '{game?.Name}': HasData={data != null}, HasAchievements={data?.HasAchievements ?? false}, AchievementCount={data?.Achievements?.Count ?? 0}");

                    if (data != null && OnGameRefreshed != null)
                    {
                        await OnGameRefreshed(data).ConfigureAwait(false);
                    }

                    summary.GamesRefreshed++;
                    if (data != null && data.HasAchievements)
                    {
                        summary.GamesWithAchievements++;
                    }
                    else
                    {
                        summary.GamesWithoutAchievements++;
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger?.Debug($"[RPCS3] RefreshAsync - Operation cancelled at game {i + 1}/{gamesToRefresh.Count}");
                    throw;
                }
                catch (Exception ex)
                {
                    _logger?.Debug(ex, $"[RPCS3] RefreshAsync - Failed to scan '{game?.Name}'");
                }
            }

            _logger?.Debug($"[RPCS3] RefreshAsync - Complete. GamesRefreshed={summary.GamesRefreshed}, GamesWithAchievements={summary.GamesWithAchievements}, GamesWithoutAchievements={summary.GamesWithoutAchievements}");
            report(new ProviderRefreshUpdate { CurrentGameName = null });
            return new RebuildPayload { Summary = summary };
        }

        /// <summary>
        /// Builds a cache mapping npcommid to trophy folder path.
        /// Trophy folder structure: rpcs3_install/trophy/npcommid/
        /// </summary>
        private async Task<Dictionary<string, string>> BuildTrophyFolderCacheAsync(CancellationToken cancel)
        {
            _logger?.Debug("[RPCS3] BuildTrophyFolderCacheAsync - Starting");
            var cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var installFolder = _settings?.Persisted?.Rpcs3InstallationFolder;
            if (string.IsNullOrWhiteSpace(installFolder))
            {
                _logger?.Debug("[RPCS3] BuildTrophyFolderCacheAsync - No RPCS3 installation folder configured");
                return cache;
            }

            _logger?.Debug($"[RPCS3] BuildTrophyFolderCacheAsync - Install folder: '{installFolder}'");
            var trophyPath = Path.Combine(installFolder, "trophy");
            _logger?.Debug($"[RPCS3] BuildTrophyFolderCacheAsync - Trophy path: '{trophyPath}'");

            if (!Directory.Exists(trophyPath))
            {
                _logger?.Warn($"[RPCS3] BuildTrophyFolderCacheAsync - Trophy folder not found at '{trophyPath}'");
                return cache;
            }

            try
            {
                var npcommidDirectories = Directory.GetDirectories(trophyPath);
                _logger?.Debug($"[RPCS3] BuildTrophyFolderCacheAsync - Found {npcommidDirectories.Length} directories in trophy folder");

                foreach (var npcommidDir in npcommidDirectories)
                {
                    cancel.ThrowIfCancellationRequested();

                    var npcommid = Path.GetFileName(npcommidDir);
                    if (string.IsNullOrWhiteSpace(npcommid))
                    {
                        _logger?.Debug($"[RPCS3] BuildTrophyFolderCacheAsync - Skipping directory with empty name");
                        continue;
                    }

                    // Verify TROPCONF.SFM exists
                    var tropconfPath = Path.Combine(npcommidDir, "TROPCONF.SFM");
                    if (File.Exists(tropconfPath))
                    {
                        cache[npcommid] = npcommidDir;
                        _logger?.Debug($"[RPCS3] BuildTrophyFolderCacheAsync - Added '{npcommid}' -> '{npcommidDir}'");
                    }
                    else
                    {
                        _logger?.Debug($"[RPCS3] BuildTrophyFolderCacheAsync - Skipping '{npcommid}' (no TROPCONF.SFM)");
                    }
                }

                _logger?.Debug($"[RPCS3] BuildTrophyFolderCacheAsync - Complete with {cache.Count} valid trophy folders");
            }
            catch (OperationCanceledException)
            {
                _logger?.Debug("[RPCS3] BuildTrophyFolderCacheAsync - Operation cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"[RPCS3] BuildTrophyFolderCacheAsync - Failed to enumerate trophy directories at '{trophyPath}'");
            }

            return await Task.FromResult(cache).ConfigureAwait(false);
        }

        private Task<GameAchievementData> FetchGameDataAsync(
            Game game,
            Dictionary<string, string> trophyFolderCache,
            string providerName,
            CancellationToken cancel)
        {
            if (game == null)
            {
                _logger?.Debug("[RPCS3] FetchGameDataAsync - Game is null, returning null");
                return Task.FromResult<GameAchievementData>(null);
            }

            _logger?.Debug($"[RPCS3] FetchGameDataAsync - Starting for game '{game.Name}' (Id: {game.Id})");
            _logger?.Debug($"[RPCS3] FetchGameDataAsync - Game Source: '{game.Source?.Name ?? "(null)"}'");
            _logger?.Debug($"[RPCS3] FetchGameDataAsync - Game InstallDirectory: '{game.InstallDirectory ?? "(null)"}'");

            // Find npcommid for this game
            var npcommid = FindNpCommIdForGame(game, trophyFolderCache, cancel);
            _logger?.Debug($"[RPCS3] FetchGameDataAsync - Found npcommid: '{npcommid ?? "(null)"}'");

            if (string.IsNullOrWhiteSpace(npcommid))
            {
                _logger?.Debug($"[RPCS3] FetchGameDataAsync - No npcommid found for game '{game.Name}', returning no achievements");
                return Task.FromResult(BuildNoAchievementsData(game, providerName));
            }

            cancel.ThrowIfCancellationRequested();

            // Look up trophy folder
            if (!trophyFolderCache.TryGetValue(npcommid, out var trophyFolderPath))
            {
                _logger?.Debug($"[RPCS3] FetchGameDataAsync - Trophy folder not found for npcommid '{npcommid}' in cache");
                return Task.FromResult(BuildNoAchievementsData(game, providerName));
            }

            _logger?.Debug($"[RPCS3] FetchGameDataAsync - Trophy folder path: '{trophyFolderPath}'");

            var tropconfPath = Path.Combine(trophyFolderPath, "TROPCONF.SFM");
            var tropusrPath = Path.Combine(trophyFolderPath, "TROPUSR.DAT");

            _logger?.Debug($"[RPCS3] FetchGameDataAsync - TROPCONF.SFM path: '{tropconfPath}', Exists: {File.Exists(tropconfPath)}");
            _logger?.Debug($"[RPCS3] FetchGameDataAsync - TROPUSR.DAT path: '{tropusrPath}', Exists: {File.Exists(tropusrPath)}");

            if (!File.Exists(tropconfPath))
            {
                _logger?.Debug($"[RPCS3] FetchGameDataAsync - TROPCONF.SFM not found at '{tropconfPath}'");
                return Task.FromResult(BuildNoAchievementsData(game, providerName));
            }

            try
            {
                _logger?.Debug($"[RPCS3] FetchGameDataAsync - Parsing trophy definitions from '{tropconfPath}'");
                // Parse trophy definitions
                var trophies = Rpcs3TrophyParser.ParseTrophyDefinitions(tropconfPath, _logger);
                _logger?.Debug($"[RPCS3] FetchGameDataAsync - Parsed {trophies.Count} trophy definitions");

                // Parse unlock data
                if (File.Exists(tropusrPath))
                {
                    _logger?.Debug($"[RPCS3] FetchGameDataAsync - Parsing unlock data from '{tropusrPath}'");
                    Rpcs3TrophyParser.ParseTrophyUnlockData(tropusrPath, trophies, _logger);
                    var parsedUnlockedCount = trophies.Count(t => t.Unlocked);
                    _logger?.Debug($"[RPCS3] FetchGameDataAsync - {parsedUnlockedCount}/{trophies.Count} trophies unlocked");
                }
                else
                {
                    _logger?.Debug($"[RPCS3] FetchGameDataAsync - No TROPUSR.DAT, all trophies remain locked");
                }

                if (trophies.Count == 0)
                {
                    _logger?.Debug($"[RPCS3] FetchGameDataAsync - No trophies parsed, returning no achievements");
                    return Task.FromResult(BuildNoAchievementsData(game, providerName));
                }

                // Convert to achievements
                _logger?.Debug($"[RPCS3] FetchGameDataAsync - Converting {trophies.Count} trophies to achievements");
                var achievements = new List<AchievementDetail>();
                var unlockedCount = 0;

                foreach (var trophy in trophies)
                {
                    if (trophy.Unlocked)
                    {
                        unlockedCount++;
                    }

                    var iconPath = GetTrophyIconPath(trophyFolderPath, npcommid, trophy.Id);
                    if (iconPath == null)
                    {
                        _logger?.Debug($"[RPCS3] FetchGameDataAsync - No icon found for trophy {trophy.Id} ('{trophy.Name}')");
                    }

                    var normalizedTrophyType = NormalizeTrophyType(trophy.TrophyType);
                    achievements.Add(new AchievementDetail
                    {
                        ApiName = trophy.Id.ToString(),
                        DisplayName = trophy.Name,
                        Description = trophy.Description,
                        UnlockedIconPath = iconPath,
                        LockedIconPath = iconPath,
                        Hidden = trophy.Hidden,
                        Unlocked = trophy.Unlocked,
                        UnlockTimeUtc = trophy.UnlockTimeUtc,
                        GlobalPercentUnlocked = GetRarityByTrophyType(trophy.TrophyType),
                        TrophyType = normalizedTrophyType,
                        IsCapstone = normalizedTrophyType == "platinum",
                        Category = trophy.GroupName
                    });
                }

                _logger?.Debug($"[RPCS3] FetchGameDataAsync - Created {achievements.Count} achievements, {unlockedCount} unlocked");

                return Task.FromResult(new GameAchievementData
                {
                    ProviderName = providerName,
                    LibrarySourceName = game?.Source?.Name,
                    GameName = game?.Name,
                    PlayniteGameId = game?.Id,
                    HasAchievements = achievements.Count > 0,
                    Achievements = achievements,
                    LastUpdatedUtc = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"[RPCS3] FetchGameDataAsync - Failed to parse trophy data for '{game.Name}'");
                return Task.FromResult(BuildNoAchievementsData(game, providerName));
            }
        }

        // PS3 title/serial ID patterns: BLUS, BLES, BCES, NPUB, NPEB, etc.
        private static readonly System.Text.RegularExpressions.Regex Ps3IdPattern =
            new System.Text.RegularExpressions.Regex(@"\b([A-Z]{2,4}\d{5})\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Pattern to extract npcommid from TROPHY.TRP file content
        private static readonly System.Text.RegularExpressions.Regex NpCommIdPattern =
            new System.Text.RegularExpressions.Regex(@"<npcommid>(.*?)<\/npcommid>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Pattern to extract title-name from TROPCONF.SFM
        private static readonly System.Text.RegularExpressions.Regex TitleNamePattern =
            new System.Text.RegularExpressions.Regex(@"<title-name>(.*?)<\/title-name>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>
        /// Finds the npcommid for a game using multiple strategies:
        /// 1. Extract PS3 ID from the install path (e.g., BLUS12345)
        /// 2. Search for TROPHY.TRP file and parse npcommid from it
        /// </summary>
        private string FindNpCommIdForGame(Game game, Dictionary<string, string> trophyFolderCache, CancellationToken cancel)
        {
            var rawInstallDir = game?.InstallDirectory;
            var gameDirectory = ExpandGamePath(game, rawInstallDir);

            _logger?.Debug($"[RPCS3] FindNpCommIdForGame - Game: '{game?.Name}'");
            _logger?.Debug($"[RPCS3] FindNpCommIdForGame - Raw InstallDirectory: '{rawInstallDir ?? "(null)"}'");
            _logger?.Debug($"[RPCS3] FindNpCommIdForGame - Expanded gameDirectory: '{gameDirectory ?? "(null)"}'");

            // Strategy 1: Extract PS3 ID from the path and look it up in cache
            if (!string.IsNullOrWhiteSpace(gameDirectory))
            {
                var match = Ps3IdPattern.Match(gameDirectory);
                _logger?.Debug($"[RPCS3] FindNpCommIdForGame - PS3 ID pattern match: Success={match.Success}");

                if (match.Success)
                {
                    var ps3Id = match.Groups[1].Value.ToUpperInvariant();
                    _logger?.Debug($"[RPCS3] FindNpCommIdForGame - Extracted PS3 ID: '{ps3Id}'");

                    if (trophyFolderCache.ContainsKey(ps3Id))
                    {
                        _logger?.Debug($"[RPCS3] FindNpCommIdForGame - Found matching npcommid '{ps3Id}' in cache");
                        return ps3Id;
                    }
                    else
                    {
                        _logger?.Debug($"[RPCS3] FindNpCommIdForGame - PS3 ID '{ps3Id}' not found in cache");
                    }
                }
            }
            else
            {
                _logger?.Debug($"[RPCS3] FindNpCommIdForGame - Game directory is null or empty");
            }

            // Strategy 2: Search for TROPHY.TRP file and extract npcommid
            _logger?.Debug($"[RPCS3] FindNpCommIdForGame - Trying TROPHY.TRP search fallback");
            var npcommidFromTrp = FindNpCommIdFromTrophyTrp(game, gameDirectory, trophyFolderCache, cancel);
            if (!string.IsNullOrWhiteSpace(npcommidFromTrp))
            {
                return npcommidFromTrp;
            }

            // Strategy 3: Match by game name against TROPCONF.SFM titles
            _logger?.Debug($"[RPCS3] FindNpCommIdForGame - Trying name-based matching fallback");
            var npcommidFromName = FindNpCommIdByName(game, trophyFolderCache);
            if (!string.IsNullOrWhiteSpace(npcommidFromName))
            {
                return npcommidFromName;
            }

            _logger?.Debug($"[RPCS3] FindNpCommIdForGame - No npcommid found for game '{game?.Name}'");
            return null;
        }

        /// <summary>
        /// Searches for TROPHY.TRP file in the game directory tree and extracts the npcommid.
        /// This follows SuccessStory's approach: go up one directory, then search recursively.
        /// </summary>
        private string FindNpCommIdFromTrophyTrp(Game game, string gameDirectory,
            Dictionary<string, string> trophyFolderCache, CancellationToken cancel)
        {
            if (string.IsNullOrWhiteSpace(gameDirectory))
            {
                _logger?.Debug("[RPCS3] FindNpCommIdFromTrophyTrp - Game directory is empty");
                return null;
            }

            try
            {
                // Go up one directory level (like SuccessStory does)
                var searchPath = Path.GetDirectoryName(gameDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrWhiteSpace(searchPath) || !Directory.Exists(searchPath))
                {
                    _logger?.Debug($"[RPCS3] FindNpCommIdFromTrophyTrp - Parent directory not found: '{searchPath}'");
                    return null;
                }

                _logger?.Debug($"[RPCS3] FindNpCommIdFromTrophyTrp - Searching for TROPHY.TRP in: '{searchPath}'");

                // Search recursively for TROPHY.TRP
                var trophyTrpFiles = FindFilesRecursive(searchPath, "TROPHY.TRP", maxDepth: 5, cancel);
                _logger?.Debug($"[RPCS3] FindNpCommIdFromTrophyTrp - Found {trophyTrpFiles.Count} TROPHY.TRP file(s)");

                foreach (var trpFile in trophyTrpFiles)
                {
                    cancel.ThrowIfCancellationRequested();

                    _logger?.Debug($"[RPCS3] FindNpCommIdFromTrophyTrp - Parsing: '{trpFile}'");
                    var npcommid = ParseNpCommIdFromTrpFile(trpFile);
                    _logger?.Debug($"[RPCS3] FindNpCommIdFromTrophyTrp - Extracted npcommid: '{npcommid ?? "(null)"}'");

                    if (!string.IsNullOrWhiteSpace(npcommid) && trophyFolderCache.ContainsKey(npcommid))
                    {
                        _logger?.Debug($"[RPCS3] FindNpCommIdFromTrophyTrp - Found matching npcommid '{npcommid}' in cache");
                        return npcommid;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"[RPCS3] FindNpCommIdFromTrophyTrp - Error searching for TROPHY.TRP");
            }

            return null;
        }

        /// <summary>
        /// Attempts to match a game by name against the titles in TROPCONF.SFM files.
        /// This is useful for ISO-based games where the PS3 ID cannot be extracted from the path.
        /// </summary>
        private string FindNpCommIdByName(Game game, Dictionary<string, string> trophyFolderCache)
        {
            if (game == null || string.IsNullOrWhiteSpace(game.Name))
            {
                _logger?.Debug("[RPCS3] FindNpCommIdByName - Game or game name is null");
                return null;
            }

            var normalizedGameName = NormalizeGameName(game.Name);
            if (string.IsNullOrWhiteSpace(normalizedGameName))
            {
                _logger?.Debug($"[RPCS3] FindNpCommIdByName - Normalized game name is empty for '{game.Name}'");
                return null;
            }

            _logger?.Debug($"[RPCS3] FindNpCommIdByName - Looking for match for '{game.Name}' (normalized: '{normalizedGameName}')");

            string bestMatch = null;
            int bestScore = 0;

            foreach (var kvp in trophyFolderCache)
            {
                var npcommid = kvp.Key;
                var trophyFolder = kvp.Value;

                var titleName = ExtractTitleNameFromTropconf(trophyFolder);
                if (string.IsNullOrWhiteSpace(titleName))
                {
                    continue;
                }

                var normalizedTitle = NormalizeGameName(titleName);
                if (string.IsNullOrWhiteSpace(normalizedTitle))
                {
                    continue;
                }

                // Check if one name contains the other (handles subtitle differences)
                var score = CalculateNameSimilarity(normalizedGameName, normalizedTitle);
                if (score > bestScore && score >= 70) // Require at least 70% similarity
                {
                    bestScore = score;
                    bestMatch = npcommid;
                    _logger?.Debug($"[RPCS3] FindNpCommIdByName - Candidate: '{titleName}' (normalized: '{normalizedTitle}') score={score}");
                }
            }

            if (!string.IsNullOrWhiteSpace(bestMatch))
            {
                _logger?.Debug($"[RPCS3] FindNpCommIdByName - Best match: '{bestMatch}' with score {bestScore}");
                return bestMatch;
            }

            _logger?.Debug($"[RPCS3] FindNpCommIdByName - No match found for '{game.Name}'");
            return null;
        }

        /// <summary>
        /// Extracts the title-name from a TROPCONF.SFM file.
        /// </summary>
        private string ExtractTitleNameFromTropconf(string trophyFolder)
        {
            if (string.IsNullOrWhiteSpace(trophyFolder))
            {
                return null;
            }

            try
            {
                var tropconfPath = Path.Combine(trophyFolder, "TROPCONF.SFM");
                if (!File.Exists(tropconfPath))
                {
                    return null;
                }

                // Read only the first few KB to find the title (title is near the top)
                using (var stream = new FileStream(tropconfPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                {
                    // Read enough lines to find the title
                    for (int i = 0; i < 20; i++)
                    {
                        var line = reader.ReadLine();
                        if (line == null) break;

                        var match = TitleNamePattern.Match(line);
                        if (match.Success)
                        {
                            return match.Groups[1].Value?.Trim();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"[RPCS3] ExtractTitleNameFromTropconf - Error reading TROPCONF.SFM in '{trophyFolder}'");
            }

            return null;
        }

        /// <summary>
        /// Normalizes a game name for comparison by removing special characters,
        /// normalizing whitespace, and converting to lowercase.
        /// </summary>
        private static string NormalizeGameName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            // Remove common suffixes/prefixes
            var normalized = System.Text.RegularExpressions.Regex.Replace(
                name,
                @"\s*[-:]\s*(PlayStation\s*)?(PS[1234])\s*(Edition|Version|Demo|Beta|Trial|Region\s*Free)?",
                "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // Remove registered/trademark symbols
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"[®™©]", "");

            // Remove content in parentheses (region info, language codes, etc.)
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\([^)]*\)", "");

            // Remove content in brackets
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\[[^\]]*\]", "");

            // Remove file extensions
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\.(iso|pkg|rap)$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // Remove special characters, keep alphanumeric and spaces
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"[^a-zA-Z0-9\s]", " ");

            // Normalize whitespace
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", " ").Trim();

            return normalized.ToLowerInvariant().Trim();
        }

        /// <summary>
        /// Calculates a similarity score (0-100) between two normalized game names.
        /// </summary>
        private static int CalculateNameSimilarity(string name1, string name2)
        {
            if (string.IsNullOrWhiteSpace(name1) || string.IsNullOrWhiteSpace(name2))
            {
                return 0;
            }

            // Exact match
            if (string.Equals(name1, name2, StringComparison.OrdinalIgnoreCase))
            {
                return 100;
            }

            // Check if one contains the other (handles subtitle differences)
            if (name1.Contains(name2) || name2.Contains(name1))
            {
                var longerLength = Math.Max(name1.Length, name2.Length);
                var shorterLength = Math.Min(name1.Length, name2.Length);
                return (int)((double)shorterLength / longerLength * 100);
            }

            // Word-based matching
            var words1 = name1.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var words2 = name2.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (words1.Length == 0 || words2.Length == 0)
            {
                return 0;
            }

            var set1 = new HashSet<string>(words1, StringComparer.OrdinalIgnoreCase);
            var set2 = new HashSet<string>(words2, StringComparer.OrdinalIgnoreCase);
            var intersection = new HashSet<string>(set1, StringComparer.OrdinalIgnoreCase);
            intersection.IntersectWith(set2);

            if (intersection.Count == 0)
            {
                return 0;
            }

            // Calculate Jaccard-like similarity with bonus for matching key words
            var union = new HashSet<string>(set1, StringComparer.OrdinalIgnoreCase);
            union.UnionWith(set2);

            var jaccardScore = (double)intersection.Count / union.Count * 100;

            // Bonus if the first word matches (usually the main title)
            if (string.Equals(words1[0], words2[0], StringComparison.OrdinalIgnoreCase))
            {
                jaccardScore = Math.Min(100, jaccardScore * 1.3);
            }

            return (int)jaccardScore;
        }

        /// <summary>
        /// Recursively searches for files with the specified name.
        /// </summary>
        private List<string> FindFilesRecursive(string directory, string fileName, int maxDepth, CancellationToken cancel)
        {
            var results = new List<string>();
            FindFilesRecursiveInternal(directory, fileName, maxDepth, 0, results, cancel);
            return results;
        }

        private void FindFilesRecursiveInternal(string directory, string fileName, int maxDepth, int currentDepth,
            List<string> results, CancellationToken cancel)
        {
            if (currentDepth > maxDepth || results.Count >= 10) // Limit search
            {
                return;
            }

            cancel.ThrowIfCancellationRequested();

            try
            {
                // Check for file in current directory
                var filePath = Path.Combine(directory, fileName);
                if (File.Exists(filePath))
                {
                    results.Add(filePath);
                    _logger?.Debug($"[RPCS3] FindFilesRecursive - Found: '{filePath}'");
                    return; // Found it, no need to search deeper in this branch
                }

                // Search subdirectories
                foreach (var subDir in Directory.GetDirectories(directory))
                {
                    cancel.ThrowIfCancellationRequested();

                    // Skip common non-game directories
                    var dirName = Path.GetFileName(subDir);
                    if (dirName?.StartsWith(".") == true ||
                        string.Equals(dirName, "dev_hdd0", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(dirName, "dev_hdd1", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(dirName, "trophy", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    FindFilesRecursiveInternal(subDir, fileName, maxDepth, currentDepth + 1, results, cancel);

                    if (results.Count >= 10)
                    {
                        return;
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip directories we can't access
            }
            catch (Exception ex)
            {
                _logger?.Debug($"[RPCS3] FindFilesRecursive - Error in '{directory}': {ex.Message}");
            }
        }

        /// <summary>
        /// Parses the npcommid from a TROPHY.TRP file.
        /// The npcommid is stored in XML format within the file.
        /// </summary>
        private string ParseNpCommIdFromTrpFile(string trpFilePath)
        {
            try
            {
                // TROPHY.TRP contains XML data that we can read as text
                // The npcommid is in a tag like: <npcommid>NPWR00807_00</npcommid>
                var fileContent = File.ReadAllText(trpFilePath);
                var match = NpCommIdPattern.Match(fileContent);

                if (match.Success)
                {
                    return match.Groups[1].Value?.Trim();
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug($"[RPCS3] ParseNpCommIdFromTrpFile - Error reading '{trpFilePath}': {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Expands path variables in game paths using Playnite's variable expansion.
        /// </summary>
        private string ExpandGamePath(Game game, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                _logger?.Debug($"[RPCS3] ExpandGamePath - Path is null or empty, returning as-is");
                return path;
            }

            // Use provider's expansion if available
            if (_provider != null)
            {
                var expanded = _provider.ExpandGamePath(game, path);
                _logger?.Debug($"[RPCS3] ExpandGamePath - Provider expansion: '{path}' -> '{expanded}'");
                return expanded;
            }

            // Fallback: use Playnite API directly if available
            try
            {
                var expanded = _playniteApi?.ExpandGameVariables(game, path) ?? path;
                _logger?.Debug($"[RPCS3] ExpandGamePath - Playnite API expansion: '{path}' -> '{expanded}'");
                return expanded;
            }
            catch (Exception ex)
            {
                _logger?.Debug($"[RPCS3] ExpandGamePath - Expansion failed: {ex.Message}, returning original path");
                return path;
            }
        }

        private static GameAchievementData BuildNoAchievementsData(Game game, string providerName)
        {
            return new GameAchievementData
            {
                ProviderName = providerName,
                LibrarySourceName = game?.Source?.Name,
                GameName = game?.Name,
                PlayniteGameId = game?.Id,
                HasAchievements = false,
                LastUpdatedUtc = DateTime.UtcNow
            };
        }

        private static string GetProviderName()
        {
            var value = ResourceProvider.GetString("LOCPlayAch_Provider_RPCS3");
            return string.IsNullOrWhiteSpace(value) ? "RPCS3" : value;
        }

        private static double GetRarityByTrophyType(string trophyType)
        {
            if (string.IsNullOrWhiteSpace(trophyType))
            {
                return BronzeRarity;
            }

            return trophyType.ToUpperInvariant() switch
            {
                "P" => PlatinumRarity,
                "G" => GoldRarity,
                "S" => SilverRarity,
                "B" => BronzeRarity,
                _ => BronzeRarity
            };
        }

        private static string NormalizeTrophyType(string trophyType)
        {
            if (string.IsNullOrWhiteSpace(trophyType))
            {
                return null;
            }

            return trophyType.ToUpperInvariant() switch
            {
                "P" => "platinum",
                "G" => "gold",
                "S" => "silver",
                "B" => "bronze",
                _ => null
            };
        }

        /// <summary>
        /// Gets the trophy icon path, copying it to plugin data folder if necessary.
        /// This ensures icons remain available even if RPCS3 is moved or updated.
        /// </summary>
        private string GetTrophyIconPath(string trophyFolderPath, string npcommid, int trophyId)
        {
            if (string.IsNullOrWhiteSpace(trophyFolderPath))
            {
                _logger?.Debug($"[RPCS3] GetTrophyIconPath - Trophy folder path is null or empty");
                return null;
            }

            try
            {
                // Trophy icons follow TROP###.PNG format with zero-padded ID
                var iconFileName = $"TROP{trophyId.ToString().PadLeft(3, '0')}.PNG";
                var sourcePath = Path.Combine(trophyFolderPath, iconFileName);

                _logger?.Debug($"[RPCS3] GetTrophyIconPath - Looking for icon at '{sourcePath}'");

                if (!File.Exists(sourcePath))
                {
                    _logger?.Debug($"[RPCS3] GetTrophyIconPath - Icon not found at '{sourcePath}'");
                    return null;
                }

                // If no plugin data path, return direct path (fallback)
                if (string.IsNullOrWhiteSpace(_pluginUserDataPath))
                {
                    _logger?.Debug($"[RPCS3] GetTrophyIconPath - No plugin data path, returning direct path");
                    return sourcePath;
                }

                // Copy icon to plugin data folder
                return CopyTrophyIconToPluginData(sourcePath, npcommid, iconFileName);
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"[RPCS3] GetTrophyIconPath - Failed to get icon path for trophy {trophyId}");
                return null;
            }
        }

        /// <summary>
        /// Copies a trophy icon from RPCS3 folder to plugin data folder.
        /// Returns the relative path to the copied file for display.
        /// </summary>
        private string CopyTrophyIconToPluginData(string sourcePath, string npcommid, string iconFileName)
        {
            try
            {
                // Create target directory: {PluginData}/rpcs3/{npcommid}/
                var rpcs3IconsDir = Path.Combine(_pluginUserDataPath, "rpcs3", npcommid);
                var targetPath = Path.Combine(rpcs3IconsDir, iconFileName);

                // Ensure directory exists
                if (!Directory.Exists(rpcs3IconsDir))
                {
                    Directory.CreateDirectory(rpcs3IconsDir);
                    _logger?.Debug($"[RPCS3] CopyTrophyIconToPluginData - Created directory '{rpcs3IconsDir}'");
                }

                // Only copy if target doesn't exist (avoid redundant I/O)
                if (File.Exists(targetPath))
                {
                    _logger?.Debug($"[RPCS3] CopyTrophyIconToPluginData - Icon already exists at '{targetPath}'");
                    return targetPath;
                }

                // Copy with retry logic for file access issues
                for (var attempt = 0; attempt < MaxCopyAttempts; attempt++)
                {
                    try
                    {
                        using (var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var destStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write))
                        {
                            sourceStream.CopyTo(destStream);
                        }

                        _logger?.Debug($"[RPCS3] CopyTrophyIconToPluginData - Copied '{sourcePath}' to '{targetPath}'");
                        return targetPath;
                    }
                    catch (IOException)
                    {
                        if (attempt == MaxCopyAttempts - 1)
                        {
                            _logger?.Debug($"[RPCS3] CopyTrophyIconToPluginData - Failed after {MaxCopyAttempts} attempts");
                            return sourcePath; // Fall back to source path
                        }

                        System.Threading.Thread.Sleep(CopyRetryDelayMs);
                    }
                }

                return sourcePath; // Fall back to source path
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"[RPCS3] CopyTrophyIconToPluginData - Error copying icon");
                return sourcePath; // Fall back to source path
            }
        }
    }
}
