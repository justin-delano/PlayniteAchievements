using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using PlayniteAchievements.Common;
using ObservableObject = PlayniteAchievements.Common.ObservableObject;

namespace PlayniteAchievements.ViewModels
{
    /// <summary>
    /// A single selectable platform (console) under a provider in the Overview platform filter.
    /// </summary>
    public sealed class PlatformFilterOption : ObservableObject
    {
        public PlatformFilterOption(string platformName, bool isSelected = false)
        {
            PlatformName = platformName;
            _isSelected = isSelected;
        }

        public string PlatformName { get; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetValue(ref _isSelected, value);
        }
    }

    /// <summary>
    /// A provider row in the Overview platform filter. Owns the per-platform options and derives a
    /// tri-state selection summary for the parent checkbox. A provider with a single platform shows
    /// no expander and behaves as a plain checkbox; the parent toggle selects/clears all platforms.
    /// </summary>
    public sealed class ProviderFilterGroup : ObservableObject
    {
        private readonly Action _onSelectionChanged;
        private bool _isBulkUpdating;

        public ProviderFilterGroup(
            string providerKey,
            string displayName,
            IEnumerable<string> platformNames,
            Func<string, bool> isPlatformSelected,
            Action onSelectionChanged)
        {
            ProviderKey = providerKey;
            DisplayName = displayName;
            _onSelectionChanged = onSelectionChanged;

            var options = new List<PlatformFilterOption>();
            foreach (var name in platformNames ?? Enumerable.Empty<string>())
            {
                var option = new PlatformFilterOption(name, isPlatformSelected?.Invoke(name) ?? false);
                option.PropertyChanged += OnOptionPropertyChanged;
                options.Add(option);
            }

            Platforms = new ObservableCollection<PlatformFilterOption>(options);
            ToggleAllCommand = new RelayCommand(_ => ToggleAll());
        }

        public string ProviderKey { get; }

        public string DisplayName { get; }

        public ObservableCollection<PlatformFilterOption> Platforms { get; }

        /// <summary>
        /// True when more than one platform exists, so the row shows an inline expander.
        /// </summary>
        public bool HasMultiplePlatforms => Platforms.Count > 1;

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetValue(ref _isExpanded, value);
        }

        public RelayCommand ToggleAllCommand { get; }

        /// <summary>
        /// Tri-state summary for the parent checkbox: true = all selected, false = none, null = some.
        /// </summary>
        public bool? SelectionState
        {
            get
            {
                var selected = Platforms.Count(p => p.IsSelected);
                if (selected == 0)
                {
                    return false;
                }

                if (selected == Platforms.Count)
                {
                    return true;
                }

                return null;
            }
        }

        /// <summary>
        /// True when at least one platform is selected (the provider is "active" for filtering).
        /// </summary>
        public bool HasAnySelected => Platforms.Any(p => p.IsSelected);

        /// <summary>
        /// True when every platform is selected (the provider is fully selected).
        /// </summary>
        public bool IsFullySelected => Platforms.Count > 0 && Platforms.All(p => p.IsSelected);

        /// <summary>
        /// Selected platform names in display order.
        /// </summary>
        public IEnumerable<string> SelectedPlatformNames =>
            Platforms.Where(p => p.IsSelected).Select(p => p.PlatformName);

        /// <summary>
        /// Selects all platforms when not all are currently selected; otherwise clears them.
        /// </summary>
        public void ToggleAll()
        {
            SetAll(SelectionState != true);
        }

        /// <summary>
        /// Sets every platform's selection in one batch, firing a single change notification.
        /// </summary>
        public void SetAll(bool isSelected)
        {
            _isBulkUpdating = true;
            try
            {
                foreach (var option in Platforms)
                {
                    option.IsSelected = isSelected;
                }
            }
            finally
            {
                _isBulkUpdating = false;
            }

            RaiseSelectionChanged();
        }

        private void OnOptionPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(PlatformFilterOption.IsSelected) || _isBulkUpdating)
            {
                return;
            }

            RaiseSelectionChanged();
        }

        private void RaiseSelectionChanged()
        {
            OnPropertyChanged(nameof(SelectionState));
            OnPropertyChanged(nameof(HasAnySelected));
            OnPropertyChanged(nameof(IsFullySelected));
            _onSelectionChanged?.Invoke();
        }
    }
}
