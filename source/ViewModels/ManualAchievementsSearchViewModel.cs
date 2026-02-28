using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Playnite.SDK;
using PlayniteAchievements.Providers.Manual;
using RelayCommand = PlayniteAchievements.Common.RelayCommand;

namespace PlayniteAchievements.ViewModels
{
    /// <summary>
    /// ViewModel for the manual achievements search dialog.
    /// Allows users to search for games on Steam and select one to link.
    /// </summary>
    public sealed class ManualAchievementsSearchViewModel : INotifyPropertyChanged
    {
        private readonly IManualSource _source;
        private readonly ILogger _logger;
        private readonly string _language;
        private CancellationTokenSource _searchCts;

        private string _searchText = string.Empty;
        private bool _isSearching;
        private ManualGameSearchResult _selectedResult;
        private string _statusMessage = string.Empty;

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler RequestClose;

        public string WindowTitle =>
            ResourceProvider.GetString("LOCPlayAch_ManualAchievements_Search_Title");

        public string PlayniteGameName { get; }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value ?? string.Empty;
                    OnPropertyChanged();
                    SearchCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsSearching
        {
            get => _isSearching;
            private set
            {
                if (_isSearching != value)
                {
                    _isSearching = value;
                    OnPropertyChanged();
                    SearchCommand.RaiseCanExecuteChanged();
                    OkCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public ObservableCollection<ManualGameSearchResult> SearchResults { get; } =
            new ObservableCollection<ManualGameSearchResult>();

        public ManualGameSearchResult SelectedResult
        {
            get => _selectedResult;
            set
            {
                if (_selectedResult != value)
                {
                    _selectedResult = value;
                    OnPropertyChanged();
                    OkCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set
            {
                if (_statusMessage != value)
                {
                    _statusMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool? DialogResult { get; private set; }

        public RelayCommand SearchCommand { get; }
        public RelayCommand OkCommand { get; }
        public RelayCommand CancelCommand { get; }

        public ManualAchievementsSearchViewModel(
            IManualSource source,
            string playniteGameName,
            string language,
            ILogger logger,
            string initialSearchText = null)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _logger = logger;
            _language = language ?? "english";
            PlayniteGameName = playniteGameName ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(initialSearchText))
            {
                _searchText = initialSearchText;
            }

            SearchCommand = new RelayCommand(
                async _ => await ExecuteSearchAsync(),
                _ => !IsSearching && !string.IsNullOrWhiteSpace(SearchText));

            OkCommand = new RelayCommand(
                _ => CloseDialog(true),
                _ => !IsSearching && SelectedResult != null);

            CancelCommand = new RelayCommand(
                _ => CloseDialog(false));
        }

        private async Task ExecuteSearchAsync()
        {
            if (string.IsNullOrWhiteSpace(SearchText) || IsSearching)
            {
                return;
            }

            // Cancel any previous search
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = new CancellationTokenSource();

            var ct = _searchCts.Token;
            IsSearching = true;
            StatusMessage = ResourceProvider.GetString("LOCPlayAch_Status_Refreshing");
            SearchResults.Clear();
            SelectedResult = null;

            try
            {
                var results = await _source.SearchGamesAsync(SearchText.Trim(), _language, ct);

                if (ct.IsCancellationRequested)
                {
                    return;
                }

                if (results == null || results.Count == 0)
                {
                    StatusMessage = ResourceProvider.GetString("LOCPlayAch_ManualAchievements_Search_NoResults");
                    return;
                }

                foreach (var result in results)
                {
                    if (result != null)
                    {
                        SearchResults.Add(result);
                    }
                }

                StatusMessage = string.Format(
                    ResourceProvider.GetString("LOCPlayAch_ManualAchievements_Search_ResultsFormat"),
                    SearchResults.Count);

                // Auto-select first result
                if (SearchResults.Count > 0)
                {
                    SelectedResult = SearchResults[0];
                }
            }
            catch (OperationCanceledException)
            {
                // Search was cancelled
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Manual achievement search failed");
                StatusMessage = string.Format(
                    ResourceProvider.GetString("LOCPlayAch_ManualAchievements_Search_Error"),
                    ex.Message);
            }
            finally
            {
                IsSearching = false;
            }
        }

        private void CloseDialog(bool result)
        {
            DialogResult = result;
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        public void CancelSearch()
        {
            _searchCts?.Cancel();
        }

        /// <summary>
        /// Triggers a search programmatically. Used for auto-search on dialog open.
        /// </summary>
        public async Task SearchAsync()
        {
            await ExecuteSearchAsync();
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
