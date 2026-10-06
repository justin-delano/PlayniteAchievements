using System;

namespace PlayniteAchievements.Services.Library
{
    /// <summary>
    /// The keys that name a link target. Settings-backed targets (colors, sounds, the global and
    /// per-platform notification and frame scopes, showcase pages) keep their links in
    /// <c>PersistedSettings.LibraryLinks</c>; per-game targets (a game's notification and frame
    /// scope, a game's data) keep them in the library's links file.
    /// </summary>
    public static class LibraryTargetKeys
    {
        public const string Colors = "colors";
        public const string Sounds = "sounds";
        public const string ToastGlobal = "toast:global";
        public const string FrameGlobal = "frame:global";

        private const string ToastPrefix = "toast:";
        private const string FramePrefix = "frame:";
        private const string ProviderSegment = "provider:";
        private const string GameSegment = "game:";
        private const string ShowcasePrefix = "showcase:";
        private const string GameDataPrefix = "gamedata:";

        public static string ToastProvider(string providerKey) => ToastPrefix + ProviderSegment + RequireToken(providerKey, nameof(providerKey));

        public static string FrameProvider(string providerKey) => FramePrefix + ProviderSegment + RequireToken(providerKey, nameof(providerKey));

        public static string Showcase(string pageId) => ShowcasePrefix + RequireToken(pageId, nameof(pageId));

        public static string ToastGame(Guid gameId) => ToastPrefix + GameSegment + GameToken(gameId);

        public static string FrameGame(Guid gameId) => FramePrefix + GameSegment + GameToken(gameId);

        public static string GameData(Guid gameId) => GameDataPrefix + GameToken(gameId);

        /// <summary>
        /// The key of a notification (<paramref name="isFrame"/> false) or frame scope: a game when
        /// <paramref name="gameId"/> is set, else a platform when <paramref name="providerKey"/> is
        /// set, else the global scope.
        /// </summary>
        public static string NotificationScope(bool isFrame, string providerKey, Guid gameId)
        {
            if (gameId != Guid.Empty)
            {
                return isFrame ? FrameGame(gameId) : ToastGame(gameId);
            }

            if (!string.IsNullOrWhiteSpace(providerKey))
            {
                return isFrame ? FrameProvider(providerKey) : ToastProvider(providerKey);
            }

            return isFrame ? FrameGlobal : ToastGlobal;
        }

        /// <summary>Reads a notification or frame scope key back into its surface and scope.</summary>
        public static bool TryParseNotificationScope(string targetKey, out bool isFrame, out string providerKey, out Guid gameId)
        {
            isFrame = false;
            providerKey = null;
            gameId = Guid.Empty;
            if (string.IsNullOrWhiteSpace(targetKey))
            {
                return false;
            }

            string rest;
            if (targetKey.StartsWith(ToastPrefix, StringComparison.OrdinalIgnoreCase))
            {
                rest = targetKey.Substring(ToastPrefix.Length);
            }
            else if (targetKey.StartsWith(FramePrefix, StringComparison.OrdinalIgnoreCase))
            {
                isFrame = true;
                rest = targetKey.Substring(FramePrefix.Length);
            }
            else
            {
                return false;
            }

            if (string.Equals(rest, "global", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (rest.StartsWith(ProviderSegment, StringComparison.OrdinalIgnoreCase))
            {
                providerKey = rest.Substring(ProviderSegment.Length).Trim();
                return providerKey.Length > 0;
            }

            return rest.StartsWith(GameSegment, StringComparison.OrdinalIgnoreCase)
                   && Guid.TryParse(rest.Substring(GameSegment.Length), out gameId)
                   && gameId != Guid.Empty;
        }

        /// <summary>True for a key whose link lives in the library's links file rather than in the settings.</summary>
        public static bool IsPerGame(string targetKey)
        {
            return TryGetGameId(targetKey, out _);
        }

        /// <summary>The game of a per-game key.</summary>
        public static bool TryGetGameId(string targetKey, out Guid gameId)
        {
            gameId = Guid.Empty;
            if (string.IsNullOrWhiteSpace(targetKey))
            {
                return false;
            }

            string token = null;
            if (targetKey.StartsWith(GameDataPrefix, StringComparison.OrdinalIgnoreCase))
            {
                token = targetKey.Substring(GameDataPrefix.Length);
            }
            else if (targetKey.StartsWith(ToastPrefix + GameSegment, StringComparison.OrdinalIgnoreCase))
            {
                token = targetKey.Substring((ToastPrefix + GameSegment).Length);
            }
            else if (targetKey.StartsWith(FramePrefix + GameSegment, StringComparison.OrdinalIgnoreCase))
            {
                token = targetKey.Substring((FramePrefix + GameSegment).Length);
            }

            return token != null && Guid.TryParse(token, out gameId) && gameId != Guid.Empty;
        }

        private static string GameToken(Guid gameId)
        {
            if (gameId == Guid.Empty)
            {
                throw new ArgumentException("A game id is required.", nameof(gameId));
            }

            return gameId.ToString("D");
        }

        private static string RequireToken(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("A value is required.", name);
            }

            return value.Trim();
        }
    }
}
