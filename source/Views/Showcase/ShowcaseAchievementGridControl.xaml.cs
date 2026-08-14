using System;
using System.Windows;
using System.Windows.Controls;
using PlayniteAchievements.Services.Showcase;

namespace PlayniteAchievements.Views.Showcase
{
    /// <summary>
    /// Hosts the shared achievement grid inside showcase widget bodies, contributing the
    /// pin reorder items for rows that are pinned achievements.
    /// </summary>
    public partial class ShowcaseAchievementGridControl : ShowcaseGridHostBase
    {
        public ShowcaseAchievementGridControl()
        {
            InitializeComponent();
        }

        protected override FrameworkElement GridElement => InnerGrid;

        protected override void RefreshGrid() => InnerGrid?.Refresh();

        protected override void AppendPinReorderItems(ContextMenu menu, object data)
        {
            if (!EnablePinReorder ||
                !ShowcasePinService.TryGetAchievementIdentity(
                    data,
                    out var gameId,
                    out var apiName,
                    out _,
                    out _,
                    out _))
            {
                return;
            }

            var showcase = PlayniteAchievementsPlugin.Instance?.Settings?.Persisted?.Showcase;
            if (showcase == null ||
                !ShowcasePinService.IsAchievementPinned(showcase, gameId, apiName))
            {
                return;
            }

            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMoveItem(
                "LOCPlayAch_Showcase_MoveEarlier",
                () => MovePin(gameId, apiName, -1)));
            menu.Items.Add(CreateMoveItem(
                "LOCPlayAch_Showcase_MoveLater",
                () => MovePin(gameId, apiName, 1)));
        }

        private static void MovePin(Guid gameId, string apiName, int direction)
        {
            var showcase = PlayniteAchievementsPlugin.Instance?.Settings?.Persisted?.Showcase;
            if (showcase != null &&
                ShowcasePinService.MoveAchievement(showcase, gameId, apiName, direction))
            {
                ShowcaseConfigurationCommit.Commit();
            }
        }
    }
}
