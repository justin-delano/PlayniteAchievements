using Playnite.SDK;
using Playnite.SDK.Models;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.Xenia
{
    internal class XeniaScanner
    {
        private readonly ILogger _logger;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly string _pluginUserDataPath;
        private readonly string _accountFolderPath;

        List<string> KnownPublishers = new List<string>() { "5444", "464F", "4143", "4156", "4158", "4142", "4144", "4150", "4151", "4157", "414B", "4148", "4153", "4159", "4154", "424D", "4241", "4257", "4253", "4242", "4248", "4246", "4245", "4247", "4254", "4244", "4252", "4256", "4255", "4343", "434D", "4356", "4354", "4458", "4445", "4443", "4546", "4553", "4541", "454D", "4543", "454C", "4556", "464C", "4649", "4653", "4746", "4745", "4756", "4857", "4850", "4845", "4855", "4946", "494F", "494D", "4947", "494C", "4950", "4958", "4A41", "4A57", "4B59", "4B4F", "4B4E", "4B41", "4B54", "4C41", "4D4A", "4D45", "4D44", "4D53", "4D57", "4D4D", "4E4D", "4E4B", "4E4C", "4F47", "4F58", "5058", "504C", "5043", "5241", "5341", "5343", "5345", "5353", "534E", "5350", "5351", "5354", "5355", "5357", "5441", "5454", "544B", "544D", "5443", "5451", "5453", "5553", "5647", "5656", "5643", "5655", "5745", "5752", "584B", "584C", "5841", "5849", "5850", "5942", "5A44" };

        public XeniaScanner(
            ILogger logger,
            PlayniteAchievementsSettings settings,
            string pluginUserDataPath,
            string accountFolderPath)
        {
            _logger = logger;
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _accountFolderPath = accountFolderPath ?? throw new ArgumentNullException(nameof(accountFolderPath));
            _pluginUserDataPath = pluginUserDataPath ?? string.Empty;
        }

        public async Task<RebuildPayload> RefreshAsync(
            IReadOnlyList<Game> gamesToRefresh,
            Action<Game> onGameStarting,
            Func<Game, GameAchievementData, Task> onGameCompleted,
            CancellationToken cancel)
        {
            if (string.IsNullOrWhiteSpace(_settings.Persisted.XeniaAccountPath))
            {
                _logger?.Warn("[Xenia] Missing path to account - cannot scan achievements.");
                return new RebuildPayload { Summary = new RebuildSummary() };
            }

            if (gamesToRefresh is null || gamesToRefresh.Count == 0)
            {
                return new RebuildPayload { Summary = new RebuildSummary() };
            }

            var result = await RefreshPipeline.RunProviderGamesAsync(
                gamesToRefresh,
                onGameStarting,
                async (game, token) =>
                {
                    if (game == null)
                    {
                        return RefreshPipeline.ProviderGameResult.Skipped();
                    }
                    if(!game.IsInstalled)
                    {
                        _logger.Warn("[Xenia] Game isn't installed unable to resolve titleID!");
                        return RefreshPipeline.ProviderGameResult.Skipped();
                    }

                    if (!ResolveTitleID(game, out var titleID))
                    {
                        return RefreshPipeline.ProviderGameResult.Skipped();
                    }

                    GPDResolver resolver = new GPDResolver(_pluginUserDataPath);
                    var achievements = resolver.LoadGPD(_accountFolderPath, titleID);
                    int.TryParse(titleID, out var parsedId);

                    var data = new GameAchievementData
                    {
                        AppId = parsedId,
                        GameName = game?.Name,
                        ProviderKey = "Xenia",
                        LibrarySourceName = game?.Source?.Name,
                        LastUpdatedUtc = DateTime.UtcNow,
                        HasAchievements = achievements.Count > 0,
                        PlayniteGameId = game?.Id,
                        Achievements = achievements
                    };

                    return new RefreshPipeline.ProviderGameResult
                    {
                        Data = data
                    };
                },
                onGameCompleted,
                isAuthRequiredException: _ => false,
                onGameError: (game, ex, consecutiveErrors) =>
                {
                    _logger?.Warn(ex, $"[Xenia] Failed to scan game '{game?.Name}'");
                },
                delayBetweenGamesAsync: null,
                delayAfterErrorAsync: null,
                cancel).ConfigureAwait(false);

            return result;
        }

        bool ResolveTitleID(Game game, out string titleID)
        {
            int exeAreaSize = 300;

            foreach (var rom in game.Roms)
            {
                var path = rom.Path;

                if (path.EndsWith(".iso") || path.EndsWith(".xex"))
                {
                    using var mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open);
                    using var accessor = mmf.CreateViewStream(0, new FileInfo(path).Length, MemoryMappedFileAccess.Read);

                    var chunksize = 8 * 1024; // 8 KB buffer
                    var buffer = new byte[chunksize];
                    ReadOnlySpan<byte> spanbuffer;
                    ReadOnlySpan<byte> previousspanbuffer = new ReadOnlySpan<byte>();
                    var position = 0;

                    var bytesRead = 0;
                    byte[] combinedbuffer = new byte[chunksize * 2];
                    byte[] exeChunk = new byte[exeAreaSize];

                    while (accessor.Position < accessor.Length)
                    {
                        bytesRead = accessor.Read(buffer, 0, buffer.Length);
                        if (bytesRead == 0) break;

                        spanbuffer = new ReadOnlySpan<byte>(buffer);
                        var foundexe = spanbuffer.IndexOf(Encoding.UTF8.GetBytes(".exe").AsSpan());
                        var foundpe = spanbuffer.IndexOf(Encoding.UTF8.GetBytes(".pe").AsSpan());

                        if (foundexe != -1)
                        {
                            previousspanbuffer.CopyTo(combinedbuffer);
                            spanbuffer.CopyTo(combinedbuffer.AsSpan(chunksize));

                            // Pull the previous 300 characters and convert to char array (300 is arbitry just to account for possible lots of data between titleID and .exe entry)
                            Array.Copy(combinedbuffer, (foundexe + chunksize) - exeAreaSize, exeChunk, 0, exeAreaSize);

                            var temptitleID = CheckChunk(ref exeChunk);
                            if (!string.IsNullOrEmpty(temptitleID))
                            {
                                titleID = temptitleID;
                                return true;

                            }
                        }
                        if (foundpe != -1)
                        {
                            previousspanbuffer.CopyTo(combinedbuffer);
                            spanbuffer.CopyTo(combinedbuffer.AsSpan(chunksize));

                            Array.Copy(combinedbuffer, (foundpe + chunksize) - exeAreaSize, exeChunk, 0, exeAreaSize);

                            var temptitleID = CheckChunk(ref exeChunk);
                            if (!string.IsNullOrEmpty(temptitleID))
                            {
                                titleID = temptitleID;
                                return true;
                            }
                        }

                        position += bytesRead;
                        previousspanbuffer = spanbuffer;
                    }
                }
                else
                {
                    _logger.Error("[Xenia] Unsupported ROM only .xex or .iso are supported!");
                }
            }

            titleID = "";
            return false;
        }

        private string CheckChunk(ref byte[] chunk)
        {
            byte[] publisherCheck = new byte[4];
            // Im not sure if this is 100% accurate but out of 28/28 ROMs tested passed taking on average 100ms to find! (.iso)
            // This will take longer with larger files and if the title ID is at the end of the file (Longest i've seen is 11s, maybe it could be multi-threaded?)
            for (int i = 0; i < chunk.Length; i++)
            {
                if (i + 8 > chunk.Length - 1)
                {
                    break;
                }

                // Check for publisher code
                publisherCheck[0] = chunk[i];
                publisherCheck[1] = chunk[i + 1];
                publisherCheck[2] = chunk[i + 2];
                publisherCheck[3] = chunk[i + 3];
                bool passedcheck = KnownPublishers.Any(x => x == System.Text.Encoding.UTF8.GetString(publisherCheck, 0, 4));
                if (!passedcheck)
                    continue;

                passedcheck &= char.IsDigit((char)chunk[i + 4]) || char.IsUpper((char)chunk[i + 4]);
                passedcheck &= char.IsDigit((char)chunk[i + 5]) || char.IsUpper((char)chunk[i + 5]);
                passedcheck &= char.IsDigit((char)chunk[i + 6]) || char.IsUpper((char)chunk[i + 6]);
                passedcheck &= char.IsDigit((char)chunk[i + 7]) || char.IsUpper((char)chunk[i + 7]);

                if (!passedcheck)
                {
                    continue;
                }
                else
                {
                    return System.Text.Encoding.UTF8.GetString(chunk, i, 8);
                }
            }

            return "";
        }
    }
}
