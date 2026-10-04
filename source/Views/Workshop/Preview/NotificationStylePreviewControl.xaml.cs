using Playnite.SDK;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Logging;
using PlayniteAchievements.Services.UI;
using PlayniteAchievements.Services.Workshop.Preview;
using PlayniteAchievements.ViewModels;
using PlayniteAchievements.Views.Helpers;
using System;
using System.Windows;
using System.Windows.Controls;

namespace PlayniteAchievements.Views.Workshop.Preview
{
    /// <summary>
    /// A previewed notification style or screenshot frame (<see cref="NotificationStylePreviewModel"/>
    /// as the DataContext). The notification card is built as the settings mockup builds it, through
    /// <see cref="ToastSurfaceFactory"/>; the frame on the compositor's 1080-DIP canvas. The template
    /// is the package's own when it carries one, else what the user would see after installing it.
    /// A test fire and, for frames, the full-screen frame preview use the package's style.
    /// </summary>
    public partial class NotificationStylePreviewControl : UserControl
    {
        public static readonly DependencyProperty NeutralRenderProperty = DependencyProperty.Register(
            nameof(NeutralRender), typeof(bool), typeof(NotificationStylePreviewControl),
            new PropertyMetadata(false, (d, e) => ((NotificationStylePreviewControl)d).Rebuild()));

        private const string DefaultSample = "rare";

        private static readonly ILogger Logger = PluginLogger.GetLogger(nameof(NotificationStylePreviewControl));

        private AchievementToastTemplateResolver _resolver;
        private PlayniteAchievementsPlugin _plugin;
        private Window _framePreviewWindow;
        private bool _initialized;

        // The package's own template for the surface, or null when it carries none (or it failed to load).
        private DataTemplate _packageTemplate;

        public NotificationStylePreviewControl()
        {
            InitializeComponent();
            _initialized = true;
            DataContextChanged += (sender, args) => Rebuild();
            Unloaded += (sender, args) => CloseFramePreview();
        }

        /// <summary>
        /// True renders with nothing taken from the user's setup: the bundled template when the
        /// package carries none, default settings, theme styling off, and no tester controls.
        /// </summary>
        public bool NeutralRender
        {
            get => (bool)GetValue(NeutralRenderProperty);
            set => SetValue(NeutralRenderProperty, value);
        }

        /// <summary>The plugin whose settings and templates the live preview resolves; the running instance when unset.</summary>
        public PlayniteAchievementsPlugin Plugin
        {
            get => _plugin ?? PlayniteAchievementsPlugin.Instance;
            set
            {
                _plugin = value;
                _resolver = null;
                Rebuild();
            }
        }

        /// <summary>Raised after a test notification fired or the frame was shown on screen with the package's files.</summary>
        public event EventHandler TestFired;

        private NotificationStylePreviewModel Model => DataContext as NotificationStylePreviewModel;

        private string SampleKind => NeutralRender
            ? DefaultSample
            : SampleSelector?.SelectedValue as string ?? DefaultSample;

        private PersistedSettings Settings => NeutralRender
            ? new PersistedSettings()
            : Plugin?.Settings?.Persisted ?? new PersistedSettings();

        // Live: null resolves the user's theme-styling choice as a real unlock would. Neutral: off.
        private bool? ThemeStylingOverride => NeutralRender ? false : (bool?)null;

        private AchievementToastTemplateResolver Resolver
        {
            get
            {
                if (_resolver == null)
                {
                    var plugin = Plugin;
                    _resolver = new AchievementToastTemplateResolver(
                        plugin?.PlayniteApi,
                        Logger,
                        customTemplatesDirectory: plugin == null
                            ? null
                            : AchievementToastTemplateResolver.GetCustomTemplatesDirectory(plugin.GetPluginUserDataPath()));
                }

                return _resolver;
            }
        }

        private void Rebuild()
        {
            if (!_initialized)
            {
                return;
            }

            try
            {
                RebuildCore();
            }
            catch (Exception ex)
            {
                // A package can carry values the preview pipeline rejects; show the failure
                // instead of letting it reach the dispatcher.
                Logger.Error(ex, "Failed building the Workshop notification style preview.");
                ShowLoadError(ex.Message);
                ToastHost.Content = null;
                FrameHost.Content = null;
                FrameHost.ContentTemplate = null;
            }
        }

