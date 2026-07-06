using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Playnite.SDK;

namespace PlayniteAchievements.ViewModels
{
    public sealed class GridControlBarViewModel : PlayniteAchievements.Common.ObservableObject
    {
        private GridSearchControl _search;

        public GridControlBarViewModel()
        {
            Items = new ObservableCollection<GridControlBarItem>();
        }

        public GridSearchControl Search
        {
            get => _search;
            set => SetValue(ref _search, value);
        }

        public ObservableCollection<GridControlBarItem> Items { get; }

        public void Refresh()
        {
            Search?.Refresh();
            foreach (var item in Items)
            {
                item?.Refresh();
            }
        }
    }

    // Serializable snapshot of the adapter's filter state. Used to persist/restore the
    // grid control bar (e.g. ViewAchievementsViewModel's per-game session snapshot)
    // without threading each field through callers.
    public sealed class GridControlBarFilterState
    {
        public string SearchText { get; set; } = string.Empty;
        public bool ShowUnlocked { get; set; } = true;
        public bool ShowLocked { get; set; } = true;
        public bool ShowHidden { get; set; } = true;
        public List<string> CategoryTypeFilters { get; set; } = new List<string>();
        public List<string> CategoryLabelFilters { get; set; } = new List<string>();
    }

    public abstract class GridControlBarItem : PlayniteAchievements.Common.ObservableObject
    {
        private bool _isVisible = true;
        private double _width = double.NaN;
        private double _minWidth = 0d;
        private string _toolTip;

        public bool IsVisible
        {
            get => _isVisible;
            set => SetValue(ref _isVisible, value);
        }

        public double Width
        {
            get => _width;
            set => SetValue(ref _width, value);
        }

        public double MinWidth
        {
            get => _minWidth;
            set => SetValue(ref _minWidth, value);
        }

        public string ToolTip
        {
            get => _toolTip;
            set => SetValue(ref _toolTip, value);
        }

        public virtual void Refresh()
        {
            OnPropertyChanged(string.Empty);
        }
    }

    public sealed class GridSearchControl : PlayniteAchievements.Common.ObservableObject
    {
        private readonly Func<string> _getText;
        private readonly Action<string> _setText;
        private readonly Action _clear;
        private string _text;
        private double _minWidth = 120d;

        public GridSearchControl(
            INotifyPropertyChanged source,
            string sourcePropertyName,
            Func<string> getText,
            Action<string> setText,
            string placeholder,
            Action clear = null)
        {
            _getText = getText;
            _setText = setText;
            _clear = clear;
            Placeholder = placeholder;
            Subscribe(source, sourcePropertyName, Refresh);
        }

        public string Text
        {
            get => _getText != null ? _getText() ?? string.Empty : _text ?? string.Empty;
            set
            {
                var normalized = value ?? string.Empty;
                if (string.Equals(Text, normalized, StringComparison.Ordinal))
                {
                    return;
                }

                if (_setText != null)
                {
                    _setText(normalized);
                }
                else
                {
                    _text = normalized;
                }

                Refresh();
            }
        }

        public string Placeholder { get; }

        public double MinWidth
        {
            get => _minWidth;
            set => SetValue(ref _minWidth, value);
        }

        public void Clear()
        {
            if (_clear != null)
            {
                _clear();
            }
            else
            {
                Text = string.Empty;
            }

            Refresh();
        }

        public void Refresh()
        {
            OnPropertyChanged(nameof(Text));
        }

        internal static void Subscribe(INotifyPropertyChanged source, string propertyName, Action refresh)
        {
            if (source == null || refresh == null)
            {
                return;
            }

            source.PropertyChanged += (sender, e) =>
            {
                if (string.IsNullOrEmpty(propertyName) ||
                    string.IsNullOrEmpty(e?.PropertyName) ||
                    string.Equals(e.PropertyName, propertyName, StringComparison.Ordinal))
                {
                    refresh();
                }
            };
        }
    }

    public sealed class GridMultiSelectFilter : GridControlBarItem
    {
        private readonly Func<string> _getDisplayText;
        private readonly Func<IEnumerable<string>> _getOptions;
        private readonly IEnumerable<string> _options;
        private readonly Func<string, bool> _isSelected;
        private readonly Action<string, bool> _setSelection;
        private readonly Func<string, string> _getDisplayLabel;

        public GridMultiSelectFilter(
            INotifyPropertyChanged source,
            string sourcePropertyName,
            Func<string> getDisplayText,
            IEnumerable<string> options,
            Func<string, bool> isSelected,
            Action<string, bool> setSelection,
            Func<string, string> getDisplayLabel = null)
        {
            _getDisplayText = getDisplayText;
            _options = options;
            _isSelected = isSelected;
            _setSelection = setSelection;
            _getDisplayLabel = getDisplayLabel;
            Subscribe(source, sourcePropertyName);
        }

        public GridMultiSelectFilter(
            INotifyPropertyChanged source,
            string sourcePropertyName,
            Func<string> getDisplayText,
            Func<IEnumerable<string>> getOptions,
            Func<string, bool> isSelected,
            Action<string, bool> setSelection,
            Func<string, string> getDisplayLabel = null)
        {
            _getDisplayText = getDisplayText;
            _getOptions = getOptions;
            _isSelected = isSelected;
            _setSelection = setSelection;
            _getDisplayLabel = getDisplayLabel;
            Subscribe(source, sourcePropertyName);
        }

