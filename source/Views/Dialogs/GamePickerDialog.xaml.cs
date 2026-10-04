using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace PlayniteAchievements.Views.Dialogs
{
    /// <summary>
    /// Picks one library game from a list, filtered as the user types. Hosted in a Playnite
    /// extension window by <see cref="Helpers.PlayniteGamePickerDialog"/>, so it draws with the
    /// plugin's themed controls and brushes rather than WPF defaults.
    /// </summary>
    public partial class GamePickerDialog : UserControl
    {
        private readonly ICollectionView _view;

        public GamePickerDialog()
        {
            InitializeComponent();
        }

        public GamePickerDialog(IEnumerable<Game> games, string initialSearch) : this()
        {
            var items = new ObservableCollection<PickerItem>(
                (games ?? Enumerable.Empty<Game>())
                    .Where(game => game != null && game.Id != Guid.Empty)
                    .OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Select(game => new PickerItem(game)));

            _view = CollectionViewSource.GetDefaultView(items);
            _view.Filter = FilterGame;
            GamesList.ItemsSource = _view;
            SearchBox.Text = initialSearch ?? string.Empty;
            SelectFirst();

            Loaded += (sender, args) => Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    // Keyboard focus is taken back when the host window shows; asking again at
                    // input priority runs after that.
                    SearchBox.Focus();
                    Keyboard.Focus(SearchBox);
                    SearchBox.SelectAll();
                }),
                System.Windows.Threading.DispatcherPriority.Input);
        }

        public Game SelectedGame { get; private set; }

        public bool? DialogResult { get; private set; }

        public event EventHandler RequestClose;

        private bool FilterGame(object value)
        {
            if (!(value is PickerItem item))
            {
                return false;
            }

            var query = SearchBox?.Text;
            return string.IsNullOrWhiteSpace(query) ||
                   item.SearchText.IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SelectFirst()
        {
            if (GamesList.SelectedItem == null && GamesList.Items.Count > 0)
            {
                GamesList.SelectedIndex = 0;
            }
        }

        private void Accept()
        {
            if (!(GamesList.SelectedItem is PickerItem item))
            {
                return;
            }

            SelectedGame = item.Game;
            DialogResult = true;
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        private void Cancel()
        {
            DialogResult = false;
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _view?.Refresh();
            SelectFirst();
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Down when GamesList.Items.Count > 0:
                    GamesList.SelectedIndex = Math.Min(GamesList.SelectedIndex + 1, GamesList.Items.Count - 1);
                    GamesList.ScrollIntoView(GamesList.SelectedItem);
                    e.Handled = true;
                    break;
                case Key.Up when GamesList.Items.Count > 0:
                    GamesList.SelectedIndex = Math.Max(GamesList.SelectedIndex - 1, 0);
                    GamesList.ScrollIntoView(GamesList.SelectedItem);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    Cancel();
                    e.Handled = true;
                    break;
            }
        }

        private void GamesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            Accept();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Accept();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Cancel();
        }

        private sealed class PickerItem
        {
            public PickerItem(Game game)
            {
                Game = game;
                Name = game.Name ?? string.Empty;
                var source = game.Source?.Name;
                var platforms = string.Join(
                    ", ",
                    (game.Platforms ?? Enumerable.Empty<Platform>())
                        .Where(platform => !string.IsNullOrWhiteSpace(platform?.Name))
                        .Select(platform => platform.Name)
                        .Distinct(StringComparer.OrdinalIgnoreCase));
                Metadata = string.Join(
                    " | ",
                    new[] { source, platforms }.Where(value => !string.IsNullOrWhiteSpace(value)));
                SearchText = string.Join(" ", Name, source, platforms);
            }

            public Game Game { get; }

            public string Name { get; }

            public string Metadata { get; }

            public bool HasMetadata => Metadata.Length > 0;

            public string SearchText { get; }
        }
    }
}
