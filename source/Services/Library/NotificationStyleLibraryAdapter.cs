using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.Images;
using PlayniteAchievements.Services.Notifications;
using PlayniteAchievements.Services.UI;

namespace PlayniteAchievements.Services.Library
{
    /// <summary>The custom template files of the notification scopes, one per surface and scope.</summary>
    public interface INotificationTemplateFiles
    {
        /// <summary>The scope's own template for the surface, or null when it has none.</summary>
        string Read(bool isFrame, string providerKey, Guid gameId);

        /// <summary>
        /// Installs <paramref name="xamlOrNull"/> as the scope's template, or removes the scope's
        /// template when it is blank. Throws when the template is not valid.
        /// </summary>
        void Write(bool isFrame, string providerKey, Guid gameId, string xamlOrNull);
    }

    /// <summary>The custom template files as <see cref="AchievementToastTemplateResolver"/> keeps them.</summary>
    public sealed class ResolverTemplateFiles : INotificationTemplateFiles
    {
        private readonly AchievementToastTemplateResolver _resolver;

        public ResolverTemplateFiles(AchievementToastTemplateResolver resolver)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }

        public string Read(bool isFrame, string providerKey, Guid gameId)
        {
            var path = _resolver.GetScopedCustomTemplatePath(isFrame, providerKey, gameId);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            try
            {
                return File.ReadAllText(path);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        public void Write(bool isFrame, string providerKey, Guid gameId, string xamlOrNull)
        {
            if (string.IsNullOrWhiteSpace(xamlOrNull))
            {
                _resolver.DeleteCustomTemplate(isFrame, providerKey, gameId);
            }
            else
            {
                _resolver.SaveCustomTemplate(isFrame, xamlOrNull, providerKey, gameId);
            }
        }
    }

    /// <summary>
    /// The notification (<see cref="LibraryItemKind.Toast"/>) or screenshot frame
    /// (<see cref="LibraryItemKind.Frame"/>) look of one scope: global, a platform or a game.
    /// The projection holds the surface's fields (<c>Surface</c>), the toast background
    /// (<c>Background</c>), each separately styled kind the package carries
    /// (<c>Kind:&lt;name&gt;</c>, with its own surface and background) and the scope's custom
    /// template (<c>Template</c>, a text hash). Images are file hashes, since every scope keeps
    /// its own slot files. A merge reads the package into a scratch folder and copies into the
    /// scope's slots only the images the merge takes from the package; slot files nothing refers
    /// to any more are pruned afterwards. Applying to one kind of a scope is a one-off copy that
    /// no link follows (<see cref="ApplyToKind"/>).
    /// </summary>
    public sealed class NotificationStyleLibraryAdapter : ILibraryAdapter<NotificationStyleScope>
    {
        public const string SurfaceKey = "Surface";
        public const string BackgroundKey = "Background";
        public const string TemplateKey = "Template";
        public const string KindPrefix = "Kind:";

        private const string BadgeImagesKey = nameof(NotificationSurfaceStyle.BadgeImages);

        private static readonly string[] AtomicPaths =
        {
            SurfaceKey + "." + BadgeImagesKey + "." + JsonThreeWayMerge.AnySegment,
            JsonThreeWayMerge.AnySegment + "." + SurfaceKey + "." + BadgeImagesKey + "." + JsonThreeWayMerge.AnySegment,
            BackgroundKey,
            JsonThreeWayMerge.AnySegment + "." + BackgroundKey
        };

        private static readonly JsonSerializer ProjectionSerializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            Converters = { new StringEnumConverter() }
        });

