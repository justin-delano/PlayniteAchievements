using Newtonsoft.Json;
using PlayniteAchievements.Models.Friends;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PlayniteAchievements.Models.Settings
{
    public enum FriendSettingsSource
    {
        AutoDiscovered,
        Manual
    }

    public sealed class FriendSettingsEntry
    {
        public string ProviderKey { get; set; }

        public string ExternalUserId { get; set; }

        public string DisplayName { get; set; }

        public string AvatarUrl { get; set; }

        public string AvatarPath { get; set; }

        public FriendSettingsSource Source { get; set; } = FriendSettingsSource.AutoDiscovered;

        public FriendLibraryScope LibraryScope { get; set; } = FriendLibraryScope.Shared;

        public bool IsIgnored { get; set; }

        public List<string> SelectedPlatforms { get; set; } = new List<string>();

        public DateTime AddedUtc { get; set; } = DateTime.UtcNow;

        public DateTime? LastRefreshedUtc { get; set; }

        public DateTime? LastProbedUtc { get; set; }

        public string LastProbeStatus { get; set; }

        public string LastError { get; set; }

        [JsonIgnore]
        public bool IsManual => Source == FriendSettingsSource.Manual;

        public FriendSettingsEntry Clone()
        {
            return new FriendSettingsEntry
            {
                ProviderKey = ProviderKey,
                ExternalUserId = ExternalUserId,
                DisplayName = DisplayName,
                AvatarUrl = AvatarUrl,
                AvatarPath = AvatarPath,
                Source = Source,
                LibraryScope = LibraryScope,
                IsIgnored = IsIgnored,
                SelectedPlatforms = SelectedPlatforms?.ToList() ?? new List<string>(),
                AddedUtc = AddedUtc,
                LastRefreshedUtc = LastRefreshedUtc,
                LastProbedUtc = LastProbedUtc,
                LastProbeStatus = LastProbeStatus,
                LastError = LastError
            };
        }

        public FriendSettingsEntry Normalize()
        {
            ProviderKey = NormalizeToken(ProviderKey);
            ExternalUserId = NormalizeToken(ExternalUserId);
            DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? ExternalUserId : DisplayName.Trim();
            AvatarUrl = NormalizeNullable(AvatarUrl);
            AvatarPath = NormalizeNullable(AvatarPath);
            LibraryScope = LibraryScope == FriendLibraryScope.Full
                ? FriendLibraryScope.Full
                : FriendLibraryScope.Shared;
            SelectedPlatforms = NormalizePlatformList(SelectedPlatforms);
            if (AddedUtc == default(DateTime))
            {
                AddedUtc = DateTime.UtcNow;
            }

            if (AddedUtc.Kind == DateTimeKind.Local)
            {
                AddedUtc = AddedUtc.ToUniversalTime();
            }

            LastProbeStatus = NormalizeNullable(LastProbeStatus);
            LastError = NormalizeNullable(LastError);
            return this;
        }

        internal static List<string> NormalizePlatformList(IEnumerable<string> platforms)
        {
            return (platforms ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        internal static string BuildKey(string providerKey, string externalUserId)
        {
            providerKey = NormalizeToken(providerKey);
            externalUserId = NormalizeToken(externalUserId);
            return string.IsNullOrWhiteSpace(providerKey) || string.IsNullOrWhiteSpace(externalUserId)
                ? null
                : providerKey.ToLowerInvariant() + "|" + externalUserId.ToLowerInvariant();
        }

        private static string NormalizeToken(string value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string NormalizeNullable(string value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
