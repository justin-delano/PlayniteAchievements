using System;
using System.Collections.Generic;
using System.Linq;

namespace PlayniteAchievements.Models.Settings
{
    public enum ShowcaseWidgetKind
    {
        Profile = 0,
        Scores = 1,
        Pie = 2,
        Timeline = 3,
        Statistics = 4,
        NativePoints = 5,
        PinnedAchievements = 6,
        FavoriteGames = 7,
        IconMosaic = 8,
        ScreenshotSlideshow = 9,
        RecentAchievements = 10,
        GameSummaries = 11,
        GameMosaic = 12,
        ActivityCalendar = 13
    }

    public enum ShowcasePageTemplate
    {
        Blank = 0,
        Showcase = 1,
        Analytics = 2,
        Collection = 3
    }

    public enum ShowcaseScoreMode
    {
        Dual = 0,
        Collection = 1,
        Prestige = 2
    }

    public enum ShowcasePieMode
    {
        CompletedGames = 0,
        Provider = 1,
        Rarity = 2,
        Trophy = 3
    }

    public enum ShowcasePointsGrouping
    {
        Provider = 0,
        Game = 1
    }

    public enum ShowcaseFavoriteGameSource
    {
        ShowcasePins = 0,
        PlayniteFavorites = 1
    }

    public enum ShowcaseMosaicSource
    {
        Recent = 0,
        Rarest = 1,
        Pinned = 2
    }

    public enum ShowcaseScreenshotVariant
    {
        All = 0,
        Clean = 1,
        Notification = 2,
        Framed = 3
    }

    public enum ShowcaseImageFitMode
    {
        Fit = 0,
        Fill = 1
    }

    public enum ShowcaseGameListSort
    {
        LastUnlock = 0,
        Completion = 1,
        Name = 2,
        Playtime = 3
    }

    public enum ShowcaseGameMosaicSource
    {
        Completed = 0,
        All = 1,
        Pinned = 2,
        PlayniteFavorites = 3
    }

    public sealed class ShowcaseProfileSettings
    {
        public string DisplayName { get; set; }

        public string Subtitle { get; set; }

        public string AvatarPath { get; set; }

        public string BackgroundPath { get; set; }

        public ShowcaseProfileSettings Clone()
        {
            return new ShowcaseProfileSettings
            {
                DisplayName = DisplayName,
                Subtitle = Subtitle,
                AvatarPath = AvatarPath,
                BackgroundPath = BackgroundPath
            };
        }
    }

    public sealed class PinnedAchievementReference
    {
        public Guid GameId { get; set; }

        public string ApiName { get; set; }

        public string LastKnownGameName { get; set; }

        public string LastKnownAchievementName { get; set; }

        public DateTime AddedUtc { get; set; } = DateTime.UtcNow;

        public string Key => $"{GameId:D}:{(ApiName ?? string.Empty).Trim()}";

        public PinnedAchievementReference Clone()
        {
            return new PinnedAchievementReference
            {
                GameId = GameId,
                ApiName = ApiName,
                LastKnownGameName = LastKnownGameName,
                LastKnownAchievementName = LastKnownAchievementName,
                AddedUtc = AddedUtc
            };
        }
    }

    public sealed class ShowcaseWidgetInstanceSettings
    {
        public string InstanceId { get; set; } = Guid.NewGuid().ToString("N");

        public ShowcaseWidgetKind Kind { get; set; }

        public string CustomTitle { get; set; }

