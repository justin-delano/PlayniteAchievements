using Playnite.SDK;
using PlayniteAchievements.Services.Workshop;
using PlayniteAchievements.Services.Workshop.Preview;
using PlayniteAchievements.ViewModels.Workshop;
using PlayniteAchievements.Views.Helpers;
using PlayniteAchievements.Views.Workshop.Preview;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace PlayniteAchievements.Views.Workshop
{
    /// <summary>
    /// Shows what a downloaded Workshop package contains before it is installed: a header naming
    /// the item, the kind's preview (one tab per part for a bundle), and Install or Update next to
    /// Close. Owns the preview model and disposes it on close, later when a test notification or
    /// sound may still be reading the model's extracted files.
    /// </summary>
    public partial class WorkshopPreviewDialog : UserControl
    {
        /// <summary>The window placement key every preview shares.</summary>
        public const string WindowPlacementKey = "WorkshopPreview";

        // How long a fired test notification can still be reading the preview's extracted files.
        private static readonly TimeSpan TestFileHold = TimeSpan.FromSeconds(15);

        private readonly PlayniteAchievementsPlugin _plugin;
        private readonly WorkshopPreviewModel _model;
        private DateTime _lastTestUtc;
        private bool _released;

        public WorkshopPreviewDialog()
        {
            InitializeComponent();
        }

        private WorkshopPreviewDialog(PlayniteAchievementsPlugin plugin, WorkshopItemViewModel row, WorkshopPreviewModel model)
            : this()
        {
            _plugin = plugin;
            _model = model ?? throw new ArgumentNullException(nameof(model));

            NameText.Text = row?.Name ?? string.Empty;
            KindRun.Text = row?.KindLabel ?? WorkshopItemViewModel.KindLabelFor(model.Kind);
            VersionRun.Text = row?.Version ?? string.Empty;
            AuthorRun.Text = row?.Author ?? string.Empty;
            if (model.Kind == WorkshopItemKind.GameCustomData && !string.IsNullOrWhiteSpace(row?.GameName))
            {
                GameRun.Text = row.GameName;
                GameLine.Visibility = Visibility.Visible;
            }

            InstallButton.Content = row?.ActionLabel ?? ResourceProvider.GetString("LOCPlayAch_Workshop_Install");
            InstallButton.IsEnabled = row?.CanInstall == true;

            BodyHost.Content = BuildBody(model);
        }

        /// <summary>True when the user chose Install or Update.</summary>
        public bool InstallRequested { get; private set; }

        public event EventHandler RequestClose;

        /// <summary>
        /// Shows the preview modally and returns true when the user chose Install or Update. The
        /// dialog takes ownership of <paramref name="model"/> and disposes it.
        /// </summary>
        public static bool Show(
            PlayniteAchievementsPlugin plugin,
            WorkshopItemViewModel row,
            WorkshopPreviewModel model,
            Window owner = null)
        {
            if (model == null)
            {
                return false;
            }

            WorkshopPreviewDialog dialog = null;
            try
            {
                dialog = new WorkshopPreviewDialog(plugin, row, model);
                var size = DefaultSize(model.Kind);
                var window = PlayniteUiProvider.CreateExtensionWindow(
                    row?.Name ?? ResourceProvider.GetString("LOCPlayAch_Common_Preview"),
                    dialog,
                    new WindowOptions
                    {
                        ShowMinimizeButton = false,
                        ShowMaximizeButton = true,
                        ShowCloseButton = true,
                        CanBeResizable = true,
                        Width = size.Width,
                        Height = size.Height
                    });
                window.MinWidth = 480;
                window.MinHeight = 360;
                WindowPlacementPersistenceService.Attach(window, WindowPlacementKey);

                try
                {
                    window.Owner = owner ?? API.Instance?.Dialogs?.GetCurrentAppWindow();
                }
                catch (InvalidOperationException)
                {
                }

                dialog.RequestClose += (sender, args) => window.Close();
                window.ShowDialog();
                return dialog.InstallRequested;
            }
            finally
            {
                if (dialog != null)
                {
                    dialog.ReleaseModel();
                }
                else
                {
                    model.Dispose();
                }
            }
        }

        private static Size DefaultSize(WorkshopItemKind kind)
        {
            switch (kind)
            {
                case WorkshopItemKind.Colors:
                    return new Size(600, 640);
                case WorkshopItemKind.UnlockSounds:
                    return new Size(600, 440);
                case WorkshopItemKind.NotificationStyle:
                    return new Size(760, 560);
                case WorkshopItemKind.ScreenshotFrame:
                    return new Size(860, 600);
                case WorkshopItemKind.ShowcasePage:
                    return new Size(760, 600);
                case WorkshopItemKind.GameCustomData:
                    return new Size(1000, 720);
                default:
                    return new Size(860, 680);
            }
        }

        private FrameworkElement BuildBody(WorkshopPreviewModel model)
        {
            if (model is BundlePreviewModel bundle)
            {
                var tabs = new TabControl { Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0) };
                AddTab(tabs, "LOCPlayAch_Settings_Display_Colors", bundle.Colors);
                AddTab(tabs, "LOCPlayAch_Workshop_Share_Sounds", bundle.Sounds);
                AddTab(tabs, "LOCPlayAch_Settings_Style_ToastTab", bundle.Toast);
                AddTab(tabs, "LOCPlayAch_Settings_FrameHeader", bundle.Frame);
                if (tabs.Items.Count > 0)
                {
                    tabs.SelectedIndex = 0;
                }

                return tabs;
            }

            return BuildKindView(model);
        }

        private void AddTab(TabControl tabs, string headerKey, WorkshopPreviewModel part)
        {
            if (part == null)
            {
                return;
            }

            var tab = new TabItem { Content = BuildKindView(part) };
            tab.SetResourceReference(HeaderedContentControl.HeaderProperty, headerKey);
            tabs.Items.Add(tab);
        }

        /// <summary>The kind's preview control; every kind but game data scrolls as a whole.</summary>
        private FrameworkElement BuildKindView(WorkshopPreviewModel model)
        {
            FrameworkElement view;
            switch (model)
            {
                case ColorsPreviewModel _:
                    view = new ColorsPreviewControl();
                    break;
                case UnlockSoundsPreviewModel _:
                    var sounds = new SoundsPreviewControl { Plugin = _plugin };
                    sounds.TestPlayed += OnTestUsedFiles;
                    view = sounds;
                    break;
                case NotificationStylePreviewModel _:
                    var style = new NotificationStylePreviewControl { Plugin = _plugin };
                    style.TestFired += OnTestUsedFiles;
                    view = style;
                    break;
                case ShowcasePagePreviewModel _:
                    view = new ShowcasePreviewControl();
                    break;
                case GameCustomDataPreviewModel _:
                    // Its list virtualizes and scrolls itself.
                    return new GameDataPreviewControl { DataContext = model };
                default:
                    return new TextBlock();
            }

            view.DataContext = model;
            return new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = view
            };
        }

        private void OnTestUsedFiles(object sender, EventArgs e)
        {
            _lastTestUtc = DateTime.UtcNow;
        }

        /// <summary>
        /// Disposes the model now, or once <see cref="TestFileHold"/> has passed since the last
        /// test, so a notification still on screen keeps its image files. Runs once.
        /// </summary>
        private void ReleaseModel()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            var remaining = _lastTestUtc == default(DateTime)
                ? TimeSpan.Zero
                : TestFileHold - (DateTime.UtcNow - _lastTestUtc);
            if (remaining <= TimeSpan.Zero)
            {
                _model.Dispose();
                return;
            }

            var model = _model;
            var timer = new DispatcherTimer { Interval = remaining };
            timer.Tick += (sender, args) =>
            {
                timer.Stop();
                model.Dispose();
            };
            timer.Start();
        }

        private void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            InstallRequested = true;
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            InstallRequested = false;
            RequestClose?.Invoke(this, EventArgs.Empty);
        }
    }
}
