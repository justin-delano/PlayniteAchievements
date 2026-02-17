using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PlayniteAchievements.Common
{
    public static class CollectionHelper
    {
        /// <summary>
        /// Efficiently synchronizes an ObservableCollection with an IEnumerable source.
        /// Avoids clearing and re-adding, which causes UI flicker.
        /// </summary>
        public static void SynchronizeCollection<T>(ObservableCollection<T> collection, IEnumerable<T> source)
        {
            var sourceList = source.ToList();
            var sourceSet = new HashSet<T>(sourceList);
            var collectionSet = new HashSet<T>(collection);

            // Remove items from collection that are not in source
            for (int i = collection.Count - 1; i >= 0; i--)
            {
                if (!sourceSet.Contains(collection[i]))
                {
                    collection.RemoveAt(i);
                }
            }

            // Rebuild collectionSet after removals
            collectionSet = new HashSet<T>(collection);

            // Now sync items from source
            int sourceIndex = 0;
            int collectionIndex = 0;

            while (sourceIndex < sourceList.Count)
            {
                var sourceItem = sourceList[sourceIndex];

                if (collectionIndex < collection.Count)
                {
                    var collectionItem = collection[collectionIndex];

                    // Check if they're the same item (by value for value types and strings)
                    if (EqualityComparer<T>.Default.Equals(sourceItem, collectionItem))
                    {
                        // Same item, same position - move to next
                        sourceIndex++;
                        collectionIndex++;
                    }
                    else if (sourceSet.Contains(collectionItem))
                    {
                        // The collection item exists somewhere in source, but not at this position
                        // Find if sourceItem exists later in collection
                        int foundIndex = -1;
                        for (int j = collectionIndex + 1; j < collection.Count; j++)
                        {
                            if (EqualityComparer<T>.Default.Equals(sourceItem, collection[j]))
                            {
                                foundIndex = j;
                                break;
                            }
                        }

                        if (foundIndex >= 0)
                        {
                            // sourceItem exists later in collection, move it here
                            collection.Move(foundIndex, collectionIndex);
                            sourceIndex++;
                            collectionIndex++;
                        }
                        else if (!collectionSet.Contains(sourceItem))
                        {
                            // sourceItem doesn't exist in collection at all, insert it
                            collection.Insert(collectionIndex, sourceItem);
                            collectionSet.Add(sourceItem);
                            sourceIndex++;
                            collectionIndex++;
                        }
                        else
                        {
                            // sourceItem exists earlier in collection (unusual), skip and continue
                            sourceIndex++;
                        }
                    }
                    else
                    {
                        // collectionItem is not in source, replace it
                        // (This shouldn't happen since we removed items not in source above)
                        collection[collectionIndex] = sourceItem;
                        sourceIndex++;
                        collectionIndex++;
                    }
                }
                else
                {
                    // Collection is shorter than source, add remaining items
                    collection.Add(sourceItem);
                    sourceIndex++;
                    collectionIndex++;
                }
            }

        }

        /// <summary>
        /// Efficiently synchronizes a collection of value types.
        /// </summary>
        public static void SynchronizeValueCollection<T>(IList<T> collection, IList<T> source)
        {
            // Add or update
            for (int i = 0; i < source.Count; i++)
            {
                var item = source[i];
                if (i < collection.Count)
                {
                    if (!EqualityComparer<T>.Default.Equals(collection[i], item))
                    {
                        collection[i] = item;
                    }
                }
                else
                {
                    collection.Add(item);
                }
            }

            // Remove surplus
            while (collection.Count > source.Count)
            {
                collection.RemoveAt(collection.Count - 1);
            }
        }
    }
}
