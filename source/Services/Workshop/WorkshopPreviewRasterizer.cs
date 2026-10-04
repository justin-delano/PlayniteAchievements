using Playnite.SDK;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.UI;
using PlayniteAchievements.Services.Workshop.Preview;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.Views.Workshop.Preview;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PlayniteAchievements.Services.Workshop
{
    /// <summary>
    /// Renders the standardized preview.png of a Workshop package from its preview model: the
    /// kind's preview control (or, for a screenshot frame, the frame over a neutral backdrop)
    /// in a fixed dark theme at fixed sizes, so every item's image looks alike whatever theme
    /// and settings the sharer runs. A bundle renders its first present part of toast, frame,
    /// colors and sounds. The PNG stays under the Workshop's 5 MB limit.
    /// </summary>
    internal sealed class WorkshopPreviewRasterizer
    {
        /// <summary>The file name the image is written under.</summary>
        public const string FileName = "preview.png";

        /// <summary>The Workshop's upload limit for a preview image.</summary>
        public const long MaxFileBytes = 5L * 1024 * 1024;

        /// <summary>How many game data rows the image lists before its "and N more" line.</summary>
        public const int GameDataMaxRows = 20;

        private const string SampleKind = "rare";
        private const string IconFontKey = "PlayAch.FontFamily.Icon";
        private const string PlayniteIconFontKey = "FontIcoFont";
        private const string WindowBackgroundKey = "PlayAch.Brush.Window.Background";
        private const string TextKey = "PlayAch.Brush.Text";
        private const string BodyFontKey = "PlayAch.FontFamily.Body";
        private const string BodySizeKey = "PlayAch.FontSize.Body";

        private const double HostPadding = 24;
        private const int CardCanvasWidth = 960;
        private const int CardCanvasHeight = 540;
        private const int FrameWidth = 1280;
        private const int FrameHeight = 720;
        private const int PanelWidth = 960;
        private const int PanelMaxHeight = 1080;
        private const int GameDataWidth = 1200;
        private const int GameDataMaxHeight = 2160;

        private static readonly Uri NeutralThemeUri = new Uri(
            "pack://application:,,,/PlayniteAchievements;component/Resources/WorkshopPreviewNeutralTheme.xaml",
            UriKind.Absolute);

        // Loaded once, on the UI thread, and merged into each render's own host dictionary.
        private static ResourceDictionary _neutralTheme;

        private readonly PlayniteAchievementsPlugin _plugin;
        private readonly ILogger _logger;

        /// <param name="plugin">Supplies the image service and the custom templates folder; may be null.</param>
        /// <param name="logger">Receives render failures; may be null.</param>
        public WorkshopPreviewRasterizer(PlayniteAchievementsPlugin plugin, ILogger logger = null)
        {
            _plugin = plugin;
            _logger = logger;
        }

        /// <summary>
        /// One render: the element laid out and drawn through a VisualBrush, or for the frame a
        /// composer that draws itself; plus the image loads to await before drawing.
        /// </summary>
        private sealed class Surface
        {
            public FrameworkElement Host { get; set; }

            public int Width { get; set; }

            /// <summary>A fixed canvas height, or null for the content's height up to <see cref="MaxHeight"/>.</summary>
            public int? Height { get; set; }

            public int MaxHeight { get; set; }

            public Func<Task> PrepareAsync { get; set; }

            /// <summary>Runs once the host is laid out; the host is laid out again after it.</summary>
            public Action AfterLayout { get; set; }

            public Func<BitmapSource> Compose { get; set; }
        }

        /// <summary>
        /// Renders <paramref name="model"/> to <c>preview.png</c> under <paramref name="directory"/>
        /// now, with whatever images are already decoded (no preloading, no waiting for the
        /// achievement icon). UI thread. Returns the path, or null when the kind has nothing to
        /// render or rendering failed (logged).
        /// </summary>
        public string TryRender(WorkshopPreviewModel model, string directory)
        {
            try
            {
                var surface = BuildSurface(model);
                var bitmap = surface == null ? null : RenderSurface(surface);
                return bitmap == null ? null : Save(bitmap, directory);
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, $"Rendering the Workshop preview image for {model?.Kind} failed.");
                return null;
            }
        }

        /// <summary>
        /// Renders <paramref name="model"/> to <c>preview.png</c> under <paramref name="directory"/>:
        /// decodes the images the model displays off the UI thread and swaps them into it, then
        /// builds and draws on the application's dispatcher, waiting for the achievement icon, and
        /// encodes off it. Returns the path, or null when the kind has nothing to render. Throws
        /// when rendering fails. The model must not be bound elsewhere while this runs.
        /// </summary>
        public async Task<string> RenderAsync(WorkshopPreviewModel model, string directory, CancellationToken cancel)
        {
            if (model == null)
            {
                return null;
            }

            var preloader = new WorkshopPreviewImagePreloader(_plugin?.ImageService ?? PlayniteAchievementsPlugin.Instance?.ImageService);
            await preloader.PreloadAsync(RenderedPart(model), GameDataMaxRows, cancel);
            cancel.ThrowIfCancellationRequested();

            var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            var bitmap = dispatcher.CheckAccess()
                ? await RenderOnDispatcherAsync(model, cancel)
                : await dispatcher.InvokeAsync(() => RenderOnDispatcherAsync(model, cancel)).Task.Unwrap();
            if (bitmap == null)
            {
                return null;
            }

            return await Task.Run(() => Save(bitmap, directory), cancel);
        }

        private async Task<BitmapSource> RenderOnDispatcherAsync(WorkshopPreviewModel model, CancellationToken cancel)
        {
            var surface = BuildSurface(model);
            if (surface == null)
            {
                return null;
            }

            if (surface.PrepareAsync != null)
            {
                await surface.PrepareAsync();
            }

            cancel.ThrowIfCancellationRequested();
            return RenderSurface(surface);
        }

        /// <summary>The part of <paramref name="model"/> the image shows: a bundle's first present part of toast, frame, colors and sounds.</summary>
        internal static WorkshopPreviewModel RenderedPart(WorkshopPreviewModel model)
        {
            if (model is BundlePreviewModel bundle)
            {
                return (WorkshopPreviewModel)bundle.Toast ?? (WorkshopPreviewModel)bundle.Frame ?? (WorkshopPreviewModel)bundle.Colors ?? bundle.Sounds;
            }

            return model;
        }

        private Surface BuildSurface(WorkshopPreviewModel model)
        {
            switch (RenderedPart(model))
            {
                case NotificationStylePreviewModel style when style.IsFrame:
                    return BuildFrameSurface(style);
                case NotificationStylePreviewModel style:
                    return BuildCardSurface(style);
                case ColorsPreviewModel colors:
                    return BuildPanelSurface(new ColorsPreviewControl { DataContext = colors }, PanelWidth, PanelMaxHeight);
                case UnlockSoundsPreviewModel sounds:
                    var soundsControl = new SoundsPreviewControl { Plugin = _plugin, NeutralRender = true };
                    soundsControl.DataContext = sounds;
                    return BuildPanelSurface(soundsControl, PanelWidth, PanelMaxHeight);
                case ShowcasePagePreviewModel showcase:
                    return BuildPanelSurface(new ShowcasePreviewControl { DataContext = showcase }, PanelWidth, PanelMaxHeight);
                case GameCustomDataPreviewModel gameData:
                    return BuildGameDataSurface(gameData);
                default:
                    return null;
            }
        }

        private static Surface BuildPanelSurface(FrameworkElement control, int width, int maxHeight)
        {
            return new Surface
            {
                Host = CreateHost(control),
                Width = width,
                MaxHeight = maxHeight
            };
        }

        // The game data preview in neutral mode. Compared against a game it shows the achievement
        // grid, whose icon cells load asynchronously and so never on an offscreen tree: their
        // sources are decoded ahead and put into the laid-out cells.
        private Surface BuildGameDataSurface(GameCustomDataPreviewModel gameData)
        {
            var control = new GameDataPreviewControl { MaxRows = GameDataMaxRows, NeutralRender = true };
            control.DataContext = gameData;
            var surface = BuildPanelSurface(control, GameDataWidth, GameDataMaxHeight);
            if (gameData.Diff?.AfterData == null)
            {
                return surface;
            }

            var preloader = new WorkshopPreviewImagePreloader(_plugin?.ImageService ?? PlayniteAchievementsPlugin.Instance?.ImageService);
            IReadOnlyDictionary<string, ImageSource> icons = null;
            surface.PrepareAsync = async () => icons = await preloader.DecodeAllAsync(
                control.DisplayedIconUris,
                uri => GameDataPreviewControl.GridIconDecodePixel,
                CancellationToken.None);
            surface.AfterLayout = () =>
            {
                control.FixStarColumnWidths(GameDataWidth - 2 * HostPadding);
                control.ApplyPreloadedIcons(icons);
            };
            return surface;
        }

        // The notification card as the preview control builds it in neutral mode, centered on a
        // fixed canvas and scaled down only when a wide card would not fit.
        private Surface BuildCardSurface(NotificationStylePreviewModel style)
        {
            var control = new NotificationStylePreviewControl
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (_plugin != null)
            {
                control.Plugin = _plugin;
            }

            control.NeutralRender = true;
            control.DataContext = style;

            var fit = new Viewbox
            {
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                Child = control
            };

            return new Surface
            {
                Host = CreateHost(fit),
                Width = CardCanvasWidth,
                Height = CardCanvasHeight,
                MaxHeight = CardCanvasHeight,
                PrepareAsync = control.PrepareImagesAsync
            };
        }

        // The frame over the gradient that stands in for a screenshot, through the compositor
        // that frames real unlock screenshots, with the package's frame template or the bundled one.
        private Surface BuildFrameSurface(NotificationStylePreviewModel style)
        {
            var resolver = CreateResolver();
            DataTemplate template = null;
            if (!string.IsNullOrWhiteSpace(style.TemplateXaml))
            {
                if (resolver.TryLoadTemplateFromXaml(style.TemplateXaml, isFrame: true, out var loaded, out var error) && loaded != null)
                {
                    template = loaded;
                }
                else
                {
                    _logger?.Warn($"The package's frame template did not load; the preview image uses the bundled one. {error}");
                }
            }

            template = template ?? resolver.ResolveBundledDefaultTemplate(isFrame: true);
            var viewModel = new AchievementToastViewModel(
                ToastPreviewFactory.BuildPreviewArgs(SampleKind),
                new PersistedSettings(),
                style.Style,
                gameCustomDataStore: null,
                toastUseThemeStylingOverride: false,
                frameUseThemeStylingOverride: false)
            {
                PreloadedImages = style.PreloadedImages
            };

            return new Surface
            {
                Width = FrameWidth,
                Height = FrameHeight,
                MaxHeight = FrameHeight,
                PrepareAsync = viewModel.PrepareImagesAsync,
                Compose = () => template == null
                    ? null
                    : new ScreenshotFrameCompositor(_logger).ComposeFramed(CreateBackdrop(), template, viewModel, CreateThemeResources())
            };
        }

        private static BitmapSource CreateBackdrop()
        {
            var backdrop = new RenderTargetBitmap(FrameWidth, FrameHeight, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                var brush = new LinearGradientBrush(
                    Color.FromRgb(0x2B, 0x33, 0x45),
                    Color.FromRgb(0x10, 0x14, 0x1C),
                    new Point(0, 0),
                    new Point(1, 1));
                context.DrawRectangle(brush, null, new Rect(0, 0, FrameWidth, FrameHeight));
            }

            backdrop.Render(visual);
            backdrop.Freeze();
            return backdrop;
        }

        /// <summary>The render host: the neutral theme, its window background, padding, and its text defaults.</summary>
        private static Border CreateHost(UIElement child)
        {
            var resources = CreateThemeResources();
            var host = new Border
            {
                Resources = resources,
                Background = resources[WindowBackgroundKey] as Brush,
                Padding = new Thickness(HostPadding),
                UseLayoutRounding = true,
                SnapsToDevicePixels = true,
                Child = child
            };

            if (resources[TextKey] is Brush text)
            {
                TextElement.SetForeground(host, text);
            }

            if (resources[BodyFontKey] is FontFamily family)
            {
                TextElement.SetFontFamily(host, family);
            }

            if (resources[BodySizeKey] is double size)
            {
                TextElement.SetFontSize(host, size);
            }

            return host;
        }

        /// <summary>
        /// A dictionary of one render's own: the shared neutral theme merged in, plus Playnite's
        /// icon font copied from the application (it lives in Playnite's assemblies, so it is
        /// read from the running application instead of being named in the theme file).
        /// </summary>
        private static ResourceDictionary CreateThemeResources()
        {
            if (_neutralTheme == null)
            {
                _neutralTheme = new ResourceDictionary { Source = NeutralThemeUri };
            }

            var resources = new ResourceDictionary();
            resources.MergedDictionaries.Add(_neutralTheme);
            if (Application.Current?.TryFindResource(PlayniteIconFontKey) is FontFamily iconFont)
            {
                resources[IconFontKey] = iconFont;
                resources[PlayniteIconFontKey] = iconFont;
            }

            // The rarity, trophy and capstone badges at their default look, so a sharer's
            // customized badges do not reach the published image.
            RarityAppearanceHelper.ApplyBadgeResources(resources, new PersistedSettings());

            return resources;
        }

        private static BitmapSource RenderSurface(Surface surface)
        {
            if (surface.Compose != null)
            {
                return surface.Compose();
            }

            var host = surface.Host;
            host.Width = surface.Width;
            if (surface.Height.HasValue)
            {
                host.Height = surface.Height.Value;
            }

            double LayOut()
            {
                host.Measure(new Size(surface.Width, surface.Height ?? double.PositiveInfinity));
                var height = surface.Height ?? Math.Max(1, host.DesiredSize.Height);
                host.Arrange(new Rect(0, 0, surface.Width, height));
                host.UpdateLayout();
                return height;
            }

            var laidOutHeight = LayOut();
            if (surface.AfterLayout != null)
            {
                surface.AfterLayout();
                host.InvalidateMeasure();
                LayOut();
                laidOutHeight = LayOut();
            }

            var height = (int)Math.Min(surface.MaxHeight, Math.Ceiling(laidOutHeight));
            return Rasterize(host, (host as Border)?.Background, surface.Width, Math.Max(1, height));
        }

        /// <summary>
        /// Draws <paramref name="visual"/> at 96 DPI through a VisualBrush mapped 1:1 onto the
        /// bitmap, over <paramref name="background"/>, cropped to the given size.
        /// </summary>
        private static BitmapSource Rasterize(Visual visual, Brush background, int width, int height)
        {
            var bounds = new Rect(0, 0, width, height);
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen())
            {
                if (background != null)
                {
                    context.DrawRectangle(background, null, bounds);
                }

                context.DrawRectangle(
                    new VisualBrush(visual)
                    {
                        Stretch = Stretch.Fill,
                        ViewboxUnits = BrushMappingMode.Absolute,
                        Viewbox = bounds,
                        ViewportUnits = BrushMappingMode.Absolute,
                        Viewport = bounds
                    },
                    null,
                    bounds);
            }

            var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            target.Render(drawing);
            target.Freeze();
            return target;
        }

        /// <summary>
        /// Encodes <paramref name="bitmap"/> as PNG into <paramref name="directory"/>, scaled down
        /// until the file is under <see cref="MaxFileBytes"/>. Thread-agnostic for a frozen bitmap.
        /// </summary>
        private static string Save(BitmapSource bitmap, string directory)
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, FileName);
            var scale = 1.0;
            var bytes = Encode(bitmap);
            for (var attempt = 0; bytes.Length > MaxFileBytes && attempt < 6; attempt++)
            {
                scale *= Math.Sqrt((double)MaxFileBytes / bytes.Length) * 0.9;
                var scaled = new TransformedBitmap(bitmap, new ScaleTransform(scale, scale));
                scaled.Freeze();
                bytes = Encode(scaled);
            }

            File.WriteAllBytes(path, bytes);
            return path;
        }

        private static byte[] Encode(BitmapSource bitmap)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = new MemoryStream())
            {
                encoder.Save(stream);
                return stream.ToArray();
            }
        }

        private AchievementToastTemplateResolver CreateResolver()
        {
            var plugin = _plugin ?? PlayniteAchievementsPlugin.Instance;
            return new AchievementToastTemplateResolver(
                plugin?.PlayniteApi,
                _logger,
                customTemplatesDirectory: plugin == null
                    ? null
                    : AchievementToastTemplateResolver.GetCustomTemplatesDirectory(plugin.GetPluginUserDataPath()));
        }
    }
}
