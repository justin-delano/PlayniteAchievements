using PlayniteAchievements.Common;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.GameCustomData;
using PlayniteAchievements.Services.Notifications;
using PlayniteAchievements.Services.Showcase;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace PlayniteAchievements.Services.Workshop.Preview
{
    /// <summary>
    /// What a downloaded Workshop package contains, read for display without installing it. Each
    /// model owns a scratch directory holding the files it extracted; disposing the model deletes
    /// that directory.
    /// </summary>
    public abstract class WorkshopPreviewModel : IDisposable
    {
        private bool _disposed;

        /// <summary>Creates a model for a package read into <paramref name="scratchDirectory"/>.</summary>
        protected WorkshopPreviewModel(WorkshopItemKind kind, string packagePath, string scratchDirectory)
        {
            Kind = kind;
            PackagePath = packagePath;
            ScratchDirectory = scratchDirectory;
        }

        /// <summary>The Workshop item kind the package was read as.</summary>
        public WorkshopItemKind Kind { get; }

        /// <summary>The package file that was read.</summary>
        public string PackagePath { get; }

        /// <summary>The directory the model's extracted files live in; deleted on dispose.</summary>
        public string ScratchDirectory { get; }

        /// <summary>True once <see cref="Dispose"/> has run.</summary>
        public bool IsDisposed => _disposed;

        /// <summary>Deletes the scratch directory. Safe to call more than once.</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DisposeCore();
            PortablePackage.TryDeleteDirectory(ScratchDirectory);
        }

        /// <summary>Releases anything the model holds besides its scratch directory. Runs once, before the directory is deleted.</summary>
        protected virtual void DisposeCore()
        {
        }
    }

    /// <summary>A color set package (.pacolors).</summary>
    public sealed class ColorsPreviewModel : WorkshopPreviewModel
    {
        /// <summary>Creates the model for a validated color set.</summary>
        public ColorsPreviewModel(string packagePath, string scratchDirectory, ColorPackFile colors)
            : base(WorkshopItemKind.Colors, packagePath, scratchDirectory)
        {
            Colors = colors;
        }

        /// <summary>The validated color set.</summary>
        public ColorPackFile Colors { get; }
    }

    /// <summary>A notification style (.panotif) or screenshot frame style (.paframe) package.</summary>
    public sealed class NotificationStylePreviewModel : WorkshopPreviewModel
    {
        /// <summary>Creates the model for one surface of a style package.</summary>
        public NotificationStylePreviewModel(
            string packagePath,
            string scratchDirectory,
            bool isFrame,
            NotificationStylePreviewPackage package)
            : base(isFrame ? WorkshopItemKind.ScreenshotFrame : WorkshopItemKind.NotificationStyle, packagePath, scratchDirectory)
        {
            IsFrame = isFrame;
            Style = package?.Style;
            Contents = package?.Contents;
            TemplateXaml = isFrame ? package?.FrameTemplateXaml : package?.ToastTemplateXaml;
        }

        /// <summary>True for the screenshot frame surface, false for the toast.</summary>
        public bool IsFrame { get; }

        /// <summary>The style, with every bundled slot image pointing at an extracted scratch file.</summary>
        public NotificationStyleSettings Style { get; }

        /// <summary>The template XAML the package carries for this surface, or null.</summary>
        public string TemplateXaml { get; }

        /// <summary>Which optional parts the package carries.</summary>
        public NotificationStylePackageContents Contents { get; }

        /// <summary>
        /// The package's images decoded ahead of an offscreen render, keyed by the absolute path
        /// the style names them by; null until a preloader has run. A card built from this model
        /// shows these instead of loading the files asynchronously.
        /// </summary>
        public IReadOnlyDictionary<string, ImageSource> PreloadedImages { get; set; }
    }

    /// <summary>One sound of a sound pack, extracted for playback.</summary>
    public sealed class UnlockSoundPreviewSlot
    {
        /// <summary>Creates a slot for an extracted sound file.</summary>
        public UnlockSoundPreviewSlot(UnlockSoundTier tier, string filePath, long sizeBytes)
        {
            Tier = tier;
            FilePath = filePath;
            SizeBytes = sizeBytes;
        }

        /// <summary>The tier the sound plays for.</summary>
        public UnlockSoundTier Tier { get; }

        /// <summary>The absolute path of the extracted sound file.</summary>
        public string FilePath { get; }

        /// <summary>The extracted file's size in bytes.</summary>
        public long SizeBytes { get; }
    }

    /// <summary>An unlock sound pack (.pasounds).</summary>
    public sealed class UnlockSoundsPreviewModel : WorkshopPreviewModel
    {
        /// <summary>Creates the model for an extracted sound pack.</summary>
        public UnlockSoundsPreviewModel(string packagePath, string scratchDirectory, IReadOnlyList<UnlockSoundPreviewSlot> slots)
            : base(WorkshopItemKind.UnlockSounds, packagePath, scratchDirectory)
        {
            Slots = slots ?? Array.Empty<UnlockSoundPreviewSlot>();
        }

        /// <summary>The sounds the pack carries, in <see cref="UnlockSoundTier"/> order.</summary>
        public IReadOnlyList<UnlockSoundPreviewSlot> Slots { get; }
    }

    /// <summary>
    /// A bundle (.pabundle). Each part it carries is read into a child model whose scratch
    /// directory is a sub-folder of the bundle's; disposing the bundle disposes the children.
    /// </summary>
    public sealed class BundlePreviewModel : WorkshopPreviewModel
    {
        /// <summary>Creates the model for a bundle and the child models of its parts.</summary>
        public BundlePreviewModel(
            string packagePath,
            string scratchDirectory,
            BundleParts parts,
            ColorsPreviewModel colors,
            UnlockSoundsPreviewModel sounds,
            NotificationStylePreviewModel toast,
            NotificationStylePreviewModel frame)
            : base(WorkshopItemKind.Bundle, packagePath, scratchDirectory)
        {
            Parts = parts;
            Colors = colors;
            Sounds = sounds;
            Toast = toast;
            Frame = frame;
        }

        /// <summary>The parts the bundle carries.</summary>
        public BundleParts Parts { get; }

        /// <summary>The colors part, or null when the bundle has none.</summary>
        public ColorsPreviewModel Colors { get; }

        /// <summary>The sounds part, or null when the bundle has none.</summary>
        public UnlockSoundsPreviewModel Sounds { get; }

        /// <summary>The toast style part, or null when the bundle has none.</summary>
        public NotificationStylePreviewModel Toast { get; }

        /// <summary>The screenshot frame style part, or null when the bundle has none.</summary>
        public NotificationStylePreviewModel Frame { get; }

        /// <inheritdoc />
        protected override void DisposeCore()
        {
            Colors?.Dispose();
            Sounds?.Dispose();
            Toast?.Dispose();
            Frame?.Dispose();
        }
    }

    /// <summary>One block of a showcase page grid and the widget it holds.</summary>
    public sealed class ShowcaseBlockPreview
    {
        /// <summary>Creates a block preview.</summary>
        public ShowcaseBlockPreview(int row, int column, int rowSpan, int columnSpan, ShowcaseWidgetKind widgetKind, string title)
        {
            Row = row;
            Column = column;
            RowSpan = rowSpan;
            ColumnSpan = columnSpan;
            WidgetKind = widgetKind;
            Title = title;
        }

        /// <summary>The block's first grid row.</summary>
        public int Row { get; }

        /// <summary>The block's first grid column.</summary>
        public int Column { get; }

        /// <summary>How many rows the block spans.</summary>
        public int RowSpan { get; }

        /// <summary>How many columns the block spans.</summary>
        public int ColumnSpan { get; }

        /// <summary>The kind of the widget in the block.</summary>
        public ShowcaseWidgetKind WidgetKind { get; }

        /// <summary>The widget's custom title, or null when it has none.</summary>
        public string Title { get; }
    }

    /// <summary>A showcase page (.pashowcase).</summary>
    public sealed class ShowcasePagePreviewModel : WorkshopPreviewModel
    {
        private readonly ShowcasePagePortableFile _portable;

        /// <summary>Creates the model for a read showcase page; it owns the page's extracted images.</summary>
        public ShowcasePagePreviewModel(
            string packagePath,
            string scratchDirectory,
            ShowcasePagePortableFile portable,
            string pageName,
            int rows,
            int columns,
            IReadOnlyList<ShowcaseBlockPreview> blocks,
            IReadOnlyList<string> imagePaths)
            : base(WorkshopItemKind.ShowcasePage, packagePath, scratchDirectory)
        {
            _portable = portable;
            PageName = pageName;
            Rows = rows;
            Columns = columns;
            Blocks = blocks ?? Array.Empty<ShowcaseBlockPreview>();
            ImagePaths = imagePaths ?? Array.Empty<string>();
            Thumbnails = ImagePaths.Cast<object>().ToList();
        }

        /// <summary>The page's name.</summary>
        public string PageName { get; }

        /// <summary>The page's row count.</summary>
        public int Rows { get; }

        /// <summary>The page's column count.</summary>
        public int Columns { get; }

        /// <summary>The page's blocks, in the order the page lists them.</summary>
        public IReadOnlyList<ShowcaseBlockPreview> Blocks { get; }

        /// <summary>The absolute paths of the images the page bundles, extracted to a temporary folder.</summary>
        public IReadOnlyList<string> ImagePaths { get; }

        /// <summary>
        /// What the preview shows for each of <see cref="ImagePaths"/>, in the same order: the
        /// path, or a decoded image once a preloader has swapped one in for an offscreen render.
        /// </summary>
        public IReadOnlyList<object> Thumbnails { get; set; }

        /// <inheritdoc />
        protected override void DisposeCore()
        {
            ShowcasePagePortableStore.DeleteExtractedImages(_portable);
        }
    }

    /// <summary>A game data package (.pa) and how installing it would change the compared game.</summary>
    public sealed class GameCustomDataPreviewModel : WorkshopPreviewModel
    {
        /// <summary>Creates the model for a read game data package.</summary>
        public GameCustomDataPreviewModel(
            string packagePath,
            string scratchDirectory,
            GameCustomDataPortablePackage package,
            GameCustomDataPreviewDiff diff)
            : base(WorkshopItemKind.GameCustomData, packagePath, scratchDirectory)
        {
            Package = package;
            Diff = diff;
        }

        /// <summary>The package as an import would read it, images extracted to the scratch directory.</summary>
        public GameCustomDataPortablePackage Package { get; }

        /// <summary>The achievement rows the install would change.</summary>
        public GameCustomDataPreviewDiff Diff { get; }
    }
}
