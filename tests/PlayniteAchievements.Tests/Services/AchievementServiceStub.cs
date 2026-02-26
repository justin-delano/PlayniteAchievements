using PlayniteAchievements.Models;
using System.Threading.Tasks;

namespace PlayniteAchievements.Services
{
    // Test-only seam used to compile RefreshCoordinator source without pulling full plugin dependencies.
    public class AchievementService
    {
        public virtual bool ValidateCanStartRefresh()
        {
            return true;
        }

        public virtual Task ExecuteRefreshAsync(RefreshRequest request)
        {
            return Task.CompletedTask;
        }
    }
}

