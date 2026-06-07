using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Playnite.SDK;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Services;
using RelayCommand = PlayniteAchievements.Common.RelayCommand;

using ObservableObject = PlayniteAchievements.Common.ObservableObject;

namespace PlayniteAchievements.ViewModels
{
    public enum AchievementFilterStateFilter
    {
        All,
        FilteredOut,
        FilteredOutOfSummaries,
        Unfiltered
    }

    public sealed class GameOptionsFiltersViewModel : ObservableObject
    {
        private readonly Guid _gameId;
        private readonly AchievementOverridesService _achievementOverridesService;
        private readonly GameOptionsDataSnapshotProvider _gameDataSnapshotProvider;
        private readonly PlayniteAchievementsSettings _settings;
        private readonly ILogger _logger;

        private List<GameOptionsFilterItem> _allRows = new List<GameOptionsFilterItem>();
        private bool _hasAchievements;
        private bool _hasCustomFilters;
        private bool _isUpdatingRows;
        private string _searchText = string.Empty;
        private FilterStateOption _selectedFilterOption;

        public GameOptionsFiltersViewModel(
            Guid gameId,
            AchievementOverridesService achievementOverridesService,
            GameOptionsDataSnapshotProvider gameDataSnapshotProvider,
            PlayniteAchievementsSettings settings,
            ILogger logger)
        {
            _gameId = gameId;
            _achievementOverridesService = achievementOverridesService ?? throw new ArgumentNullException(nameof(achievementOverridesService));
            _gameDataSnapshotProvider = gameDataSnapshotProvider ?? throw new ArgumentNullException(nameof(gameDataSnapshotProvider));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger;

            AchievementRows = new ObservableCollection<GameOptionsFilterItem>();
            FilterOptions = new ObservableCollection<FilterStateOption>(CreateFilterOptions());
            _selectedFilterOption = FilterOptions.FirstOrDefault();
            ClearSearchCommand = new RelayCommand(_ => SearchText = string.Empty);

            ReloadData();
        }

        public ObservableCollection<GameOptionsFilterItem> AchievementRows { get; }

        public ObservableCollection<FilterStateOption> FilterOptions { get; }

        public RelayCommand ClearSearchCommand { get; }

        public bool HasAchievements
        {
            get => _hasAchievements;
            private set => SetValue(ref _hasAchievements, value);
        }

