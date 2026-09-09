using System;
using System.Linq;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models;
using PlayniteAchievements.ViewModels.Items;

namespace PlayniteAchievements.ViewModels.Showcase.Widgets
{
    /// <summary>
    /// Backs the IconMosaic widget by reusing AchievementCompactItemControl for each achievement.
    /// Icon size follows density (compact 32 / standard 42 / expanded 54); every size shows the
    /// same icons. Rarity-glow appearance comes from the widget's own per-instance options.
    /// </summary>
    public sealed class IconMosaicWidgetViewModel : ShowcaseWidgetViewModelBase
    {
        private double _iconSize = 42;
        private bool _showRarityGlow = true;
        private bool _animateRarityGlows = true;

        public BulkObservableCollection<AchievementDisplayItem> Items { get; } =
            new BulkObservableCollection<AchievementDisplayItem>();

        public double IconSize { get => _iconSize; private set => SetValue(ref _iconSize, value); }

        public bool ShowRarityGlow { get => _showRarityGlow; private set => SetValue(ref _showRarityGlow, value); }

        public bool AnimateRarityGlows { get => _animateRarityGlows; private set => SetValue(ref _animateRarityGlows, value); }

        protected override void Refresh()
        {
            var achievements = Projection?.MosaicAchievements ?? Array.Empty<AchievementDisplayItem>();
            IconSize = Density == WidgetViewportDensity.Compact
                ? 32
                : Density == WidgetViewportDensity.Expanded ? 54 : 42;

            // Glow on/off is a per-widget option; the glow ANIMATION stays a global setting.
            ShowRarityGlow = ShowcaseWidgetOptions.GetMosaicShowRarityGlow(Projection?.Instance);
            AnimateRarityGlows =
                PlayniteAchievementsPlugin.Instance?.Settings?.Persisted?.AnimateRarityGlows ?? true;

            Items.ReplaceAll(achievements);
        }
    }
}
