using PlayniteAchievements.Common;

namespace PlayniteAchievements.Models.Settings
{
    /// <summary>
    /// Configuration for a single tag type, including its display name and enabled state.
    /// </summary>
    public class TagConfig : ObservableObject
    {
        private string _displayName;
        private bool _isEnabled = true;
        private string _previousDisplayName;

        /// <summary>
        /// The display name shown in Playnite's library.
        /// This is the actual tag name that will be applied to games.
        /// </summary>
        public string DisplayName
        {
            get => _displayName;
            set
            {
                // Track the old value before changing
                if (_displayName != value)
                {
                    _previousDisplayName = _displayName;
                    SetValue(ref _displayName, value);
                }
            }
        }

        /// <summary>
        /// The previous display name before the last change.
        /// Used for tag migration when renaming tags.
        /// </summary>
        public string PreviousDisplayName
        {
            get => _previousDisplayName;
            set => _previousDisplayName = value;
        }

        /// <summary>
        /// Whether this tag type should be synced to games.
        /// When disabled, the tag will be removed from all games.
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetValue(ref _isEnabled, value);
        }

        /// <summary>
        /// Clears the previous display name tracking.
        /// Call this after migration is complete.
        /// </summary>
        public void ClearPreviousName()
        {
            _previousDisplayName = null;
        }

        /// <summary>
        /// Creates a deep copy of this TagConfig.
        /// </summary>
        public TagConfig Clone()
        {
            return new TagConfig
            {
                DisplayName = DisplayName,
                IsEnabled = IsEnabled,
                _previousDisplayName = null // Don't copy previous name tracking
            };
        }
    }
}
