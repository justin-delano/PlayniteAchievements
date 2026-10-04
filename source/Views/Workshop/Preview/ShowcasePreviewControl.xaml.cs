using Playnite.SDK;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Workshop.Preview;
using System;
using System.Linq;
using System.Windows.Controls;

namespace PlayniteAchievements.Views.Workshop.Preview
{
    /// <summary>One block of a previewed showcase page: the widget's kind and its custom title.</summary>
    public sealed class ShowcaseBlockRow
    {
        public ShowcaseBlockRow(string kindLabel, string title)
        {
            KindLabel = kindLabel;
            Title = title;
        }

        public string KindLabel { get; }

        public string Title { get; }

        public bool HasTitle => !string.IsNullOrWhiteSpace(Title);
    }

    /// <summary>
    /// A previewed showcase page (<see cref="ShowcasePagePreviewModel"/> as the DataContext): its
    /// name, a card per block naming the widget, and thumbnails of the images it bundles.
    /// </summary>
    public partial class ShowcasePreviewControl : UserControl
    {
        public ShowcasePreviewControl()
        {
            InitializeComponent();
            DataContextChanged += (sender, args) => Rebuild();
        }

        private void Rebuild()
        {
            var model = DataContext as ShowcasePagePreviewModel;
            BlockList.ItemsSource = (model?.Blocks ?? Array.Empty<ShowcaseBlockPreview>())
                .Select(block => new ShowcaseBlockRow(KindLabel(block.WidgetKind), block.Title))
                .ToList();
            ImageList.ItemsSource = model?.ImagePaths ?? Array.Empty<string>();
        }

        private static string KindLabel(ShowcaseWidgetKind kind)
        {
            var definition = ShowcaseWidgetCatalog.Definitions.FirstOrDefault(candidate => candidate.Kind == kind);
            return definition == null ? kind.ToString() : ResourceProvider.GetString(definition.NameKey);
        }
    }
}
