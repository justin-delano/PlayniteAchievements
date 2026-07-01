using HtmlAgilityPack;
using Playnite.SDK;
using Playnite.SDK.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Friends;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.Exophase
{
    internal sealed class ExophaseFriendsProvider : IFriendsProvider
    {
        private const string Provider = "Exophase";
        private readonly ExophaseApiClient _apiClient;
        private readonly ExophaseSettings _settings;
        private readonly IPlayniteAPI _playniteApi;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _webViewGate = new SemaphoreSlim(1, 1);
        private readonly Dictionary<string, IReadOnlyList<ExophaseFriendAchievementRow>> _awardRowsByFriendPlatform =
            new Dictionary<string, IReadOnlyList<ExophaseFriendAchievementRow>>(StringComparer.OrdinalIgnoreCase);

        public ExophaseFriendsProvider(
            ExophaseApiClient apiClient,
            ExophaseSettings settings,
            IPlayniteAPI playniteApi,
            ILogger logger)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _playniteApi = playniteApi;
            _logger = logger;
        }

        public string ProviderKey => Provider;

        public Task<FriendsProviderResult<FriendsRefreshPreparation>> BeginRefreshAsync(CancellationToken cancel)
        {
            _awardRowsByFriendPlatform.Clear();
            return Task.FromResult(FriendsProviderResult<FriendsRefreshPreparation>.FromData(new FriendsRefreshPreparation
            {
                CanRefreshAchievements = true
            }));
        }

        public void EndRefresh()
        {
            _awardRowsByFriendPlatform.Clear();
        }

        public Task<FriendsProviderResult<IReadOnlyList<FriendIdentity>>> GetFriendsAsync(CancellationToken cancel)
        {
            var now = DateTime.UtcNow;
            var friends = (_settings.Friends ?? new List<ExophaseFriendSettings>())
                .Where(friend => !string.IsNullOrWhiteSpace(friend?.Username))
                .Select(friend => new FriendIdentity
                {
                    ProviderKey = Provider,
                    ExternalUserId = friend.Username.Trim(),
                    DisplayName = string.IsNullOrWhiteSpace(friend.DisplayName) ? friend.Username.Trim() : friend.DisplayName.Trim(),
                    AvatarUrl = friend.AvatarUrl,
                    AvatarPath = friend.AvatarPath,
                    LastRefreshedUtc = now
                })
                .ToList();

            return Task.FromResult(FriendsProviderResult<IReadOnlyList<FriendIdentity>>.FromData(friends));
        }

        public async Task<FriendsProviderResult<IReadOnlyList<FriendGameOwnership>>> GetOwnedGamesAsync(
            FriendIdentity friend,
            CancellationToken cancel)
        {
            var config = _settings.GetFriend(friend?.ExternalUserId);
            if (config == null)
            {
                return FriendsProviderResult<IReadOnlyList<FriendGameOwnership>>.FromData(Array.Empty<FriendGameOwnership>());
            }

            var platforms = NormalizePlatforms(config.SelectedPlatforms);
            if (platforms.Count == 0)
            {
                return FriendsProviderResult<IReadOnlyList<FriendGameOwnership>>.FromData(Array.Empty<FriendGameOwnership>());
            }

            var result = new List<FriendGameOwnership>();
            foreach (var platform in platforms)
            {
                cancel.ThrowIfCancellationRequested();
                var games = await FetchOwnedGamesForPlatformAsync(config.Username, platform, cancel).ConfigureAwait(false);
                foreach (var game in games)
                {
                    var key = ExophaseFriendGameKey.Build(platform, game.Slug);
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    result.Add(new FriendGameOwnership
                    {
                        ProviderKey = Provider,
                        ExternalUserId = config.Username,
                        ProviderGameKey = key,
                        ProviderPlatformKey = ExophaseDataProvider.MapSlugToProviderPlatformKey(platform),
                        PlayniteGameId = ResolveMappedPlayniteGameId(key, platform, game.Title),
                        GameName = game.Title,
                        IconUrl = game.ImageUrl,
                        CoverUrl = game.ImageUrl,
                        PlaytimeForeverMinutes = Math.Max(0, game.PlaytimeMinutes)
                    });
                }
            }

            var deduped = result
                .GroupBy(item => item.ProviderGameKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(item => item.GameName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return FriendsProviderResult<IReadOnlyList<FriendGameOwnership>>.FromData(deduped);
        }

        public async Task<FriendsProviderResult<FriendGameDefinition>> GetFriendGameDefinitionAsync(
            string providerGameKey,
            int appId,
            string gameName,
            CancellationToken cancel)
        {
            var parsed = ExophaseFriendGameKey.Parse(providerGameKey);
            if (string.IsNullOrWhiteSpace(parsed.GameSlug))
            {
                return FriendsProviderResult<FriendGameDefinition>.Failed("Exophase friend game key is missing a slug.");
            }

            var url = ExophaseApiClient.BuildUrlFromSlug(parsed.GameSlug);
            var achievements = await _apiClient
                .FetchAchievementsAsync(url, ExophaseApiClient.MapLanguageToAcceptLanguage("en-US"), cancel)
                .ConfigureAwait(false);

            var rows = achievements ?? new List<AchievementDetail>();
            return FriendsProviderResult<FriendGameDefinition>.FromData(new FriendGameDefinition
            {
                ProviderKey = Provider,
                ProviderGameKey = providerGameKey,
                ProviderPlatformKey = ExophaseDataProvider.MapSlugToProviderPlatformKey(parsed.PlatformSlug),
                GameName = gameName,
                Status = rows.Count > 0 ? FriendGameDefinitionStatus.Ok : FriendGameDefinitionStatus.NoAchievements,
                LastCheckedUtc = DateTime.UtcNow,
                Achievements = rows
            });
        }

        public async Task<FriendsProviderResult<FriendGameAchievements>> GetFriendGameAchievementsAsync(
            FriendIdentity friend,
            string providerGameKey,
            int appId,
            string gameName,
            CancellationToken cancel)
        {
            var parsed = ExophaseFriendGameKey.Parse(providerGameKey);
            if (string.IsNullOrWhiteSpace(friend?.ExternalUserId) ||
                string.IsNullOrWhiteSpace(parsed.PlatformSlug) ||
                string.IsNullOrWhiteSpace(parsed.GameSlug))
            {
                return FriendsProviderResult<FriendGameAchievements>.Failed("Exophase friend id or game key is missing.");
            }

            var allRows = await FetchAwardRowsForPlatformAsync(friend.ExternalUserId, parsed.PlatformSlug, cancel)
                .ConfigureAwait(false);
            var rows = allRows
                .Where(row => string.Equals(row.GameSlug, parsed.GameSlug, StringComparison.OrdinalIgnoreCase))
                .Select(ToFriendAchievementRow)
                .ToList();

            return FriendsProviderResult<FriendGameAchievements>.FromData(new FriendGameAchievements
            {
                Friend = friend,
                ProviderGameKey = providerGameKey,
                LastUpdatedUtc = DateTime.UtcNow,
                StatsUnavailable = false,
                Rows = rows
            });
        }

        private async Task<IReadOnlyList<ExophaseFriendGame>> FetchOwnedGamesForPlatformAsync(
            string username,
            string platform,
            CancellationToken cancel)
        {
            var result = new List<ExophaseFriendGame>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();
            queue.Enqueue(BuildGamesUrl(username, platform, 1));
            queue.Enqueue(BuildProfileUrl(username, platform));

            while (queue.Count > 0 && visited.Count < 25)
            {
                cancel.ThrowIfCancellationRequested();
                var url = queue.Dequeue();
                if (!visited.Add(url))
                {
                    continue;
                }

                var html = await FetchRenderedHtmlSerializedAsync(url, cancel).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(html))
                {
                    continue;
                }

                var page = ExophaseFriendPageParser.ParseGames(html, platform);
                result.AddRange(page.Games);
                foreach (var next in page.NextUrls.Where(next => !visited.Contains(next)))
                {
                    queue.Enqueue(next);
                }
            }

            return result
                .Where(game => !string.IsNullOrWhiteSpace(game?.Slug))
                .GroupBy(game => game.Slug, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        private async Task<IReadOnlyList<ExophaseFriendAchievementRow>> FetchAwardRowsForPlatformAsync(
            string username,
            string platform,
            CancellationToken cancel)
        {
            var cacheKey = username.Trim() + "|" + platform.Trim();
            if (_awardRowsByFriendPlatform.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            var result = new List<ExophaseFriendAchievementRow>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();
            queue.Enqueue(BuildAwardsUrl(username, platform, 1));

            while (queue.Count > 0 && visited.Count < 25)
            {
                cancel.ThrowIfCancellationRequested();
                var url = queue.Dequeue();
                if (!visited.Add(url))
                {
                    continue;
                }

                var html = await FetchRenderedHtmlSerializedAsync(url, cancel).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(html))
                {
                    continue;
                }

                var page = ExophaseFriendPageParser.ParseAwards(html);
                result.AddRange(page.Rows);
                foreach (var next in page.NextUrls.Where(next => !visited.Contains(next)))
                {
                    queue.Enqueue(next);
                }
            }

            var deduped = result
                .GroupBy(row => row.GameSlug + "\u001f" + NormalizeText(row.DisplayName), StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(row => row.UnlockTimeUtc ?? DateTime.MinValue)
                    .First())
                .ToList();
            _awardRowsByFriendPlatform[cacheKey] = deduped;
            return deduped;
        }

        private static FriendAchievementRow ToFriendAchievementRow(ExophaseFriendAchievementRow row)
        {
            return new FriendAchievementRow
            {
                DisplayName = row?.DisplayName,
                Description = row?.Description,
                IconUrl = row?.IconUrl,
                Unlocked = row?.Unlocked ?? false,
                UnlockTimeUtc = row?.UnlockTimeUtc,
                ProgressNum = row?.ProgressNum,
                ProgressDenom = row?.ProgressDenom
            };
        }

        private async Task<string> FetchRenderedHtmlSerializedAsync(string url, CancellationToken cancel)
        {
            await _webViewGate.WaitAsync(cancel).ConfigureAwait(false);
            try
            {
                return await _apiClient.FetchRenderedHtmlAsync(url, cancel).ConfigureAwait(false);
            }
            finally
            {
                _webViewGate.Release();
            }
        }

        private Guid? ResolveMappedPlayniteGameId(string providerGameKey, string platform, string title)
        {
            var normalizedKey = ExophaseSettings.NormalizeFriendGameMappingKey(providerGameKey);
            if (!string.IsNullOrWhiteSpace(normalizedKey) &&
                _settings.FriendGameMappings?.TryGetValue(normalizedKey, out var manualGameId) == true &&
                manualGameId != Guid.Empty)
            {
                return manualGameId;
            }

            var slug = ExophaseFriendGameKey.Parse(providerGameKey).GameSlug;
            var overrideMatch = (_settings.SlugOverrides ?? new Dictionary<Guid, string>())
                .FirstOrDefault(pair => string.Equals(pair.Value, slug, StringComparison.OrdinalIgnoreCase));
            if (overrideMatch.Key != Guid.Empty)
            {
                return overrideMatch.Key;
            }

            return ResolveAutomaticPlayniteGameId(platform, title);
        }

        private Guid? ResolveAutomaticPlayniteGameId(string platform, string title)
        {
            if (string.IsNullOrWhiteSpace(title) || _playniteApi?.Database?.Games == null)
            {
                return null;
            }

            var normalizedTitle = NormalizeTitle(title);
            if (string.IsNullOrWhiteSpace(normalizedTitle))
            {
                return null;
            }

            var candidates = _playniteApi.Database.Games
                .Where(game => game != null && IsPlatformCompatible(game, platform))
                .Select(game => new
                {
                    Game = game,
                    Score = TitleSimilarity(normalizedTitle, NormalizeTitle(game.Name))
                })
                .Where(item => item.Score >= 0.92)
                .OrderByDescending(item => item.Score)
                .Take(3)
                .ToList();

            if (candidates.Count == 1 ||
                (candidates.Count > 1 && candidates[0].Score >= 0.98 && candidates[0].Score - candidates[1].Score >= 0.05))
            {
                return candidates[0].Game.Id;
            }

            return null;
        }

        private static bool IsPlatformCompatible(Game game, string platform)
        {
            if (game == null || string.IsNullOrWhiteSpace(platform))
            {
                return true;
            }

            var token = platform.Trim().ToLowerInvariant();
            var source = game.Source?.Name ?? string.Empty;
            var platforms = string.Join(" ", game.Platforms?.Select(item => item?.Name) ?? Enumerable.Empty<string>());
            var haystack = (source + " " + platforms).ToLowerInvariant();

            if (token == "steam") return haystack.Contains("steam");
            if (token == "gog") return haystack.Contains("gog");
            if (token == "epic") return haystack.Contains("epic");
            if (token == "psn" || token == "ps3" || token == "ps4" || token == "ps5" || token == "vita")
            {
                return haystack.Contains("playstation") || haystack.Contains("psn") || haystack.Contains(token);
            }

            if (token.StartsWith("xbox", StringComparison.Ordinal))
            {
                return haystack.Contains("xbox");
            }

            if (token == "android" || token == "apple")
            {
                return haystack.Contains(token) || haystack.Contains("ios") || haystack.Contains("mobile");
            }

            if (token == "ubisoft" || token == "uplay")
            {
                return haystack.Contains("ubisoft") || haystack.Contains("uplay");
            }

            return true;
        }

        private static double TitleSimilarity(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return 0;
            }

            if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            var distance = LevenshteinDistance(left, right);
            var max = Math.Max(left.Length, right.Length);
            return max <= 0 ? 0 : 1d - ((double)distance / max);
        }

        private static int LevenshteinDistance(string left, string right)
        {
            var costs = new int[right.Length + 1];
            for (var j = 0; j < costs.Length; j++)
            {
                costs[j] = j;
            }

            for (var i = 1; i <= left.Length; i++)
            {
                costs[0] = i;
                var previous = i - 1;
                for (var j = 1; j <= right.Length; j++)
                {
                    var current = costs[j];
                    costs[j] = Math.Min(
                        Math.Min(costs[j] + 1, costs[j - 1] + 1),
                        previous + (left[i - 1] == right[j - 1] ? 0 : 1));
                    previous = current;
                }
            }

            return costs[right.Length];
        }

        private static string BuildProfileUrl(string username, string platform)
        {
            var url = $"https://www.exophase.com/user/{Uri.EscapeDataString(username.Trim())}/";
            return string.IsNullOrWhiteSpace(platform)
                ? url
                : url + "?environment=" + Uri.EscapeDataString(platform.Trim());
        }

        private static string BuildGamesUrl(string username, string platform, int page)
        {
            var url = $"https://www.exophase.com/user/{Uri.EscapeDataString(username.Trim())}/games/";
            var query = "?environment=" + Uri.EscapeDataString(platform.Trim());
            if (page > 1)
            {
                query += "&page=" + page.ToString(CultureInfo.InvariantCulture);
            }

            return url + query;
        }

        private static string BuildAwardsUrl(string username, string platform, int page)
        {
            var url = $"https://www.exophase.com/user/{Uri.EscapeDataString(username.Trim())}/awards/";
            var query = "?environment=" + Uri.EscapeDataString(platform.Trim());
            if (page > 1)
            {
                query += "&page=" + page.ToString(CultureInfo.InvariantCulture);
            }

            return url + query;
        }

        private static List<string> NormalizePlatforms(IEnumerable<string> platforms)
        {
            return (platforms ?? Enumerable.Empty<string>())
                .Where(platform => !string.IsNullOrWhiteSpace(platform))
                .Select(platform => platform.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string NormalizeTitle(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var lower = WebUtility.HtmlDecode(value).ToLowerInvariant();
            lower = Regex.Replace(lower, @"[^\p{L}\p{Nd}]+", " ");
            lower = Regex.Replace(lower, @"\b(the|a|an|edition|remastered|remaster|complete|definitive)\b", " ");
            return Regex.Replace(lower, @"\s+", " ").Trim();
        }

        private static string NormalizeText(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : Regex.Replace(WebUtility.HtmlDecode(value), @"\s+", " ").Trim();
        }

        private sealed class ExophaseFriendGame
        {
            public string Slug { get; set; }
            public string Title { get; set; }
            public string ImageUrl { get; set; }
            public int PlaytimeMinutes { get; set; }
        }

        private sealed class ExophaseFriendAchievementRow
        {
            public string GameSlug { get; set; }
            public string DisplayName { get; set; }
            public string Description { get; set; }
            public string IconUrl { get; set; }
            public bool Unlocked { get; set; }
            public DateTime? UnlockTimeUtc { get; set; }
            public int? ProgressNum { get; set; }
            public int? ProgressDenom { get; set; }
        }

        private sealed class ParsedGamesPage
        {
            public List<ExophaseFriendGame> Games { get; set; } = new List<ExophaseFriendGame>();
            public List<string> NextUrls { get; set; } = new List<string>();
        }

        private sealed class ParsedAwardsPage
        {
            public List<ExophaseFriendAchievementRow> Rows { get; set; } = new List<ExophaseFriendAchievementRow>();
            public List<string> NextUrls { get; set; } = new List<string>();
        }

        private static class ExophaseFriendGameKey
        {
            public static string Build(string platformSlug, string gameSlug)
            {
                if (string.IsNullOrWhiteSpace(platformSlug) || string.IsNullOrWhiteSpace(gameSlug))
                {
                    return null;
                }

                return platformSlug.Trim().ToLowerInvariant() + "|" + gameSlug.Trim().ToLowerInvariant();
            }

            public static (string PlatformSlug, string GameSlug) Parse(string key)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    return (null, null);
                }

                var parts = key.Split(new[] { '|' }, 2);
                return parts.Length == 2
                    ? (parts[0].Trim().ToLowerInvariant(), parts[1].Trim().ToLowerInvariant())
                    : (null, key.Trim().ToLowerInvariant());
            }
        }

        private static class ExophaseFriendPageParser
        {
            public static ParsedGamesPage ParseGames(string html, string platform)
            {
                var result = new ParsedGamesPage();
                var doc = LoadDocument(html);
                if (doc?.DocumentNode == null)
                {
                    return result;
                }

                foreach (var link in Nodes(doc.DocumentNode.SelectNodes("//a[contains(@href, '/game/')]")))
                {
                    var href = NormalizeUrl(link.GetAttributeValue("href", null));
                    var slug = ExophaseApiClient.ExtractSlugFromUrl(href);
                    if (string.IsNullOrWhiteSpace(slug))
                    {
                        continue;
                    }

                    var container = FindContainer(link);
                    var title = FirstNonEmpty(
                        Clean(link.GetAttributeValue("title", null)),
                        Clean(link.InnerText),
                        Clean(container?.SelectSingleNode(".//img")?.GetAttributeValue("alt", null)),
                        SlugToTitle(slug, platform));
                    var image = NormalizeUrl(FirstNonEmpty(
                        link.SelectSingleNode(".//img")?.GetAttributeValue("src", null),
                        link.SelectSingleNode(".//img")?.GetAttributeValue("data-src", null),
                        container?.SelectSingleNode(".//img")?.GetAttributeValue("src", null),
                        container?.SelectSingleNode(".//img")?.GetAttributeValue("data-src", null)));

                    result.Games.Add(new ExophaseFriendGame
                    {
                        Slug = slug,
                        Title = title,
                        ImageUrl = image,
                        PlaytimeMinutes = ParsePlaytimeMinutes(container?.InnerText)
                    });
                }

                result.NextUrls.AddRange(ParseNextUrls(doc));
                return result;
            }

            public static ParsedAwardsPage ParseAwards(string html)
            {
                var result = new ParsedAwardsPage();
                var doc = LoadDocument(html);
                if (doc?.DocumentNode == null)
                {
                    return result;
                }

                var containers = doc.DocumentNode
                    .SelectNodes("//*[self::tr or self::li or contains(@class, 'award') or contains(@class, 'achievement')]");
                foreach (var container in Nodes(containers))
                {
                    var gameLink = container.SelectSingleNode(".//a[contains(@href, '/game/')]");
                    var slug = ExophaseApiClient.ExtractSlugFromUrl(NormalizeUrl(gameLink?.GetAttributeValue("href", null)));
                    if (string.IsNullOrWhiteSpace(slug))
                    {
                        continue;
                    }

                    var displayName = ResolveAwardName(container, gameLink);
                    if (string.IsNullOrWhiteSpace(displayName))
                    {
                        continue;
                    }

                    result.Rows.Add(new ExophaseFriendAchievementRow
                    {
                        GameSlug = slug,
                        DisplayName = displayName,
                        Description = Clean(FirstNonEmpty(
                            container.SelectSingleNode(".//*[contains(@class, 'description')]")?.InnerText,
                            container.SelectSingleNode(".//*[contains(@class, 'desc')]")?.InnerText)),
                        IconUrl = NormalizeUrl(FirstNonEmpty(
                            container.SelectSingleNode(".//img")?.GetAttributeValue("src", null),
                            container.SelectSingleNode(".//img")?.GetAttributeValue("data-src", null))),
                        Unlocked = true,
                        UnlockTimeUtc = ParseUnlockTime(container)
                    });
                }

                result.NextUrls.AddRange(ParseNextUrls(doc));
                return result;
            }

            private static string ResolveAwardName(HtmlNode container, HtmlNode gameLink)
            {
                var explicitName = FirstNonEmpty(
                    container.GetAttributeValue("data-title", null),
                    container.SelectSingleNode(".//*[contains(@class, 'award-title')]")?.InnerText,
                    container.SelectSingleNode(".//*[contains(@class, 'achievement-title')]")?.InnerText,
                    container.SelectSingleNode(".//*[contains(@class, 'title')]")?.InnerText,
                    container.SelectSingleNode(".//h3")?.InnerText,
                    container.SelectSingleNode(".//h4")?.InnerText);
                if (!string.IsNullOrWhiteSpace(explicitName))
                {
                    return Clean(explicitName);
                }

                var anchors = Nodes(container.SelectNodes(".//a"));
                foreach (var anchor in anchors)
                {
                    if (ReferenceEquals(anchor, gameLink))
                    {
                        continue;
                    }

                    var text = Clean(anchor.InnerText);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }

                return null;
            }

            private static IEnumerable<string> ParseNextUrls(HtmlDocument doc)
            {
                foreach (var link in Nodes(doc.DocumentNode.SelectNodes("//a[@href]")))
                {
                    var rel = link.GetAttributeValue("rel", string.Empty);
                    var text = Clean(link.InnerText);
                    if (!string.Equals(rel, "next", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(text, "next", StringComparison.OrdinalIgnoreCase) &&
                        !Regex.IsMatch(link.GetAttributeValue("href", string.Empty), @"[?&]page=\d+", RegexOptions.IgnoreCase))
                    {
                        continue;
                    }

                    var url = NormalizeUrl(link.GetAttributeValue("href", null));
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        yield return url;
                    }
                }
            }

            private static HtmlDocument LoadDocument(string html)
            {
                if (string.IsNullOrWhiteSpace(html))
                {
                    return null;
                }

                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                return doc;
            }

            private static IEnumerable<HtmlNode> Nodes(HtmlNodeCollection nodes)
            {
                return nodes ?? Enumerable.Empty<HtmlNode>();
            }

            private static HtmlNode FindContainer(HtmlNode node)
            {
                var current = node;
                for (var i = 0; i < 4 && current != null; i++)
                {
                    current = current.ParentNode;
                    var className = current?.GetAttributeValue("class", string.Empty) ?? string.Empty;
                    if (current?.Name == "tr" ||
                        current?.Name == "li" ||
                        className.IndexOf("game", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        className.IndexOf("card", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return current;
                    }
                }

                return node.ParentNode;
            }

            private static string SlugToTitle(string slug, string platform)
            {
                var value = slug ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(platform) &&
                    value.EndsWith("-" + platform, StringComparison.OrdinalIgnoreCase))
                {
                    value = value.Substring(0, value.Length - platform.Length - 1);
                }

                return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(value.Replace('-', ' '));
            }

            private static int ParsePlaytimeMinutes(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return 0;
                }

                var match = Regex.Match(text, @"(?:(\d+(?:\.\d+)?)\s*h(?:ours?)?)?\s*(?:(\d+)\s*m(?:in(?:utes?)?)?)?", RegexOptions.IgnoreCase);
                if (!match.Success)
                {
                    return 0;
                }

                var total = 0;
                if (double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var hours))
                {
                    total += (int)Math.Round(hours * 60);
                }

                if (int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes))
                {
                    total += minutes;
                }

                return Math.Max(0, total);
            }

            private static DateTime? ParseUnlockTime(HtmlNode container)
            {
                var datetime = FirstNonEmpty(
                    container.SelectSingleNode(".//time")?.GetAttributeValue("datetime", null),
                    container.GetAttributeValue("data-date", null),
                    container.GetAttributeValue("data-unlocked", null));
                if (!string.IsNullOrWhiteSpace(datetime) &&
                    DateTime.TryParse(datetime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                {
                    return parsed;
                }

                return null;
            }

            private static string NormalizeUrl(string url)
            {
                if (string.IsNullOrWhiteSpace(url))
                {
                    return null;
                }

                url = WebUtility.HtmlDecode(url.Trim());
                if (url.StartsWith("//", StringComparison.Ordinal))
                {
                    return "https:" + url;
                }

                if (url.StartsWith("/", StringComparison.Ordinal))
                {
                    return "https://www.exophase.com" + url;
                }

                return url;
            }

            private static string Clean(string value)
            {
                return string.IsNullOrWhiteSpace(value)
                    ? null
                    : Regex.Replace(WebUtility.HtmlDecode(value), @"\s+", " ").Trim();
            }

            private static string FirstNonEmpty(params string[] values)
            {
                return values?.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            }
        }
    }
}