        public string DisplayText => _getDisplayText?.Invoke() ?? string.Empty;

        public IEnumerable<string> Options => _getOptions?.Invoke() ?? _options;

        public bool IsSelected(string option)
        {
            return _isSelected?.Invoke(option) == true;
        }

        public void SetSelected(string option, bool isSelected)
        {
            _setSelection?.Invoke(option, isSelected);
            Refresh();
        }

        public string GetDisplayLabel(string option)
        {
            var label = _getDisplayLabel?.Invoke(option);
            return string.IsNullOrWhiteSpace(label) ? option : label;
        }

        public override void Refresh()
        {
            OnPropertyChanged(nameof(DisplayText));
        }

        private void Subscribe(INotifyPropertyChanged source, string propertyName)
        {
            GridSearchControl.Subscribe(source, propertyName, Refresh);
        }
    }

    public enum GridToggleFilterIcon
    {
        None,
        Unlocked,
        Locked,
        Hidden,
    }

    public sealed class GridToggleFilter : GridControlBarItem
    {
        private readonly Func<bool> _getIsChecked;
        private readonly Action<bool> _setIsChecked;
        private readonly string _content;

        public GridToggleFilter(
            INotifyPropertyChanged source,
            string sourcePropertyName,
            string content,
            Func<bool> getIsChecked,
            Action<bool> setIsChecked,
            GridToggleFilterIcon icon = GridToggleFilterIcon.None)
        {
            _content = content;
            _getIsChecked = getIsChecked;
            _setIsChecked = setIsChecked;
            Icon = icon;
            GridSearchControl.Subscribe(source, sourcePropertyName, Refresh);
        }

        public string Content => _content;

        // When set, the toggle renders a glyph (matching the grid Status column) instead
        // of the text Content; Content is surfaced as the tooltip. None keeps text rendering.
        public GridToggleFilterIcon Icon { get; }

        public bool IsChecked
        {
            get => _getIsChecked?.Invoke() == true;
            set
            {
                if (IsChecked == value)
                {
                    return;
                }

                _setIsChecked?.Invoke(value);
                Refresh();
            }
        }

        public override void Refresh()
        {
            OnPropertyChanged(nameof(IsChecked));
        }
    }

    // A toolbar toggle rendered as a ToggleButton (distinct from GridToggleFilter, which is a
    // filter checkbox). Used to switch the achievement grid into category-summaries mode.
    public sealed class GridModeToggle : GridControlBarItem
    {
        private readonly Func<bool> _getIsChecked;
        private readonly Action<bool> _setIsChecked;

        public GridModeToggle(
            INotifyPropertyChanged source,
            string sourcePropertyName,
            string content,
            Func<bool> getIsChecked,
            Action<bool> setIsChecked,
            string toolTip = null)
        {
            Content = content;
            _getIsChecked = getIsChecked;
            _setIsChecked = setIsChecked;
            ToolTip = toolTip;
            GridSearchControl.Subscribe(source, sourcePropertyName, Refresh);
        }

        public string Content { get; }

        public bool IsChecked
        {
            get => _getIsChecked?.Invoke() == true;
            set
            {
                if (IsChecked == value)
                {
                    return;
                }

                _setIsChecked?.Invoke(value);
                Refresh();
            }
        }

        public override void Refresh()
        {
            OnPropertyChanged(nameof(IsChecked));
        }
    }

    public sealed class GridProviderPlatformFilter : GridControlBarItem
    {
        private readonly Func<string> _getDisplayText;
        private readonly Func<ObservableCollection<ProviderFilterGroup>> _getGroups;
        private readonly ObservableCollection<ProviderFilterGroup> _groups;
        private readonly Action _closed;

        public GridProviderPlatformFilter(
            INotifyPropertyChanged source,
            string sourcePropertyName,
            Func<string> getDisplayText,
            ObservableCollection<ProviderFilterGroup> groups,
            Action closed = null)
        {
            _getDisplayText = getDisplayText;
            _groups = groups;
            _closed = closed;
            GridSearchControl.Subscribe(source, sourcePropertyName, Refresh);
        }

        public GridProviderPlatformFilter(
            INotifyPropertyChanged source,
            string sourcePropertyName,
            Func<string> getDisplayText,
            Func<ObservableCollection<ProviderFilterGroup>> getGroups,
            Action closed = null)
        {
            _getDisplayText = getDisplayText;
            _getGroups = getGroups;
            _closed = closed;
            GridSearchControl.Subscribe(source, sourcePropertyName, Refresh);
        }

        public string DisplayText => _getDisplayText?.Invoke() ?? string.Empty;

        public ObservableCollection<ProviderFilterGroup> Groups => _getGroups?.Invoke() ?? _groups;

        public void OnClosed()
        {
            _closed?.Invoke();
        }

        public override void Refresh()
        {
            OnPropertyChanged(nameof(DisplayText));
        }
    }

    internal static class GridControlBarText
    {
        public static string Get(string key, string fallback)
        {
            var value = ResourceProvider.GetString(key);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
    }
}
