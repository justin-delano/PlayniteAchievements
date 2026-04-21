using HtmlAgilityPack;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using PlayniteAchievements.Providers.Steam;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace PlayniteAchievements.Providers.ImportedGameMetadata
{
    internal sealed class ImportedGameMetadataApplier
    {
        private sealed class ImportedProviderPageMetadata
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public string IconUrl { get; set; }
            public string Url { get; set; }
            public string UrlName { get; set; }
        }

        private readonly IPlayniteAPI _api;
        private readonly ILogger _logger;
        private readonly string _logPrefix;

        public ImportedGameMetadataApplier(IPlayniteAPI api, ILogger logger, string logPrefix)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _logger = logger;
            _logPrefix = string.IsNullOrWhiteSpace(logPrefix) ? "ImportMetadata" : logPrefix.Trim();
        }

        public bool ApplyToGame(
            Game importedGame,
            int appId,
            string metadataSourceId,
            MetadataPlugin selectedMetadataPlugin,
            LibraryMetadataProvider fastMetadataDownloader)
        {
            if (importedGame == null || appId <= 0)
            {
                return false;
            }

            var normalizedSourceId = ImportedGameMetadataSourceCatalog.NormalizeMetadataSourceId(_api, _logger, metadataSourceId);

            if (ImportedGameMetadataSourceCatalog.IsFastMethodSource(normalizedSourceId))
            {
                return ApplyFastMetadata(importedGame, appId, fastMetadataDownloader);
            }

            if (ImportedGameMetadataSourceCatalog.IsUniversalSteamMetadataSource(normalizedSourceId) && selectedMetadataPlugin != null)
            {
                return ApplyMetadataPlugin(importedGame, appId, selectedMetadataPlugin);
            }

            if (ImportedGameMetadataSourceCatalog.IsBuiltInSource(normalizedSourceId))
            {
                ApplyBuiltInMetadata(importedGame, appId, normalizedSourceId);
                return true;
            }

            if (selectedMetadataPlugin != null)
            {
                return ApplyMetadataPlugin(importedGame, appId, selectedMetadataPlugin);
            }

            if (fastMetadataDownloader != null)
            {
                return ApplyFastMetadata(importedGame, appId, fastMetadataDownloader);
            }

            _logger?.Info($"[{_logPrefix}] No metadata source was available for appId={appId}; skipping metadata download.");
            return false;
        }

        private void ApplyBuiltInMetadata(Game importedGame, int appId, string metadataSourceId)
        {
            var applied = false;
            if (string.Equals(metadataSourceId, ImportedGameMetadataSourceCatalog.SteamHuntersId, StringComparison.OrdinalIgnoreCase))
            {
                applied = ApplySteamHuntersMetadata(importedGame, appId);
            }
            else if (string.Equals(metadataSourceId, ImportedGameMetadataSourceCatalog.CompletionistId, StringComparison.OrdinalIgnoreCase))
            {
                applied = ApplyCompletionistMetadata(importedGame, appId);
            }

            if (applied || ShouldApplySteamStoreFallback(importedGame, appId))
            {
                ApplySteamStoreFallbackMetadata(importedGame, appId);
            }
        }

        private bool ApplyFastMetadata(Game importedGame, int appId, LibraryMetadataProvider metadataDownloader)
        {
            if (importedGame == null || appId <= 0 || metadataDownloader == null)
            {
                return false;
            }

            try
            {
                var lookupGame = CreateFastMetadataLookupGame(importedGame, appId);
                var metadata = metadataDownloader.GetMetadata(lookupGame);
                if (metadata == null)
                {
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(metadata.Name))
                {
                    importedGame.Name = metadata.Name;
                }

                if (!string.IsNullOrWhiteSpace(metadata.SortingName))
                {
                    importedGame.SortingName = metadata.SortingName;
                }

                if (!string.IsNullOrWhiteSpace(metadata.Description))
                {
                    importedGame.Description = metadata.Description;
                }

                var iconId = PersistMetadataFile(importedGame.Id, metadata.Icon);
                if (!string.IsNullOrWhiteSpace(iconId))
                {
                    importedGame.Icon = iconId;
                }

                var coverId = PersistMetadataFile(importedGame.Id, metadata.CoverImage);
                if (!string.IsNullOrWhiteSpace(coverId))
                {
                    importedGame.CoverImage = coverId;
                }

                var backgroundId = PersistMetadataFile(importedGame.Id, metadata.BackgroundImage);
                if (!string.IsNullOrWhiteSpace(backgroundId))
                {
                    importedGame.BackgroundImage = backgroundId;
                }

                if (metadata.ReleaseDate != null)
                {
                    importedGame.ReleaseDate = metadata.ReleaseDate;
                }

                if (metadata.CriticScore.HasValue)
                {
                    importedGame.CriticScore = metadata.CriticScore;
                }

                if (metadata.CommunityScore.HasValue)
                {
                    importedGame.CommunityScore = metadata.CommunityScore;
                }

                if (metadata.Platforms?.Count > 0)
                {
                    ReplaceCollection(importedGame.Platforms, _api.Database.Platforms.Add(metadata.Platforms));
                }

                if (metadata.Genres?.Count > 0)
                {
                    ReplaceCollection(importedGame.Genres, _api.Database.Genres.Add(metadata.Genres));
                }

                if (metadata.Developers?.Count > 0)
                {
                    ReplaceCollection(importedGame.Developers, _api.Database.Companies.Add(metadata.Developers));
                }

                if (metadata.Publishers?.Count > 0)
                {
                    ReplaceCollection(importedGame.Publishers, _api.Database.Companies.Add(metadata.Publishers));
                }

                if (metadata.Tags?.Count > 0)
                {
                    ReplaceCollection(importedGame.Tags, _api.Database.Tags.Add(metadata.Tags));
                }

                if (metadata.Categories?.Count > 0)
                {
                    ReplaceCollection(importedGame.Categories, _api.Database.Categories.Add(metadata.Categories));
                }

                if (metadata.Features?.Count > 0)
                {
                    ReplaceCollection(importedGame.Features, _api.Database.Features.Add(metadata.Features));
                }

                if (metadata.AgeRatings?.Count > 0)
                {
                    ReplaceCollection(importedGame.AgeRatings, _api.Database.AgeRatings.Add(metadata.AgeRatings));
                }

                if (metadata.Regions?.Count > 0)
                {
                    ReplaceCollection(importedGame.Regions, _api.Database.Regions.Add(metadata.Regions));
                }

                if (metadata.Series?.Count > 0)
                {
                    ReplaceCollection(importedGame.Series, _api.Database.Series.Add(metadata.Series));
                }

                if (metadata.Links?.Count > 0)
                {
                    ReplaceCollection(importedGame.Links, metadata.Links);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"[{_logPrefix}] Failed applying fast metadata for appId={appId}.");
                return false;
            }
        }

        private bool ApplyMetadataPlugin(Game importedGame, int appId, MetadataPlugin metadataPlugin)
        {
            if (importedGame == null || appId <= 0 || metadataPlugin == null)
            {
                return false;
            }

            try
            {
                ClearImportedMetadataForOverwrite(importedGame);
                var lookupGame = CreateMetadataPluginLookupGame(importedGame, appId, metadataPlugin);

                using (var provider = metadataPlugin.GetMetadataProvider(new MetadataRequestOptions(lookupGame, true)))
                {
                    if (provider == null)
                    {
                        return false;
                    }

                    var args = new GetMetadataFieldArgs();
                    var applied = false;

                    var name = provider.GetName(args);
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        importedGame.Name = name;
                        importedGame.SortingName = name;
                        applied = true;
                    }

                    var description = provider.GetDescription(args);
                    if (!string.IsNullOrWhiteSpace(description))
                    {
                        importedGame.Description = PrepareProviderMetadataDescription(description);
                        applied = true;
                    }

                    var iconId = PersistMetadataFile(importedGame.Id, provider.GetIcon(args));
                    if (!string.IsNullOrWhiteSpace(iconId))
                    {
                        importedGame.Icon = iconId;
                        applied = true;
                    }

                    var coverId = PersistMetadataFile(importedGame.Id, provider.GetCoverImage(args));
                    if (!string.IsNullOrWhiteSpace(coverId))
                    {
                        importedGame.CoverImage = coverId;
                        applied = true;
                    }

                    var backgroundId = PersistMetadataFile(importedGame.Id, provider.GetBackgroundImage(args));
                    if (!string.IsNullOrWhiteSpace(backgroundId))
                    {
                        importedGame.BackgroundImage = backgroundId;
                        applied = true;
                    }

                    var releaseDate = provider.GetReleaseDate(args);
                    if (releaseDate != null)
                    {
                        importedGame.ReleaseDate = releaseDate;
                        applied = true;
                    }

                    var criticScore = provider.GetCriticScore(args);
                    if (criticScore.HasValue)
                    {
                        importedGame.CriticScore = criticScore;
                        applied = true;
                    }

                    var communityScore = provider.GetCommunityScore(args);
                    if (communityScore.HasValue)
                    {
                        importedGame.CommunityScore = communityScore;
                        applied = true;
                    }

                    var platforms = provider.GetPlatforms(args)?.ToList();
                    var genres = provider.GetGenres(args)?.ToList();
                    var developers = provider.GetDevelopers(args)?.ToList();
                    var publishers = provider.GetPublishers(args)?.ToList();
                    var tags = provider.GetTags(args)?.ToList();
                    var features = provider.GetFeatures(args)?.ToList();
                    var ageRatings = provider.GetAgeRatings(args)?.ToList();
                    var regions = provider.GetRegions(args)?.ToList();
                    var series = provider.GetSeries(args)?.ToList();

                    if (platforms?.Count > 0)
                    {
                        ReplaceCollection(importedGame.Platforms, _api.Database.Platforms.Add(platforms));
                        applied = true;
                    }

                    if (genres?.Count > 0)
                    {
                        ReplaceCollection(importedGame.Genres, _api.Database.Genres.Add(genres));
                        applied = true;
                    }

                    if (developers?.Count > 0)
                    {
                        ReplaceCollection(importedGame.Developers, _api.Database.Companies.Add(developers));
                        applied = true;
                    }

                    if (publishers?.Count > 0)
                    {
                        ReplaceCollection(importedGame.Publishers, _api.Database.Companies.Add(publishers));
                        applied = true;
                    }

                    if (tags?.Count > 0)
                    {
                        ReplaceCollection(importedGame.Tags, _api.Database.Tags.Add(tags));
                        applied = true;
                    }

                    if (features?.Count > 0)
                    {
                        ReplaceCollection(importedGame.Features, _api.Database.Features.Add(features));
                        applied = true;
                    }

                    if (ageRatings?.Count > 0)
                    {
                        ReplaceCollection(importedGame.AgeRatings, _api.Database.AgeRatings.Add(ageRatings));
                        applied = true;
                    }

                    if (regions?.Count > 0)
                    {
                        ReplaceCollection(importedGame.Regions, _api.Database.Regions.Add(regions));
                        applied = true;
                    }

                    if (series?.Count > 0)
                    {
                        ReplaceCollection(importedGame.Series, _api.Database.Series.Add(series));
                        applied = true;
                    }

                    var links = provider.GetLinks(args)?.ToList();
                    if (links?.Count > 0)
                    {
                        ReplaceCollection(importedGame.Links, links);
                        applied = true;
                    }

                    return applied;
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"[{_logPrefix}] Failed applying metadata plugin '{metadataPlugin?.Name}' for appId={appId}.");
                return false;
            }
        }

        private bool ApplySteamHuntersMetadata(Game importedGame, int appId)
        {
            var url = $"https://steamhunters.com/apps/{appId}/achievements";
            return ApplyImportedProviderPageMetadata(
                importedGame,
                TryFetchImportedMetadataPage(
                    url,
                    document => new ImportedProviderPageMetadata
                    {
                        Name = FirstNonEmpty(
                            ExtractMetaContent(document, "og:title"),
                            TryExtractPageTitle(document),
                            ExtractHeadingText(document)),
                        Description = FirstNonEmpty(
                            FindTextAfterHeading(document, "Description"),
                            ExtractMetaContent(document, "description")),
                        IconUrl = ExtractImageUrl(document, url, "//img[contains(concat(' ', normalize-space(@class), ' '), ' image-rounded ') and contains(concat(' ', normalize-space(@class), ' '), ' image-1em ')]"),
                        Url = url,
                        UrlName = "SteamHunters"
                    }));
        }

        private bool ApplyCompletionistMetadata(Game importedGame, int appId)
        {
            var url = $"https://completionist.me/steam/app/{appId}/achievements";
            return ApplyImportedProviderPageMetadata(
                importedGame,
                TryFetchImportedMetadataPage(
                    url,
                    document => new ImportedProviderPageMetadata
                    {
                        Name = FirstNonEmpty(
                            ExtractMetaContent(document, "og:title"),
                            TryExtractPageTitle(document),
                            ExtractHeadingText(document)),
                        Description = FirstNonEmpty(
                            ExtractMetaContent(document, "description"),
                            FindTextAfterHeading(document, "Description")),
                        IconUrl = ExtractImageUrl(document, url, "//*[contains(concat(' ', normalize-space(@class), ' '), ' dropdown-toggle ')]//img[@src]"),
                        Url = url,
                        UrlName = "Completionist.me"
                    }));
        }

        private ImportedProviderPageMetadata TryFetchImportedMetadataPage(string url, Func<HtmlDocument, ImportedProviderPageMetadata> extractor)
        {
            if (string.IsNullOrWhiteSpace(url) || extractor == null)
            {
                return null;
            }

            try
            {
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36";
                request.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
                request.Referer = BuildRequestReferer(url);
                request.Headers[HttpRequestHeader.AcceptLanguage] = "en-US,en;q=0.9";

                using (var response = (HttpWebResponse)request.GetResponse())
                using (var stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream ?? Stream.Null, Encoding.UTF8))
                {
                    var html = reader.ReadToEnd();
                    if (string.IsNullOrWhiteSpace(html))
                    {
                        return null;
                    }

                    var document = new HtmlDocument();
                    document.LoadHtml(html);
                    return extractor(document);
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"[{_logPrefix}] Failed downloading imported metadata page '{url}'.");
                return null;
            }
        }

        private bool ApplyImportedProviderPageMetadata(Game importedGame, ImportedProviderPageMetadata metadata)
        {
            if (importedGame == null || metadata == null)
            {
                return false;
            }

            var applied = false;

            if (!string.IsNullOrWhiteSpace(metadata.Name))
            {
                importedGame.Name = metadata.Name.Trim();
                importedGame.SortingName = importedGame.Name;
                applied = true;
            }

            if (!string.IsNullOrWhiteSpace(metadata.Description))
            {
                importedGame.Description = PrepareProviderMetadataDescription(metadata.Description);
                applied = true;
            }

            var iconId = PersistMetadataFile(importedGame.Id, string.IsNullOrWhiteSpace(metadata.IconUrl) ? null : new MetadataFile(metadata.IconUrl));
            if (!string.IsNullOrWhiteSpace(iconId))
            {
                importedGame.Icon = iconId;
                applied = true;
            }

            if (!string.IsNullOrWhiteSpace(metadata.Url))
            {
                var links = importedGame.Links?.ToList() ?? new List<Link>();
                AddLinkIfMissing(links, metadata.UrlName ?? "Source", metadata.Url);
                ReplaceCollection(importedGame.Links, links);
                applied = true;
            }

            return applied;
        }

        private void ApplySteamStoreFallbackMetadata(Game importedGame, int appId)
        {
            if (importedGame == null || appId <= 0)
            {
                return;
            }

            try
            {
                var requestUrl = $"https://store.steampowered.com/api/appdetails?appids={appId}";
                using (var webClient = new WebClient())
                {
                    webClient.Headers[HttpRequestHeader.UserAgent] =
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
                    webClient.Headers[HttpRequestHeader.Accept] = "application/json";
                    webClient.Encoding = Encoding.UTF8;

                    var json = webClient.DownloadString(requestUrl);
                    if (string.IsNullOrWhiteSpace(json))
                    {
                        return;
                    }

                    var root = JObject.Parse(json);
                    var envelope = root[appId.ToString(CultureInfo.InvariantCulture)] as JObject;
                    if (envelope?["success"]?.Value<bool>() != true)
                    {
                        return;
                    }

                    var data = envelope["data"] as JObject;
                    if (data == null)
                    {
                        return;
                    }

                    var resolvedName = data["name"]?.Value<string>()?.Trim();
                    if (!string.IsNullOrWhiteSpace(resolvedName))
                    {
                        importedGame.Name = resolvedName;
                        importedGame.SortingName = resolvedName;
                    }

                    if (string.IsNullOrWhiteSpace(importedGame.Description))
                    {
                        var description = data["about_the_game"]?.Value<string>()?.Trim();
                        if (string.IsNullOrWhiteSpace(description))
                        {
                            description = data["detailed_description"]?.Value<string>()?.Trim();
                        }

                        if (string.IsNullOrWhiteSpace(description))
                        {
                            description = data["short_description"]?.Value<string>()?.Trim();
                        }

                        importedGame.Description = NormalizeMetadataDescription(description);
                    }

                    var iconUrl = data["capsule_image"]?.Value<string>()?.Trim();
                    if (string.IsNullOrWhiteSpace(iconUrl))
                    {
                        iconUrl = data["header_image"]?.Value<string>()?.Trim();
                    }

                    var coverUrl = data["header_image"]?.Value<string>()?.Trim();
                    var backgroundUrl = data["background_raw"]?.Value<string>()?.Trim();
                    if (string.IsNullOrWhiteSpace(backgroundUrl))
                    {
                        backgroundUrl = data["background"]?.Value<string>()?.Trim();
                    }

                    var iconId = PersistMetadataFile(importedGame.Id, string.IsNullOrWhiteSpace(iconUrl) || !string.IsNullOrWhiteSpace(importedGame.Icon) ? null : new MetadataFile(iconUrl));
                    if (!string.IsNullOrWhiteSpace(iconId))
                    {
                        importedGame.Icon = iconId;
                    }

                    var coverId = PersistMetadataFile(importedGame.Id, string.IsNullOrWhiteSpace(coverUrl) || !string.IsNullOrWhiteSpace(importedGame.CoverImage) ? null : new MetadataFile(coverUrl));
                    if (!string.IsNullOrWhiteSpace(coverId))
                    {
                        importedGame.CoverImage = coverId;
                    }

                    var backgroundId = PersistMetadataFile(importedGame.Id, string.IsNullOrWhiteSpace(backgroundUrl) || !string.IsNullOrWhiteSpace(importedGame.BackgroundImage) ? null : new MetadataFile(backgroundUrl));
                    if (!string.IsNullOrWhiteSpace(backgroundId))
                    {
                        importedGame.BackgroundImage = backgroundId;
                    }

                    var websiteUrl = data["website"]?.Value<string>()?.Trim();
                    var links = importedGame.Links?.ToList() ?? new List<Link>();
                    AddLinkIfMissing(links, "Steam Store", $"https://store.steampowered.com/app/{appId}/");
                    AddLinkIfMissing(links, "Community Hub", $"https://steamcommunity.com/app/{appId}");
                    AddLinkIfMissing(links, "Discussions", $"https://steamcommunity.com/app/{appId}/discussions/");
                    AddLinkIfMissing(links, "Guides", $"https://steamcommunity.com/app/{appId}/guides/");
                    AddLinkIfMissing(links, "News", $"https://store.steampowered.com/news/app/{appId}");

                    var categoryIds = data["categories"]?
                        .Values<JObject>()
                        .Select(category => category?["id"]?.Value<int>() ?? 0)
                        .Where(id => id > 0)
                        .ToHashSet() ?? new HashSet<int>();
                    if (categoryIds.Contains(22))
                    {
                        AddLinkIfMissing(links, "Achievements", $"https://steamcommunity.com/stats/{appId}/achievements/");
                    }

                    if (categoryIds.Contains(30))
                    {
                        AddLinkIfMissing(links, "Workshop", $"https://steamcommunity.com/app/{appId}/workshop/");
                    }

                    if (!string.IsNullOrWhiteSpace(websiteUrl))
                    {
                        AddLinkIfMissing(links, "Website", websiteUrl);
                    }

                    if (links.Count > 0)
                    {
                        ReplaceCollection(importedGame.Links, links);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"[{_logPrefix}] Failed applying Steam Store fallback metadata for appId={appId}.");
            }
        }

        private static bool ShouldApplySteamStoreFallback(Game importedGame, int appId)
        {
            if (importedGame == null)
            {
                return false;
            }

            if (IsPlaceholderSteamName(importedGame.Name, appId) || IsPlaceholderSteamName(importedGame.SortingName, appId))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(importedGame.Description))
            {
                return true;
            }

            return importedGame.Links == null || importedGame.Links.Count == 0;
        }

        private static bool IsPlaceholderSteamName(string value, int appId)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            return string.Equals(value.Trim(), $"Steam App {appId}", StringComparison.OrdinalIgnoreCase);
        }

        private Game CreateMetadataPluginLookupGame(Game importedGame, int appId, MetadataPlugin metadataPlugin)
        {
            var isUniversalSteamMetadata = ImportedGameMetadataSourceCatalog.IsUniversalSteamMetadataPlugin(metadataPlugin, _api, _logger);
            var lookupName = isUniversalSteamMetadata
                ? BuildUniversalSteamMetadataLookupName(importedGame.Name, appId)
                : importedGame.Name;

            return new Game
            {
                GameId = appId > 0 ? appId.ToString(CultureInfo.InvariantCulture) : importedGame.GameId,
                PluginId = isUniversalSteamMetadata ? SteamDataProvider.SteamPluginId : importedGame.PluginId,
                Name = lookupName,
                SortingName = lookupName,
                SourceId = importedGame.SourceId
            };
        }

        private static Game CreateFastMetadataLookupGame(Game importedGame, int appId)
        {
            var lookupName = BuildFastMethodLookupName(importedGame?.Name, appId);
            return new Game
            {
                GameId = appId > 0 ? appId.ToString(CultureInfo.InvariantCulture) : importedGame?.GameId,
                PluginId = SteamDataProvider.SteamPluginId,
                Name = lookupName,
                SortingName = lookupName,
                SourceId = importedGame?.SourceId ?? Guid.Empty
            };
        }

        private static string BuildUniversalSteamMetadataLookupName(string currentName, int appId)
        {
            return appId > 0
                ? appId.ToString(CultureInfo.InvariantCulture)
                : (currentName ?? string.Empty).Trim();
        }

        private static string BuildFastMethodLookupName(string currentName, int appId)
        {
            var trimmedName = (currentName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmedName) || IsPlaceholderSteamName(trimmedName, appId))
            {
                return appId.ToString(CultureInfo.InvariantCulture);
            }

            return trimmedName;
        }

        private static void ClearImportedMetadataForOverwrite(Game importedGame)
        {
            if (importedGame == null)
            {
                return;
            }

            importedGame.Description = null;
            importedGame.Icon = null;
            importedGame.CoverImage = null;
            importedGame.BackgroundImage = null;
            importedGame.ReleaseDate = null;
            importedGame.CriticScore = null;
            importedGame.CommunityScore = null;

            importedGame.Platforms?.Clear();
            importedGame.Genres?.Clear();
            importedGame.Developers?.Clear();
            importedGame.Publishers?.Clear();
            importedGame.Tags?.Clear();
            importedGame.Features?.Clear();
            importedGame.AgeRatings?.Clear();
            importedGame.Regions?.Clear();
            importedGame.Series?.Clear();
            importedGame.Links?.Clear();
            importedGame.Categories?.Clear();
        }

        private string PersistMetadataFile(Guid gameId, MetadataFile metadataFile)
        {
            if (gameId == Guid.Empty || metadataFile == null || !metadataFile.HasImageData)
            {
                return null;
            }

            string tempFilePath = null;
            try
            {
                if (metadataFile.HasContent && metadataFile.Content?.Length > 0)
                {
                    var fileName = string.IsNullOrWhiteSpace(metadataFile.FileName)
                        ? $"import_meta_{Guid.NewGuid():N}{GetExtensionFromPath(metadataFile.Path)}"
                        : metadataFile.FileName.Trim();
                    tempFilePath = Path.Combine(CreateMetadataTempDirectory(), SanitizeFileName(fileName));
                    File.WriteAllBytes(tempFilePath, metadataFile.Content);
                    return _api.Database.AddFile(tempFilePath, gameId);
                }

                if (!string.IsNullOrWhiteSpace(metadataFile.Path) && File.Exists(metadataFile.Path))
                {
                    return _api.Database.AddFile(metadataFile.Path, gameId);
                }

                if (!string.IsNullOrWhiteSpace(metadataFile.Path) && Uri.TryCreate(metadataFile.Path, UriKind.Absolute, out var uri))
                {
                    var targetExtension = GetExtensionFromPath(uri.AbsolutePath);
                    var fileName = string.IsNullOrWhiteSpace(metadataFile.FileName)
                        ? $"import_meta_{Guid.NewGuid():N}{targetExtension}"
                        : metadataFile.FileName.Trim();
                    tempFilePath = Path.Combine(CreateMetadataTempDirectory(), SanitizeFileName(fileName));

                    using (var webClient = new WebClient())
                    {
                        webClient.DownloadFile(uri, tempFilePath);
                    }

                    return _api.Database.AddFile(tempFilePath, gameId);
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"[{_logPrefix}] Failed persisting metadata file for gameId={gameId} from '{metadataFile?.Path}'.");
            }
            finally
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(tempFilePath) && File.Exists(tempFilePath))
                    {
                        File.Delete(tempFilePath);
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static string CreateMetadataTempDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "PlayniteAchievements", "ImportedMetadata");
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static string GetExtensionFromPath(string path)
        {
            try
            {
                var extension = Path.GetExtension(path ?? string.Empty);
                return string.IsNullOrWhiteSpace(extension) ? ".img" : extension;
            }
            catch
            {
                return ".img";
            }
        }

        private static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return $"import_meta_{Guid.NewGuid():N}.img";
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string(fileName.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
            return string.IsNullOrWhiteSpace(sanitized) ? $"import_meta_{Guid.NewGuid():N}.img" : sanitized;
        }

        private static void ReplaceCollection<T>(ICollection<T> target, IEnumerable<T> source)
        {
            if (target == null || source == null)
            {
                return;
            }

            target.Clear();
            foreach (var item in source)
            {
                target.Add(item);
            }
        }

        private static void AddLinkIfMissing(ICollection<Link> links, string name, string url)
        {
            if (links == null || string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            if (links.Any(link => link != null && string.Equals(link.Url, url, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            links.Add(new Link(name, url));
        }

        private static string PrepareProviderMetadataDescription(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        }

        private static string NormalizeMetadataDescription(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = Regex.Replace(value, @"(?i)<br\s*/?>", "\n");
            normalized = Regex.Replace(normalized, @"(?i)</p\s*>", "\n\n");
            normalized = Regex.Replace(normalized, @"(?i)</div\s*>", "\n");
            normalized = Regex.Replace(normalized, @"(?i)</li\s*>", "\n");
            normalized = Regex.Replace(normalized, @"<[^>]+>", string.Empty);
            normalized = WebUtility.HtmlDecode(normalized ?? string.Empty);
            normalized = normalized.Replace("\r\n", "\n").Replace('\r', '\n');
            normalized = Regex.Replace(normalized, @"[ \t]+\n", "\n");
            normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");
            return normalized.Trim();
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return values?.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
        }

        private static string ExtractMetaContent(HtmlDocument document, string propertyName)
        {
            if (document?.DocumentNode == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return null;
            }

            var node = document.DocumentNode.SelectSingleNode($"//meta[@property='{propertyName}' or @name='{propertyName}']");
            return node?.GetAttributeValue("content", null)?.Trim();
        }

        private static string TryExtractPageTitle(HtmlDocument document)
        {
            return NormalizeMetadataDescription(document?.DocumentNode.SelectSingleNode("//title")?.InnerText);
        }

        private static string ExtractHeadingText(HtmlDocument document)
        {
            var heading = document?.DocumentNode.SelectSingleNode("//h1|//h2");
            return NormalizeMetadataDescription(heading?.InnerText);
        }

        private static string FindTextAfterHeading(HtmlDocument document, string headingText)
        {
            if (document?.DocumentNode == null || string.IsNullOrWhiteSpace(headingText))
            {
                return null;
            }

            var headingNodes = document.DocumentNode.SelectNodes("//h1|//h2|//h3|//h4");
            if (headingNodes == null || headingNodes.Count == 0)
            {
                return null;
            }

            foreach (var heading in headingNodes)
            {
                var text = NormalizeMetadataDescription(heading.InnerText);
                if (!string.Equals(text, headingText, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                for (var sibling = heading.NextSibling; sibling != null; sibling = sibling.NextSibling)
                {
                    if (sibling.NodeType != HtmlNodeType.Element)
                    {
                        continue;
                    }

                    var value = NormalizeMetadataDescription(sibling.InnerText);
                    if (!string.IsNullOrWhiteSpace(value) && !string.Equals(value, headingText, StringComparison.OrdinalIgnoreCase))
                    {
                        return value;
                    }
                }
            }

            return null;
        }

        private static string ExtractImageUrl(HtmlDocument document, string pageUrl, string xpath)
        {
            var source = document?.DocumentNode.SelectSingleNode(xpath)?.GetAttributeValue("src", null)?.Trim();
            return ResolveAbsoluteUrl(pageUrl, source);
        }

        private static string ResolveAbsoluteUrl(string pageUrl, string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return null;
            }

            if (Uri.TryCreate(source, UriKind.Absolute, out var absoluteUri))
            {
                return absoluteUri.ToString();
            }

            if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var baseUri))
            {
                return source;
            }

            return Uri.TryCreate(baseUri, source, out var combinedUri)
                ? combinedUri.ToString()
                : source;
        }

        private static string BuildRequestReferer(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return null;
            }

            return uri.GetLeftPart(UriPartial.Authority) + "/";
        }
    }
}