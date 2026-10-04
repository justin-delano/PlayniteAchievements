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
    /// <summary>One row of the bundle composer: a part, whether it travels, and from which source.</summary>
    public sealed class BundleComposerRow : Common.ObservableObject
    {
        private bool _isIncluded;
        private BundlePartSource _selectedSource;

        public BundleComposerRow(BundleParts part, string label, IReadOnlyList<BundlePartSource> sources, bool included)
        {
            Part = part;
            Label = label;
            Sources = sources ?? new List<BundlePartSource>();
            IsEnabled = Sources.Count > 0;
            _isIncluded = included && IsEnabled;
            _selectedSource = Sources.FirstOrDefault();
        }

        public BundleParts Part { get; }

        public string Label { get; }

        public IReadOnlyList<BundlePartSource> Sources { get; }

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

        public BundlePartSource SelectedSource
        {
            get => _selectedSource;
            set => SetValue(ref _selectedSource, value);
        }

        public BundlePartChoice ToChoice() => new BundlePartChoice(Part, IsIncluded && IsEnabled, SelectedSource);
    }

    /// <summary>
    /// The bundle composer: one row per part with an include checkbox and a dropdown of sources
    /// (the current settings, then every saved preset of that kind), so the user sees exactly
    /// what a bundle will contain before it is written or shared.
    /// </summary>
    public partial class BundleComposerDialog : UserControl
    {
        public static readonly DependencyProperty HintProperty =
            DependencyProperty.Register(nameof(Hint), typeof(string), typeof(BundleComposerDialog), new PropertyMetadata(string.Empty));

        public BundleComposerDialog()
        {
            InitializeComponent();
            DataContext = this;
        }

        public BundleComposerDialog(string hint, IEnumerable<BundleComposerRow> rows) : this()
        {
            Hint = hint ?? string.Empty;
            foreach (var row in rows ?? Enumerable.Empty<BundleComposerRow>())
            {
                Rows.Add(row);
            }
        }

        public string Hint
        {
            get => (string)GetValue(HintProperty);
            set => SetValue(HintProperty, value);
        }

        public ObservableCollection<BundleComposerRow> Rows { get; } = new ObservableCollection<BundleComposerRow>();

        public bool? DialogResult { get; private set; }

        public event EventHandler RequestClose;

        /// <summary>
        /// Builds the rows from <paramref name="composer"/>, shows the dialog modally and returns
        /// the included choices, or null when cancelled or when nothing was included.
        /// </summary>
        public static IReadOnlyList<BundlePartChoice> Show(
            BundleComposer composer,
            string title,
            string hint,
            Window owner = null)
        {
            if (composer == null)
            {
                throw new ArgumentNullException(nameof(composer));
            }

            var rows = BundleComposer.Parts
                .Select(part => new BundleComposerRow(part, BundleComposer.LabelFor(part), composer.SourcesFor(part), included: true))
                .ToList();

            var dialog = new BundleComposerDialog(hint, rows);
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
