using Newtonsoft.Json;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace PlayniteAchievements.Services.Workshop
{
    /// <summary>
    /// One Workshop install as <c>installed.json</c> recorded it before the customization library
    /// kept Workshop items; read only to bring those installs into the library.
    /// </summary>
    public sealed class WorkshopInstalledItem
    {
        public string Id { get; set; }
        public string Version { get; set; }
        public WorkshopItemKind Kind { get; set; }
        public string Name { get; set; }
        /// <summary>The library game a per-game package was installed onto.</summary>
        public Guid? PlayniteGameId { get; set; }
        public DateTime InstalledUtc { get; set; }

        /// <summary>SHA-256 of the preset file(s) the install wrote, so a later update can tell a user edit from the original; for a bundle, one entry per part.</summary>
        public string ContentHash { get; set; }

        /// <summary>For game data, the JSON snapshot of the custom data as the install left it: the baseline a later update merges against.</summary>
        public string BaselineFile { get; set; }
    }

    /// <summary>A submission this install made, so its progress can be followed and it can be updated.</summary>
    public sealed class WorkshopSubmissionRecord
    {
        public int IssueNumber { get; set; }
        public string IssueUrl { get; set; }
        public string Name { get; set; }
        public WorkshopItemKind Kind { get; set; }
        /// <summary>The Workshop item id once known (set on updates, or when the item is seen published).</summary>
        public string ItemId { get; set; }
        public DateTime SubmittedUtc { get; set; }
        public string LastState { get; set; }
    }

    /// <summary>
    /// Persists the identity this install submits with: a random submitter key whose SHA-256 the
    /// Workshop stores as the owner of anything this user shares, so updates need no account, and
    /// the submissions made with it. Both live in <c>UserData\workshop\</c>. The key is not a
    /// secret worth more than the user's own submissions, so it is stored as plain JSON. The
    /// installs recorded in <c>installed.json</c> by versions before the customization library
    /// are only read, by the library's one-time migration (<see cref="ReadLegacyInstalls"/>),
    /// which retires the file afterwards (<see cref="RetireLegacyInstalls"/>).
    /// </summary>
    public sealed class WorkshopIdentityStore
    {
        public const string DirectoryName = "workshop";
        private const string InstalledFileName = "installed.json";
        private const string RetiredInstalledFileName = "installed.migrated.json";
        private const string IdentityFileName = "identity.json";

        private readonly string _directory;
        private readonly ILogger _logger;
        private readonly object _sync = new object();
        private string _submitterKey;

        private sealed class IdentityFile
        {
            public string SubmitterKey { get; set; }
            public string DisplayName { get; set; }
            public List<WorkshopSubmissionRecord> Submissions { get; set; } = new List<WorkshopSubmissionRecord>();
        }

        private List<WorkshopSubmissionRecord> _submissions = new List<WorkshopSubmissionRecord>();

        /// <summary>Submissions made from this install, newest first.</summary>
        public IReadOnlyList<WorkshopSubmissionRecord> Submissions
        {
            get
            {
                lock (_sync)
                {
                    EnsureIdentityLoaded();
                    return _submissions.OrderByDescending(s => s.SubmittedUtc).ToList();
                }
            }
        }

        /// <summary>
        /// Raised after a submission is recorded, on the thread that recorded it. Every Workshop
        /// list shares this store, so a share made from one reaches the others through this
        /// event. Linking submissions and updating their state stay silent: both run while a list
        /// loads.
        /// </summary>
        public event EventHandler Changed;

        private void RaiseChanged()
        {
            try
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "A Workshop identity change handler failed.");
            }
        }

        public void RecordSubmission(WorkshopSubmissionRecord record)
        {
            if (record == null || record.IssueNumber <= 0)
            {
                return;
            }

            lock (_sync)
            {
                EnsureIdentityLoaded();
                _submissions.RemoveAll(s => s.IssueNumber == record.IssueNumber);
                _submissions.Add(record);
                SaveIdentity();
            }

            RaiseChanged();
        }

        public void UpdateSubmissionState(int issueNumber, string state, string itemId = null)
        {
            lock (_sync)
            {
                EnsureIdentityLoaded();
                var record = _submissions.FirstOrDefault(s => s.IssueNumber == issueNumber);
                if (record == null)
                {
                    return;
                }

                record.LastState = state;
                if (!string.IsNullOrWhiteSpace(itemId))
                {
                    record.ItemId = itemId;
                }

                SaveIdentity();
            }
        }

        /// <summary>
        /// Gives records that do not know their Workshop id yet the id of the published item
        /// with the same kind and name, so later updates can be addressed to it.
        /// </summary>
        public void LinkSubmissions(IEnumerable<WorkshopItem> published)
        {
            if (published == null)
            {
                return;
            }

            lock (_sync)
            {
                EnsureIdentityLoaded();
                var changed = false;
                foreach (var item in published)
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.Id))
                    {
                        continue;
                    }

                    foreach (var record in _submissions)
                    {
                        if (!string.IsNullOrWhiteSpace(record.ItemId) || record.Kind != item.Kind ||
                            !string.Equals(record.Name, item.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        record.ItemId = item.Id;
                        changed = true;
                    }
                }

                if (changed)
                {
                    SaveIdentity();
                }
            }
        }

        public WorkshopIdentityStore(string pluginUserDataPath, ILogger logger = null)
        {
            _directory = string.IsNullOrWhiteSpace(pluginUserDataPath)
                ? null
                : Path.Combine(pluginUserDataPath, DirectoryName);
            _logger = logger;
        }

        public string Directory => _directory;

        /// <summary>
        /// The installs <c>installed.json</c> records, written by versions before the
        /// customization library; empty when there is no such file or it cannot be read.
        /// </summary>
        public IReadOnlyList<WorkshopInstalledItem> ReadLegacyInstalls()
        {
            var path = _directory == null ? null : Path.Combine(_directory, InstalledFileName);
            if (path == null || !File.Exists(path))
            {
                return new List<WorkshopInstalledItem>();
            }

            try
            {
                var loaded = JsonConvert.DeserializeObject<List<WorkshopInstalledItem>>(File.ReadAllText(path));
                return (loaded ?? new List<WorkshopInstalledItem>())
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Id))
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "Failed reading the Workshop installs recorded before the library.");
                return new List<WorkshopInstalledItem>();
            }
        }

        /// <summary>
        /// Renames <c>installed.json</c> to <c>installed.migrated.json</c> once the library holds
        /// its installs, so it is read no more and stays as a backup. True when a file was renamed.
        /// </summary>
        public bool RetireLegacyInstalls()
        {
            var path = _directory == null ? null : Path.Combine(_directory, InstalledFileName);
            if (path == null || !File.Exists(path))
            {
                return false;
            }

            try
            {
                var retired = Path.Combine(_directory, RetiredInstalledFileName);
                if (File.Exists(retired))
                {
                    File.Delete(retired);
                }

                File.Move(path, retired);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "Failed retiring the Workshop installs recorded before the library.");
                return false;
            }
        }

        /// <summary>
        /// True when the index carries a newer version than the one recorded, by numeric
        /// MAJOR.MINOR.PATCH comparison.
        /// </summary>
        public static bool IsNewer(string indexVersion, string installedVersion)
        {
            if (!TryParse(indexVersion, out var a) || !TryParse(installedVersion, out var b))
            {
                return false;
            }

            return a > b;
        }

        /// <summary>The submitter key, created on first use. Its hash is what the Workshop records.</summary>
        public string GetOrCreateSubmitterKey()
        {
            lock (_sync)
            {
                EnsureIdentityLoaded();
                if (string.IsNullOrWhiteSpace(_submitterKey))
                {
                    var bytes = new byte[32];
                    using (var rng = RandomNumberGenerator.Create())
                    {
                        rng.GetBytes(bytes);
                    }

                    _submitterKey = BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
                    SaveIdentity();
                }

                return _submitterKey;
            }
        }

        /// <summary>A submitter key as Copy spells it: 64 hex digits, any case.</summary>
        public bool IsValidSubmitterKey(string key)
        {
            var trimmed = key?.Trim();
            return !string.IsNullOrEmpty(trimmed)
                && trimmed.Length == 64
                && trimmed.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));
        }

        /// <summary>
        /// Replaces the submitter key with one copied from another install, so that the other
        /// install's submissions can be updated from here. Returns false for anything that is
        /// not a key; the current key is then left alone.
        /// </summary>
        public bool TrySetSubmitterKey(string key)
        {
            if (!IsValidSubmitterKey(key))
            {
                return false;
            }

            lock (_sync)
            {
                EnsureIdentityLoaded();
                _submitterKey = key.Trim().ToLowerInvariant();
                SaveIdentity();
            }

            return true;
        }

        /// <summary>SHA-256 hex of the submitter key: the value sent to the Workshop.</summary>
        public string GetSubmitterHash()
        {
            return HashKey(GetOrCreateSubmitterKey());
        }

        /// <summary>
        /// SHA-256 hex of the submitter key, or null when this install has never created one.
        /// Unlike <see cref="GetSubmitterHash"/>, it never creates a key, so browsing alone leaves
        /// the identity file untouched.
        /// </summary>
        public string TryGetSubmitterHash()
        {
            string key;
            lock (_sync)
            {
                EnsureIdentityLoaded();
                key = _submitterKey;
            }

            return string.IsNullOrWhiteSpace(key) ? null : HashKey(key);
        }

        private static string HashKey(string key)
        {
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(key)))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
        }

        /// <summary>The author name used on the last share, remembered for the next dialog.</summary>
        public string DisplayName
        {
            get
            {
                lock (_sync)
                {
                    EnsureIdentityLoaded();
                    return _displayName;
                }
            }
            set
            {
                lock (_sync)
                {
                    EnsureIdentityLoaded();
                    _displayName = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                    SaveIdentity();
                }
            }
        }

        private string _displayName;

        private void EnsureIdentityLoaded()
        {
            if (_identityLoaded)
            {
                return;
            }

            _identityLoaded = true;
            var path = _directory == null ? null : Path.Combine(_directory, IdentityFileName);
            if (path == null || !File.Exists(path))
            {
                return;
            }

            try
            {
                var identity = JsonConvert.DeserializeObject<IdentityFile>(File.ReadAllText(path));
                _submitterKey = identity?.SubmitterKey;
                _displayName = identity?.DisplayName;
                _submissions = identity?.Submissions?.Where(s => s != null && s.IssueNumber > 0).ToList()
                               ?? new List<WorkshopSubmissionRecord>();
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "Failed reading the Workshop identity; a new submitter key will be created.");
            }
        }

        private bool _identityLoaded;

        private void SaveIdentity()
        {
            if (_directory == null)
            {
                return;
            }

            try
            {
                System.IO.Directory.CreateDirectory(_directory);
                File.WriteAllText(
                    Path.Combine(_directory, IdentityFileName),
                    JsonConvert.SerializeObject(
                        new IdentityFile { SubmitterKey = _submitterKey, DisplayName = _displayName, Submissions = _submissions },
                        Formatting.Indented));
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "Failed writing the Workshop identity.");
            }
        }

        private static bool TryParse(string version, out Version parsed)
        {
            parsed = null;
            if (string.IsNullOrWhiteSpace(version))
            {
                return false;
            }

            var parts = version.Trim().Split('.');
            if (parts.Length < 3)
            {
                return false;
            }

            return Version.TryParse(string.Join(".", parts, 0, 3), out parsed);
        }
    }
}
