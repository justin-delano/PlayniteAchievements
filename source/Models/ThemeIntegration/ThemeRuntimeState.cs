namespace PlayniteAchievements.Models.ThemeIntegration
{
    internal sealed class ThemeRuntimeState
    {
        public SelectedGameRuntimeState SelectedGame { get; set; } = SelectedGameRuntimeState.Empty;
        public LibraryRuntimeState Library { get; set; } = new LibraryRuntimeState();
    }
}
