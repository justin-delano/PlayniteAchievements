using PlayniteAchievements.Common;
using PlayniteAchievements.Views.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace PlayniteAchievements.Views.Dialogs
{
    /// <summary>One checkable row of a <see cref="PartPickerDialog"/>.</summary>
    public sealed class PartPickerItem : Common.ObservableObject
    {
        private bool _isChecked;

        public PartPickerItem(object key, string label, bool isChecked = true, bool isEnabled = true)
        {
            Key = key;
            Label = label;
            _isChecked = isChecked && isEnabled;
            IsEnabled = isEnabled;
        }

        public object Key { get; }

        public string Label { get; }

        public bool IsEnabled { get; }

        public bool IsChecked
        {
            get => _isChecked;
            set => SetValue(ref _isChecked, value);
        }
    }

    /// <summary>
    /// A checklist dialog: a hint, one checkbox per offered part, OK and Cancel. Used wherever a
    /// package carries several independently applicable parts (bundles, workshop installs)
    /// so the user picks a subset in one step instead of answering a chain of yes/no prompts.
    /// </summary>
    public partial class PartPickerDialog : UserControl
    {
        public static readonly DependencyProperty HintProperty =
            DependencyProperty.Register(nameof(Hint), typeof(string), typeof(PartPickerDialog), new PropertyMetadata(string.Empty));

        public PartPickerDialog()
        {
            InitializeComponent();
            DataContext = this;
        }

        public PartPickerDialog(string hint, IEnumerable<PartPickerItem> items) : this()
        {
            Hint = hint ?? string.Empty;
            foreach (var item in items ?? Enumerable.Empty<PartPickerItem>())
            {
                Items.Add(item);
            }
        }

        public string Hint
        {
            get => (string)GetValue(HintProperty);
            set => SetValue(HintProperty, value);
        }

        public ObservableCollection<PartPickerItem> Items { get; } = new ObservableCollection<PartPickerItem>();

        public bool? DialogResult { get; private set; }

        public event EventHandler RequestClose;

        /// <summary>The keys of the checked rows; empty when the dialog was cancelled.</summary>
        public IReadOnlyList<object> SelectedKeys =>
            DialogResult == true
                ? Items.Where(item => item.IsEnabled && item.IsChecked).Select(item => item.Key).ToList()
                : new List<object>();

        /// <summary>
        /// Shows the picker modally in a plugin window and returns the checked keys, or null when
        /// cancelled. Callers are on the UI thread.
        /// </summary>
        public static IReadOnlyList<object> Show(
            string title,
            string hint,
            IEnumerable<PartPickerItem> items,
            Window owner = null)
        {
            var dialog = new PartPickerDialog(hint, items);
            var window = PlayniteUiProvider.CreateExtensionWindow(
                title,
                dialog,
                new WindowOptions
                {
                    ShowMinimizeButton = false,
                    ShowMaximizeButton = false,
                    ShowCloseButton = true,
                    CanBeResizable = false,
                    Width = 460,
                    Height = 160 + 30 * Math.Max(1, dialog.Items.Count)
                });

            if (owner != null && window.Owner == null)
            {
                try
                {
                    window.Owner = owner;
                }
                catch (InvalidOperationException)
                {
                }
            }

            dialog.RequestClose += (s, e) => window.Close();
            window.ShowDialog();
            return dialog.DialogResult == true ? dialog.SelectedKeys : null;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            RequestClose?.Invoke(this, EventArgs.Empty);
        }
    }
}
