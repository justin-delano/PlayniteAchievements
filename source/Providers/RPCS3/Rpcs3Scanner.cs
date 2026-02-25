using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Providers.RetroAchievements.Hashing;
using PlayniteAchievements.Providers.RPCS3.Models;
using PlayniteAchievements.Services;
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

        public Rpcs3Scanner(ILogger logger, PlayniteAchievementsSettings settings, Rpcs3DataProvider provider = null, IPlayniteAPI playniteApi = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _provider = provider;
            _playniteApi = playniteApi;
        }

        public async Task<RebuildPayload> RefreshAsync(
            List<Game> gamesToRefresh,
            Action<ProviderRefreshUpdate> progressCallback,
            Func<GameAchievementData, Task> OnGameRefreshed,
            CancellationToken cancel)
        {
            _logger?.Debug("[RPCS3] RefreshAsync - Starting refresh");
            var summary = new RebuildSummary();

            if (gamesToRefresh == null || gamesToRefresh.Count == 0)
            {
                _logger?.Debug("[RPCS3] RefreshAsync - No games to refresh");
                return new RebuildPayload { Summary = summary };
            }

            _logger?.Debug($"[RPCS3] RefreshAsync - Processing {gamesToRefresh.Count} games");

            var progress = new RebuildProgressReporter(progressCallback, gamesToRefresh.Count);

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

                progress.Emit(new ProviderRefreshUpdate { CurrentGameName = game?.Name });

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

                progress.Step();
            }

            _logger?.Debug($"[RPCS3] RefreshAsync - Complete. GamesRefreshed={summary.GamesRefreshed}, GamesWithAchievements={summary.GamesWithAchievements}, GamesWithoutAchievements={summary.GamesWithoutAchievements}");
            progress.Emit(new ProviderRefreshUpdate { CurrentGameName = null }, force: true);
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
                        GlobalPercentUnlocked = null,
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
        /// 2. Extract npcommid from PS3 ISO file
        /// 3. Match by game name against TROPCONF.SFM titles
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

            // Strategy 2: Extract npcommid from PS3 ISO file
            _logger?.Debug($"[RPCS3] FindNpCommIdForGame - Trying ISO extraction fallback");
            var npcommidFromIso = FindNpCommIdFromIso(game, gameDirectory, trophyFolderCache);
            if (!string.IsNullOrWhiteSpace(npcommidFromIso))
            {
                return npcommidFromIso;
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
        /// Extracts the npcommid from a PS3 ISO file by reading the embedded TROPHY.TRP.
        /// </summary>
        private string FindNpCommIdFromIso(Game game, string gameDirectory,
            Dictionary<string, string> trophyFolderCache)
        {
            if (string.IsNullOrWhiteSpace(gameDirectory))
            {
                _logger?.Debug("[RPCS3] FindNpCommIdFromIso - Game directory is empty");
                return null;
            }

            try
            {
                // Find ISO files in the game directory
                var isoFiles = FindIsoFiles(gameDirectory);
                if (isoFiles.Count == 0)
                {
                    _logger?.Debug($"[RPCS3] FindNpCommIdFromIso - No ISO files found in '{gameDirectory}'");
                    return null;
                }

                _logger?.Debug($"[RPCS3] FindNpCommIdFromIso - Found {isoFiles.Count} ISO file(s)");

                foreach (var isoPath in isoFiles)
                {
                    _logger?.Debug($"[RPCS3] FindNpCommIdFromIso - Checking ISO: '{isoPath}'");

                    string npcommid = null;

                    // First try DiscUtils (works for ISO9660)
                    try
                    {
                        using (var disc = new DiscUtilsFacade(isoPath))
                        {
                            // Try PS3_GAME/TROPHY/TROPHY.TRP (standard PS3 structure)
                            npcommid = ExtractNpCommIdFromDisc(disc, "PS3_GAME/TROPHY/TROPHY.TRP");

                            // If not found, try just TROPHY/TROPHY.TRP (some structures)
                            if (string.IsNullOrWhiteSpace(npcommid))
                            {
                                npcommid = ExtractNpCommIdFromDisc(disc, "TROPHY/TROPHY.TRP");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.Debug($"[RPCS3] FindNpCommIdFromIso - DiscUtils failed (likely UDF format): {ex.Message}");
                    }

                    // If DiscUtils failed, try raw byte scanning (works for UDF ISOs)
                    if (string.IsNullOrWhiteSpace(npcommid))
                    {
                        _logger?.Debug($"[RPCS3] FindNpCommIdFromIso - Trying raw byte scan for UDF ISO");
                        npcommid = ExtractNpCommIdFromRawScan(isoPath);
                    }

                    if (!string.IsNullOrWhiteSpace(npcommid))
                    {
                        _logger?.Debug($"[RPCS3] FindNpCommIdFromIso - Extracted npcommid: '{npcommid}'");

                        if (trophyFolderCache.ContainsKey(npcommid))
                        {
                            _logger?.Debug($"[RPCS3] FindNpCommIdFromIso - Found matching npcommid '{npcommid}' in cache");
                            return npcommid;
                        }
                        else
                        {
                            _logger?.Debug($"[RPCS3] FindNpCommIdFromIso - npcommid '{npcommid}' not in cache");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"[RPCS3] FindNpCommIdFromIso - Error searching for ISO files");
            }

            return null;
        }

        /// <summary>
        /// Scans a raw ISO file for the npcommid pattern.
        /// This works for UDF-format PS3 ISOs without needing UDF parsing libraries.
        /// The npcommid appears in TROPHY.TRP as XML: &lt;npcommid>NPWR05636_00&lt;/npcommid>
        /// </summary>
        private string ExtractNpCommIdFromRawScan(string isoPath)
        {
            try
            {
                // Read the ISO file in chunks, looking for <npcommid>...</npcommid>
                // The TROPHY.TRP file is typically within the first 100MB of the ISO
                var searchPattern = System.Text.Encoding.ASCII.GetBytes("<npcommid>");
                var endPattern = System.Text.Encoding.ASCII.GetBytes("</npcommid>");
                var maxSearchBytes = 100L * 1024 * 1024; // 100MB max search

                using (var fs = new FileStream(isoPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var fileSize = fs.Length;
                    var searchLimit = Math.Min(fileSize, maxSearchBytes);
                    var buffer = new byte[64 * 1024]; // 64KB buffer
                    var overlap = new byte[256]; // For patterns spanning buffer boundaries
                    var overlapCount = 0;

                    long position = 0;
                    while (position < searchLimit)
                    {
                        var bytesToRead = (int)Math.Min(buffer.Length, searchLimit - position);
                        var bytesRead = fs.Read(buffer, 0, bytesToRead);
                        if (bytesRead == 0) break;

                        // Combine overlap from previous chunk with current buffer
                        var searchBuffer = new byte[overlapCount + bytesRead];
                        if (overlapCount > 0)
                        {
                            Array.Copy(overlap, 0, searchBuffer, 0, overlapCount);
                        }
                        Array.Copy(buffer, 0, searchBuffer, overlapCount, bytesRead);

                        // Search for <npcommid> pattern using byte-by-byte comparison
                        for (int i = 0; i <= searchBuffer.Length - searchPattern.Length - 20; i++)
                        {
                            // Check for start pattern
                            bool match = true;
                            for (int p = 0; p < searchPattern.Length; p++)
                            {
                                if (searchBuffer[i + p] != searchPattern[p])
                                {
                                    match = false;
                                    break;
                                }
                            }

                            if (match)
                            {
                                // Find end pattern
                                for (int j = searchPattern.Length; j < searchBuffer.Length - i - endPattern.Length; j++)
                                {
                                    bool endMatch = true;
                                    for (int e = 0; e < endPattern.Length; e++)
                                    {
                                        if (searchBuffer[i + j + e] != endPattern[e])
                                        {
                                            endMatch = false;
                                            break;
                                        }
                                    }

                                    if (endMatch)
                                    {
                                        // Extract the npcommid value
                                        var startIndex = i + searchPattern.Length;
                                        var length = j - searchPattern.Length;
                                        var npcommidBytes = new byte[length];
                                        Array.Copy(searchBuffer, startIndex, npcommidBytes, 0, length);
                                        var npcommid = System.Text.Encoding.ASCII.GetString(npcommidBytes).Trim();

                                        _logger?.Debug($"[RPCS3] ExtractNpCommIdFromRawScan - Found npcommid: '{npcommid}'");
                                        return npcommid;
                                    }
                                }
                            }
                        }

                        // Save last bytes for overlap handling
                        overlapCount = Math.Min(256, bytesRead);
                        Array.Copy(buffer, bytesRead - overlapCount, overlap, 0, overlapCount);

                        position += bytesRead;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"[RPCS3] ExtractNpCommIdFromRawScan - Error scanning ISO '{isoPath}'");
            }

            return null;
        }

        /// <summary>
        /// Extracts the npcommid from a TROPHY.TRP file inside a disc image.
        /// </summary>
        private string ExtractNpCommIdFromDisc(DiscUtilsFacade disc, string trpPath)
        {
            if (!disc.FileExists(trpPath))
            {
                _logger?.Debug($"[RPCS3] ExtractNpCommIdFromDisc - File not found: '{trpPath}'");
                return null;
            }

            using (var stream = disc.OpenFileOrNull(trpPath))
            {
                if (stream == null)
                {
                    _logger?.Debug($"[RPCS3] ExtractNpCommIdFromDisc - Could not open stream: '{trpPath}'");
                    return null;
                }

                using (var reader = new StreamReader(stream))
                {
                    // TROPHY.TRP contains XML with <npcommid> tag
                    // Read enough to find the npcommid (usually near the top)
                    for (int i = 0; i < 20; i++)
                    {
                        var line = reader.ReadLine();
                        if (line == null) break;

                        var match = NpCommIdPattern.Match(line);
                        if (match.Success)
                        {
                            return match.Groups[1].Value?.Trim();
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Finds ISO files in the specified directory.
        /// </summary>
        private List<string> FindIsoFiles(string directory)
        {
            var results = new List<string>();

            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return results;
            }

            try
            {
                // Check if the directory itself is pointing to an ISO
                var files = Directory.GetFiles(directory, "*.iso");
                results.AddRange(files);
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"[RPCS3] FindIsoFiles - Error searching in '{directory}'");
            }

            return results;
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

            // Separate digits from letters (e.g., "PlayStation3" -> "PlayStation 3")
            // This handles cases like "PlayStation3 Edition" vs "PlayStation 3 Edition"
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"(\d)([a-zA-Z])", "$1 $2");
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"([a-zA-Z])(\d)", "$1 $2");

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
        /// Gets the trophy icon path from the RPCS3 trophy folder.
        /// Returns the direct source path; icon caching is handled centrally by DiskImageService.
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

                if (!File.Exists(sourcePath))
                {
                    _logger?.Debug($"[RPCS3] GetTrophyIconPath - Icon not found at '{sourcePath}'");
                    return null;
                }

                return sourcePath;
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"[RPCS3] GetTrophyIconPath - Failed to get icon path for trophy {trophyId}");
                return null;
            }
        }
    }
}
