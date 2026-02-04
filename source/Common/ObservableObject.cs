using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PlayniteAchievements.Common
{
    /// <summary>
    /// Represents object implementing INotifyPropertyChanged.
    /// </summary>
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        /// <summary>
        /// If set to <c>true</c> no <see cref="PropertyChanged"/> events will be fired.
        /// </summary>
        internal bool SuppressNotifications
        {
            get; set;
        } = false;

        /// <summary>
        /// Occurs when a property value changes
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Invokes PropertyChanged events.
        /// </summary>
        /// <param name="name">Name of property that changed.</param>
        public void OnPropertyChanged([CallerMemberName] string name = null)
        {
            if (!SuppressNotifications)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }

        protected void SetValue<T>(ref T property, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(property, value))
            {
                return;
            }

            property = value;
            OnPropertyChanged(propertyName);
        }

        protected bool SetValueAndReturn<T>(ref T property, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(property, value))
            {
                return false;
            }

            property = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void SetValue<T>(ref T property, T value, params string[] propertyNames)
        {
            property = value;
            foreach (var pro in propertyNames)
            {
                OnPropertyChanged(pro);
            }
        }
    }
}
