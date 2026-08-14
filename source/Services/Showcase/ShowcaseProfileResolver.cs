using System;
using System.Collections.Generic;
using System.Linq;
using PlayniteAchievements.Models.Friends;
using PlayniteAchievements.Models.Settings;

namespace PlayniteAchievements.Services.Showcase
{
    /// <summary>
    /// The profile values a profile widget renders after merging the manual
    /// showcase settings with the persisted provider identity.
    /// </summary>
    public sealed class ShowcaseProfileProjection
    {
        public string DisplayName { get; set; }

        public string Subtitle { get; set; }

        public string AvatarPath { get; set; }

        public string BackgroundPath { get; set; }

        public bool FromProviderIdentity { get; set; }
    }

    /// <summary>
    /// Merges manual showcase profile settings over the current user's provider
    /// identity. Manual fields win per-field when non-blank; Steam is the
    /// preferred identity source, then any identity with a usable name or avatar.
    /// </summary>
    public static class ShowcaseProfileResolver
    {
        private const string PreferredProviderKey = "Steam";

        public static ShowcaseProfileProjection Resolve(
            ShowcaseProfileSettings manual,
            IReadOnlyList<FriendIdentity> identities)
        {
            var identity = PickIdentity(identities);
            return new ShowcaseProfileProjection
            {
                DisplayName = FirstNonBlank(
                    manual?.DisplayName,
                    identity?.DisplayName,
                    identity?.ProviderNickname),
                Subtitle = TrimOrNull(manual?.Subtitle),
                AvatarPath = FirstNonBlank(
                    manual?.AvatarPath,
                    identity?.AvatarPath,
                    identity?.AvatarUrl),
                BackgroundPath = TrimOrNull(manual?.BackgroundPath),
                FromProviderIdentity = identity != null &&
                    string.IsNullOrWhiteSpace(manual?.DisplayName)
            };
        }

        private static FriendIdentity PickIdentity(IReadOnlyList<FriendIdentity> identities)
        {
            var usable = (identities ?? Array.Empty<FriendIdentity>())
                .Where(identity => identity != null && HasUsableContent(identity))
                .ToList();
            return usable.FirstOrDefault(identity =>
                    string.Equals(identity.ProviderKey, PreferredProviderKey, StringComparison.OrdinalIgnoreCase))
                ?? usable.FirstOrDefault();
        }

        private static bool HasUsableContent(FriendIdentity identity)
        {
            return !string.IsNullOrWhiteSpace(identity.DisplayName) ||
                !string.IsNullOrWhiteSpace(identity.ProviderNickname) ||
                !string.IsNullOrWhiteSpace(identity.AvatarPath) ||
                !string.IsNullOrWhiteSpace(identity.AvatarUrl);
        }

        private static string FirstNonBlank(params string[] values)
        {
            return values?.Select(TrimOrNull).FirstOrDefault(value => value != null);
        }

        private static string TrimOrNull(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
