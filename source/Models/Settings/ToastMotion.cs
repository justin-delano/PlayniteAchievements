namespace PlayniteAchievements.Models.Settings
{
    /// <summary>
    /// How the toast card enters or leaves the screen. A null choice on the style keeps the
    /// theme's slide storyboard, or the built-in slide when the theme has none.
    /// </summary>
    public enum ToastMotion
    {
        /// <summary>Slides vertically from the screen edge the toast corner sits on.</summary>
        Slide,

        /// <summary>Slides horizontally from the side the toast corner sits on.</summary>
        SlideSide,

        /// <summary>Fades in or out in place.</summary>
        Fade,

        /// <summary>Scales up from slightly smaller while fading in, and the reverse on exit.</summary>
        Zoom,

        /// <summary>Appears and disappears with no motion.</summary>
        None
    }

    /// <summary>
    /// Easing applied to a <see cref="ToastMotion"/>.
    /// </summary>
    public enum ToastMotionFeel
    {
        /// <summary>Cubic ease with no overshoot.</summary>
        Smooth,

        /// <summary>Overshoots slightly on entry before settling.</summary>
        Bouncy
    }

    /// <summary>
    /// Duration scale for the entrance and exit. The hold time between them is the global
    /// notification duration and does not change.
    /// </summary>
    public enum ToastMotionSpeed
    {
        Quick,
        Normal,
        Relaxed
    }
}
