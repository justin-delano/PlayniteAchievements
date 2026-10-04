using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PlayniteAchievements.Models.Settings;

namespace PlayniteAchievements.Services.Workshop
{
    /// <summary>
    /// Updates a Workshop game data item without discarding what the user changed since they
    /// installed it. Three inputs: the data as it was right after the previous install (the
    /// baseline), the data as it is now (current), and the freshly imported new version
    /// (incoming). Per field, and per key for the dictionaries keyed by achievement or
    /// category, the rule is: if the user left it as the baseline had it, take the incoming
    /// value (including its removal); if the user changed it, keep the user's value. Lists and
    /// scalars count as one field each.
    /// </summary>
    public static class GameCustomDataThreeWayMerge
    {
        private static readonly HashSet<string> Untouched = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(GameCustomDataFile.PlayniteGameId),
            nameof(GameCustomDataFile.SchemaVersion)
        };

        private static readonly JsonSerializer Serializer = JsonSerializer.CreateDefault();

        /// <summary>
        /// The merged data, starting from <paramref name="incoming"/> and re-applying the user's
        /// edits. <paramref name="keptEdits"/> counts the fields and keys where the user's value
        /// won over a different incoming one.
        /// </summary>
        public static GameCustomDataFile Merge(
            GameCustomDataFile baseline,
            GameCustomDataFile current,
            GameCustomDataFile incoming,
            out int keptEdits)
        {
            if (incoming == null)
            {
                throw new ArgumentNullException(nameof(incoming));
            }

            keptEdits = 0;
            if (baseline == null || current == null)
            {
                return incoming;
            }

            var result = incoming.Clone();
            foreach (var property in typeof(GameCustomDataFile).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length > 0 || Untouched.Contains(property.Name))
                {
                    continue;
                }

                var baseValue = property.GetValue(baseline);
                var currentValue = property.GetValue(current);
                var incomingValue = property.GetValue(incoming);

                if (IsStringKeyedDictionary(property.PropertyType))
                {
                    property.SetValue(result, MergeDictionary(property.PropertyType, baseValue, currentValue, incomingValue, ref keptEdits));
                    continue;
                }

                if (!SameJson(currentValue, baseValue))
                {
                    if (!SameJson(currentValue, incomingValue))
                    {
                        keptEdits++;
                    }

                    property.SetValue(result, CloneValue(currentValue));
                }
            }

            return result;
        }

        private static bool IsStringKeyedDictionary(Type type)
        {
            return type.IsGenericType
                   && type.GetGenericTypeDefinition() == typeof(Dictionary<,>)
                   && type.GetGenericArguments()[0] == typeof(string);
        }

        private static object MergeDictionary(Type dictionaryType, object baseValue, object currentValue, object incomingValue, ref int keptEdits)
        {
            var baseMap = baseValue as IDictionary;
            var currentMap = currentValue as IDictionary;
            var incomingMap = incomingValue as IDictionary;
            if (currentMap == null && baseMap == null)
            {
                return incomingValue;
            }

            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var map in new[] { baseMap, currentMap, incomingMap })
            {
                if (map == null)
                {
                    continue;
                }

                foreach (var key in map.Keys)
                {
                    keys.Add((string)key);
                }
            }

            var result = (IDictionary)Activator.CreateInstance(dictionaryType);
            foreach (var key in keys)
            {
                var baseEntry = Lookup(baseMap, key);
                var currentEntry = Lookup(currentMap, key);
                var incomingEntry = Lookup(incomingMap, key);

                object chosen;
                if (SameJson(currentEntry, baseEntry))
                {
                    chosen = incomingEntry;
                }
                else
                {
                    chosen = currentEntry;
                    if (!SameJson(currentEntry, incomingEntry))
                    {
                        keptEdits++;
                    }
                }

                if (chosen != null)
                {
                    result[key] = CloneValue(chosen);
                }
            }

            return result.Count > 0 ? result : null;
        }

        private static object Lookup(IDictionary map, string key)
        {
            if (map == null)
            {
                return null;
            }

            foreach (var entry in map.Keys)
            {
                if (string.Equals((string)entry, key, StringComparison.OrdinalIgnoreCase))
                {
                    return map[entry];
                }
            }

            return null;
        }

        private static bool SameJson(object left, object right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            return JToken.DeepEquals(ToToken(left), ToToken(right));
        }

        private static JToken ToToken(object value)
        {
            if (value == null)
            {
                return JValue.CreateNull();
            }

            if (value is string s)
            {
                return new JValue(s);
            }

            if (value is IEnumerable enumerable && !(value is string))
            {
                var token = JToken.FromObject(value, Serializer);
                // An empty collection and a missing one mean the same thing to the resolvers.
                return token is JContainer container && !container.HasValues ? JValue.CreateNull() : token;
            }

            return JToken.FromObject(value, Serializer);
        }

        private static object CloneValue(object value)
        {
            if (value == null || value is string || value.GetType().IsValueType)
            {
                return value;
            }

            return JToken.FromObject(value, Serializer).ToObject(value.GetType(), Serializer);
        }
    }
}
