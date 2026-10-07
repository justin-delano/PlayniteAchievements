using System.Windows.Media;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;

namespace PlayniteAchievements.ViewModels.Items
{
    /// <summary>
    /// A pie legend row: color swatch, label, and formatted count. Rows are updated in place so a
    /// data change does not regenerate the legend's row visuals.
    /// </summary>
    public sealed class PieLegendRowViewModel : ObservableObject
    {
        private Brush _swatch;
        private string _swatchHex;
        private string _label = string.Empty;
        private string _countText = string.Empty;

        public PieLegendRowViewModel(LegendItem item)
        {
            Update(item);
        }

        public Brush Swatch
        {
            get => _swatch;
            private set => SetValue(ref _swatch, value);
        }

        /// <summary>Matches the slice's series title, so a legend click can stand in for a slice click.</summary>
        public string Label
        {
            get => _label;
            private set => SetValue(ref _label, value);
        }

        public string CountText
        {
            get => _countText;
            private set => SetValue(ref _countText, value);
        }

        public void Update(LegendItem item)
        {
            Label = item?.Label ?? string.Empty;
            CountText = (item?.Count ?? 0).ToString("N0", FormattingCulture.Current);

            var colorHex = item?.ColorHex;
            if (_swatch == null || !string.Equals(_swatchHex, colorHex, System.StringComparison.OrdinalIgnoreCase))
            {
                _swatchHex = colorHex;
                Swatch = CreateSwatch(colorHex);
            }
        }

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
