using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PlayniteAchievements.Views.Helpers
{
    /// <summary>
    /// Builds the plugin-logo icon used by the sidebar and top panel. The geometry comes from
    /// LogoResources.xaml (PlayAch.Geometry.Logo, the monochrome silhouette) and the fill binds
    /// to the host control's Foreground, so Playnite's theme brushes — including hover and
    /// selection states — color it the same way they color a font glyph.
    /// </summary>
    internal static class PluginLogo
    {
        private const string ResourceUri =
            "pack://application:,,,/PlayniteAchievements;component/Resources/LogoResources.xaml";

        private static Geometry _geometry;

        public static Path CreateIcon(double size)
        {
            var icon = new Path
            {
                Data = GetGeometry(),
                Stretch = Stretch.Uniform,
                Width = size,
                Height = size
            };

            icon.SetBinding(Shape.FillProperty, new Binding("Foreground")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Control), 1)
            });

            return icon;
        }

        private static Geometry GetGeometry()
        {
            if (_geometry == null)
            {
                var dictionary = new ResourceDictionary { Source = new Uri(ResourceUri) };
                var geometry = dictionary["PlayAch.Geometry.Logo"] as Geometry;
                if (geometry != null && geometry.CanFreeze)
                {
                    geometry.Freeze();
                }

                _geometry = geometry;
            }

            return _geometry;
        }
    }
}