        private void RebuildCore()
        {
            var model = Model;
            var isFrame = model?.IsFrame == true;
            ToastPanel.Visibility = model != null && !isFrame ? Visibility.Visible : Visibility.Collapsed;
            FramePanel.Visibility = model != null && isFrame ? Visibility.Visible : Visibility.Collapsed;
            TesterPanel.Visibility = model != null && !NeutralRender ? Visibility.Visible : Visibility.Collapsed;
            FireButton.Visibility = isFrame ? Visibility.Collapsed : Visibility.Visible;
            FrameOnScreenButton.Visibility = isFrame ? Visibility.Visible : Visibility.Collapsed;

            // The settings frame tester offers no friend or progress sample.
            FriendSample.Visibility = isFrame ? Visibility.Collapsed : Visibility.Visible;
            ProgressSample.Visibility = isFrame ? Visibility.Collapsed : Visibility.Visible;
            if (isFrame && (Equals(SampleSelector.SelectedItem, FriendSample) || Equals(SampleSelector.SelectedItem, ProgressSample)))
            {
                SampleSelector.SelectedValue = DefaultSample;
            }

            ShowLoadError(null);
            _packageTemplate = null;
            ToastHost.Content = null;
            FrameHost.Content = null;
            FrameHost.ContentTemplate = null;
            if (model == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(model.TemplateXaml))
            {
                if (Resolver.TryLoadTemplateFromXaml(model.TemplateXaml, isFrame, out var template, out var error) && template != null)
                {
                    _packageTemplate = template;
                }
                else
                {
                    ShowLoadError(error);
                }
            }

            var viewModel = BuildViewModel(SampleKind);
            if (isFrame)
            {
                FrameHost.ContentTemplate = _packageTemplate ?? ResolveFallbackFrameTemplate(viewModel);
                FrameHost.Content = viewModel;
                return;
            }

            var items = new[] { viewModel };
            var toastTemplate = _packageTemplate ?? (NeutralRender
                ? Resolver.ResolveBundledDefaultTemplate(isFrame: false)
                : ToastSurfaceFactory.ResolveToastTemplate(Resolver, items, viewModel.ToastUseThemeStyling, null, Guid.Empty));
            ToastHost.Content = ToastSurfaceFactory.BuildToastSurface(items, toastTemplate);
        }

        private DataTemplate ResolveFallbackFrameTemplate(AchievementToastViewModel viewModel)
        {
            return NeutralRender
                ? Resolver.ResolveBundledDefaultTemplate(isFrame: true)
                : Resolver.ResolveFrameTemplate(viewModel.FrameUseThemeStyling, null, Guid.Empty);
        }

        private AchievementToastViewModel BuildViewModel(string sampleKind, NotificationTemplatePreviewSource? previewSource = null)
        {
            return BuildViewModel(ToastPreviewFactory.BuildPreviewArgs(sampleKind, providerKey: null, previewSource));
        }

        private AchievementToastViewModel BuildViewModel(AchievementUnlockedEventArgs args)
        {
            return new AchievementToastViewModel(
                args,
                Settings,
                Model?.Style,
                gameCustomDataStore: null,
                toastUseThemeStylingOverride: ThemeStylingOverride,
                frameUseThemeStylingOverride: ThemeStylingOverride);
        }

        private void ShowLoadError(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                LoadErrorText.Text = string.Empty;
                LoadErrorText.Visibility = Visibility.Collapsed;
                return;
            }

            LoadErrorText.Text = string.Format(ResourceProvider.GetString("LOCPlayAch_Status_Failed"), error);
            LoadErrorText.Visibility = Visibility.Visible;
        }

        private void SampleSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Rebuild();
        }

        private void FireButton_Click(object sender, RoutedEventArgs e)
        {
            var model = Model;
            if (model == null || model.IsFrame)
            {
                return;
            }

            try
            {
                // A fire-test as the settings tester fires one: the package's style verbatim, and
                // its template when it carries one, else the template the user's own setup resolves.
                var probe = BuildViewModel(SampleKind);
                var source = probe.ToastUseThemeStyling
                    ? NotificationTemplatePreviewSource.ActiveTheme
                    : NotificationTemplatePreviewSource.PluginStyle;
                var args = ToastPreviewFactory.BuildPreviewArgs(SampleKind, providerKey: null, previewSource: source);
                args.PreviewStyleOverride = model.Style;
                args.PreviewTemplateOverride = _packageTemplate;
                PlayniteAchievementsPlugin.NotifyAchievementUnlocked(args);
                TestFired?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed firing the Workshop notification style preview.");
                ShowLoadError(ex.Message);
            }
        }

        private void FrameOnScreenButton_Click(object sender, RoutedEventArgs e)
        {
            var model = Model;
            if (model == null || !model.IsFrame)
            {
                return;
            }

            try
            {
                CloseFramePreview();
                var viewModel = BuildViewModel(SampleKind);
                var template = _packageTemplate ?? ResolveFallbackFrameTemplate(viewModel);
                var window = FramePreviewOverlay.Show(Plugin?.PlayniteApi, Window.GetWindow(this), template, viewModel);
                if (window == null)
                {
                    return;
                }

                window.Closed += (s, args) =>
                {
                    if (ReferenceEquals(_framePreviewWindow, window))
                    {
                        _framePreviewWindow = null;
                    }
                };
                _framePreviewWindow = window;
                TestFired?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed showing the Workshop frame preview on screen.");
                ShowLoadError(ex.Message);
            }
        }

        private void CloseFramePreview()
        {
            try
            {
                _framePreviewWindow?.Close();
            }
            catch (InvalidOperationException)
            {
            }

            _framePreviewWindow = null;
        }
    }
}
