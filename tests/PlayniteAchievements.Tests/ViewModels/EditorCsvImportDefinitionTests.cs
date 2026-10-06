using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PlayniteAchievements.Tests.ViewModels
{
    /// <summary>
    /// The editor's CSV import is not linked into this project, so the rules that keep it from
    /// writing provider-owned values, or from splitting into one undo step per field, are checked
    /// against its source.
    /// </summary>
    [TestClass]
    public class EditorCsvImportDefinitionTests
    {
        [TestMethod]
        public void ProviderOwnedFields_AreOnlyAssignedBehindTheRowsOwnGate()
        {
            var source = ReadCsvPartial();

            AssertGuarded(source, "row.RarityInput =", "if (row.CanEditRarity)");
            AssertGuarded(source, "row.ProgressNumText =", "if (row.CanEditProgress)");
            AssertGuarded(source, "row.ProgressDenomText =", "if (row.CanEditProgress)");
            AssertGuarded(source, "row.CategoryLabel = ResolveImportedCategory", "if (row.CanEditAssignments)");
        }

        [TestMethod]
        public void TheImport_IsOneUndoStep()
        {
            var csv = ReadCsvPartial();
            var viewModel = File.ReadAllText(FindRepoFile(
                "source", "ViewModels", "ManageAchievements", "ManageAchievementsEditorViewModel.cs"));

            StringAssert.Contains(csv, "MarkUndoIntent(EditorEditIntent.Command(\"ImportCsv\"");
            StringAssert.Contains(csv, "_isImportingCsv = true;");
            StringAssert.Contains(csv, "_isImportingCsv = false;");
            StringAssert.Contains(
                viewModel,
                "if (_isApplyingUndo || _isImportingCsv)",
                "The cell setters name a field edit; during an import that would close the import's step.");
            StringAssert.Contains(viewModel, "_isImportingCsv ||");
        }

        private static void AssertGuarded(string source, string assignment, string guard)
        {
            var index = source.IndexOf(assignment, StringComparison.Ordinal);
            Assert.IsTrue(index >= 0, $"Expected {assignment} in the CSV import.");
            while (index >= 0)
            {
                // The nearest preceding if must be the gate.
                var preceding = source.Substring(0, index);
                var lastIf = Regex.Matches(preceding, @"if \([^\r\n]*\)").Cast<Match>().LastOrDefault();
                Assert.IsNotNull(lastIf, $"{assignment} is not inside any condition.");
                Assert.AreEqual(guard, lastIf.Value, $"{assignment} must sit directly under {guard}.");
                index = source.IndexOf(assignment, index + assignment.Length, StringComparison.Ordinal);
            }
        }

        private static string ReadCsvPartial()
        {
            return File.ReadAllText(FindRepoFile(
                "source", "ViewModels", "ManageAchievements", "ManageAchievementsEditorViewModel.Csv.cs"));
        }

        private static string FindRepoFile(params string[] parts)
        {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null)
            {
                var path = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
                if (File.Exists(path))
                {
                    return path;
                }

                directory = directory.Parent;
            }

            Assert.Fail($"Could not find {string.Join(Path.DirectorySeparatorChar.ToString(), parts)}.");
            return null;
        }
    }
}
