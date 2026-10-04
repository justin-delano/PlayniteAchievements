using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PlayniteAchievements.Services.Tests
{
    /// <summary>
    /// Guards the fixed theme the Workshop preview image renders with: every PlayAch.* resource
    /// the preview controls, the bundled notification templates and the dictionaries they merge
    /// look up dynamically must be defined in WorkshopPreviewNeutralTheme.xaml (or by one of those
    /// merged dictionaries), so no value falls through to the sharer's Playnite theme.
    /// </summary>
    [TestClass]
    public class WorkshopPreviewNeutralThemeTests
    {
        private const string NeutralThemeFile = "WorkshopPreviewNeutralTheme.xaml";

        // Set by WorkshopPreviewRasterizer from the application at render time (Playnite's icon font).
        private static readonly string[] CodeSetKeys = { "PlayAch.FontFamily.Icon" };

        private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

        private static readonly Regex DynamicResourcePattern =
            new Regex(@"\{DynamicResource\s+([^\s}]+)\s*\}", RegexOptions.Compiled);

        private static readonly Regex MergedSourcePattern =
            new Regex(@"Source=""pack://application:,,,/PlayniteAchievements;component/([^""]+\.xaml)""", RegexOptions.Compiled);

        [TestMethod]
        public void NeutralTheme_DefinesEveryDynamicPlayAchKeyThePreviewRendersWith()
        {
            var sourceRoot = FindSourceRoot();
            var files = CollectPreviewXamlClosure(sourceRoot);
            var neutralKeys = ReadKeys(Path.Combine(sourceRoot, "Resources", NeutralThemeFile));

            // A key a merged dictionary defines resolves there, before the host's neutral theme.
            var mergedKeys = new HashSet<string>(files.SelectMany(ReadKeys), StringComparer.Ordinal);

            var referenced = files
                .SelectMany(file => DynamicResourcePattern.Matches(File.ReadAllText(file)).Cast<Match>()
                    .Select(match => new { File = file, Key = match.Groups[1].Value }))
                .Where(reference => reference.Key.StartsWith("PlayAch.", StringComparison.Ordinal))
                .ToList();
            Assert.IsTrue(referenced.Any(reference => reference.Key == "PlayAch.Brush.Text"), "The scan found no PlayAch.Brush.Text reference; the file set is wrong.");

            var missing = referenced
                .Where(reference => !neutralKeys.Contains(reference.Key)
                                    && !mergedKeys.Contains(reference.Key)
                                    && !CodeSetKeys.Contains(reference.Key))
                .Select(reference => reference.Key + " (" + Path.GetFileName(reference.File) + ")")
                .Distinct()
                .OrderBy(text => text, StringComparer.Ordinal)
                .ToList();

            Assert.AreEqual(0, missing.Count, "Not in the neutral preview theme: " + string.Join(", ", missing));
        }

        [TestMethod]
        public void NeutralTheme_DefinesEveryTokenThePluginPublishesToTheApplication()
        {
            var sourceRoot = FindSourceRoot();
            var neutralKeys = ReadKeys(Path.Combine(sourceRoot, "Resources", NeutralThemeFile));

            var service = File.ReadAllText(Path.Combine(sourceRoot, "Services", "UI", "PlayAchResourceService.cs"));
            var helper = File.ReadAllText(Path.Combine(sourceRoot, "Models", "Achievements", "RarityAppearanceHelper.cs"));
            var published = Regex.Matches(service, @"\b(?:Brush|FontSize|FontFamily|StaticFontFamily|Alias)\(""(PlayAch\.[^""]+)""")
                .Cast<Match>()
                .Concat(Regex.Matches(helper, @"resources\[""(PlayAch\.[^""]+)""\]\s*=").Cast<Match>())
                .Select(match => match.Groups[1].Value)
                .Distinct()
                .ToList();
            Assert.IsTrue(published.Count > 20, "The token scan found too few keys; the source patterns changed.");

            var missing = published
                .Where(key => !neutralKeys.Contains(key) && !CodeSetKeys.Contains(key))
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToList();
            Assert.AreEqual(0, missing.Count, "Not in the neutral preview theme: " + string.Join(", ", missing));
        }

        [TestMethod]
        public void Rasterizer_SetsTheCodeSetKeys()
        {
            var rasterizer = File.ReadAllText(Path.Combine(FindSourceRoot(), "Services", "Workshop", "WorkshopPreviewRasterizer.cs"));
            foreach (var key in CodeSetKeys)
            {
                StringAssert.Contains(rasterizer, "\"" + key + "\"");
            }
        }

        /// <summary>The preview controls, the bundled templates, and every plugin dictionary they merge, transitively.</summary>
        private static IReadOnlyList<string> CollectPreviewXamlClosure(string sourceRoot)
        {
            var pending = new Queue<string>(
                Directory.GetFiles(Path.Combine(sourceRoot, "Views", "Workshop", "Preview"), "*.xaml")
                    .Concat(Directory.GetFiles(Path.Combine(sourceRoot, "Resources", "DefaultTemplates"), "*.xaml")));
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            while (pending.Count > 0)
            {
                var file = Path.GetFullPath(pending.Dequeue());
                if (!seen.Add(file))
                {
                    continue;
                }

                Assert.IsTrue(File.Exists(file), "Merged dictionary not found: " + file);
                result.Add(file);
                foreach (Match match in MergedSourcePattern.Matches(File.ReadAllText(file)))
                {
                    pending.Enqueue(Path.Combine(sourceRoot, match.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar)));
                }
            }

            return result;
        }

        private static HashSet<string> ReadKeys(string file)
        {
            var document = XDocument.Load(file);
            return new HashSet<string>(
                document.Descendants()
                    .Select(element => (string)element.Attribute(XamlNamespace + "Key"))
                    .Where(key => !string.IsNullOrEmpty(key)),
                StringComparer.Ordinal);
        }

        private static string FindSourceRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, "source");
                if (File.Exists(Path.Combine(candidate, "PlayniteAchievements.csproj")))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            Assert.Fail("The repository's source folder was not found.");
            return null;
        }
    }
}
