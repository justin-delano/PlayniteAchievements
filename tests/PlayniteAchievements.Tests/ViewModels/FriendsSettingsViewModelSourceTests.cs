using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;

namespace PlayniteAchievements.Tests.ViewModels
{
    [TestClass]
    public class FriendsSettingsViewModelSourceTests
    {
        [TestMethod]
        public void FriendsSettings_MirrorsExophaseRowsIntoProviderSettingsBeforePersist()
        {
            var code = File.ReadAllText(FindRepoFile("source", "ViewModels", "FriendsSettingsViewModel.cs"));

            StringAssert.Contains(code, "SyncExophaseProviderFriends(providerKey)");
            StringAssert.Contains(code, "exophaseSettings.Friends = friends");
            StringAssert.Contains(code, "_providerRegistry?.Save(exophaseSettings, persistToDisk: false)");
        }

        [TestMethod]
        public void FriendsSettings_MergedSteamAndExophaseDisablesExophaseSteamPlatform()
        {
            var code = File.ReadAllText(FindRepoFile("source", "ViewModels", "FriendsSettingsViewModel.cs"));

            StringAssert.Contains(code, "ApplyExophasePlatformConflicts()");
            StringAssert.Contains(code, "disabled.Add(\"steam\")");
            StringAssert.Contains(code, "SelectedPlatforms?.RemoveAll(token => string.Equals(token, \"steam\", StringComparison.OrdinalIgnoreCase))");
        }

        [TestMethod]
        public void FriendsSettings_UnmergeActionBindsToGridCommandAndRefreshesCanExecute()
        {
            var xaml = File.ReadAllText(FindRepoFile("source", "Views", "FriendsSettingsTab.xaml"));
            var code = File.ReadAllText(FindRepoFile("source", "ViewModels", "FriendsSettingsViewModel.cs"));

            StringAssert.Contains(xaml, "DataContext.UnmergeFriendCommand, RelativeSource={RelativeSource AncestorType={x:Type DataGrid}}");
            StringAssert.Contains(xaml, "IsEnabled=\"{Binding CanUnmerge}\"");
            StringAssert.Contains(code, "(UnmergeFriendCommand as RelayCommand)?.RaiseCanExecuteChanged();");
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

            throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar.ToString(), parts));
        }
    }
}
