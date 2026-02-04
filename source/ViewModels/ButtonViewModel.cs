using System.Windows.Input;
using PlayniteAchievements.Common;

namespace PlayniteAchievements.ViewModels
{
    public class ButtonViewModel : ObservableObject
    {
        private string _content;
        public string Content
        {
            get => _content;
            set => SetValue(ref _content, value);
        }

        private string _toolTip;
        public string ToolTip
        {
            get => _toolTip;
            set => SetValue(ref _toolTip, value);
        }

        private ICommand _command;
        public ICommand Command
        {
            get => _command;
            set => SetValue(ref _command, value);
        }

        private double _width = 110;
        public double Width
        {
            get => _width;
            set => SetValue(ref _width, value);
        }
    }
}
