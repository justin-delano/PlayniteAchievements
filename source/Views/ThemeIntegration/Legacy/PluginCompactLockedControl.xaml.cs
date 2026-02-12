// --SUCCESSSTORY--
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.ThemeIntegration;
using PlayniteAchievements.Views.ThemeIntegration.Base;
using PlayniteAchievements.Views.ThemeIntegration.Legacy.Controls;

namespace PlayniteAchievements.Views.ThemeIntegration.Legacy
{
    /// <summary>
    /// SuccessStory-compatible compact locked control for theme integration.
    /// Displays locked achievements in a horizontal grid, with "+X more" in the last column.
    /// </summary>
    public partial class PluginCompactLockedControl : CompactAchievementControlBase
    {
        protected override Grid CompactViewGrid => PART_ScCompactView;

        public PluginCompactLockedControl()
        {
            InitializeComponent();
            DataContext = this;

            // Some themes (e.g., FusionX derivatives) place a sibling "Locked Achievements X/N"
            // header next to this control but bind X to `Unlocked`. Patch that header at runtime
            // to bind X to `Locked` without requiring theme file edits.
            Loaded += PluginCompactLockedControl_Loaded;
            Unloaded += PluginCompactLockedControl_Unloaded;
        }

        private void PluginCompactLockedControl_Loaded(object sender, RoutedEventArgs e)
        {
            Dispatcher?.BeginInvoke(new System.Action(TryPatchSiblingLockedCountHeader), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void PluginCompactLockedControl_Unloaded(object sender, RoutedEventArgs e)
        {
            // No-op: bindings are recreated by the theme when controls are reloaded.
        }

        private void TryPatchSiblingLockedCountHeader()
        {
            if (Plugin?.Settings == null)
            {
                return;
            }

            var current = (DependencyObject)this;
            while (current != null)
            {
                if (current is UIElement element)
                {
                    if (VisualTreeHelper.GetParent(current) is Panel panel)
                    {
                        int index = panel.Children.IndexOf(element);
                        if (index > 0)
                        {
                            int start = System.Math.Max(0, index - 3);
                            for (int i = start; i < index; i++)
                            {
                                if (panel.Children[i] is StackPanel sibling && sibling.Orientation == Orientation.Horizontal)
                                {
                                    PatchUnlockedBindingToLocked(sibling);
                                }
                            }
                        }
                    }
                }

                current = VisualTreeHelper.GetParent(current);
            }
        }

        private void PatchUnlockedBindingToLocked(StackPanel headerPanel)
        {
            bool hasTotalBinding = false;
            bool hasSlashText = false;

            for (int i = 0; i < headerPanel.Children.Count; i++)
            {
                if (headerPanel.Children[i] is TextBlock tb)
                {
                    if ((tb.Text ?? string.Empty).Trim() == "/")
                    {
                        hasSlashText = true;
                    }

                    var binding = BindingOperations.GetBinding(tb, TextBlock.TextProperty);
                    if (binding?.Path?.Path == nameof(PlayniteAchievementsSettings.Total))
                    {
                        hasTotalBinding = true;
                    }
                }
            }

            if (!hasTotalBinding || !hasSlashText)
            {
                return;
            }

            for (int i = 0; i < headerPanel.Children.Count; i++)
            {
                if (headerPanel.Children[i] is TextBlock tb)
                {
                    var binding = BindingOperations.GetBinding(tb, TextBlock.TextProperty);
                    if (binding?.Path?.Path == nameof(PlayniteAchievementsSettings.Unlocked))
                    {
                        BindingOperations.SetBinding(
                            tb,
                            TextBlock.TextProperty,
                            new Binding(nameof(PlayniteAchievementsSettings.Locked))
                            {
                                Source = Plugin.Settings,
                                Mode = BindingMode.OneWay,
                                FallbackValue = string.Empty
                            });
                    }
                }
            }
        }

        protected override List<AchievementDetail> GetSourceAchievements()
        {
            var all = Plugin?.Settings?.LegacyTheme?.ListAchievements;
            if (all == null || all.Count == 0)
            {
                return new List<AchievementDetail>();
            }

            var result = new List<AchievementDetail>();
            for (int i = 0; i < all.Count; i++)
            {
                var a = all[i];
                if (a != null && !a.Unlocked)
                {
                    result.Add(a);
                }
            }

            return result;
        }

        protected override int GetTotalCount()
        {
            return Plugin?.Settings?.LegacyTheme?.Locked ?? 0;
        }

        protected override string GetHasAchievementPropertyName()
        {
            return nameof(HasLocked);
        }

        protected override string GetTotalCountPropertyName()
        {
            return nameof(TotalLocked);
        }

        protected override string[] GetSettingsPropertyNames()
        {
            return new[]
            {
                nameof(LegacyThemeData.ListAchievements),
                nameof(LegacyThemeData.Locked)
            };
        }

        protected override AchievementImage CreateAchievementImage(AchievementDetail achievement)
        {
            return new AchievementImage
            {
                Width = IconHeight,
                Height = IconHeight,
                ToolTip = achievement.DisplayName,
                // Icon is for unlocked state, IconCustom is for locked state
                Icon = achievement.UnlockedIconDisplay,
                IconCustom = achievement.LockedIconDisplay,
                IsLocked = true,
                Percent = achievement.GlobalPercentUnlocked ?? 0,
                EnableRaretyIndicator = true,
                DisplayRaretyValue = true,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
        }

        public bool HasLocked => GetTotalCount() > 0;

        public int TotalLocked => GetTotalCount();

        // RemainingCount is provided by the base class
    }
}
// --END SUCCESSSTORY--
