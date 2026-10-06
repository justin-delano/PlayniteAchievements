using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using PlayniteAchievements.Models.Settings;

namespace PlayniteAchievements.Services.Library
{
    /// <summary>
    /// Finds the adapter of a target that its kind has many of (each notification and frame
    /// scope, each showcase page), by the target's <see cref="LibraryTargetKeys"/> key.
    /// </summary>
    public interface ILibraryTargetResolver
    {
        /// <summary>The adapter of a settings-backed target, or null when this resolver does not know the key.</summary>
        ISettingsLibraryAdapter SettingsAdapter(string targetKey);

        /// <summary>The per-game target a key names, or null when this resolver does not know the key.</summary>
        ILibraryGameTarget GameTarget(string targetKey);
    }

    /// <summary>
    /// A per-game target with its adapter: applies, merges and reads the state of the target. The
    /// caller keeps the link in the <see cref="GameLinkStore"/>.
    /// </summary>
    public interface ILibraryGameTarget
    {
        LibraryItemKind Kind { get; }

        /// <summary>Writes the item onto the target as published and returns the new link.</summary>
        LibraryLink Apply(LibraryApplyService apply, LibraryItem item);

        /// <summary>Links the target to an item saved from it, without writing it.</summary>
        LibraryLink Link(LibraryApplyService apply, LibraryItem item);

        /// <summary>Merges the item's current version into the target and returns the new link.</summary>
        LibraryLink Update(LibraryApplyService apply, LibraryItem item, LibraryLink link, out int keptEdits);

        LibraryLinkState GetState(LibraryApplyService apply, LibraryLink link);
    }

    /// <summary>A per-game target of any adapter.</summary>
    public sealed class LibraryGameTarget<TTarget> : ILibraryGameTarget
    {
        private readonly ILibraryAdapter<TTarget> _adapter;
        private readonly TTarget _target;

        public LibraryGameTarget(ILibraryAdapter<TTarget> adapter, TTarget target)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _target = target;
        }

        public LibraryItemKind Kind => _adapter.Kind;

        public LibraryLink Apply(LibraryApplyService apply, LibraryItem item) => apply.Apply(_adapter, item, _target);

        public LibraryLink Link(LibraryApplyService apply, LibraryItem item) => apply.Link(_adapter, item, _target);

        public LibraryLink Update(LibraryApplyService apply, LibraryItem item, LibraryLink link, out int keptEdits) =>
            apply.Update(_adapter, item, _target, link, out keptEdits);

        public LibraryLinkState GetState(LibraryApplyService apply, LibraryLink link) => apply.GetState(_adapter, _target, link);
    }

    /// <summary>
    /// A settings-backed target of an adapter whose target is not the settings themselves (a
    /// notification scope inside the settings), bound to its key.
    /// </summary>
    public sealed class SettingsLibraryAdapter<TTarget> : ISettingsLibraryAdapter
    {
        private readonly ILibraryAdapter<TTarget> _adapter;
        private readonly Func<PersistedSettings, TTarget> _targetOf;

        public SettingsLibraryAdapter(string targetKey, ILibraryAdapter<TTarget> adapter, Func<PersistedSettings, TTarget> targetOf)
        {
            if (string.IsNullOrWhiteSpace(targetKey))
            {
                throw new ArgumentException("A target key is required.", nameof(targetKey));
            }

            TargetKey = targetKey;
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _targetOf = targetOf ?? throw new ArgumentNullException(nameof(targetOf));
        }

        public string TargetKey { get; }

        public LibraryItemKind Kind => _adapter.Kind;

        public JObject Project(PersistedSettings target, IEnumerable<string> ownedKeys = null) =>
            _adapter.Project(_targetOf(target), ownedKeys);

        public IReadOnlyCollection<string> OwnedKeys(string packagePath) => _adapter.OwnedKeys(packagePath);

        public void ApplyReplace(string packagePath, PersistedSettings target) =>
            _adapter.ApplyReplace(packagePath, _targetOf(target));

        public void ApplyMerged(string packagePath, PersistedSettings target, JToken baseline, out int keptEdits) =>
            _adapter.ApplyMerged(packagePath, _targetOf(target), baseline, out keptEdits);
    }
}
