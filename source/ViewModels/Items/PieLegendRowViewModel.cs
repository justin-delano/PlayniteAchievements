using System.Windows.Media;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;

namespace PlayniteAchievements.ViewModels.Items
{
    /// <summary>A pie legend row: color swatch, label, and formatted count.</summary>
    public sealed class PieLegendRowViewModel
    {
        public PieLegendRowViewModel(LegendItem item)
        {
            Label = item?.Label ?? string.Empty;
            CountText = (item?.Count ?? 0).ToString("N0", FormattingCulture.Current);
            Swatch = CreateSwatch(item?.ColorHex);
        }

        public Brush Swatch { get; }

        /// <summary>Matches the slice's series title, so a legend click can stand in for a slice click.</summary>
        public string Label { get; }

        public string CountText { get; }

        private static Brush CreateSwatch(string colorHex)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(colorHex) &&
                    new BrushConverter().ConvertFromString(colorHex) is Brush brush)
                {
                    if (brush.CanFreeze)
                    {
                        brush.Freeze();
                    }

                    return brush;
                }
            }
            catch
            {
                // Fall through to a neutral swatch when the provider color cannot be parsed.
            }

            var fallback = new SolidColorBrush(Colors.Gray);
            fallback.Freeze();
            return fallback;
        }
    }
}
