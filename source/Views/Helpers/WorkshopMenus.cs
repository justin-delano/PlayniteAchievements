using System;
using System.Windows.Controls;
using Playnite.SDK;

namespace PlayniteAchievements.Views.Helpers
{
    /// <summary>
    /// The Import and Export menus shared by every surface that owns a portable format: the
    /// local-file action the surface always had, with the Workshop beside it.
    /// </summary>
    internal static class WorkshopMenus
    {
        /// <summary>Opens "From file... / From Workshop..." under <paramref name="button"/>.</summary>
        public static void OpenImport(Button button, Action fromFile, Action fromWorkshop, bool workshopEnabled = true)
        {
            SelectorContextMenuHelper.Open(button, Build(ImportItems(fromFile, fromWorkshop, workshopEnabled)));
        }

        /// <summary>Opens "To file... / Share to Workshop" under <paramref name="button"/>.</summary>
        public static void OpenExport(Button button, Action toFile, Action shareToWorkshop, bool workshopEnabled = true)
        {
            SelectorContextMenuHelper.Open(button, Build(ExportItems(toFile, shareToWorkshop, workshopEnabled)));
        }

        /// <summary>The two import items, for hosts that place them in a menu of their own.</summary>
        public static MenuItem[] ImportItems(Action fromFile, Action fromWorkshop, bool workshopEnabled = true)
        {
            return new[]
            {
                Item("LOCPlayAch_Workshop_Menu_FromFile", fromFile, true),
                Item("LOCPlayAch_Workshop_Menu_FromWorkshop", fromWorkshop, workshopEnabled)
            };
        }

        /// <summary>The two export items, for hosts that place them in a menu of their own.</summary>
        public static MenuItem[] ExportItems(Action toFile, Action shareToWorkshop, bool workshopEnabled = true)
        {
            return new[]
            {
                Item("LOCPlayAch_Workshop_Menu_ToFile", toFile, true),
                Item("LOCPlayAch_Workshop_Share", shareToWorkshop, workshopEnabled)
            };
        }

        private static ContextMenu Build(MenuItem[] items)
        {
            var menu = new ContextMenu();
            foreach (var item in items)
            {
                menu.Items.Add(item);
            }

            return menu;
        }

        private static MenuItem Item(string key, Action action, bool isEnabled)
        {
            var item = new MenuItem
            {
                Header = ResourceProvider.GetString(key),
                IsEnabled = isEnabled && action != null
            };
            item.Click += (_, __) => action?.Invoke();
            return item;
        }
    }
}
