using Playnite.SDK;
using PlayniteAchievements.Services.Logging;
using PlayniteAchievements.Services.Workshop.Preview;
using PlayniteAchievements.ViewModels;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace PlayniteAchievements.Views.Workshop.Preview
{
    /// <summary>One sound of a previewed sound pack, as the list shows it.</summary>
    public sealed class SoundPreviewRow
    {
        public SoundPreviewRow(string tierLabel, string filePath)
        {
            TierLabel = tierLabel;
            FilePath = filePath;
            FileName = string.IsNullOrWhiteSpace(filePath) ? string.Empty : Path.GetFileName(filePath);
        }

        public string TierLabel { get; }

        public string FilePath { get; }

        public string FileName { get; }
    }

    /// <summary>
    /// The sounds of a previewed sound pack (<see cref="UnlockSoundsPreviewModel"/> as the
    /// DataContext): one row per tier the pack carries, each with a Test button that plays the
    /// extracted file.
    /// </summary>
    public partial class SoundsPreviewControl : UserControl
    {
        public static readonly DependencyProperty NeutralRenderProperty = DependencyProperty.Register(
            nameof(NeutralRender), typeof(bool), typeof(SoundsPreviewControl),
            new PropertyMetadata(false, (d, e) => ((SoundsPreviewControl)d).ShowTestButton = !(bool)e.NewValue));

        public static readonly DependencyProperty ShowTestButtonProperty = DependencyProperty.Register(
            nameof(ShowTestButton), typeof(bool), typeof(SoundsPreviewControl), new PropertyMetadata(true));

        private static readonly ILogger Logger = PluginLogger.GetLogger(nameof(SoundsPreviewControl));

        public SoundsPreviewControl()
        {
            InitializeComponent();
            DataContextChanged += (sender, args) => Rebuild();
        }

        /// <summary>True renders without the Test buttons, for an image of the preview.</summary>
        public bool NeutralRender
        {
            get => (bool)GetValue(NeutralRenderProperty);
            set => SetValue(NeutralRenderProperty, value);
        }

        /// <summary>Whether the rows show their Test button; the inverse of <see cref="NeutralRender"/>.</summary>
        public bool ShowTestButton
        {
            get => (bool)GetValue(ShowTestButtonProperty);
            private set => SetValue(ShowTestButtonProperty, value);
        }

        /// <summary>The plugin whose sound service plays the Test sound; the running instance when unset.</summary>
        public PlayniteAchievementsPlugin Plugin { get; set; }

        /// <summary>Raised after a Test button played a sound from the preview's extracted files.</summary>
        public event EventHandler TestPlayed;

        private void Rebuild()
        {
            var slots = (DataContext as UnlockSoundsPreviewModel)?.Slots ?? Array.Empty<UnlockSoundPreviewSlot>();
            SlotList.ItemsSource = slots
                .Select(slot => new SoundPreviewRow(
                    ResourceProvider.GetString(UnlockSoundRowItem.TierLabelKey(slot.Tier)),
                    slot.FilePath))
                .ToList();
        }

        private void TestButton_Click(object sender, RoutedEventArgs e)
        {
            var path = (sender as FrameworkElement)?.Tag as string;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                (Plugin ?? PlayniteAchievementsPlugin.Instance)?.UnlockSounds?.PlayFile(path);
                TestPlayed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"Failed playing previewed sound {path}.");
            }
        }
    }
}
