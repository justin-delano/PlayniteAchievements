using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using LiveCharts;
using LiveCharts.Wpf;
using PlayniteAchievements.Views.Helpers;

namespace PlayniteAchievements.Views.Controls
{
    /// <summary>
    /// Custom tooltip for pie chart slices that displays the provider/badge icon and count.
    /// </summary>
    public partial class PieChartTooltip : UserControl, IChartTooltip
    {
        public PieChartTooltip()
        {
            InitializeComponent();
            DataContext = this;
            Focusable = false;
            IsHitTestVisible = false;
            IsTabStop = false;
            ClickThroughPopupHost.Attach(this);
        }

        public TooltipSelectionMode? SelectionMode { get; set; } = TooltipSelectionMode.OnlySender;

        private TooltipData _data;
        public TooltipData Data
        {
            get => _data;
            set
            {
                _data = value;
                OnPropertyChanged();
                Points = value?.Points;
            }
        }

        private System.Collections.Generic.IEnumerable<DataPointViewModel> _points;

        /// <summary>
        /// The rows shown: the points LiveCharts hands over in <see cref="Data"/>, or the ones a
        /// host sets directly when it shows the tooltip itself (TooltipData cannot be built
        /// outside LiveCharts).
        /// </summary>
        public System.Collections.Generic.IEnumerable<DataPointViewModel> Points
        {
            get => _points;
            set
            {
                _points = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
