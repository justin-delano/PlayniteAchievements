using System;
using System.Collections.Generic;
using System.Linq;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;

namespace PlayniteAchievements.Services.Showcase
{
    public static class ShowcaseLayoutService
    {
        public const int GridSize = 3;

        public static ShowcaseSettings CreateDefault(
            bool showCollectionScore = true,
            bool showPrestigeScore = true)
        {
            var settings = new ShowcaseSettings();
            var page = CreatePage(
                ShowcasePageTemplate.Showcase,
                settings,
                "Showcase",
                showCollectionScore,
                showPrestigeScore);
            settings.Pages.Add(page);
            settings.LastSelectedPageId = page.PageId;
            return settings;
        }

        public static ShowcasePageSettings AddPage(
            ShowcaseSettings settings,
            ShowcasePageTemplate template,
            string name = null)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            settings.Pages = settings.Pages ?? new List<ShowcasePageSettings>();
            settings.WidgetInstances = settings.WidgetInstances ??
                new List<ShowcaseWidgetInstanceSettings>();
            var page = CreatePage(
                template,
                settings,
                name,
                showCollectionScore: true,
                showPrestigeScore: true);
            settings.Pages.Add(page);
            settings.LastSelectedPageId = page.PageId;
            return page;
        }

        public static bool RenamePage(
            ShowcaseSettings settings,
            string pageId,
            string name)
        {
            Normalize(settings);
            var page = FindPage(settings, pageId);
            var normalizedName = name?.Trim();
            if (page == null || string.IsNullOrWhiteSpace(normalizedName))
            {
                return false;
            }

            page.Name = MakeUniquePageName(settings, normalizedName, page.PageId);
            return true;
        }

        public static bool ResetPage(
            ShowcaseSettings settings,
            string pageId,
            ShowcasePageTemplate template = ShowcasePageTemplate.Showcase)
        {
            Normalize(settings);
            var page = FindPage(settings, pageId);
            if (page == null)
            {
                return false;
            }

            var pageIndex = settings.Pages.IndexOf(page);
            var replacement = CreatePage(
                template,
                settings,
                page.Name,
                showCollectionScore: true,
                showPrestigeScore: true);
            replacement.PageId = page.PageId;
            replacement.Name = page.Name;
            settings.Pages[pageIndex] = replacement;
            settings.LastSelectedPageId = replacement.PageId;
            PruneOrphanedWidgets(settings);
            return true;
        }

        public static ShowcasePageSettings DuplicatePage(
            ShowcaseSettings settings,
            string pageId,
            string copySuffix = null)
        {
            Normalize(settings);
            var source = FindPage(settings, pageId);
            if (source == null)
            {
                return null;
            }

            var duplicate = source.Clone();
            duplicate.PageId = NewId();
            duplicate.Name = MakeUniquePageName(
                settings,
                $"{source.Name} {(string.IsNullOrWhiteSpace(copySuffix) ? "Copy" : copySuffix.Trim())}");

            var widgetIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var block in duplicate.Blocks)
            {
                block.BlockId = NewId();
                if (string.IsNullOrWhiteSpace(block.WidgetInstanceId))
                {
                    continue;
                }

                if (!widgetIdMap.TryGetValue(block.WidgetInstanceId, out var newWidgetId))
                {
                    var sourceWidget = FindWidget(settings, block.WidgetInstanceId);
                    if (sourceWidget == null)
                    {
                        block.WidgetInstanceId = null;
                        continue;
                    }

                    var widgetCopy = sourceWidget.Clone();
                    widgetCopy.InstanceId = newWidgetId = NewId();
                    settings.WidgetInstances.Add(widgetCopy);
                    widgetIdMap[block.WidgetInstanceId] = newWidgetId;
                }

                block.WidgetInstanceId = newWidgetId;
            }

