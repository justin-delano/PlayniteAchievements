using System;
using System.Windows.Threading;

namespace PlayniteAchievements.Common
{
    public static class DispatcherExtensions
    {
        /// <summary>
        /// Invoke action on dispatcher if required, else run inline. Null-safe for dispatcher.
        /// </summary>
        public static void InvokeIfNeeded(this Dispatcher dispatcher, Action action, DispatcherPriority priority = DispatcherPriority.Background)
        {
            if (action == null)
            {
                return;
            }

            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
                return;
            }

            dispatcher.BeginInvoke(action, priority);
        }
    }
}
