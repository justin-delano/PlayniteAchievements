using PlayniteAchievements.Services.Workshop;
using PlayniteAchievements.Views.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace PlayniteAchievements.Views.Dialogs
{
    /// <summary>One row of the theme composer: a part, whether it travels, and from which source.</summary>
    public sealed class ThemeComposerRow : Common.ObservableObject
    {
        private bool _isIncluded;
        private ThemePartSource _selectedSource;

        public ThemeComposerRow(ThemePackParts part, string label, IReadOnlyList<ThemePartSource> sources, bool included)
        {
            Part = part;
            Label = label;
            Sources = sources ?? new List<ThemePartSource>();
            IsEnabled = Sources.Count > 0;
            _isIncluded = included && IsEnabled;
            _selectedSource = Sources.FirstOrDefault();
        }

        public ThemePackParts Part { get; }

        public string Label { get; }

        public IReadOnlyList<ThemePartSource> Sources { get; }

        /// <summary>False when neither the current settings nor any preset can feed this part.</summary>
        public bool IsEnabled { get; }

        public bool IsIncluded
        {
            get => _isIncluded;
            set
            {
                if (SetValueAndReturn(ref _isIncluded, value, nameof(IsIncluded)))
                {
                    OnPropertyChanged(nameof(CanPickSource));
                }
            }
        }

        public bool CanPickSource => IsEnabled && IsIncluded && Sources.Count > 1;

        public ThemePartSource SelectedSource
        {
            get => _selectedSource;
            set => SetValue(ref _selectedSource, value);
        }

        public ThemePartChoice ToChoice() => new ThemePartChoice(Part, IsIncluded && IsEnabled, SelectedSource);
    }

    /// <summary>
    /// The theme composer: one row per part with an include checkbox and a dropdown of sources
    /// (the current settings, then every saved preset of that kind), so the user sees exactly
    /// what a bundle will contain before it is written or shared.
    /// </summary>
    public partial class ThemeComposerDialog : UserControl
    {
        public static readonly DependencyProperty HintProperty =
            DependencyProperty.Register(nameof(Hint), typeof(string), typeof(ThemeComposerDialog), new PropertyMetadata(string.Empty));

        public ThemeComposerDialog()
        {
            InitializeComponent();
            DataContext = this;
        }

        public ThemeComposerDialog(string hint, IEnumerable<ThemeComposerRow> rows) : this()
        {
            Hint = hint ?? string.Empty;
            foreach (var row in rows ?? Enumerable.Empty<ThemeComposerRow>())
            {
                Rows.Add(row);
            }
        }

        public string Hint
        {
            get => (string)GetValue(HintProperty);
            set => SetValue(HintProperty, value);
        }

        public ObservableCollection<ThemeComposerRow> Rows { get; } = new ObservableCollection<ThemeComposerRow>();

        public bool? DialogResult { get; private set; }

        public event EventHandler RequestClose;

        /// <summary>
        /// Builds the rows from <paramref name="composer"/>, shows the dialog modally and returns
        /// the included choices, or null when cancelled or when nothing was included.
        /// </summary>
        public static IReadOnlyList<ThemePartChoice> Show(
            ThemeComposer composer,
            string title,
            string hint,
            Window owner = null)
        {
            if (composer == null)
            {
                throw new ArgumentNullException(nameof(composer));
            }

            var rows = ThemeComposer.Parts
                .Select(part => new ThemeComposerRow(part, ThemeComposer.LabelFor(part), composer.SourcesFor(part), included: true))
                .ToList();

            var dialog = new ThemeComposerDialog(hint, rows);
            var window = PlayniteUiProvider.CreateExtensionWindow(
                title,
                dialog,
                new WindowOptions
                {
                    ShowMinimizeButton = false,
                    ShowMaximizeButton = false,
                    ShowCloseButton = true,
                    CanBeResizable = false,
                    Width = 600,
                    Height = 180 + 36 * Math.Max(1, rows.Count)
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
            if (dialog.DialogResult != true)
            {
                return null;
            }

            var choices = dialog.Rows.Select(row => row.ToChoice()).Where(choice => choice.Included && choice.Source != null).ToList();
            return choices.Count == 0 ? null : choices;
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