            var sourceIndex = settings.Pages.IndexOf(source);
            settings.Pages.Insert(Math.Min(settings.Pages.Count, sourceIndex + 1), duplicate);
            settings.LastSelectedPageId = duplicate.PageId;
            return duplicate;
        }

        public static bool DeletePage(ShowcaseSettings settings, string pageId)
        {
            Normalize(settings);
            if (settings.Pages.Count <= 1)
            {
                return false;
            }

            var page = FindPage(settings, pageId);
            if (page == null)
            {
                return false;
            }

            var oldIndex = settings.Pages.IndexOf(page);
            settings.Pages.Remove(page);
            PruneOrphanedWidgets(settings);
            if (string.Equals(settings.LastSelectedPageId, pageId, StringComparison.OrdinalIgnoreCase))
            {
                settings.LastSelectedPageId =
                    settings.Pages[Math.Min(oldIndex, settings.Pages.Count - 1)].PageId;
            }

            return true;
        }

        public static bool MovePage(ShowcaseSettings settings, string pageId, int direction)
        {
            Normalize(settings);
            var page = FindPage(settings, pageId);
            if (page == null || direction == 0)
            {
                return false;
            }

            var current = settings.Pages.IndexOf(page);
            var target = Math.Max(0, Math.Min(settings.Pages.Count - 1, current + Math.Sign(direction)));
            if (target == current)
            {
                return false;
            }

            settings.Pages.RemoveAt(current);
            settings.Pages.Insert(target, page);
            return true;
        }

        public static bool TrySplit(
            ShowcaseSettings settings,
            string pageId,
            string blockId,
            bool vertical,
            int gridLine)
        {
            Normalize(settings);
            var page = FindPage(settings, pageId);
            var block = FindBlock(page, blockId);
            if (block == null)
            {
                return false;
            }

            if (vertical)
            {
                if (gridLine <= block.Column || gridLine >= block.Column + block.ColumnSpan)
                {
                    return false;
                }

                var right = block.Clone();
                right.BlockId = NewId();
                right.Column = gridLine;
                right.ColumnSpan = block.Column + block.ColumnSpan - gridLine;
                right.WidgetInstanceId = null;
                block.ColumnSpan = gridLine - block.Column;
                if (right.ColumnSpan > block.ColumnSpan)
                {
                    right.WidgetInstanceId = block.WidgetInstanceId;
                    block.WidgetInstanceId = null;
                }

                page.Blocks.Add(right);
            }
            else
            {
                if (gridLine <= block.Row || gridLine >= block.Row + block.RowSpan)
                {
                    return false;
                }

                var bottom = block.Clone();
                bottom.BlockId = NewId();
                bottom.Row = gridLine;
                bottom.RowSpan = block.Row + block.RowSpan - gridLine;
                bottom.WidgetInstanceId = null;
                block.RowSpan = gridLine - block.Row;
                if (bottom.RowSpan > block.RowSpan)
                {
                    bottom.WidgetInstanceId = block.WidgetInstanceId;
                    block.WidgetInstanceId = null;
                }

                page.Blocks.Add(bottom);
            }

            SortBlocks(page);
            return true;
        }

        public static bool TrySplitThreeWays(
            ShowcaseSettings settings,
            string pageId,
            string blockId,
            bool vertical)
        {
            Normalize(settings);
            var page = FindPage(settings, pageId);
            var block = FindBlock(page, blockId);
            var span = vertical ? block?.ColumnSpan ?? 0 : block?.RowSpan ?? 0;
            if (block == null || span != GridSize)
            {
                return false;
            }

            var middle = block.Clone();
            middle.BlockId = NewId();
            middle.WidgetInstanceId = null;
            var last = block.Clone();
            last.BlockId = NewId();
            last.WidgetInstanceId = null;

            if (vertical)
            {
                block.ColumnSpan = 1;
                middle.Column = block.Column + 1;
                middle.ColumnSpan = 1;
                last.Column = block.Column + 2;
                last.ColumnSpan = 1;
            }
            else
            {
                block.RowSpan = 1;
                middle.Row = block.Row + 1;
                middle.RowSpan = 1;
                last.Row = block.Row + 2;
                last.RowSpan = 1;
            }

            page.Blocks.Add(middle);
            page.Blocks.Add(last);
            SortBlocks(page);
            return true;
        }

        public static bool TryMerge(
            ShowcaseSettings settings,
            string pageId,
            string firstBlockId,
            string secondBlockId)
        {
            return TryMerge(
                settings,
                pageId,
                firstBlockId,
                secondBlockId,
                preferredWidgetInstanceId: null);
        }

        public static bool TryMerge(
            ShowcaseSettings settings,
            string pageId,
            string firstBlockId,
            string secondBlockId,
            string preferredWidgetInstanceId)
        {
            Normalize(settings);
            var page = FindPage(settings, pageId);
            var first = FindBlock(page, firstBlockId);
            var second = FindBlock(page, secondBlockId);
            var closure = GetMergeClosureCore(page, first, second);
            return closure.Count == 2 && TryMergeClosure(
                settings,
                page,
                first,
                closure,
                preferredWidgetInstanceId);
        }

        public static IReadOnlyList<ShowcaseBlockSettings> GetMergeClosure(
            ShowcaseSettings settings,
            string pageId,
            string firstBlockId,
            string secondBlockId)
        {
            Normalize(settings);
            var page = FindPage(settings, pageId);
            var first = FindBlock(page, firstBlockId);
            var second = FindBlock(page, secondBlockId);
            return GetMergeClosureCore(page, first, second);
        }

        public static bool TryMergeWithFallback(
            ShowcaseSettings settings,
            string pageId,
            string firstBlockId,
            string secondBlockId,
            string preferredWidgetInstanceId = null)
        {
            Normalize(settings);
            var page = FindPage(settings, pageId);
            var first = FindBlock(page, firstBlockId);
            var second = FindBlock(page, secondBlockId);
            var closure = GetMergeClosureCore(page, first, second);
            if (closure.Count < 2)
            {
                return false;
            }

            return TryMergeClosure(
                settings,
                page,
                first,
                closure,
                preferredWidgetInstanceId);
        }

        private static bool TryMergeClosure(
            ShowcaseSettings settings,
            ShowcasePageSettings page,
            ShowcaseBlockSettings survivor,
            IReadOnlyList<ShowcaseBlockSettings> closure,
            string preferredWidgetInstanceId)
        {
            if (survivor == null || closure == null || closure.Count < 2)
            {
                return false;
            }

            var occupiedWidgetIds = closure
                .Where(block => !string.IsNullOrWhiteSpace(block.WidgetInstanceId))
                .Select(block => block.WidgetInstanceId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (occupiedWidgetIds.Count > 1 &&
                !occupiedWidgetIds.Contains(
                    preferredWidgetInstanceId,
                    StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            var survivorWidgetId = occupiedWidgetIds.Count == 0
                ? null
                : occupiedWidgetIds.Count == 1
                    ? occupiedWidgetIds[0]
                    : preferredWidgetInstanceId;
            var row = closure.Min(block => block.Row);
            var column = closure.Min(block => block.Column);
            var rowEnd = closure.Max(block => block.Row + block.RowSpan);
            var columnEnd = closure.Max(block => block.Column + block.ColumnSpan);
            var cellCount = closure.Sum(block => block.RowSpan * block.ColumnSpan);
            if (cellCount != (rowEnd - row) * (columnEnd - column))
            {
                return false;
            }

            survivor.Row = row;
            survivor.Column = column;
            survivor.RowSpan = rowEnd - row;
            survivor.ColumnSpan = columnEnd - column;
            survivor.WidgetInstanceId = survivorWidgetId;
            foreach (var block in closure.Where(block => !ReferenceEquals(block, survivor)).ToList())
            {
                page.Blocks.Remove(block);
            }

            foreach (var removedWidgetId in occupiedWidgetIds.Where(id =>
                         !string.Equals(
                             id,
                             survivorWidgetId,
                             StringComparison.OrdinalIgnoreCase)))
            {
                var removed = FindWidget(settings, removedWidgetId);
                if (removed != null)
                {
                    settings.WidgetInstances.Remove(removed);
                }
            }

            SortBlocks(page);
            return IsValidPartition(page.Blocks);
        }

        public static bool PlaceWidget(
            ShowcaseSettings settings,
            string pageId,
            string blockId,
            string widgetInstanceId)
        {
            Normalize(settings);
            var placement = ResolvePlacement(settings, pageId, blockId, widgetInstanceId);
            if (!IsPlacementAllowed(settings, placement))
            {
                return false;
            }

            var displaced = placement.Target.WidgetInstanceId;
            placement.Target.WidgetInstanceId = placement.Widget.InstanceId;
            if (placement.Source != null &&
                !ReferenceEquals(placement.Source, placement.Target))
            {
                placement.Source.WidgetInstanceId = displaced;
            }

            return true;
        }

        public static bool CanPlaceWidget(
            ShowcaseSettings settings,
            string pageId,
            string blockId,
            string widgetInstanceId)
        {
            return IsPlacementAllowed(
                settings,
                ResolvePlacement(settings, pageId, blockId, widgetInstanceId));
        }

        private static WidgetPlacement ResolvePlacement(
            ShowcaseSettings settings,
            string pageId,
            string blockId,
            string widgetInstanceId)
        {
            var targetPage = FindPage(settings, pageId);
            var target = FindBlock(targetPage, blockId);
            var widget = FindWidget(settings, widgetInstanceId);
            if (target == null || widget == null)
            {
                return null;
            }

            var placement = new WidgetPlacement
            {
                TargetPage = targetPage,
                Target = target,
                Widget = widget
            };
            var resolvedWidgetId = widget.InstanceId;
            foreach (var candidatePage in settings?.Pages ?? new List<ShowcasePageSettings>())
            {
                var source = candidatePage?.Blocks?.FirstOrDefault(block =>
                    string.Equals(block.WidgetInstanceId, resolvedWidgetId, StringComparison.OrdinalIgnoreCase));
                if (source != null)
                {
                    placement.SourcePage = candidatePage;
                    placement.Source = source;
                    break;
                }
            }

            return placement;
        }

        private static bool IsPlacementAllowed(
            ShowcaseSettings settings,
            WidgetPlacement placement)
        {
            if (placement == null)
            {
                return false;
            }

            if (ShowcaseWidgetCatalog.Get(placement.Widget.Kind).SingleInstancePerPage &&
                placement.TargetPage.Blocks.Any(block =>
                    !ReferenceEquals(block, placement.Target) &&
                    !ReferenceEquals(block, placement.Source) &&
                    FindWidget(settings, block.WidgetInstanceId)?.Kind == placement.Widget.Kind))
            {
                return false;
            }

            var displaced = FindWidget(settings, placement.Target.WidgetInstanceId);
            if (placement.Source != null &&
                !ReferenceEquals(placement.Source, placement.Target) &&
                displaced != null &&
                ShowcaseWidgetCatalog.Get(displaced.Kind).SingleInstancePerPage &&
                placement.SourcePage?.Blocks?.Any(block =>
                    !ReferenceEquals(block, placement.Source) &&
                    !ReferenceEquals(block, placement.Target) &&
                    FindWidget(settings, block.WidgetInstanceId)?.Kind == displaced.Kind) == true)
            {
                return false;
            }

            return true;
        }

        public static ShowcaseWidgetInstanceSettings CreateWidget(
            ShowcaseSettings settings,
            ShowcaseWidgetKind kind)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            var widget = NewWidget(kind);
            settings.WidgetInstances.Add(widget);
            return widget;
        }

        private static void ClearWidgetAssignments(ShowcaseSettings settings, string widgetInstanceId)
        {
            if (settings?.Pages == null || string.IsNullOrWhiteSpace(widgetInstanceId))
            {
                return;
            }

            foreach (var block in settings.Pages
                .Where(page => page?.Blocks != null)
                .SelectMany(page => page.Blocks)
                .Where(block => block != null))
            {
                if (string.Equals(block.WidgetInstanceId, widgetInstanceId, StringComparison.OrdinalIgnoreCase))
                {
                    block.WidgetInstanceId = null;
                }
            }
        }

        public static bool DeleteWidget(ShowcaseSettings settings, string widgetInstanceId)
        {
            if (settings?.WidgetInstances == null || string.IsNullOrWhiteSpace(widgetInstanceId))
            {
                return false;
            }

            ClearWidgetAssignments(settings, widgetInstanceId);
            var widget = FindWidget(settings, widgetInstanceId);
            return widget != null && settings.WidgetInstances.Remove(widget);
        }

        public static int PruneOrphanedWidgets(ShowcaseSettings settings)
        {
            if (settings?.WidgetInstances == null)
            {
                return 0;
            }

            var placedIds = new HashSet<string>(
                (settings.Pages ?? new List<ShowcasePageSettings>())
                    .Where(page => page?.Blocks != null)
                    .SelectMany(page => page.Blocks)
                    .Where(block => !string.IsNullOrWhiteSpace(block?.WidgetInstanceId))
                    .Select(block => block.WidgetInstanceId),
                StringComparer.OrdinalIgnoreCase);
            var removed = settings.WidgetInstances.RemoveAll(widget =>
                widget == null || !placedIds.Contains(widget.InstanceId));
            return removed;
        }

        public static void Normalize(ShowcaseSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            settings.LayoutVersion = ShowcaseSettings.CurrentLayoutVersion;
            settings.WidgetInstances = NormalizeWidgets(settings.WidgetInstances);
            var widgetIds = new HashSet<string>(
                settings.WidgetInstances.Select(widget => widget.InstanceId),
                StringComparer.OrdinalIgnoreCase);

            settings.Pages = settings.Pages ?? new List<ShowcasePageSettings>();
            if (settings.Pages.Count == 0)
            {
                var seeded = CreateDefault();
                settings.Pages = seeded.Pages;
                settings.WidgetInstances = seeded.WidgetInstances;
                widgetIds = new HashSet<string>(
                    settings.WidgetInstances.Select(widget => widget.InstanceId),
                    StringComparer.OrdinalIgnoreCase);
            }

            var pageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var blockIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var placedWidgetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var pageIndex = 0; pageIndex < settings.Pages.Count; pageIndex++)
            {
                var page = settings.Pages[pageIndex];
                if (page == null)
                {
                    page = new ShowcasePageSettings();
                    settings.Pages[pageIndex] = page;
                }
                page.PageId = NormalizeUniqueId(page.PageId, pageIds);
                page.Name = string.IsNullOrWhiteSpace(page.Name)
                    ? $"Page {pageIndex + 1}"
                    : page.Name.Trim();
                page.Blocks = NormalizeBlocks(page.Blocks, blockIds);

                var singletonKinds = new HashSet<ShowcaseWidgetKind>();
                foreach (var block in page.Blocks)
                {
                    if (string.IsNullOrWhiteSpace(block.WidgetInstanceId) ||
                        !widgetIds.Contains(block.WidgetInstanceId) ||
                        placedWidgetIds.Contains(block.WidgetInstanceId))
                    {
                        block.WidgetInstanceId = null;
                        continue;
                    }

                    var widget = FindWidget(settings, block.WidgetInstanceId);
                    if (widget == null ||
                        (ShowcaseWidgetCatalog.Get(widget.Kind).SingleInstancePerPage &&
                         !singletonKinds.Add(widget.Kind)))
                    {
                        block.WidgetInstanceId = null;
                        continue;
                    }

                    placedWidgetIds.Add(widget.InstanceId);
                }
            }

            if (!settings.Pages.Any(page =>
                string.Equals(page.PageId, settings.LastSelectedPageId, StringComparison.OrdinalIgnoreCase)))
            {
                settings.LastSelectedPageId = settings.Pages[0].PageId;
            }

            settings.PinnedGameIds = (settings.PinnedGameIds ?? new List<Guid>())
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();
            settings.PinnedAchievements = NormalizeAchievementPins(settings.PinnedAchievements);
            settings.Profile = settings.Profile ?? new ShowcaseProfileSettings();
            settings.StartPageInstances = NormalizeStartPageInstances(settings.StartPageInstances);
        }

        public static bool IsValidPartition(IEnumerable<ShowcaseBlockSettings> blocks)
        {
            var cells = new bool[GridSize, GridSize];
            if (blocks == null)
            {
                return false;
            }

            foreach (var block in blocks)
            {
                if (!IsValidBlock(block))
                {
                    return false;
                }

                for (var row = block.Row; row < block.Row + block.RowSpan; row++)
                {
                    for (var column = block.Column; column < block.Column + block.ColumnSpan; column++)
                    {
                        if (cells[row, column])
                        {
                            return false;
                        }

                        cells[row, column] = true;
                    }
                }
            }

            for (var row = 0; row < GridSize; row++)
            {
                for (var column = 0; column < GridSize; column++)
                {
                    if (!cells[row, column])
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static ShowcasePageSettings CreatePage(
            ShowcasePageTemplate template,
            ShowcaseSettings settings,
            string name,
            bool showCollectionScore,
            bool showPrestigeScore)
        {
            var page = new ShowcasePageSettings
            {
                Name = MakeUniquePageName(
                    settings,
                    string.IsNullOrWhiteSpace(name) ? GetDefaultPageName(template) : name.Trim())
            };

            switch (template)
            {
                case ShowcasePageTemplate.Showcase:
                    AddBlock(settings, page, 0, 0, 1, 2, ShowcaseWidgetKind.Profile);
                    AddScoreBlock(settings, page, 0, 2, 1, 1, showCollectionScore, showPrestigeScore);
                    AddBlock(settings, page, 1, 0, 2, 2, ShowcaseWidgetKind.PinnedAchievements);
                    AddBlock(settings, page, 1, 2, 1, 1, ShowcaseWidgetKind.Statistics);
                    AddBlock(settings, page, 2, 2, 1, 1, ShowcaseWidgetKind.FavoriteGames);
                    break;
                case ShowcasePageTemplate.Analytics:
                    AddScoreBlock(settings, page, 0, 0, 1, 3, true, true);
                    AddBlock(settings, page, 1, 0, 2, 2, ShowcaseWidgetKind.NativePoints);
                    AddBlock(settings, page, 1, 2, 1, 1, ShowcaseWidgetKind.Statistics);
                    AddBlock(settings, page, 2, 2, 1, 1, ShowcaseWidgetKind.Pie);
                    break;
                case ShowcasePageTemplate.Collection:
                    AddBlock(settings, page, 0, 0, 2, 2, ShowcaseWidgetKind.PinnedAchievements);
                    AddBlock(settings, page, 0, 2, 2, 1, ShowcaseWidgetKind.FavoriteGames);
                    AddBlock(settings, page, 2, 0, 1, 3, ShowcaseWidgetKind.GameMosaic);
                    break;
                default:
                    for (var row = 0; row < GridSize; row++)
                    {
                        for (var column = 0; column < GridSize; column++)
                        {
                            page.Blocks.Add(NewBlock(row, column, 1, 1, null));
                        }
                    }

                    break;
            }

            SortBlocks(page);
            return page;
        }

        private static void AddScoreBlock(
            ShowcaseSettings settings,
            ShowcasePageSettings page,
            int row,
            int column,
            int rowSpan,
            int columnSpan,
            bool showCollectionScore,
            bool showPrestigeScore)
        {
            ShowcaseWidgetInstanceSettings widget = null;
            if (showCollectionScore || showPrestigeScore)
            {
                widget = NewWidget(ShowcaseWidgetKind.Scores);
                ShowcaseWidgetOptions.SetScoreMode(
                    widget,
                    showCollectionScore && showPrestigeScore
                        ? ShowcaseScoreMode.Dual
                        : showCollectionScore
                            ? ShowcaseScoreMode.Collection
                            : ShowcaseScoreMode.Prestige);
                settings.WidgetInstances.Add(widget);
            }

            page.Blocks.Add(NewBlock(row, column, rowSpan, columnSpan, widget?.InstanceId));
        }

        private static void AddBlock(
            ShowcaseSettings settings,
            ShowcasePageSettings page,
            int row,
            int column,
            int rowSpan,
            int columnSpan,
            ShowcaseWidgetKind kind)
        {
            var widget = NewWidget(kind);
            settings.WidgetInstances.Add(widget);
            page.Blocks.Add(NewBlock(row, column, rowSpan, columnSpan, widget.InstanceId));
        }

        private static ShowcaseWidgetInstanceSettings NewWidget(ShowcaseWidgetKind kind)
        {
            return ShowcaseWidgetSettingsFactory.CreateDefault(kind);
        }

        private static ShowcaseBlockSettings NewBlock(
            int row,
            int column,
            int rowSpan,
            int columnSpan,
            string widgetInstanceId)
        {
            return new ShowcaseBlockSettings
            {
                Row = row,
                Column = column,
                RowSpan = rowSpan,
                ColumnSpan = columnSpan,
                WidgetInstanceId = widgetInstanceId
            };
        }

        private static List<ShowcaseWidgetInstanceSettings> NormalizeWidgets(
            IEnumerable<ShowcaseWidgetInstanceSettings> widgets)
        {
            var result = new List<ShowcaseWidgetInstanceSettings>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var widget in widgets ?? Array.Empty<ShowcaseWidgetInstanceSettings>())
            {
                if (widget == null || !Enum.IsDefined(typeof(ShowcaseWidgetKind), widget.Kind))
                {
                    continue;
                }

                widget.InstanceId = NormalizeUniqueId(widget.InstanceId, ids);
                widget.Options = widget.Options != null
                    ? new Dictionary<string, string>(widget.Options, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                result.Add(widget);
            }

            return result;
        }

        private static List<ShowcaseBlockSettings> NormalizeBlocks(
            IEnumerable<ShowcaseBlockSettings> blocks,
            HashSet<string> blockIds)
        {
            var result = new List<ShowcaseBlockSettings>();
            var occupied = new bool[GridSize, GridSize];
            foreach (var block in blocks ?? Array.Empty<ShowcaseBlockSettings>())
            {
                if (!IsValidBlock(block) || Overlaps(occupied, block))
                {
                    continue;
                }

                block.BlockId = NormalizeUniqueId(block.BlockId, blockIds);
                Mark(occupied, block);
                result.Add(block);
            }

            for (var row = 0; row < GridSize; row++)
            {
                for (var column = 0; column < GridSize; column++)
                {
                    if (occupied[row, column])
                    {
                        continue;
                    }

                    var block = NewBlock(row, column, 1, 1, null);
                    block.BlockId = NormalizeUniqueId(block.BlockId, blockIds);
                    result.Add(block);
                }
            }

            result = result.OrderBy(block => block.Row).ThenBy(block => block.Column).ToList();
            return result;
        }

        private static List<PinnedAchievementReference> NormalizeAchievementPins(
            IEnumerable<PinnedAchievementReference> pins)
        {
            var result = new List<PinnedAchievementReference>();
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pin in pins ?? Array.Empty<PinnedAchievementReference>())
            {
                if (pin == null || pin.GameId == Guid.Empty || string.IsNullOrWhiteSpace(pin.ApiName))
                {
                    continue;
                }

                pin.ApiName = pin.ApiName.Trim();
                if (keys.Add(pin.Key))
                {
                    result.Add(pin);
                }
            }

            return result;
        }

        private static Dictionary<string, ShowcaseWidgetInstanceSettings> NormalizeStartPageInstances(
            IDictionary<string, ShowcaseWidgetInstanceSettings> source)
        {
            var result = new Dictionary<string, ShowcaseWidgetInstanceSettings>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in source ??
                new Dictionary<string, ShowcaseWidgetInstanceSettings>(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(pair.Key) ||
                    pair.Value == null ||
                    !Enum.IsDefined(typeof(ShowcaseWidgetKind), pair.Value.Kind))
                {
                    continue;
                }

                pair.Value.InstanceId = string.IsNullOrWhiteSpace(pair.Value.InstanceId)
                    ? NewId()
                    : pair.Value.InstanceId.Trim();
                pair.Value.Options = pair.Value.Options != null
                    ? new Dictionary<string, string>(pair.Value.Options, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                result[pair.Key.Trim()] = pair.Value;
            }

            return result;
        }

        private static bool IsValidBlock(ShowcaseBlockSettings block)
        {
            return block != null &&
                   block.Row >= 0 &&
                   block.Column >= 0 &&
                   block.RowSpan > 0 &&
                   block.ColumnSpan > 0 &&
                   block.Row + block.RowSpan <= GridSize &&
                   block.Column + block.ColumnSpan <= GridSize;
        }

        private static IReadOnlyList<ShowcaseBlockSettings> GetMergeClosureCore(
            ShowcasePageSettings page,
            ShowcaseBlockSettings first,
            ShowcaseBlockSettings second)
        {
            if (page?.Blocks == null ||
                first == null ||
                second == null ||
                ReferenceEquals(first, second) ||
                !SharesEdge(first, second))
            {
                return Array.Empty<ShowcaseBlockSettings>();
            }

            var row = Math.Min(first.Row, second.Row);
            var column = Math.Min(first.Column, second.Column);
            var rowEnd = Math.Max(
                first.Row + first.RowSpan,
                second.Row + second.RowSpan);
            var columnEnd = Math.Max(
                first.Column + first.ColumnSpan,
                second.Column + second.ColumnSpan);
            var expanded = true;
            while (expanded)
            {
                expanded = false;
                foreach (var block in page.Blocks.Where(block =>
                             Intersects(block, row, column, rowEnd, columnEnd)))
                {
                    var nextRow = Math.Min(row, block.Row);
                    var nextColumn = Math.Min(column, block.Column);
                    var nextRowEnd = Math.Max(rowEnd, block.Row + block.RowSpan);
                    var nextColumnEnd = Math.Max(columnEnd, block.Column + block.ColumnSpan);
                    if (nextRow != row ||
                        nextColumn != column ||
                        nextRowEnd != rowEnd ||
                        nextColumnEnd != columnEnd)
                    {
                        row = nextRow;
                        column = nextColumn;
                        rowEnd = nextRowEnd;
                        columnEnd = nextColumnEnd;
                        expanded = true;
                    }
                }
            }

            return page.Blocks
                .Where(block => Intersects(block, row, column, rowEnd, columnEnd))
                .OrderBy(block => block.Row)
                .ThenBy(block => block.Column)
                .ToList();
        }

        private static bool SharesEdge(
            ShowcaseBlockSettings first,
            ShowcaseBlockSettings second)
        {
            var sharesVerticalEdge =
                (first.Column + first.ColumnSpan == second.Column ||
                 second.Column + second.ColumnSpan == first.Column) &&
                ShowcaseGeometry.RangesOverlap(first.Row, first.RowSpan, second.Row, second.RowSpan);
            var sharesHorizontalEdge =
                (first.Row + first.RowSpan == second.Row ||
                 second.Row + second.RowSpan == first.Row) &&
                ShowcaseGeometry.RangesOverlap(first.Column, first.ColumnSpan, second.Column, second.ColumnSpan);
            return sharesVerticalEdge || sharesHorizontalEdge;
        }

        private static bool Intersects(
            ShowcaseBlockSettings block,
            int row,
            int column,
            int rowEnd,
            int columnEnd)
        {
            return block != null &&
                   block.Row < rowEnd &&
                   row < block.Row + block.RowSpan &&
                   block.Column < columnEnd &&
                   column < block.Column + block.ColumnSpan;
        }

        private static bool Overlaps(bool[,] occupied, ShowcaseBlockSettings block)
        {
            for (var row = block.Row; row < block.Row + block.RowSpan; row++)
            {
                for (var column = block.Column; column < block.Column + block.ColumnSpan; column++)
                {
                    if (occupied[row, column])
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void Mark(bool[,] occupied, ShowcaseBlockSettings block)
        {
            for (var row = block.Row; row < block.Row + block.RowSpan; row++)
            {
                for (var column = block.Column; column < block.Column + block.ColumnSpan; column++)
                {
                    occupied[row, column] = true;
                }
            }
        }

        private static ShowcasePageSettings FindPage(ShowcaseSettings settings, string pageId)
        {
            return settings?.Pages?.FirstOrDefault(page =>
                string.Equals(page?.PageId, pageId, StringComparison.OrdinalIgnoreCase));
        }

        private static ShowcaseBlockSettings FindBlock(ShowcasePageSettings page, string blockId)
        {
            return page?.Blocks?.FirstOrDefault(block =>
                string.Equals(block?.BlockId, blockId, StringComparison.OrdinalIgnoreCase));
        }

        private static ShowcaseWidgetInstanceSettings FindWidget(
            ShowcaseSettings settings,
            string widgetInstanceId)
        {
            return settings?.WidgetInstances?.FirstOrDefault(widget =>
                string.Equals(widget?.InstanceId, widgetInstanceId, StringComparison.OrdinalIgnoreCase));
        }

        private static void SortBlocks(ShowcasePageSettings page)
        {
            page.Blocks = page.Blocks
                .OrderBy(block => block.Row)
                .ThenBy(block => block.Column)
                .ToList();
        }

        private static string NormalizeUniqueId(string value, HashSet<string> used)
        {
            var normalized = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized) || !used.Add(normalized))
            {
                do
                {
                    normalized = NewId();
                }
                while (!used.Add(normalized));
            }

            return normalized;
        }

        private static string MakeUniquePageName(
            ShowcaseSettings settings,
            string preferred,
            string excludedPageId = null)
        {
            var baseName = string.IsNullOrWhiteSpace(preferred) ? "Showcase" : preferred.Trim();
            var names = new HashSet<string>(
                (settings?.Pages ?? new List<ShowcasePageSettings>())
                    .Where(page =>
                        page != null &&
                        !string.Equals(
                            page.PageId,
                            excludedPageId,
                            StringComparison.OrdinalIgnoreCase))
                    .Select(page => page.Name ?? string.Empty),
                StringComparer.CurrentCultureIgnoreCase);
            if (!names.Contains(baseName))
            {
                return baseName;
            }

            var suffix = 2;
            while (names.Contains($"{baseName} {suffix}"))
            {
                suffix++;
            }

            return $"{baseName} {suffix}";
        }

        private static string GetDefaultPageName(ShowcasePageTemplate template)
        {
            switch (template)
            {
                case ShowcasePageTemplate.Analytics:
                    return "Analytics";
                case ShowcasePageTemplate.Collection:
                    return "Collection";
                case ShowcasePageTemplate.Blank:
                    return "New Page";
                default:
                    return "Showcase";
            }
        }

        private sealed class WidgetPlacement
        {
            public ShowcasePageSettings TargetPage { get; set; }

            public ShowcaseBlockSettings Target { get; set; }

            public ShowcasePageSettings SourcePage { get; set; }

            public ShowcaseBlockSettings Source { get; set; }

            public ShowcaseWidgetInstanceSettings Widget { get; set; }
        }

        private static string NewId() => Guid.NewGuid().ToString("N");
    }
}
