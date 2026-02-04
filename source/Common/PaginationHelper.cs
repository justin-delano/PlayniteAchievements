using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;

namespace PlayniteAchievements.Common
{
    public sealed class PaginationPropertyNames
    {
        public string CurrentPage { get; set; } = nameof(PaginationManager<object>.CurrentPage);
        public string TotalPages { get; set; } = nameof(PaginationManager<object>.TotalPages);
        public string CanGoNext { get; set; } = nameof(PaginationManager<object>.CanGoNext);
        public string CanGoPrevious { get; set; } = nameof(PaginationManager<object>.CanGoPrevious);
        public string HasMultiplePages { get; set; } = nameof(PaginationManager<object>.HasMultiplePages);
        public string TotalItems { get; set; } = nameof(PaginationManager<object>.TotalItems);
    }

    /// <summary>
    /// Reusable pagination manager for ObservableCollections.
    /// Provides consistent pagination behavior across multiple views.
    /// </summary>
    /// <typeparam name="T">The type of items in the paginated collection.</typeparam>
    public class PaginationManager<T> where T : class
    {
        private readonly int _pageSize;
        private readonly Action<string> _onPropertyChanged;
        private readonly Action _onPaginationChanged;
        private readonly ObservableCollection<T> _targetCollection;
        private readonly bool _useDispatcherInvoke;
        private readonly PaginationPropertyNames _names;

        private List<T> _allItems = new List<T>();
        private int _currentPage = 1;

        /// <summary>
        /// Initialize a new PaginationManager.
        /// </summary>
        /// <param name="pageSize">Number of items per page.</param>
        /// <param name="targetCollection">The collection to display the current page.</param>
        /// <param name="onPropertyChanged">Callback to raise PropertyChanged events.</param>
        /// <param name="onPaginationChanged">Callback to notify when pagination state changes.</param>
        /// <param name="useDispatcherInvoke">Whether to use Dispatcher.Invoke for collection updates.</param>
        public PaginationManager(
            int pageSize,
            ObservableCollection<T> targetCollection,
            Action<string> onPropertyChanged = null,
            Action onPaginationChanged = null,
            bool useDispatcherInvoke = true,
            PaginationPropertyNames propertyNames = null)
        {
            _pageSize = pageSize;
            _targetCollection = targetCollection ?? throw new ArgumentNullException(nameof(targetCollection));
            _onPropertyChanged = onPropertyChanged;
            _onPaginationChanged = onPaginationChanged;
            _useDispatcherInvoke = useDispatcherInvoke;
            _names = propertyNames ?? new PaginationPropertyNames();
        }

