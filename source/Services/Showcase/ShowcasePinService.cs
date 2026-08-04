using System;
using System.Linq;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.ViewModels.Items;

namespace PlayniteAchievements.Services.Showcase
{
    public static class ShowcasePinService
    {
        public static bool IsGamePinned(ShowcaseSettings settings, Guid gameId)
        {
            return gameId != Guid.Empty &&
                settings?.PinnedGameIds?.Contains(gameId) == true;
        }

        public static bool ToggleGame(ShowcaseSettings settings, Guid gameId)
        {
            if (settings == null || gameId == Guid.Empty)
            {
                return false;
            }

            settings.PinnedGameIds = settings.PinnedGameIds ??
                new System.Collections.Generic.List<Guid>();
            if (settings.PinnedGameIds.Remove(gameId))
            {
                return false;
            }

            settings.PinnedGameIds.Add(gameId);
            return true;
        }

        public static bool MoveGame(ShowcaseSettings settings, Guid gameId, int direction)
        {
            var pins = settings?.PinnedGameIds;
            if (pins == null || direction == 0)
            {
                return false;
            }

            var index = pins.IndexOf(gameId);
            var target = Math.Max(0, Math.Min(pins.Count - 1, index + Math.Sign(direction)));
            if (index < 0 || target == index)
            {
                return false;
            }

            pins.RemoveAt(index);
            pins.Insert(target, gameId);
            return true;
        }

        public static bool IsAchievementPinned(
            ShowcaseSettings settings,
            Guid gameId,
            string apiName)
        {
            return gameId != Guid.Empty &&
                !string.IsNullOrWhiteSpace(apiName) &&
                settings?.PinnedAchievements?.Any(pin =>
                    pin != null &&
                    pin.GameId == gameId &&
                    string.Equals(
                        pin.ApiName,
                        apiName,
                        StringComparison.OrdinalIgnoreCase)) == true;
        }

        public static bool ToggleAchievement(
            ShowcaseSettings settings,
            Guid gameId,
            string apiName,
            string gameName,
            string achievementName)
        {
            if (settings == null ||
                gameId == Guid.Empty ||
                string.IsNullOrWhiteSpace(apiName))
            {
                return false;
            }

            settings.PinnedAchievements = settings.PinnedAchievements ??
                new System.Collections.Generic.List<PinnedAchievementReference>();
            var existing = settings.PinnedAchievements.FirstOrDefault(pin =>
                pin != null &&
                pin.GameId == gameId &&
                string.Equals(pin.ApiName, apiName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                settings.PinnedAchievements.Remove(existing);
                return false;
            }

            settings.PinnedAchievements.Add(new PinnedAchievementReference
            {
                GameId = gameId,
                ApiName = apiName.Trim(),
                LastKnownGameName = gameName,
                LastKnownAchievementName = achievementName,
                AddedUtc = DateTime.UtcNow
            });
            return true;
        }

        public static bool MoveAchievement(
            ShowcaseSettings settings,
            Guid gameId,
            string apiName,
            int direction)
        {
            var pins = settings?.PinnedAchievements;
            if (pins == null || direction == 0)
            {
                return false;
            }

            var pin = pins.FirstOrDefault(candidate =>
                candidate != null &&
                candidate.GameId == gameId &&
                string.Equals(
                    candidate.ApiName,
                    apiName,
                    StringComparison.OrdinalIgnoreCase));
            var index = pin == null ? -1 : pins.IndexOf(pin);
            var target = Math.Max(0, Math.Min(pins.Count - 1, index + Math.Sign(direction)));
            if (index < 0 || target == index)
            {
                return false;
            }

            pins.RemoveAt(index);
            pins.Insert(target, pin);
            return true;
        }

        public static bool TryGetAchievementIdentity(
            object data,
            out Guid gameId,
            out string apiName,
            out string gameName,
            out string achievementName,
            out bool friendOwned)
        {
            if (data is FriendAchievementDisplayItem)
            {
                gameId = Guid.Empty;
                apiName = null;
                gameName = null;
                achievementName = null;
                friendOwned = true;
                return false;
            }

            if (data is AchievementDisplayItem item &&
                item.PlayniteGameId.HasValue &&
                !string.IsNullOrWhiteSpace(item.ApiName))
            {
                gameId = item.PlayniteGameId.Value;
                apiName = item.ApiName;
                gameName = item.GameName;
                achievementName = item.DisplayName;
                friendOwned = !string.IsNullOrWhiteSpace(item.FriendName);
                return !friendOwned;
            }

            if (data is RecentAchievementItem recent &&
                recent.PlayniteGameId.HasValue &&
                !string.IsNullOrWhiteSpace(recent.ApiName))
            {
                gameId = recent.PlayniteGameId.Value;
                apiName = recent.ApiName;
                gameName = recent.GameName;
                achievementName = recent.Name;
                friendOwned = false;
                return true;
            }

            gameId = Guid.Empty;
            apiName = null;
            gameName = null;
            achievementName = null;
            friendOwned = false;
            return false;
        }
    }
}
