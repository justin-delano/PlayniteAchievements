using PlayniteAchievements.Providers.Settings;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PlayniteAchievements.Providers.Steam
{
    /// <summary>
    /// Steam-specific provider settings.
    /// </summary>
    public class SteamSettings : ProviderSettingsBase
    {
        private string _steamUserId;
        private ObservableCollection<SteamIgnoredFriend> _ignoredFriends =
            new ObservableCollection<SteamIgnoredFriend>();
        private ObservableCollection<SteamFullLibraryFriend> _fullLibraryFriends =
            new ObservableCollection<SteamFullLibraryFriend>();

        /// <inheritdoc />
        public override string ProviderKey => "Steam";

        /// <summary>
        /// Gets or sets the last successfully probed Steam user ID.
        /// This is derived auth state, not user-editable configuration.
        /// </summary>
        public string SteamUserId
        {
            get => _steamUserId;
            set => SetValue(ref _steamUserId, value);
        }

        public ObservableCollection<SteamIgnoredFriend> IgnoredFriends
        {
            get => _ignoredFriends;
            set => SetValue(
                ref _ignoredFriends,
                new ObservableCollection<SteamIgnoredFriend>(
                    (value ?? new ObservableCollection<SteamIgnoredFriend>())
                    .Where(friend => !string.IsNullOrWhiteSpace(friend?.SteamId))
                    .GroupBy(friend => friend.SteamId.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First().Normalize())));
        }

        public bool IsFriendIgnored(string steamId)
        {
            return !string.IsNullOrWhiteSpace(steamId) &&
                   IgnoredFriends.Any(friend =>
                       string.Equals(friend?.SteamId, steamId.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public HashSet<string> GetIgnoredSteamIds()
        {
            return new HashSet<string>(
                IgnoredFriends
                    .Where(friend => !string.IsNullOrWhiteSpace(friend?.SteamId))
                    .Select(friend => friend.SteamId.Trim()),
                StringComparer.OrdinalIgnoreCase);
        }

        public void AddIgnoredFriend(string steamId, string displayName, string avatarUrl)
        {
            if (string.IsNullOrWhiteSpace(steamId))
            {
                return;
            }

            var normalizedId = steamId.Trim();
            var existing = IgnoredFriends.FirstOrDefault(friend =>
                string.Equals(friend?.SteamId, normalizedId, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.DisplayName = string.IsNullOrWhiteSpace(displayName) ? existing.DisplayName : displayName.Trim();
                existing.AvatarUrl = string.IsNullOrWhiteSpace(avatarUrl) ? existing.AvatarUrl : avatarUrl.Trim();
                return;
            }

            IgnoredFriends.Add(new SteamIgnoredFriend
            {
                SteamId = normalizedId,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? normalizedId : displayName.Trim(),
                AvatarUrl = string.IsNullOrWhiteSpace(avatarUrl) ? null : avatarUrl.Trim(),
                IgnoredUtc = DateTime.UtcNow
            });
            OnPropertyChanged(nameof(IgnoredFriends));
        }

        public bool RemoveIgnoredFriend(string steamId)
        {
            if (string.IsNullOrWhiteSpace(steamId))
            {
                return false;
            }

            var normalizedId = steamId.Trim();
            var removed = false;
            for (var i = IgnoredFriends.Count - 1; i >= 0; i--)
            {
                if (!string.Equals(IgnoredFriends[i]?.SteamId, normalizedId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                IgnoredFriends.RemoveAt(i);
                removed = true;
            }

            if (removed)
            {
                OnPropertyChanged(nameof(IgnoredFriends));
            }

            return removed;
        }

        /// <summary>
        /// Friends who opted into the full library scope (their entire Steam library, including games
        /// the current user does not own). Absence from this set means the shared library scope, which
        /// is the default. Only deviations from the default are persisted.
        /// </summary>
        public ObservableCollection<SteamFullLibraryFriend> FullLibraryFriends
        {
            get => _fullLibraryFriends;
            set => SetValue(
                ref _fullLibraryFriends,
                new ObservableCollection<SteamFullLibraryFriend>(
                    (value ?? new ObservableCollection<SteamFullLibraryFriend>())
                    .Where(friend => !string.IsNullOrWhiteSpace(friend?.SteamId))
                    .GroupBy(friend => friend.SteamId.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First().Normalize())));
        }

        public bool IsFullLibraryFriend(string steamId)
        {
            return !string.IsNullOrWhiteSpace(steamId) &&
                   FullLibraryFriends.Any(friend =>
                       string.Equals(friend?.SteamId, steamId.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public HashSet<string> GetFullLibrarySteamIds()
        {
            return new HashSet<string>(
                FullLibraryFriends
                    .Where(friend => !string.IsNullOrWhiteSpace(friend?.SteamId))
                    .Select(friend => friend.SteamId.Trim()),
                StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Enables or disables the full library scope for a friend. Enabling adds (or refreshes the
        /// metadata of) an entry; disabling removes it so the friend reverts to the shared default.
        /// </summary>
        public void SetFullLibraryFriend(string steamId, string displayName, string avatarUrl, bool enabled)
        {
            if (string.IsNullOrWhiteSpace(steamId))
            {
                return;
            }

            var normalizedId = steamId.Trim();
            var existing = FullLibraryFriends.FirstOrDefault(friend =>
                string.Equals(friend?.SteamId, normalizedId, StringComparison.OrdinalIgnoreCase));

            if (enabled)
            {
                if (existing != null)
                {
                    existing.DisplayName = string.IsNullOrWhiteSpace(displayName) ? existing.DisplayName : displayName.Trim();
                    existing.AvatarUrl = string.IsNullOrWhiteSpace(avatarUrl) ? existing.AvatarUrl : avatarUrl.Trim();
                    return;
                }

                FullLibraryFriends.Add(new SteamFullLibraryFriend
                {
                    SteamId = normalizedId,
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? normalizedId : displayName.Trim(),
                    AvatarUrl = string.IsNullOrWhiteSpace(avatarUrl) ? null : avatarUrl.Trim(),
                    AddedUtc = DateTime.UtcNow
                });
                OnPropertyChanged(nameof(FullLibraryFriends));
                return;
            }

            if (existing == null)
            {
                return;
            }

            for (var i = FullLibraryFriends.Count - 1; i >= 0; i--)
            {
                if (string.Equals(FullLibraryFriends[i]?.SteamId, normalizedId, StringComparison.OrdinalIgnoreCase))
                {
                    FullLibraryFriends.RemoveAt(i);
                }
            }

            OnPropertyChanged(nameof(FullLibraryFriends));
        }
    }

    public sealed class SteamIgnoredFriend
    {
        public string SteamId { get; set; }
        public string DisplayName { get; set; }
        public string AvatarUrl { get; set; }
        public DateTime IgnoredUtc { get; set; }

        public DateTime IgnoredLocal => IgnoredUtc.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(IgnoredUtc, DateTimeKind.Utc).ToLocalTime()
            : IgnoredUtc.ToUniversalTime().ToLocalTime();

        public SteamIgnoredFriend Normalize()
        {
            SteamId = SteamId?.Trim();
            DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? SteamId : DisplayName.Trim();
            AvatarUrl = string.IsNullOrWhiteSpace(AvatarUrl) ? null : AvatarUrl.Trim();
            if (IgnoredUtc == default)
            {
                IgnoredUtc = DateTime.UtcNow;
            }

            if (IgnoredUtc.Kind == DateTimeKind.Local)
            {
                IgnoredUtc = IgnoredUtc.ToUniversalTime();
            }

            return this;
        }
    }

    public sealed class SteamFullLibraryFriend
    {
        public string SteamId { get; set; }
        public string DisplayName { get; set; }
        public string AvatarUrl { get; set; }
        public DateTime AddedUtc { get; set; }

        public SteamFullLibraryFriend Normalize()
        {
            SteamId = SteamId?.Trim();
            DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? SteamId : DisplayName.Trim();
            AvatarUrl = string.IsNullOrWhiteSpace(AvatarUrl) ? null : AvatarUrl.Trim();
            if (AddedUtc == default)
            {
                AddedUtc = DateTime.UtcNow;
            }

            if (AddedUtc.Kind == DateTimeKind.Local)
            {
                AddedUtc = AddedUtc.ToUniversalTime();
            }

            return this;
        }
    }
}
