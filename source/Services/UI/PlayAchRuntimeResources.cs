using System;
using System.Collections;
using System.Windows;

namespace PlayniteAchievements.Services.UI
{
    /// <summary>
    /// The application-scope dictionary that holds the generated PlayAch tokens, badges and
    /// brushes. Each write to Application.Current.Resources re-resolves DynamicResource
    /// references across every open window, so <see cref="Update"/> stages a pass of writes in
    /// a copy and swaps the copy in with one merged-dictionary change. The dictionary is kept
    /// last in the merged list so it wins over the plugin's static dictionaries and the theme's.
    /// </summary>
    internal static class PlayAchRuntimeResources
    {
        private static ResourceDictionary _current;

        public static void Update(Action<ResourceDictionary> write)
        {
            var app = Application.Current;
            if (app == null || write == null)
            {
                return;
            }

            if (!app.Dispatcher.CheckAccess())
            {
                app.Dispatcher.Invoke(() => Update(write));
                return;
            }

            var staged = new ResourceDictionary();
            if (_current != null)
            {
                foreach (DictionaryEntry entry in _current)
                {
                    staged[entry.Key] = entry.Value;
                }
            }

            write(staged);

            var merged = app.Resources.MergedDictionaries;
            var index = _current == null ? -1 : merged.IndexOf(_current);
            if (index >= 0 && index == merged.Count - 1)
            {
                merged[index] = staged;
            }
            else
            {
                if (index >= 0)
                {
                    merged.RemoveAt(index);
                }

                merged.Add(staged);
            }

            _current = staged;
        }
    }
}