        /// <summary>
        /// Gets or sets the current page number (1-based).
        /// </summary>
        public int CurrentPage
        {
            get => _currentPage;
            set
            {
                var clamped = Math.Max(1, Math.Min(value, TotalPages));
                if (_currentPage != clamped)
                {
                    _currentPage = clamped;
                    UpdatePagedCollection();
                    RaisePropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets the total number of pages.
        /// </summary>
        public int TotalPages => Math.Max(1, (int)Math.Ceiling(_allItems.Count / (double)_pageSize));

        /// <summary>
        /// Gets the total number of items across all pages.
        /// </summary>
        public int TotalItems => _allItems.Count;

        /// <summary>
        /// Gets whether navigation to the next page is possible.
        /// </summary>
        public bool CanGoNext => CurrentPage < TotalPages;

        /// <summary>
        /// Gets whether navigation to the previous page is possible.
        /// </summary>
        public bool CanGoPrevious => CurrentPage > 1;

        /// <summary>
        /// Gets whether there are multiple pages.
        /// </summary>
        public bool HasMultiplePages => TotalPages > 1;

        /// <summary>
        /// Gets the page info string for display (e.g., "Page 1 of 5").
        /// </summary>
        /// <param name="formatString">Optional format string with {0} for current page and {1} for total pages.</param>
        /// <returns>Formatted page info string.</returns>
        public string GetPageInfo(string formatString = "Page {0} of {1}")
        {
            return string.Format(formatString, CurrentPage, TotalPages);
        }

        /// <summary>
        /// Sets the source items and resets to the first page.
        /// </summary>
        /// <param name="items">The complete list of items to paginate.</param>
        public void SetSourceItems(IEnumerable<T> items)
        {
            _allItems = (items ?? Enumerable.Empty<T>()).ToList();

            // Adjust current page if needed
            if (_currentPage > TotalPages)
            {
                _currentPage = Math.Max(1, TotalPages);
            }

            UpdatePagedCollection();
            RaisePropertyChanged();
        }

        /// <summary>
        /// Navigates to the next page if possible.
        /// </summary>
        public void GoToNextPage()
        {
            if (CanGoNext) CurrentPage++;
        }

        /// <summary>
        /// Navigates to the previous page if possible.
        /// </summary>
        public void GoToPreviousPage()
        {
            if (CanGoPrevious) CurrentPage--;
        }

        /// <summary>
        /// Navigates to the first page.
        /// </summary>
        public void GoToFirstPage()
        {
            CurrentPage = 1;
        }

        /// <summary>
        /// Navigates to the last page.
        /// </summary>
        public void GoToLastPage()
        {
            CurrentPage = TotalPages;
        }

        /// <summary>
        /// Resets pagination to the first page.
        /// </summary>
        public void ResetToFirstPage()
        {
            CurrentPage = 1;
        }

        private void UpdatePagedCollection()
        {
            var skip = (_currentPage - 1) * _pageSize;
            var pageItems = _allItems.Skip(skip).Take(_pageSize).ToList();

            if (_useDispatcherInvoke)
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.Invoke(() => CollectionHelper.SynchronizeCollection(_targetCollection, pageItems));
                }
                else
                {
                    CollectionHelper.SynchronizeCollection(_targetCollection, pageItems);
                }
            }
            else
            {
                CollectionHelper.SynchronizeCollection(_targetCollection, pageItems);
            }

            _onPaginationChanged?.Invoke();
        }

        private void RaisePropertyChanged()
        {
            _onPropertyChanged?.Invoke(_names.CurrentPage);
            _onPropertyChanged?.Invoke(_names.TotalPages);
            _onPropertyChanged?.Invoke(_names.CanGoNext);
            _onPropertyChanged?.Invoke(_names.CanGoPrevious);
            _onPropertyChanged?.Invoke(_names.HasMultiplePages);
            _onPropertyChanged?.Invoke(_names.TotalItems);
        }

        /// <summary>
        /// Creates commands for pagination navigation.
        /// </summary>
        /// <param name="canExecuteChangedCallback">Optional callback to raise CanExecuteChanged.</param>
        /// <returns>Tuple containing Next, Previous, First, and Last page commands.</returns>
        public (ICommand NextCommand, ICommand PreviousCommand, ICommand FirstCommand, ICommand LastCommand) CreateCommands(
            Action<ICommand> canExecuteChangedCallback = null)
        {
            var nextCommand = new RelayCommand(_ => GoToNextPage(), _ => CanGoNext);
            var previousCommand = new RelayCommand(_ => GoToPreviousPage(), _ => CanGoPrevious);
            var firstCommand = new RelayCommand(_ => GoToFirstPage(), _ => CanGoPrevious);
            var lastCommand = new RelayCommand(_ => GoToLastPage(), _ => CanGoNext);

            // Wire up CanExecuteChanged raising
            if (canExecuteChangedCallback != null)
            {
                RaisePropertyChanged();
                canExecuteChangedCallback(nextCommand);
                canExecuteChangedCallback(previousCommand);
                canExecuteChangedCallback(firstCommand);
                canExecuteChangedCallback(lastCommand);
            }

            return (nextCommand, previousCommand, firstCommand, lastCommand);
        }
    }
}
