using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace PlayniteAchievements.Services.Library
{
    /// <summary>
    /// Merges a new version of a JSON document into a copy the user may have edited, the generic
    /// form of <see cref="Workshop.GameCustomDataThreeWayMerge"/>. Three inputs: the document as
    /// it was right after the previous apply (the baseline), the document as it is now (current),
    /// and the new version (incoming). Objects, string-keyed dictionaries included, are merged per
    /// property down to their leaves; arrays, scalars and the caller-listed atomic paths are
    /// leaves. Per leaf the rule is: if the user left it as the baseline had it, take the incoming
    /// value (including its removal); if the user changed it, keep the user's value. A missing
    /// value, a JSON null and an empty object or array count as the same value.
    /// </summary>
    public static class JsonThreeWayMerge
    {
        /// <summary>The wildcard that matches any one property name in an atomic path.</summary>
        public const string AnySegment = "*";

        /// <summary>
        /// The merged document, starting from <paramref name="incoming"/> and re-applying the
        /// user's edits. Without a baseline or a current document the result is a copy of
        /// <paramref name="incoming"/>. <paramref name="keptEdits"/> counts the leaves where the
        /// user's value won over a different incoming one. The result shares no tokens with the
        /// inputs.
        /// </summary>
        /// <param name="atomicPaths">
        /// Dotted property paths merged as one value instead of per property, matched without
        /// regard to case; <see cref="AnySegment"/> matches any one name, so
        /// <c>Overrides.*</c> makes every entry of the <c>Overrides</c> dictionary one leaf. An
        /// empty path makes the whole document one leaf.
        /// </param>
        public static JToken Merge(
            JToken baseline,
            JToken current,
            JToken incoming,
            IEnumerable<string> atomicPaths,
            out int keptEdits)
        {
            if (incoming == null)
            {
                throw new ArgumentNullException(nameof(incoming));
            }

            keptEdits = 0;
            if (baseline == null || current == null)
            {
                return incoming.DeepClone();
            }

            var context = new MergeContext(atomicPaths);
            var merged = MergeNode(baseline, current, incoming, new List<string>(), context);
            keptEdits = context.KeptEdits;
            return merged ?? JValue.CreateNull();
        }

        /// <summary>The merge with every object merged per property down to its leaves.</summary>
        public static JToken Merge(JToken baseline, JToken current, JToken incoming, out int keptEdits)
        {
            return Merge(baseline, current, incoming, null, out keptEdits);
        }

        /// <summary>
        /// True when the two tokens are the same value under the merge's equality: missing, null
        /// and empty containers are equal, and object properties that hold one of those count as
        /// absent.
        /// </summary>
        public static bool SameValue(JToken left, JToken right)
        {
            return JToken.DeepEquals(Canonical(left) ?? JValue.CreateNull(), Canonical(right) ?? JValue.CreateNull());
        }

        private sealed class MergeContext
        {
            private readonly List<string[]> _atomic;

            public MergeContext(IEnumerable<string> atomicPaths)
            {
                _atomic = (atomicPaths ?? Enumerable.Empty<string>())
                    .Where(path => path != null)
                    .Select(path => path.Length == 0 ? new string[0] : path.Split('.'))
                    .ToList();
            }

            public int KeptEdits { get; set; }

            public bool IsAtomic(List<string> path)
            {
                foreach (var pattern in _atomic)
                {
                    if (pattern.Length != path.Count)
                    {
                        continue;
                    }

                    var matches = true;
                    for (var i = 0; i < pattern.Length && matches; i++)
                    {
                        matches = pattern[i] == AnySegment
                                  || string.Equals(pattern[i], path[i], StringComparison.OrdinalIgnoreCase);
                    }

                    if (matches)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>The merged value at one path, or null when the result has no value there.</summary>
        private static JToken MergeNode(JToken baseline, JToken current, JToken incoming, List<string> path, MergeContext context)
        {
            var descend = !context.IsAtomic(path)
                          && IsObjectOrEmpty(baseline) && IsObjectOrEmpty(current) && IsObjectOrEmpty(incoming)
                          && (baseline is JObject || current is JObject || incoming is JObject);
            if (!descend)
            {
                return MergeLeaf(baseline, current, incoming, context);
            }

            var baseObject = baseline as JObject;
            var currentObject = current as JObject;
            var incomingObject = incoming as JObject;

            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in new[] { incomingObject, currentObject, baseObject })
            {
                if (source == null)
                {
                    continue;
                }

                foreach (var property in source.Properties())
                {
                    if (seen.Add(property.Name))
                    {
                        names.Add(property.Name);
                    }
                }
            }

            var result = new JObject();
            foreach (var name in names)
            {
                path.Add(name);
                var merged = MergeNode(
                    Lookup(baseObject, name),
                    Lookup(currentObject, name),
                    Lookup(incomingObject, name),
                    path,
                    context);
                path.RemoveAt(path.Count - 1);

                if (merged != null)
                {
                    result[name] = merged;
                }
            }

            if (result.HasValues)
            {
                return result;
            }

            // Nothing is left: keep the incoming document's own spelling of "empty".
            return incoming?.DeepClone();
        }

        private static JToken MergeLeaf(JToken baseline, JToken current, JToken incoming, MergeContext context)
        {
            if (SameValue(current, baseline))
            {
                return incoming?.DeepClone();
            }

            if (!SameValue(current, incoming))
            {
                context.KeptEdits++;
            }

            return current?.DeepClone();
        }

        private static JToken Lookup(JObject source, string name)
        {
            return source?.GetValue(name, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsObjectOrEmpty(JToken token)
        {
            return token is JObject || IsEmpty(token);
        }

        // A JValue made from a null string has the String type and a null value; it is the same
        // JSON null as one read from text.
        private static bool IsEmpty(JToken token)
        {
            return token == null
                   || token.Type == JTokenType.Null
                   || token.Type == JTokenType.Undefined
                   || (token is JValue value && value.Value == null)
                   || (token is JContainer container && !container.HasValues);
        }

        /// <summary>The token with every empty value removed from objects, or null when it is empty itself.</summary>
        private static JToken Canonical(JToken token)
        {
            if (IsEmpty(token))
            {
                return null;
            }

            if (token is JObject obj)
            {
                var result = new JObject();
                foreach (var property in obj.Properties())
                {
                    var value = Canonical(property.Value);
                    if (value != null)
                    {
                        result[property.Name] = value;
                    }
                }

                return result.HasValues ? result : null;
            }

            if (token is JArray array)
            {
                var result = new JArray();
                foreach (var element in array)
                {
                    result.Add(Canonical(element) ?? JValue.CreateNull());
                }

                return result;
            }

            return token;
        }
    }
}
