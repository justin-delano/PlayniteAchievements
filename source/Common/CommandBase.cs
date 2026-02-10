using System;
using System.Windows;

namespace PlayniteAchievements.Common
{
    public abstract class CommandBase
    {
        protected void RaiseCanExecuteChangedOnUIThread(EventHandler handler)
        {
            if (handler == null) return;

            try
            {
                var app = Application.Current;
                if (app != null && !app.Dispatcher.CheckAccess())
                {
                    app.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        handler.Invoke(this, EventArgs.Empty);
                    }));
                }
                else
                {
                    handler.Invoke(this, EventArgs.Empty);
                }
            }
            catch { /* swallow */ }
        }
    }
}
