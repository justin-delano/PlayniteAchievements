using PlayniteAchievements.Common;

namespace PlayniteAchievements.Models.PieChart
{
    public class LegendItem : ObservableObject
    {
        private string _label;
        public string Label
        {
            get => _label;
            set => SetValue(ref _label, value);
        }

        private int _count;
        public int Count
        {
            get => _count;
            set => SetValue(ref _count, value);
        }

        private string _iconKey;
        public string IconKey
        {
            get => _iconKey;
            set => SetValue(ref _iconKey, value);
        }

        private string _colorHex;
        public string ColorHex
        {
            get => _colorHex;
            set => SetValue(ref _colorHex, value);
        }
    }
}
