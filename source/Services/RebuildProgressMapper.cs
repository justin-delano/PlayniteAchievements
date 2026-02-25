using System;
using PlayniteAchievements.Models;
using Playnite.SDK;

namespace PlayniteAchievements.Services
{
    internal sealed class RebuildProgressMapper
    {
        public void Reset()
        {
            // No mapper state is currently persisted between updates.
        }

        public ProgressReport Map(RebuildUpdate update)
        {
            if (update == null)
            {
                return null;
            }

            switch (update.Kind)
            {
                case RebuildUpdateKind.Stage:
                    return MapStageUpdate(update);

                case RebuildUpdateKind.UserProgress:
                    return MapUserProgressUpdate(update);

                case RebuildUpdateKind.UserCompleted:
                    return MapUserCompletedUpdate(update);

                case RebuildUpdateKind.Completed:
                    return MapCompletedUpdate(update);

                case RebuildUpdateKind.AuthRequired:
                    return Build(
                        ResourceProvider.GetString("LOCPlayAch_Status_AuthRequired"),
                        0, 1, canceled: true);

                default:
                    return null;
            }
        }

        private ProgressReport MapStageUpdate(RebuildUpdate update)
        {
            // Handle the NotConfigured stage as an error/cancellation
            if (update.Stage == RebuildStage.NotConfigured)
            {
                return Build(StageMessage(RebuildStage.NotConfigured), 0, 1, canceled: true);
            }

            // Handle completion stage
            if (update.Stage == RebuildStage.Completed)
            {
                var (cur, tot) = ProgressSteps(update, 1, 1);
                return Build(ResourceProvider.GetString("LOCPlayAch_Rebuild_Completed"), cur, tot);
            }

            // Regular stage updates (loading owned games, loading cache, etc.)
            var message = StageMessage(update.Stage);
            if (string.IsNullOrWhiteSpace(message))
            {
                message = ResourceProvider.GetString("LOCPlayAch_Status_UpdatingCache");
            }

            var steps = ProgressSteps(update);
            return Build(message, steps.Cur, steps.Total);
        }

        private ProgressReport MapUserProgressUpdate(RebuildUpdate update)
        {
            var (cur, tot) = ProgressSteps(update);
            var gameName = !string.IsNullOrWhiteSpace(update.CurrentGameName)
                ? update.CurrentGameName
                : ResourceProvider.GetString("LOCPlayAch_Text_UnknownGame");

            string message;
            if (update.TotalIcons > 0)
            {
                message = string.Format(
                    ResourceProvider.GetString("LOCPlayAch_Targeted_RefreshingGameWithIcons"),
                    gameName,
                    update.IconsDownloaded,
                    update.TotalIcons);
            }
            else
            {
                var countsText = CountsText(update.UserAppIndex + 1, update.UserAppCount);
                message = string.Format(
                    ResourceProvider.GetString("LOCPlayAch_Targeted_RefreshingGameWithCounts"),
                    gameName,
                    countsText);
            }

            return Build(message, cur, tot);
        }

        private ProgressReport MapUserCompletedUpdate(RebuildUpdate update)
        {
            var (cur, tot) = ProgressSteps(update, 1, 1);
            var message = ResourceProvider.GetString("LOCPlayAch_Rebuild_YourCacheUpToDate");

            return Build(message, cur, tot);
        }

        private ProgressReport MapCompletedUpdate(RebuildUpdate update)
        {
            var (cur, tot) = ProgressSteps(update, 1, 1);
            var message = ResourceProvider.GetString("LOCPlayAch_Rebuild_Completed");

            return Build(message, cur, tot);
        }

        private ProgressReport Build(string message, int current = 0, int total = 0, bool canceled = false)
            => new ProgressReport { Message = message, CurrentStep = current, TotalSteps = total, IsCanceled = canceled };

        private string StageMessage(RebuildStage stage)
        {
            switch (stage)
            {
                case RebuildStage.NotConfigured: return ResourceProvider.GetString("LOCPlayAch_Error_SteamNotConfigured");
                case RebuildStage.LoadingOwnedGames: return ResourceProvider.GetString("LOCPlayAch_Targeted_LoadingOwnedGames");
                case RebuildStage.LoadingExistingCache: return ResourceProvider.GetString("LOCPlayAch_Targeted_LoadingExistingCache");
                case RebuildStage.RefreshingUserAchievements: return ResourceProvider.GetString("LOCPlayAch_Targeted_RefreshingSelfAchievements");
                case RebuildStage.Completed: return ResourceProvider.GetString("LOCPlayAch_Rebuild_Completed");
                default: return null;
            }
        }

        private (int Cur, int Total) ProgressSteps(RebuildUpdate update, int fallbackCur = 0, int fallbackTotal = 0)
        {
            if (update != null && update.OverallCount > 0)
            {
                return (Math.Max(0, update.OverallIndex), Math.Max(1, update.OverallCount));
            }

            return (Math.Max(0, fallbackCur), Math.Max(0, fallbackTotal));
        }

        private string CountsText(int current, int total)
        {
            if (total > 0)
            {
                return string.Format(ResourceProvider.GetString("LOCPlayAch_Format_Counts"), Math.Max(0, current), Math.Max(1, total));
            }

            return ResourceProvider.GetString("LOCPlayAch_Text_Ellipsis");
        }
    }
}
