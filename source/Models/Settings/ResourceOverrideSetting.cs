using PlayniteAchievements.Common;

namespace PlayniteAchievements.Models.Settings
{
    public class ResourceOverrideSetting : ObservableObject
    {
        private ResourceOverrideMode _mode = ResourceOverrideMode.FollowPlaynite;
        private string _customValue;

        public ResourceOverrideMode Mode
        {
            get => _mode;
            set => SetValue(ref _mode, value);
        }

        public string CustomValue
        {
            get => _customValue;
            set => SetValue(ref _customValue, value);
        }

        public ResourceOverrideSetting Clone()
        {
            return new ResourceOverrideSetting
            {
                Mode = Mode,
                CustomValue = CustomValue
            };
        }
    }
}
