using System;
using System.Collections.Generic;
using System.Linq;

namespace PlayniteAchievements.Services
{
    public sealed class SearchTextIndex<T>
    {
        private readonly Func<T, string> _textBuilder;
        private readonly Dictionary<T, string> _textByItem;

        public SearchTextIndex(Func<T, string> textBuilder)
            : this(textBuilder, null)
        {
        }

        public SearchTextIndex(Func<T, string> textBuilder, IEqualityComparer<T> comparer)
        {
            _textBuilder = textBuilder ?? throw new ArgumentNullException(nameof(textBuilder));
            _textByItem = new Dictionary<T, string>(comparer ?? EqualityComparer<T>.Default);
        }

        public void Rebuild(IEnumerable<T> items)
        {
            _textByItem.Clear();

            foreach (var item in items ?? Enumerable.Empty<T>())
            {
                if (item == null)
                {
                    continue;
                }

                _textByItem[item] = Normalize(_textBuilder(item));
            }
        }

        public void Clear()
        {
            _textByItem.Clear();
        }

        public void Invalidate(T item)
        {
            if (item == null)
            {
                return;
            }

            _textByItem.Remove(item);
        }

        public bool Matches(T item, SearchQuery query)
        {
            if (!query.HasValue)
            {
                return true;
            }

            if (item == null)
            {
                return false;
            }

            return query.Matches(GetText(item));
        }

        private string GetText(T item)
        {
            if (!_textByItem.TryGetValue(item, out var text))
            {
                text = Normalize(_textBuilder(item));
                _textByItem[item] = text;
            }

            return text;
        }

        private static string Normalize(string value)
        {
            return value ?? string.Empty;
        }
    }
}
