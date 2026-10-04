using Newtonsoft.Json;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace PlayniteAchievements.Services.Workshop
{
    /// <summary>One Workshop item this install has imported, for "Installed" and "Update available".</summary>
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
    /// Persists what was installed from the Workshop and the identity this install submits with:
    /// a random submitter key whose SHA-256 the Workshop stores as the owner of anything this user
    /// shares, so updates need no account. Both live in <c>UserData\workshop\</c>. The key is not
    /// a secret worth more than the user's own submissions, so it is stored as plain JSON.
    /// </summary>
    public sealed class WorkshopInstalledRegistry
    {
        public const string DirectoryName = "workshop";
        private const string InstalledFileName = "installed.json";
        private const string IdentityFileName = "identity.json";

        private readonly string _directory;
        private readonly ILogger _logger;
        private readonly object _sync = new object();
        private List<WorkshopInstalledItem> _items;
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

        public WorkshopInstalledRegistry(string pluginUserDataPath, ILogger logger = null)
        {
            _directory = string.IsNullOrWhiteSpace(pluginUserDataPath)
                ? null
                : Path.Combine(pluginUserDataPath, DirectoryName);
            _logger = logger;
        }

        public string Directory => _directory;

        public IReadOnlyList<WorkshopInstalledItem> Items
        {
            get
            {
                lock (_sync)
                {
                    EnsureLoaded();
                    return _items.Select(Clone).ToList();
                }
            }
        }

        public WorkshopInstalledItem Find(string id, Guid? playniteGameId = null)
        {
            lock (_sync)
            {
                EnsureLoaded();
                var match = _items.FirstOrDefault(item =>
                    string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase) &&
                    (playniteGameId == null || item.PlayniteGameId == playniteGameId));
                return match == null ? null : Clone(match);
            }
        }

        /// <summary>Records an install, replacing any earlier record of the same item (and game).</summary>
        public void Record(WorkshopItem item, Guid? playniteGameId = null, string contentHash = null, string baselineFile = null)
        {
            if (item == null)
            {
                return;
            }

            lock (_sync)
            {
                EnsureLoaded();
                _items.RemoveAll(existing =>
                    string.Equals(existing.Id, item.Id, StringComparison.OrdinalIgnoreCase) &&
                    existing.PlayniteGameId == playniteGameId);
                _items.Add(new WorkshopInstalledItem
                {
                    Id = item.Id,
                    Version = item.Version,
                    Kind = item.Kind,
                    Name = item.Name,
                    PlayniteGameId = playniteGameId,
                    ContentHash = contentHash,
                    BaselineFile = baselineFile,
                    InstalledUtc = DateTime.UtcNow
                });
                Save();
            }
        }

        public void Forget(string id, Guid? playniteGameId = null)
        {
            lock (_sync)
            {
                EnsureLoaded();
                if (_items.RemoveAll(existing =>
                        string.Equals(existing.Id, id, StringComparison.OrdinalIgnoreCase) &&
                        (playniteGameId == null || existing.PlayniteGameId == playniteGameId)) > 0)
                {
                    Save();
                }
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
            var key = GetOrCreateSubmitterKey();
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

        private void EnsureLoaded()
        {
            if (_items != null)
            {
                return;
            }

            _items = new List<WorkshopInstalledItem>();
            var path = _directory == null ? null : Path.Combine(_directory, InstalledFileName);
            if (path == null || !File.Exists(path))
            {
                return;
            }

            try
            {
                var loaded = JsonConvert.DeserializeObject<List<WorkshopInstalledItem>>(File.ReadAllText(path));
                if (loaded != null)
                {
                    _items = loaded.Where(item => item != null && !string.IsNullOrWhiteSpace(item.Id)).ToList();
                }
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "Failed reading the Workshop installed list; starting empty.");
            }
        }

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

        private void Save()
        {
            if (_directory == null)
            {
                return;
            }

            try
            {
                System.IO.Directory.CreateDirectory(_directory);
                File.WriteAllText(
                    Path.Combine(_directory, InstalledFileName),
                    JsonConvert.SerializeObject(_items, Formatting.Indented));
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "Failed writing the Workshop installed list.");
            }
        }

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

        private static WorkshopInstalledItem Clone(WorkshopInstalledItem item)
        {
            return new WorkshopInstalledItem
            {
                Id = item.Id,
                Version = item.Version,
                Kind = item.Kind,
                Name = item.Name,
                PlayniteGameId = item.PlayniteGameId,
                InstalledUtc = item.InstalledUtc
            };
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