        // Null leaves keep the surface default (null for every optional field); lists and nested
        // objects are replaced rather than appended to.
        private static readonly JsonSerializer PopulateSerializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            Converters = { new StringEnumConverter() },
            NullValueHandling = NullValueHandling.Ignore,
            ObjectCreationHandling = ObjectCreationHandling.Replace
        });

        private static readonly IReadOnlyList<KeyValuePair<string, NotificationImageSlot>> ToastBadgeSlots = new[]
        {
            new KeyValuePair<string, NotificationImageSlot>(nameof(NotificationBadgeImageSet.CommonPath), NotificationImageSlot.BadgeCommon),
            new KeyValuePair<string, NotificationImageSlot>(nameof(NotificationBadgeImageSet.UncommonPath), NotificationImageSlot.BadgeUncommon),
            new KeyValuePair<string, NotificationImageSlot>(nameof(NotificationBadgeImageSet.RarePath), NotificationImageSlot.BadgeRare),
            new KeyValuePair<string, NotificationImageSlot>(nameof(NotificationBadgeImageSet.UltraRarePath), NotificationImageSlot.BadgeUltraRare),
            new KeyValuePair<string, NotificationImageSlot>(nameof(NotificationBadgeImageSet.CompletionPath), NotificationImageSlot.BadgeCompletion)
        };

        private static readonly IReadOnlyList<KeyValuePair<string, NotificationImageSlot>> FrameBadgeSlots = new[]
        {
            new KeyValuePair<string, NotificationImageSlot>(nameof(NotificationBadgeImageSet.CommonPath), NotificationImageSlot.FrameBadgeCommon),
            new KeyValuePair<string, NotificationImageSlot>(nameof(NotificationBadgeImageSet.UncommonPath), NotificationImageSlot.FrameBadgeUncommon),
            new KeyValuePair<string, NotificationImageSlot>(nameof(NotificationBadgeImageSet.RarePath), NotificationImageSlot.FrameBadgeRare),
            new KeyValuePair<string, NotificationImageSlot>(nameof(NotificationBadgeImageSet.UltraRarePath), NotificationImageSlot.FrameBadgeUltraRare),
            new KeyValuePair<string, NotificationImageSlot>(nameof(NotificationBadgeImageSet.CompletionPath), NotificationImageSlot.FrameBadgeCompletion)
        };

        private readonly bool _isFrame;
        private readonly NotificationStylePortableStore _portable;
        private readonly NotificationImageStore _images;
        private readonly INotificationTemplateFiles _templates;
        private readonly Action _pruneImages;
        private readonly Action<Exception, string> _warn;
        private readonly object _errorsSync = new object();
        private readonly List<string> _templateErrors = new List<string>();

        /// <param name="isFrame">True for the screenshot frame surface, false for the notification.</param>
        /// <param name="portable">Reads style packages.</param>
        /// <param name="images">Copies images into a scope's slots.</param>
        /// <param name="templates">The scopes' custom templates.</param>
        /// <param name="pruneImages">Removes slot files no style refers to any more; null to leave them.</param>
        /// <param name="warn">Optional sink for a template that could not be installed.</param>
        public NotificationStyleLibraryAdapter(
            bool isFrame,
            NotificationStylePortableStore portable,
            NotificationImageStore images,
            INotificationTemplateFiles templates,
            Action pruneImages = null,
            Action<Exception, string> warn = null)
        {
            _isFrame = isFrame;
            _portable = portable ?? throw new ArgumentNullException(nameof(portable));
            _images = images ?? throw new ArgumentNullException(nameof(images));
            _templates = templates ?? throw new ArgumentNullException(nameof(templates));
            _pruneImages = pruneImages;
            _warn = warn;
        }

        public LibraryItemKind Kind => _isFrame ? LibraryItemKind.Frame : LibraryItemKind.Toast;

        public bool IsFrame => _isFrame;

        public JObject Project(NotificationStyleScope target, IEnumerable<string> ownedKeys = null)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            return ProjectStyle(target.EffectiveStyle, ReadTemplate(target), ownedKeys);
        }

        public IReadOnlyCollection<string> OwnedKeys(string packagePath)
        {
            return OwnedKeysOf(_portable.ReadStyle(packagePath));
        }

        public void ApplyReplace(string packagePath, NotificationStyleScope target)
        {
            ApplyCore(packagePath, target, baseline: null, replace: true, out _);
        }

        public void ApplyMerged(string packagePath, NotificationStyleScope target, JToken baseline, out int keptEdits)
        {
            ApplyCore(packagePath, target, baseline, replace: false, out keptEdits);
        }

        /// <summary>
        /// Copies the package's own surface, as published, onto one kind's copy of the scope's
        /// style, which it gets if it has none. The scope's template and the scope's link stay as
        /// they are: this is a one-off copy.
        /// </summary>
        public void ApplyToKind(string packagePath, NotificationStyleScope scope, NotificationKind kind)
        {
            if (scope == null)
            {
                throw new ArgumentNullException(nameof(scope));
            }

            if (kind == NotificationKind.Base)
            {
                throw new ArgumentException("A notification kind is required.", nameof(kind));
            }

            var scratch = PortablePackage.CreateScratchDirectory("LibraryStyle");
            try
            {
                var package = ReadPackage(packagePath, scratch);
                var incoming = ProjectKind(package.Style);
                var result = PrepareOwnCopy(scope);
                var target = result.EnableKindStyle(kind);
                var owner = scope.ImageOwner.ForNotificationKind(kind);
                WriteSurface(target, null, package.Style, incoming[SurfaceKey], null, incoming[SurfaceKey], owner);
                if (!_isFrame)
                {
                    target.ToastBackgroundImagePath = ResolveImage(
                        incoming[BackgroundKey], null, incoming[BackgroundKey], null, package.Style.ToastBackgroundImagePath, owner, NotificationImageSlot.Background);
                }

                scope.Write(result);
                _pruneImages?.Invoke();
            }
            finally
            {
                PortablePackage.TryDeleteDirectory(scratch);
            }
        }

        /// <summary>The messages of the templates that could not be installed since the last call.</summary>
        public IReadOnlyList<string> TakeTemplateErrors()
        {
            lock (_errorsSync)
            {
                var errors = _templateErrors.ToList();
                _templateErrors.Clear();
                return errors;
            }
        }

        /// <summary>
        /// The projection of <paramref name="style"/> with <paramref name="templateXaml"/> as the
        /// scope's template, over <paramref name="ownedKeys"/> or over everything when null.
        /// </summary>
        public JObject ProjectStyle(NotificationStyleSettings style, string templateXaml, IEnumerable<string> ownedKeys = null)
        {
            var owned = ownedKeys == null ? null : new HashSet<string>(ownedKeys, StringComparer.OrdinalIgnoreCase);
            bool Wants(string key) => owned == null || owned.Contains(key);

            var result = new JObject();
            if (Wants(SurfaceKey))
            {
                result[SurfaceKey] = ProjectSurface(style);
            }

            if (!_isFrame && Wants(BackgroundKey))
            {
                result[BackgroundKey] = LibraryFileTokens.FileValue(style?.ToastBackgroundImagePath);
            }

            var kinds = owned == null
                ? KindsOf(style)
                : owned.Select(ParseKindKey).Where(kind => kind.HasValue).Select(kind => kind.Value);
            foreach (var kind in kinds.Distinct().OrderBy(kind => kind.ToString(), StringComparer.Ordinal))
            {
                result[KindKey(kind)] = style != null && style.HasKindStyle(kind)
                    ? ProjectKind(style.ResolveKind(kind))
                    : null;
            }

            if (Wants(TemplateKey))
            {
                result[TemplateKey] = LibraryFileTokens.TextToken(templateXaml);
            }

            return result;
        }

        /// <summary>The top-level projection keys a package with this style owns.</summary>
        public IReadOnlyCollection<string> OwnedKeysOf(NotificationStyleSettings packageStyle)
        {
            var keys = new List<string> { SurfaceKey };
            if (!_isFrame)
            {
                keys.Add(BackgroundKey);
            }

            keys.AddRange(KindsOf(packageStyle).Select(KindKey));
            keys.Add(TemplateKey);
            return keys;
        }

        public static string KindKey(NotificationKind kind) => KindPrefix + kind;

        // ---- apply ------------------------------------------------------------------------------

        private void ApplyCore(string packagePath, NotificationStyleScope scope, JToken baseline, bool replace, out int keptEdits)
        {
            if (scope == null)
            {
                throw new ArgumentNullException(nameof(scope));
            }

            keptEdits = 0;
            var scratch = PortablePackage.CreateScratchDirectory("LibraryStyle");
            try
            {
                var package = ReadPackage(packagePath, scratch);
                var incomingTemplate = _isFrame ? package.FrameTemplateXaml : package.ToastTemplateXaml;
                var keys = OwnedKeysOf(package.Style).ToList();
                if (!replace && baseline is JObject baselineObject)
                {
                    // A kind the earlier version carried and this one does not is still the item's.
                    keys.AddRange(baselineObject.Properties()
                        .Select(property => property.Name)
                        .Where(name => !keys.Contains(name, StringComparer.OrdinalIgnoreCase)));
                }

                var current = ProjectStyle(scope.EffectiveStyle, ReadTemplate(scope), keys);
                var incoming = ProjectStyle(package.Style, incomingTemplate, keys);
                var merged = replace ? incoming : MergeProjections(baseline, current, incoming, out keptEdits);

                var result = PrepareOwnCopy(scope);
                var before = result.Clone();
                WriteProjection(result, before, package.Style, merged, current, incoming, scope.ImageOwner);
                scope.Write(result);
                WriteTemplate(scope, merged, current, incoming, incomingTemplate);
                _pruneImages?.Invoke();
            }
            finally
            {
                PortablePackage.TryDeleteDirectory(scratch);
            }
        }

        private NotificationStylePreviewPackage ReadPackage(string packagePath, string scratch)
        {
            var package = _portable.ReadForPreview(packagePath, scratch);
            var carries = _isFrame ? package?.Contents?.HasFrameStyle == true : package?.Contents?.HasToastStyle == true;
            if (!carries || package.Style == null)
            {
                throw new InvalidOperationException(_isFrame
                    ? "This package does not contain a frame style."
                    : "This package does not contain a notification style.");
            }

            return package;
        }

        /// <summary>
        /// A copy of the scope's own style; a scope that follows an inherited style gets a copy of
        /// it with the images copied into its own slots, so it never points at another scope's files.
        /// </summary>
        private NotificationStyleSettings PrepareOwnCopy(NotificationStyleScope scope)
        {
            var own = scope.OwnStyle;
            if (own != null)
            {
                return own.Clone();
            }

            var copy = (scope.EffectiveStyle ?? NotificationStyleSettings.CreateDefault()).Clone();
            RunSync(() => _images.CopyImagesAsync(copy, scope.ImageOwner, CancellationToken.None));
            return copy;
        }

        /// <summary>
        /// Three-way merge of the projections. Whether a kind is styled separately is the user's
        /// choice when they changed it, so such a kind stays as they left it; a kind the new
        /// version drops goes too, unless the user edited it. Everything else merges leaf by leaf.
        /// </summary>
        private static JObject MergeProjections(JToken baseline, JObject current, JObject incoming, out int keptEdits)
        {
            var baseObject = (baseline as JObject)?.DeepClone() as JObject ?? new JObject();
            var currentObject = (JObject)current.DeepClone();
            var incomingObject = (JObject)incoming.DeepClone();
            var held = new List<KeyValuePair<string, JToken>>();
            var heldEdits = 0;

            var kindNames = baseObject.Properties()
                .Concat(currentObject.Properties())
                .Concat(incomingObject.Properties())
                .Select(property => property.Name)
                .Where(name => name.StartsWith(KindPrefix, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var name in kindNames)
            {
                var b = baseObject.GetValue(name, StringComparison.OrdinalIgnoreCase);
                var c = currentObject.GetValue(name, StringComparison.OrdinalIgnoreCase);
                var i = incomingObject.GetValue(name, StringComparison.OrdinalIgnoreCase);
                var baseStyled = !JsonThreeWayMerge.SameValue(b, null);
                var currentStyled = !JsonThreeWayMerge.SameValue(c, null);
                var incomingStyled = !JsonThreeWayMerge.SameValue(i, null);

                JToken value;
                if (baseStyled != currentStyled)
                {
                    value = c;
                }
                else if (currentStyled && !incomingStyled)
                {
                    value = JsonThreeWayMerge.SameValue(c, b) ? null : c;
                }
                else
                {
                    continue;
                }

                if (!JsonThreeWayMerge.SameValue(value, i))
                {
                    heldEdits++;
                }

                held.Add(new KeyValuePair<string, JToken>(name, value?.DeepClone() ?? JValue.CreateNull()));
                baseObject.Remove(name);
                currentObject.Remove(name);
                incomingObject.Remove(name);
            }

            var merged = JsonThreeWayMerge.Merge(baseObject, currentObject, incomingObject, AtomicPaths, out keptEdits) as JObject
                         ?? new JObject();
            foreach (var pair in held)
            {
                merged[pair.Key] = pair.Value;
            }

            keptEdits += heldEdits;
            return merged;
        }

        private void WriteProjection(
            NotificationStyleSettings result,
            NotificationStyleSettings before,
            NotificationStyleSettings incomingStyle,
            JObject merged,
            JObject current,
            JObject incoming,
            NotificationImageOwner owner)
        {
            if (merged.GetValue(SurfaceKey, StringComparison.OrdinalIgnoreCase) is JObject surface)
            {
                WriteSurface(result, before, incomingStyle, surface, current[SurfaceKey], incoming[SurfaceKey], owner);
            }

            if (!_isFrame && merged.GetValue(BackgroundKey, StringComparison.OrdinalIgnoreCase) != null)
            {
                result.ToastBackgroundImagePath = ResolveImage(
                    merged[BackgroundKey],
                    current[BackgroundKey],
                    incoming[BackgroundKey],
                    before.ToastBackgroundImagePath,
                    incomingStyle.ToastBackgroundImagePath,
                    owner,
                    NotificationImageSlot.Background);
            }

            foreach (var property in merged.Properties().ToList())
            {
                var kind = ParseKindKey(property.Name);
                if (!kind.HasValue)
                {
                    continue;
                }

                if (JsonThreeWayMerge.SameValue(property.Value, null))
                {
                    result.ClearKindStyle(kind.Value);
                    continue;
                }

                var beforeKind = before.HasKindStyle(kind.Value) ? before.ResolveKind(kind.Value) : null;
                var incomingKind = incomingStyle.HasKindStyle(kind.Value) ? incomingStyle.ResolveKind(kind.Value) : null;
                var currentKind = current[property.Name] as JObject;
                var incomingKindValue = incoming[property.Name] as JObject;
                var kindOwner = owner.ForNotificationKind(kind.Value);
                var target = result.EnableKindStyle(kind.Value);
                WriteSurface(target, beforeKind, incomingKind, property.Value[SurfaceKey], currentKind?[SurfaceKey], incomingKindValue?[SurfaceKey], kindOwner);
                if (!_isFrame)
                {
                    target.ToastBackgroundImagePath = ResolveImage(
                        property.Value[BackgroundKey],
                        currentKind?[BackgroundKey],
                        incomingKindValue?[BackgroundKey],
                        beforeKind?.ToastBackgroundImagePath,
                        incomingKind?.ToastBackgroundImagePath,
                        kindOwner,
                        NotificationImageSlot.Background);
                }
            }
        }

        /// <summary>Writes a merged surface onto <paramref name="target"/>, its badge images resolved slot by slot.</summary>
        private void WriteSurface(
            NotificationStyleSettings target,
            NotificationStyleSettings before,
            NotificationStyleSettings incomingStyle,
            JToken merged,
            JToken current,
            JToken incoming,
            NotificationImageOwner owner)
        {
            if (!(merged is JObject mergedSurface))
            {
                return;
            }

            var fields = (JObject)mergedSurface.DeepClone();
            fields.Remove(BadgeImagesKey);
            var surface = _isFrame ? NotificationSurfaceStyle.CreateFrameDefault() : NotificationSurfaceStyle.CreateToastDefault();
            using (var reader = fields.CreateReader())
            {
                PopulateSerializer.Populate(reader, surface);
            }

            var mergedBadges = mergedSurface.GetValue(BadgeImagesKey, StringComparison.OrdinalIgnoreCase);
            var currentBadges = (current as JObject)?.GetValue(BadgeImagesKey, StringComparison.OrdinalIgnoreCase);
            var incomingBadges = (incoming as JObject)?.GetValue(BadgeImagesKey, StringComparison.OrdinalIgnoreCase);
            var paths = new List<KeyValuePair<NotificationImageSlot, string>>();
            foreach (var pair in _isFrame ? FrameBadgeSlots : ToastBadgeSlots)
            {
                paths.Add(new KeyValuePair<NotificationImageSlot, string>(pair.Value, ResolveImage(
                    mergedBadges?[pair.Key],
                    currentBadges?[pair.Key],
                    incomingBadges?[pair.Key],
                    NotificationImageSlotMap.GetPath(before, pair.Value),
                    NotificationImageSlotMap.GetPath(incomingStyle, pair.Value),
                    owner,
                    pair.Value)));
            }

            if (_isFrame)
            {
                target.Frame = surface;
            }
            else
            {
                target.Toast = surface;
            }

            foreach (var pair in paths)
            {
                NotificationImageSlotMap.SetPath(target, pair.Key, pair.Value);
            }
        }

        /// <summary>
        /// The slot's path after the merge: none, the file the scope already has, or the
        /// package's file copied into the scope's slot.
        /// </summary>
        private string ResolveImage(
            JToken merged,
            JToken current,
            JToken incoming,
            string currentPath,
            string incomingPath,
            NotificationImageOwner owner,
            NotificationImageSlot slot)
        {
            if (JsonThreeWayMerge.SameValue(merged, null))
            {
                return null;
            }

            if (JsonThreeWayMerge.SameValue(merged, current))
            {
                return currentPath;
            }

            if (JsonThreeWayMerge.SameValue(merged, incoming) && !string.IsNullOrWhiteSpace(incomingPath))
            {
                return RunSync(() => _images.MaterializeAsync(incomingPath, owner, slot, CancellationToken.None));
            }

            return currentPath;
        }

        private void WriteTemplate(NotificationStyleScope scope, JObject merged, JObject current, JObject incoming, string incomingTemplate)
        {
            var value = merged.GetValue(TemplateKey, StringComparison.OrdinalIgnoreCase);
            if (JsonThreeWayMerge.SameValue(value, current[TemplateKey])
                || !JsonThreeWayMerge.SameValue(value, incoming[TemplateKey]))
            {
                return;
            }

            try
            {
                _templates.Write(_isFrame, scope.TemplateProviderKey, scope.GameId, incomingTemplate);
            }
            catch (Exception ex)
            {
                _warn?.Invoke(ex, $"Failed installing the {(_isFrame ? "frame" : "notification")} template of a library item.");
                lock (_errorsSync)
                {
                    _templateErrors.Add(ex.Message);
                }
            }
        }

        private string ReadTemplate(NotificationStyleScope scope)
        {
            return _templates.Read(_isFrame, scope.TemplateProviderKey, scope.GameId);
        }

        // ---- projection -------------------------------------------------------------------------

        private JObject ProjectKind(NotificationStyleSettings kindStyle)
        {
            var result = new JObject { [SurfaceKey] = ProjectSurface(kindStyle) };
            if (!_isFrame)
            {
                result[BackgroundKey] = LibraryFileTokens.FileValue(kindStyle?.ToastBackgroundImagePath);
            }

            return result;
        }

        private JObject ProjectSurface(NotificationStyleSettings style)
        {
            var source = style ?? NotificationStyleSettings.CreateDefault();
            var surface = _isFrame ? source.Frame : source.Toast;
            var result = JObject.FromObject(surface, ProjectionSerializer);
            var badges = new JObject();
            foreach (var pair in _isFrame ? FrameBadgeSlots : ToastBadgeSlots)
            {
                badges[pair.Key] = LibraryFileTokens.FileValue(NotificationImageSlotMap.GetPath(source, pair.Value));
            }

            result[BadgeImagesKey] = badges;
            return result;
        }

        private static IEnumerable<NotificationKind> KindsOf(NotificationStyleSettings style)
        {
            if (style == null)
            {
                return Enumerable.Empty<NotificationKind>();
            }

            return style.KindStyles
                .Where(pair => pair.Value != null)
                .Select(pair => NotificationImageStore.TryParseNotificationKind(pair.Key, out var kind) ? (NotificationKind?)kind : null)
                .Where(kind => kind.HasValue)
                .Select(kind => kind.Value)
                .Distinct()
                .ToList();
        }

        private static NotificationKind? ParseKindKey(string key)
        {
            if (string.IsNullOrEmpty(key) || !key.StartsWith(KindPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return NotificationImageStore.TryParseNotificationKind(key.Substring(KindPrefix.Length), out var kind)
                ? kind
                : (NotificationKind?)null;
        }

        private static void RunSync(Func<Task> work)
        {
            Task.Run(work).GetAwaiter().GetResult();
        }

        private static T RunSync<T>(Func<Task<T>> work)
        {
            return Task.Run(work).GetAwaiter().GetResult();
        }
    }
}
