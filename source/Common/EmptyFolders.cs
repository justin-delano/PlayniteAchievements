using System;
using System.IO;

namespace PlayniteAchievements.Common
{
    /// <summary>
    /// Removes the folders a store created for files it no longer holds, so a folder exists in
    /// the user data folder only while something is in it. Stores create their folder again on
    /// the next write.
    /// </summary>
    public static class EmptyFolders
    {
        /// <summary>
        /// Removes <paramref name="directory"/> when it is empty, then each parent up to and
        /// including <paramref name="root"/> while it is empty. A missing folder is skipped; a
        /// folder that is not empty or cannot be removed ends the walk. Does nothing for a
        /// folder outside <paramref name="root"/>. Never throws.
        /// </summary>
        public static void RemoveUpTo(string directory, string root)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(root))
                {
                    return;
                }

                var stop = Trim(Path.GetFullPath(root));
                var current = Trim(Path.GetFullPath(directory));
                if (!string.Equals(current, stop, StringComparison.OrdinalIgnoreCase)
                    && !current.StartsWith(stop + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                while (current != null)
                {
                    if (Directory.Exists(current))
                    {
                        // Not recursive: a folder something wrote into meanwhile throws and stays.
                        Directory.Delete(current, recursive: false);
                    }

                    if (string.Equals(current, stop, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    current = Path.GetDirectoryName(current);
                }
            }
            catch (IOException)
            {
                // Not empty, or in use.
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (ArgumentException)
            {
            }
            catch (NotSupportedException)
            {
            }
        }

        private static string Trim(string path)
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
