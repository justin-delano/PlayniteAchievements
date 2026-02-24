using System.ComponentModel;
using System.Windows.Controls;

namespace PlayniteAchievements.Views.Helpers
{
    /// <summary>
    /// Provides uniform sorting behavior for DataGrid controls.
    /// </summary>
    public static class DataGridSortingHelper
    {
        /// <summary>
        /// Handles the DataGrid.Sorting event with uniform sort direction toggling.
        /// Sets e.Handled to true, toggles the sort direction, clears other columns' sort indicators,
        /// and returns the computed sort direction for the caller to apply.
        /// </summary>
        /// <param name="sender">The DataGrid that raised the Sorting event.</param>
        /// <param name="e">The DataGridSortingEventArgs.</param>
        /// <returns>The new sort direction, or null if the column is invalid.</returns>
        public static ListSortDirection? HandleSorting(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true;

            var column = e.Column;
            if (column == null || string.IsNullOrEmpty(column.SortMemberPath))
            {
                return null;
            }

            var sortDirection = ListSortDirection.Ascending;
            if (column.SortDirection != null && column.SortDirection == ListSortDirection.Ascending)
            {
                sortDirection = ListSortDirection.Descending;
            }

            // Clear other columns' sort direction
            if (sender is DataGrid grid)
            {
                foreach (var c in grid.Columns)
                {
                    if (c != column)
                    {
                        c.SortDirection = null;
                    }
                }
            }

            column.SortDirection = sortDirection;
            return sortDirection;
        }
    }
}
