using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace PlayniteAchievements.Views.ManageAchievements
{
    public partial class ManageAchievementsOverviewTab : UserControl
    {
        public ManageAchievementsOverviewTab()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Opens a button's context menu below it on a plain click, so the Export and Import
        /// buttons act as file-or-Workshop menus.
        /// </summary>
        private void ContextMenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || button.ContextMenu == null)
            {
                return;
            }

            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = PlacementMode.Bottom;
            button.ContextMenu.IsOpen = true;
        }
    }
}
