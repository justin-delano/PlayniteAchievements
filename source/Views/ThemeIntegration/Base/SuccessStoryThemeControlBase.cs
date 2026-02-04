using System;
using Playnite.SDK.Controls;
using Playnite.SDK.Models;
using PlayniteAchievements;

namespace PlayniteAchievements.Views.ThemeIntegration.Base
{
    /// <summary>
    /// Base class for SuccessStory theme integration controls.
    /// Provides common initialization and game context change handling for SuccessStory-compatible controls.
    /// </summary>
    public abstract class SuccessStoryThemeControlBase : PluginUserControl
    {
        /// <summary>
        /// Gets the plugin instance for this control.
        /// </summary>
        protected PlayniteAchievementsPlugin Plugin { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SuccessStoryThemeControlBase"/> class.
        /// Derived classes must call InitializeComponent in their constructors.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the PlayniteAchievementsPlugin instance is not available.
        /// </exception>
        protected SuccessStoryThemeControlBase()
        {
            Plugin = PlayniteAchievementsPlugin.Instance
                ?? throw new InvalidOperationException("Plugin instance not available");

            DataContext = Plugin.Settings;
        }

        /// <summary>
        /// Called when the game context changes for this control.
        /// Requests a theme update for the new game context.
        /// </summary>
        /// <param name="oldContext">The previous game context.</param>
        /// <param name="newContext">The new game context.</param>
        public override void GameContextChanged(Game oldContext, Game newContext)
        {
            Plugin.RequestThemeUpdate(newContext);
        }
    }
}