        public bool HasCustomFilters
        {
            get => _hasCustomFilters;
            private set => SetValue(ref _hasCustomFilters, value);
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetValueAndReturn(ref _searchText, value ?? string.Empty))
                {
                    ApplyFilter();
                }
            }
        }

        public FilterStateOption SelectedFilterOption
        {
            get => _selectedFilterOption;
            set
            {
                if (SetValueAndReturn(ref _selectedFilterOption, value ?? FilterOptions.FirstOrDefault()))
                {
                    ApplyFilter();
                }
            }
        }

        public void ReloadData()
        {
            try
            {
                var revealedStateByApiName = AchievementRows
                    .Where(row => row != null && !string.IsNullOrWhiteSpace(row.ApiName))
                    .GroupBy(row => row.ApiName.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First().IsRevealed, StringComparer.OrdinalIgnoreCase);

                var hydratedGameData = _gameDataSnapshotProvider.GetHydratedGameData();
                var rawGameData = _gameDataSnapshotProvider.GetRawGameData();
                var rawAchievements = rawGameData?.Achievements?
                    .Where(a => a != null && !string.IsNullOrWhiteSpace(a.ApiName))
                    .ToList() ?? new List<AchievementDetail>();
                var projectionSource = hydratedGameData ?? rawGameData;
                var categoryOverrides = GetCurrentCategoryOverrideMap();
                var categoryTypeOverrides = GetCurrentCategoryTypeOverrideMap();
                var filteredApiNames = GameCustomDataLookup.GetFilteredAchievementApiNames(_gameId, _settings?.Persisted);
                var summaryFilteredApiNames = GameCustomDataLookup.GetSummaryFilteredAchievementApiNames(_gameId, _settings?.Persisted);

                List<AchievementDetail> orderedAchievements;
                if (hydratedGameData?.AchievementOrder != null && hydratedGameData.AchievementOrder.Count > 0)
                {
                    orderedAchievements = AchievementOrderHelper.ApplyOrder(
                        rawAchievements,
                        a => a.ApiName,
                        hydratedGameData.AchievementOrder);
                }
                else
                {
                    orderedAchievements = rawAchievements
                        .OrderBy(a => a.DisplayName ?? a.ApiName, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }

                _allRows = orderedAchievements.Select(a =>
                {
                    var apiName = (a.ApiName ?? string.Empty).Trim();
                    var projected = AchievementDisplayItem.Create(
                        projectionSource,
                        a,
                        _settings,
                        playniteGameIdOverride: _gameId);
                    if (projected == null)
                    {
                        return null;
                    }

                    var item = new GameOptionsFilterItem(
                        filteredApiNames.Contains(apiName),
                        summaryFilteredApiNames.Contains(apiName),
                        OnRowFilterChanged)
                    {
                        ProviderKey = projected.ProviderKey,
                        GameName = projected.GameName,
                        SortingName = projected.SortingName,
                        PlayniteGameId = projected.PlayniteGameId,
                        ApiName = apiName,
                        DisplayName = projected.DisplayName,
                        Description = projected.Description,
                        UnlockedIconPath = projected.UnlockedIconPath,
                        LockedIconPath = projected.LockedIconPath,
                        UnlockTimeUtc = projected.UnlockTimeUtc,
                        GlobalPercentUnlocked = projected.GlobalPercentUnlocked,
                        PointsValue = projected.PointsValue,
                        ProgressNum = projected.ProgressNum,
                        ProgressDenom = projected.ProgressDenom,
                        TrophyType = projected.TrophyType,
                        Unlocked = projected.Unlocked,
                        Hidden = projected.Hidden,
                        ShowHiddenIcon = projected.ShowHiddenIcon,
                        ShowHiddenTitle = projected.ShowHiddenTitle,
                        ShowHiddenDescription = projected.ShowHiddenDescription,
                        ShowRarityGlow = projected.ShowRarityGlow,
                        ShowRarityBar = projected.ShowRarityBar,
                        ShowHiddenSuffix = projected.ShowHiddenSuffix,
                        ShowLockedIcon = projected.ShowLockedIcon,
                        UseSeparateLockedIconsWhenAvailable = projected.UseSeparateLockedIconsWhenAvailable,
                        IsRevealed = revealedStateByApiName.TryGetValue(apiName, out var isRevealed)
                            ? isRevealed
                            : projected.IsRevealed,
                        CategoryLabel = ResolveEffectiveCategoryLabel(a, categoryOverrides),
                        CategoryType = ResolveEffectiveCategoryType(a, categoryTypeOverrides)
                    };

                    return item;
                })
                .Where(a => a != null)
                .ToList();

                HasAchievements = _allRows.Count > 0;
                RefreshCustomFilterState();
                ApplyFilter();
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed loading filter rows for gameId={_gameId}");
                _allRows = new List<GameOptionsFilterItem>();
                CollectionHelper.SynchronizeCollection(AchievementRows, _allRows);
                HasAchievements = false;
                HasCustomFilters = false;
            }
        }

        public bool ResetFilters()
        {
            if (!HasCustomFilters)
            {
                return false;
            }

            _isUpdatingRows = true;
            try
            {
                foreach (var row in _allRows.Where(row => row != null))
                {
                    row.SetFilterState(isFiltered: false, isSummaryFiltered: false);
                }
            }
            finally
            {
                _isUpdatingRows = false;
            }

            PersistCurrentFilters();
            ApplyFilter();
            return true;
        }

        private void OnRowFilterChanged(GameOptionsFilterItem item)
        {
            if (_isUpdatingRows)
            {
                return;
            }

            PersistCurrentFilters();
            ApplyFilter();
        }

        private void PersistCurrentFilters()
        {
            var filteredApiNames = _allRows
                .Where(row => row?.IsFiltered == true && !string.IsNullOrWhiteSpace(row.ApiName))
                .Select(row => row.ApiName)
                .ToList();
            var summaryFilteredApiNames = _allRows
                .Where(row => row?.IsSummaryFiltered == true && !string.IsNullOrWhiteSpace(row.ApiName))
                .Select(row => row.ApiName)
                .ToList();

            _achievementOverridesService.SetAchievementFilters(
                _gameId,
                filteredApiNames,
                summaryFilteredApiNames);

            RefreshCustomFilterState();
        }

        private void RefreshCustomFilterState()
        {
            HasCustomFilters = _allRows.Any(row => row?.IsFiltered == true || row?.IsSummaryFiltered == true);
        }

        private void ApplyFilter()
        {
            var filtered = _allRows.AsEnumerable();

            if (!string.IsNullOrEmpty(SearchText))
            {
                filtered = filtered.Where(a =>
                    (a.DisplayName?.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (a.Description?.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (a.ApiName?.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (a.CategoryDisplay?.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (a.CategoryTypeDisplay?.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            switch (SelectedFilterOption?.Value ?? AchievementFilterStateFilter.All)
            {
                case AchievementFilterStateFilter.FilteredOut:
                    filtered = filtered.Where(a => a.IsFiltered);
                    break;
                case AchievementFilterStateFilter.FilteredOutOfSummaries:
                    filtered = filtered.Where(a => a.IsFiltered || a.IsSummaryFiltered);
                    break;
                case AchievementFilterStateFilter.Unfiltered:
                    filtered = filtered.Where(a => !a.IsFiltered && !a.IsSummaryFiltered);
                    break;
            }

            CollectionHelper.SynchronizeCollection(AchievementRows, filtered.ToList());
        }

        private Dictionary<string, string> GetCurrentCategoryOverrideMap()
        {
            var map = GameCustomDataLookup.GetAchievementCategoryOverrides(_gameId, _settings?.Persisted);
            var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in map)
            {
                var apiName = (pair.Key ?? string.Empty).Trim();
                var category = AchievementCategoryTypeHelper.NormalizeCategory(pair.Value);
                if (string.IsNullOrWhiteSpace(apiName) || string.IsNullOrWhiteSpace(category))
                {
                    continue;
                }

                normalized[apiName] = category;
            }

            return normalized;
        }

        private Dictionary<string, string> GetCurrentCategoryTypeOverrideMap()
        {
            var map = GameCustomDataLookup.GetAchievementCategoryTypeOverrides(_gameId, _settings?.Persisted);
            var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in map)
            {
                var apiName = (pair.Key ?? string.Empty).Trim();
                var categoryType = AchievementCategoryTypeHelper.Normalize(pair.Value);
                if (string.IsNullOrWhiteSpace(apiName) || string.IsNullOrWhiteSpace(categoryType))
                {
                    continue;
                }

                normalized[apiName] = categoryType;
            }

            return normalized;
        }

        private static string ResolveEffectiveCategoryLabel(
            AchievementDetail achievement,
            IReadOnlyDictionary<string, string> categoryOverrides)
        {
            var apiName = (achievement?.ApiName ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(apiName) &&
                categoryOverrides != null &&
                categoryOverrides.TryGetValue(apiName, out var categoryOverride) &&
                !string.IsNullOrWhiteSpace(categoryOverride))
            {
                return AchievementCategoryTypeHelper.NormalizeCategoryOrDefault(categoryOverride);
            }

            return AchievementCategoryTypeHelper.NormalizeCategoryOrDefault(achievement?.Category);
        }

        private static string ResolveEffectiveCategoryType(
            AchievementDetail achievement,
            IReadOnlyDictionary<string, string> categoryTypeOverrides)
        {
            var apiName = (achievement?.ApiName ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(apiName) &&
                categoryTypeOverrides != null &&
                categoryTypeOverrides.TryGetValue(apiName, out var categoryTypeOverride) &&
                !string.IsNullOrWhiteSpace(categoryTypeOverride))
            {
                return AchievementCategoryTypeHelper.NormalizeOrDefault(categoryTypeOverride);
            }

            return AchievementCategoryTypeHelper.NormalizeOrDefault(achievement?.CategoryType);
        }

        private static IEnumerable<FilterStateOption> CreateFilterOptions()
        {
            return new[]
            {
                new FilterStateOption(AchievementFilterStateFilter.All, L("LOCPlayAch_Common_All", "All")),
                new FilterStateOption(AchievementFilterStateFilter.FilteredOut, L("LOCPlayAch_GameOptions_Filters_FilteredOut", "Filtered Out")),
                new FilterStateOption(AchievementFilterStateFilter.FilteredOutOfSummaries, L("LOCPlayAch_GameOptions_Filters_FilteredOutOfSummaries", "Filtered Out of Summaries")),
                new FilterStateOption(AchievementFilterStateFilter.Unfiltered, L("LOCPlayAch_GameOptions_Filters_Unfiltered", "Unfiltered"))
            };
        }

        private static string L(string key, string fallback)
        {
            var value = ResourceProvider.GetString(key);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
    }

    public sealed class GameOptionsFilterItem : AchievementDisplayItem
    {
        private readonly Action<GameOptionsFilterItem> _filterChanged;
        private bool _isFiltered;
        private bool _isSummaryFiltered;

        public GameOptionsFilterItem(
            bool isFiltered,
            bool isSummaryFiltered,
            Action<GameOptionsFilterItem> filterChanged)
        {
            _isFiltered = isFiltered;
            _isSummaryFiltered = isSummaryFiltered;
            _filterChanged = filterChanged;
        }

        public bool IsFiltered
        {
            get => _isFiltered;
            set
            {
                if (SetValueAndReturn(ref _isFiltered, value))
                {
                    OnPropertyChanged(nameof(IsSummaryFilterEffective));
                    OnPropertyChanged(nameof(CanSetSummaryFilter));
                    _filterChanged?.Invoke(this);
                }
            }
        }

        public bool IsSummaryFiltered
        {
            get => _isSummaryFiltered;
            set
            {
                if (SetValueAndReturn(ref _isSummaryFiltered, value))
                {
                    OnPropertyChanged(nameof(IsSummaryFilterEffective));
                    _filterChanged?.Invoke(this);
                }
            }
        }

        public bool IsSummaryFilterEffective
        {
            get => IsFiltered || IsSummaryFiltered;
            set
            {
                if (!IsFiltered)
                {
                    IsSummaryFiltered = value;
                }
            }
        }

        public bool CanSetSummaryFilter => !IsFiltered;

        public string CategoryDisplay => AchievementCategoryTypeHelper.NormalizeCategoryOrDefault(CategoryLabel);

        public void SetFilterState(bool isFiltered, bool isSummaryFiltered)
        {
            var changed = _isFiltered != isFiltered || _isSummaryFiltered != isSummaryFiltered;
            _isFiltered = isFiltered;
            _isSummaryFiltered = isSummaryFiltered;
            if (!changed)
            {
                return;
            }

            OnPropertyChanged(nameof(IsFiltered));
            OnPropertyChanged(nameof(IsSummaryFiltered));
            OnPropertyChanged(nameof(IsSummaryFilterEffective));
            OnPropertyChanged(nameof(CanSetSummaryFilter));
            _filterChanged?.Invoke(this);
        }
    }

    public sealed class FilterStateOption
    {
        public FilterStateOption(AchievementFilterStateFilter value, string displayName)
        {
            Value = value;
            DisplayName = displayName;
        }

        public AchievementFilterStateFilter Value { get; }

        public string DisplayName { get; }
    }
}
