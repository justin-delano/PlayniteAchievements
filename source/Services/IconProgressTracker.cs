namespace PlayniteAchievements.Services
{
    /// <summary>
    /// Thread-safe tracker for global icon download progress across concurrent downloads.
    /// </summary>
    internal sealed class IconProgressTracker
    {
        private int _totalIcons;
        private int _iconsDownloaded;
        private readonly object _lock = new object();

        public void IncrementTotal(int count)
        {
            lock (_lock) _totalIcons += count;
        }

        public void IncrementDownloaded()
        {
            lock (_lock) _iconsDownloaded++;
        }

        public (int Downloaded, int Total) GetSnapshot()
        {
            lock (_lock) return (_iconsDownloaded, _totalIcons);
        }

        public void Reset()
        {
            lock (_lock)
            {
                _totalIcons = 0;
                _iconsDownloaded = 0;
            }
        }
    }
}
