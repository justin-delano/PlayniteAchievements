using Playnite.SDK;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.UI;
using PlayniteAchievements.ViewModels;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PlayniteAchievements.Services.Workshop
{
    /// <summary>
    /// Renders a preview image for the share dialog where the plugin already knows how to draw
    /// the thing: the notification toast and the screenshot frame, through the same template and
    /// view model path the settings mockups use. Best effort and UI-thread only; returns null
    /// when a kind has no cheap render (showcase pages, sounds, game data), and the dialog falls
    /// back to a file picker.
    /// </summary>
    internal sealed class WorkshopPreviewRenderer
    {
        private const string SampleKind = "rare";
        private const int FrameWidth = 1280;
        private const int FrameHeight = 720;

        private readonly PlayniteAchievementsPlugin _plugin;
        private readonly ILogger _logger;

        public WorkshopPreviewRenderer(PlayniteAchievementsPlugin plugin, ILogger logger = null)
        {
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            _logger = logger;
        }

        /// <summary>A PNG under <paramref name="directory"/>, or null when this kind is not rendered.</summary>
        /// <param name="kind">What is being shared.</param>
        /// <param name="directory">Where the PNG goes.</param>
        /// <param name="stylePackagePath">A .panotif or .paframe whose style the preview should
        /// show instead of the live one: a saved preset being shared, or the notification part of
        /// a composed theme.</param>
        public string TryRender(WorkshopItemKind kind, string directory, string stylePackagePath = null)
        {
            try
            {
                var persisted = _plugin.Settings?.Persisted;
                if (persisted == null)
                {
                    return null;
                }

                if (!string.IsNullOrWhiteSpace(stylePackagePath)
                    && (kind == WorkshopItemKind.Bundle || kind == WorkshopItemKind.NotificationStyle || kind == WorkshopItemKind.ScreenshotFrame))
                {
                    var composed = persisted.Clone();
                    composed.NotificationStyle = _plugin.NotificationStylePortableStore.ReadStyle(stylePackagePath);
                    persisted = composed;
                }

                BitmapSource bitmap;
                switch (kind)
                {
                    case WorkshopItemKind.NotificationStyle:
                    case WorkshopItemKind.Bundle:
                        bitmap = RenderToast(persisted);
                        break;
                    case WorkshopItemKind.ScreenshotFrame:
                        bitmap = RenderFrame(persisted);
                        break;
                    default:
                        return null;
                }

                if (bitmap == null)
                {
                    return null;
                }

                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "preview-" + kind.ToString().ToLowerInvariant() + ".png");
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(path))
                {
                    encoder.Save(stream);
                }

                return path;
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, $"Workshop preview render for {kind} failed; the share dialog falls back to a file picker.");
                return null;
            }
        }

        private BitmapSource RenderToast(PersistedSettings persisted)
        {
            var resolver = CreateResolver();
            var viewModel = new AchievementToastViewModel(
                ToastPreviewFactory.BuildPreviewArgs(SampleKind),
                persisted,
                persisted.NotificationStyle,
                gameCustomDataStore: null,
                toastUseThemeStylingOverride: persisted.ToastUseThemeStyling,
                frameUseThemeStylingOverride: persisted.FrameUseThemeStyling);
            var items = new[] { viewModel };
            var template = ToastSurfaceFactory.ResolveToastTemplate(resolver, items, persisted.ToastUseThemeStyling, null, Guid.Empty);
            var surface = ToastSurfaceFactory.BuildToastSurface(items, template);
            return RenderElement(surface);
        }

        private BitmapSource RenderFrame(PersistedSettings persisted)
        {
            var resolver = CreateResolver();
            var template = resolver.ResolveFrameTemplate(persisted.FrameUseThemeStyling, null, Guid.Empty);
            if (template == null)
            {
                return null;
            }

            var viewModel = new AchievementToastViewModel(
                ToastPreviewFactory.BuildPreviewArgs(SampleKind),
                persisted,
                persisted.NotificationStyle,
                gameCustomDataStore: null,
                toastUseThemeStylingOverride: persisted.ToastUseThemeStyling,
                frameUseThemeStylingOverride: persisted.FrameUseThemeStyling);

            // A neutral gradient stands in for the screenshot the frame would surround.
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

            return new ScreenshotFrameCompositor(_logger).ComposeFramed(backdrop, template, viewModel);
        }

        /// <summary>Lays an element out at its natural size and rasterizes it at 96 DPI.</summary>
        private static BitmapSource RenderElement(FrameworkElement element)
        {
            if (element == null)
            {
                return null;
            }

            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = element.DesiredSize;
            if (size.Width < 1 || size.Height < 1)
            {
                return null;
            }

            element.Arrange(new Rect(size));
            element.UpdateLayout();

            var target = new RenderTargetBitmap(
                (int)Math.Ceiling(size.Width),
                (int)Math.Ceiling(size.Height),
                96,
                96,
                PixelFormats.Pbgra32);
            target.Render(element);
            target.Freeze();
            return target;
        }

        private AchievementToastTemplateResolver CreateResolver()
        {
            return new AchievementToastTemplateResolver(
                _plugin.PlayniteApi,
                _logger,
                customTemplatesDirectory: AchievementToastTemplateResolver.GetCustomTemplatesDirectory(
                    _plugin.GetPluginUserDataPath()));
        }
    }
}
