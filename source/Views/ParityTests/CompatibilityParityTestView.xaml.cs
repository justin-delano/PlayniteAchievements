using System;
using System.Windows;
using System.Windows.Controls;
using Playnite.SDK.Controls;
using Playnite.SDK.Models;
using PlayniteAchievements.Views.ThemeIntegration.SuccessStory;
using PlayniteAchievements.Models;
using Playnite.SDK;

namespace PlayniteAchievements.Views.ParityTests
{
    public partial class CompatibilityParityTestView : UserControl
    {
        private static readonly ILogger _logger = LogManager.GetLogger(nameof(CompatibilityParityTestView));

        private readonly PlayniteAchievementsPlugin _plugin;
        private readonly Game _game;
        private bool _initialized;

        public string GameName { get; }
        public Guid GameId { get; }
        public PlayniteAchievementsSettings Settings => _plugin.Settings;

        public CompatibilityParityTestView(Game game)
        {
            InitializeComponent();

            _plugin = PlayniteAchievementsPlugin.Instance ?? throw new InvalidOperationException("Plugin instance not available");
            _game = game ?? throw new ArgumentNullException(nameof(game));

            GameName = _game.Name;
            GameId = _game.Id;

            DataContext = this;

            Loaded += CompatibilityParityTestView_Loaded;
        }

        private void CompatibilityParityTestView_Loaded(object sender, RoutedEventArgs e)
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;

            // Defer heavy initialization until after first paint.
            Dispatcher?.BeginInvoke(new Action(() =>
            {
                if (!IsLoaded)
                {
                    _initialized = false;
                    return;
                }

                try
                {
                    // Populate theme properties once up-front.
                    // (This also queues icon resolution and a refresh once thumbnails are ready.)
                    SuccessStoryIntegrationHelper.UpdateThemeProperties(_plugin, _game);

                    ApplyGameToHosts(this, _game);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Compatibility parity test view failed during Loaded.");
                    _plugin?.PlayniteApi?.Dialogs?.ShowErrorMessage(
                        $"Compatibility parity test view failed to load: {ex.Message}",
                        ResourceProvider.GetString("LOCPlayAch_Title_PluginName"));
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private static void ApplyGameToHosts(DependencyObject root, Game game)
        {
            if (root == null)
            {
                return;
            }

            var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);

                if (child is GameViewControlHost host)
                {
                    host.Game = game;
                }

                ApplyGameToHosts(child, game);
            }
        }
    }
}
