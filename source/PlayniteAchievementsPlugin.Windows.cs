using System;
using System.Threading.Tasks;
using PlayniteAchievements.ViewModels;

namespace PlayniteAchievements
{
    public partial class PlayniteAchievementsPlugin
    {
        private void ShowRefreshProgressControlAndRun(Func<Task> refreshTask, Guid? singleGameRefreshId = null)
        {
            _windowService.ShowRefreshProgressControlAndRun(refreshTask, OpenSingleGameAchievementsView, singleGameRefreshId);
        }

        private void ShowRefreshProgressControl(
            Guid? singleGameRefreshId = null,
            Func<Task> refreshTask = null,
            bool validateCanStart = false)
        {
            _windowService.ShowRefreshProgressControl(singleGameRefreshId, refreshTask, OpenSingleGameAchievementsView, validateCanStart);
        }

        /// <summary>
        /// Opens the per-game achievements view window for the specified game.
        /// Public for access from theme integration controls.
        /// </summary>
        public void OpenSingleGameAchievementsView(Guid gameId)
        {
            _windowService.OpenSingleGameAchievementsView(gameId);
        }

        /// <summary>
        /// Opens the modern parity test view window for testing theme integration controls.
        /// </summary>
        public void OpenModernParityTestView(Guid gameId)
        {
            _windowService.OpenModernParityTestView(gameId);
        }

        public void OpenGameOptionsView(Guid gameId, GameOptionsTab initialTab = GameOptionsTab.Overview)
        {
            _windowService.OpenGameOptionsView(gameId, initialTab);
        }

        public void OpenCapstoneView(Guid gameId)
        {
            _windowService.OpenCapstoneView(gameId);
        }

        private void EnsureAchievementResourcesLoaded()
        {
            _windowService.EnsureAchievementResourcesLoaded();
        }

        private enum ParityTestMode
        {
            Modern,
            Compatibility
        }

        private void OpenParityTestView(Guid gameId, ParityTestMode mode)
        {
            _windowService.OpenParityTestView(gameId, mode == ParityTestMode.Modern);
        }
    }
}
