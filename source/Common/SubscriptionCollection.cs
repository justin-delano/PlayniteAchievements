using System;
using System.Collections.Generic;

namespace PlayniteAchievements.Common
{
    internal sealed class SubscriptionCollection
    {
        private readonly object _sync = new object();
        private readonly List<Action> _unsubscribeActions = new List<Action>();
        private bool _disposed;

        public void Add(Action unsubscribeAction)
        {
            if (unsubscribeAction == null)
            {
                return;
            }

            lock (_sync)
            {
                if (_disposed)
                {
                    SafeInvoke(unsubscribeAction);
                    return;
                }

                _unsubscribeActions.Add(unsubscribeAction);
            }
        }

        public void DisposeAll()
        {
            List<Action> actions;

            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                actions = new List<Action>(_unsubscribeActions);
                _unsubscribeActions.Clear();
            }

            for (var i = actions.Count - 1; i >= 0; i--)
            {
                SafeInvoke(actions[i]);
            }
        }

        private static void SafeInvoke(Action action)
        {
            try
            {
                action();
            }
            catch
            {
                // Best-effort cleanup should not throw during shutdown.
            }
        }
    }
}
