namespace PlayniteAchievements.Services.Showcase
{
    /// <summary>
    /// Persists showcase settings through the plugin and notifies live surfaces of the change.
    /// Consolidates the persist-then-broadcast idiom shared by the widget renderers.
    /// </summary>
    public static class ShowcaseConfigurationCommit
    {
        public static void Commit()
        {
            PlayniteAchievementsPlugin.Instance?.PersistSettingsForUi();
            ShowcaseConfigurationEvents.RaiseChanged();
        }
    }
}
