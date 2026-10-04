using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace PlayniteAchievements.Views.Controls
{
    /// <summary>
    /// One icon-font glyph drawn as filled vector geometry taken from the font's outlines, in a
    /// square box of <see cref="Size"/> device-independent pixels. Text rendering hints a 16px
    /// glyph onto the pixel grid, which keeps edges hard but mangles the filled shapes IcoFont is
    /// drawn with; the outline path keeps the shape and reads smoother at 18px and above. When the
    /// font has no glyph for the character (or no outline access), the glyph falls back to text so
    /// nothing renders blank.
    /// </summary>
    public sealed class GlyphIcon : FrameworkElement
    {
        private const double FallbackSize = 20;

        public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
            nameof(Glyph), typeof(string), typeof(GlyphIcon),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnGeometryInputChanged));

        public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
            nameof(Size), typeof(double), typeof(GlyphIcon),
            new FrameworkPropertyMetadata(FallbackSize, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnGeometryInputChanged));

        public static readonly DependencyProperty FontFamilyProperty = DependencyProperty.Register(
            nameof(FontFamily), typeof(FontFamily), typeof(GlyphIcon),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnGeometryInputChanged));

        public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
            nameof(Foreground), typeof(Brush), typeof(GlyphIcon),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Dictionary<string, Geometry> GeometryCache = new Dictionary<string, Geometry>(StringComparer.Ordinal);

        private Geometry _geometry;
        private bool _geometryResolved;

        public GlyphIcon()
        {
            // The first paint can land before the font behind a pack URI answers outline
            // queries, and nothing else invalidates a header glyph afterwards; paint again once
            // the element is loaded and whenever it comes back into view.
            Loaded += (sender, args) => InvalidateVisual();
            IsVisibleChanged += (sender, args) =>
            {
                if (args.NewValue is bool visible && visible)
                {
                    InvalidateVisual();
                }
            };
        }

        /// <summary>The character to draw, as the same string a TextBlock would show.</summary>
        public string Glyph
        {
            get => (string)GetValue(GlyphProperty);
            set => SetValue(GlyphProperty, value);
        }

        /// <summary>Em size and box edge, in device-independent pixels.</summary>
        public double Size
        {
            get => (double)GetValue(SizeProperty);
            set => SetValue(SizeProperty, value);
        }

        public FontFamily FontFamily
        {
            get => (FontFamily)GetValue(FontFamilyProperty);
            set => SetValue(FontFamilyProperty, value);
        }

        public Brush Foreground
        {
            get => (Brush)GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }

        private static void OnGeometryInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is GlyphIcon icon)
            {
                icon._geometry = null;
                icon._geometryResolved = false;
            }
        }

        protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize)
        {
            var size = Math.Max(0, Size);
            return new System.Windows.Size(size, size);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var glyph = Glyph;
            var size = Size;
            var brush = Foreground;
            if (string.IsNullOrEmpty(glyph) || size <= 0 || brush == null)
            {
                return;
            }

            if (!_geometryResolved)
            {
                _geometry = ResolveGeometry(FontFamily, glyph[0], size);
                // Only a successful resolution is final; a failed one is asked again on the next
                // paint, since the fallback text below is what shows until then.
                _geometryResolved = _geometry != null;
            }

            if (_geometry != null)
            {
                drawingContext.DrawGeometry(brush, null, _geometry);
                return;
            }

            // No outline access for this font: draw the character as text in the same box.
            var typeface = new Typeface(FontFamily ?? new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var text = new FormattedText(
                glyph, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, size, brush, 1.0);
            drawingContext.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2));
        }

        /// <summary>
        /// The glyph's outline at <paramref name="size"/>, translated so it sits centered in a
        /// size-by-size box with its baseline where the font puts it. Cached per font, character
        /// and size, since the same few icons repeat across every settings row.
        /// </summary>
        private static Geometry ResolveGeometry(FontFamily family, char character, double size)
        {
            if (family == null)
            {
                return null;
            }

            var key = family.Source + "|" + ((int)character).ToString("X4") + "|" + size.ToString(CultureInfo.InvariantCulture);
            lock (GeometryCache)
            {
                if (GeometryCache.TryGetValue(key, out var cached))
                {
                    return cached;
                }
            }

            var typeface = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            if (!TryBuildGeometry(typeface, character, size, out var geometry))
            {
                return null;
            }

            lock (GeometryCache)
            {
                GeometryCache[key] = geometry;
            }

            return geometry;
        }

        /// <summary>
        /// Builds the filled outline of <paramref name="character"/> in <paramref name="typeface"/>
        /// at <paramref name="size"/>, positioned inside a size-by-size box: horizontally centered
        /// on the advance width, baseline at the font's baseline. False when the typeface exposes
        /// no outlines or lacks the character.
        /// </summary>
        public static bool TryBuildGeometry(Typeface typeface, char character, double size, out Geometry geometry)
        {
            geometry = null;
            if (typeface == null || size <= 0 || !typeface.TryGetGlyphTypeface(out var glyphTypeface) || glyphTypeface == null)
            {
                return false;
            }

            if (!glyphTypeface.CharacterToGlyphMap.TryGetValue(character, out var glyphIndex))
            {
                return false;
            }

            var outline = glyphTypeface.GetGlyphOutline(glyphIndex, size, size);
            if (outline == null || outline.IsEmpty())
            {
                return false;
            }

            var advance = glyphTypeface.AdvanceWidths[glyphIndex] * size;
            var offsetX = (size - advance) / 2;
            var baseline = glyphTypeface.Baseline * size;
            // Bring the glyph box (ascent above the baseline, descent below) to the element's box.
            var fontHeight = glyphTypeface.Height * size;
            var offsetY = baseline + (size - fontHeight) / 2;

            var positioned = outline.Clone();
            positioned.Transform = new TranslateTransform(offsetX, offsetY);
            positioned.Freeze();
            geometry = positioned;
            return true;
        }
    }
}