        public Dictionary<string, string> Options { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public ShowcaseWidgetInstanceSettings Clone()
        {
            return new ShowcaseWidgetInstanceSettings
            {
                InstanceId = InstanceId,
                Kind = Kind,
                CustomTitle = CustomTitle,
                Options = Options != null
                    ? new Dictionary<string, string>(Options, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };
        }

        public T GetOption<T>(string key, T fallback)
            where T : struct
        {
            if (Options == null ||
                string.IsNullOrWhiteSpace(key) ||
                !Options.TryGetValue(key, out var raw) ||
                string.IsNullOrWhiteSpace(raw))
            {
                return fallback;
            }

            if (typeof(T).IsEnum && Enum.TryParse(raw, true, out T enumValue))
            {
                return enumValue;
            }

            try
            {
                return (T)Convert.ChangeType(raw, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        public void SetOption<T>(string key, T value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (Options == null)
            {
                Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            Options[key] = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    public sealed class ShowcaseBlockSettings
    {
        public string BlockId { get; set; } = Guid.NewGuid().ToString("N");

        public int Row { get; set; }

        public int Column { get; set; }

        public int RowSpan { get; set; } = 1;

        public int ColumnSpan { get; set; } = 1;

        public string WidgetInstanceId { get; set; }

        public ShowcaseBlockSettings Clone()
        {
            return new ShowcaseBlockSettings
            {
                BlockId = BlockId,
                Row = Row,
                Column = Column,
                RowSpan = RowSpan,
                ColumnSpan = ColumnSpan,
                WidgetInstanceId = WidgetInstanceId
            };
        }
    }

    public sealed class ShowcasePageSettings
    {
        public string PageId { get; set; } = Guid.NewGuid().ToString("N");

        public string Name { get; set; } = "Showcase";

        public List<ShowcaseBlockSettings> Blocks { get; set; } =
            new List<ShowcaseBlockSettings>();

        public ShowcasePageSettings Clone()
        {
            return new ShowcasePageSettings
            {
                PageId = PageId,
                Name = Name,
                Blocks = (Blocks ?? new List<ShowcaseBlockSettings>())
                    .Where(block => block != null)
                    .Select(block => block.Clone())
                    .ToList()
            };
        }
    }

    public sealed class ShowcaseSettings
    {
        public const int CurrentLayoutVersion = 1;

        public int LayoutVersion { get; set; } = CurrentLayoutVersion;

        public string LastSelectedPageId { get; set; }

        public List<ShowcasePageSettings> Pages { get; set; } =
            new List<ShowcasePageSettings>();

        public List<ShowcaseWidgetInstanceSettings> WidgetInstances { get; set; } =
            new List<ShowcaseWidgetInstanceSettings>();

        public List<Guid> PinnedGameIds { get; set; } = new List<Guid>();

        public List<PinnedAchievementReference> PinnedAchievements { get; set; } =
            new List<PinnedAchievementReference>();

        public ShowcaseProfileSettings Profile { get; set; } =
            new ShowcaseProfileSettings();

        public Dictionary<string, ShowcaseWidgetInstanceSettings> StartPageInstances { get; set; } =
            new Dictionary<string, ShowcaseWidgetInstanceSettings>(StringComparer.OrdinalIgnoreCase);

        public ShowcaseSettings Clone()
        {
            return new ShowcaseSettings
            {
                LayoutVersion = LayoutVersion,
                LastSelectedPageId = LastSelectedPageId,
                Pages = (Pages ?? new List<ShowcasePageSettings>())
                    .Where(page => page != null)
                    .Select(page => page.Clone())
                    .ToList(),
                WidgetInstances = (WidgetInstances ?? new List<ShowcaseWidgetInstanceSettings>())
                    .Where(widget => widget != null)
                    .Select(widget => widget.Clone())
                    .ToList(),
                PinnedGameIds = (PinnedGameIds ?? new List<Guid>()).ToList(),
                PinnedAchievements = (PinnedAchievements ?? new List<PinnedAchievementReference>())
                    .Where(pin => pin != null)
                    .Select(pin => pin.Clone())
                    .ToList(),
                Profile = Profile?.Clone() ?? new ShowcaseProfileSettings(),
                StartPageInstances = (StartPageInstances ??
                    new Dictionary<string, ShowcaseWidgetInstanceSettings>(StringComparer.OrdinalIgnoreCase))
                    .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null)
                    .ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value.Clone(),
                        StringComparer.OrdinalIgnoreCase)
            };
        }
    }
}
