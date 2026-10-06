using System;
using System.IO;
using System.Linq;
using PlayniteAchievements.Services.Notifications;
using PlayniteAchievements.Services.Workshop;

namespace PlayniteAchievements.Services.Library
{
    /// <summary>The color set or sound pack preset folder as an <see cref="ILibraryPackageFolder"/>.</summary>
    public sealed class PackagePresetFolder : ILibraryPackageFolder
    {
        private readonly PackagePresetStore _store;

        public PackagePresetFolder(PackagePresetStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public string Find(string name) => _store.Find(name)?.FilePath;

        public string UniqueName(string name) => _store.UniqueName(name);

        public string Save(string name, string packagePath) => _store.SaveFrom(name, packagePath).FilePath;

        public string NameOf(string path) => Path.GetFileNameWithoutExtension(path);
    }

    /// <summary>One surface's notification style preset folder as an <see cref="ILibraryPackageFolder"/>.</summary>
    public sealed class NotificationStylePresetFolder : ILibraryPackageFolder
    {
        private readonly NotificationStylePresetStore _store;
        private readonly bool _isFrame;

        public NotificationStylePresetFolder(NotificationStylePresetStore store, bool isFrame)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _isFrame = isFrame;
        }

        public string Find(string name)
        {
            var wanted = NotificationStylePresetStore.SanitizeName(name);
            return _store.ListPresets(_isFrame)
                .FirstOrDefault(preset => string.Equals(preset.Name, wanted, StringComparison.OrdinalIgnoreCase))
                ?.FilePath;
        }

        public string UniqueName(string name) => _store.UniqueName(_isFrame, name);

        public string Save(string name, string packagePath) => _store.SavePresetFromPackage(_isFrame, name, packagePath).FilePath;

        public string NameOf(string path) => NotificationStylePortableStore.StripRecognizedSuffix(Path.GetFileName(path) ?? string.Empty);
    }
}
