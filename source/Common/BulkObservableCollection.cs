using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace PlayniteAchievements.Common
{
    /// <summary>
    /// ObservableCollection with a ReplaceAll method that updates the collection using a single Reset notification.
    /// This is much faster than per-item moves/inserts for large lists.
    /// </summary>
    public class BulkObservableCollection<T> : ObservableCollection<T>
    {
        private bool _suppressNotifications;

        public void ReplaceAll(IEnumerable<T> items)
        {
            var newItems = (items ?? Enumerable.Empty<T>()).ToList();

            try
            {
                _suppressNotifications = true;
                Items.Clear();
                foreach (var item in newItems)
                {
                    Items.Add(item);
                }
            }
            finally
            {
                _suppressNotifications = false;
            }

            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (_suppressNotifications)
            {
                return;
            }

            base.OnCollectionChanged(e);
        }

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            if (_suppressNotifications)
            {
                return;
            }

            base.OnPropertyChanged(e);
        }
    }
}
