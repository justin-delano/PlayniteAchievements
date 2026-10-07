using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using PlayniteAchievements.Models.Settings;

namespace PlayniteAchievements.Services.UI
{
    /// <summary>
    /// The toast motion one wave runs, resolved from the style's motion fields. Null from
    /// <see cref="ToastMotionStoryboardFactory.Resolve"/> means the style sets none, and the
    /// theme's slide storyboards apply as before.
    /// </summary>
    internal sealed class ToastMotionPlan
    {
        public ToastMotion Entrance { get; set; }

        public ToastMotion Exit { get; set; }

        public ToastMotionFeel? Feel { get; set; }

        public double SpeedScale { get; set; } = 1d;
    }

    /// <summary>
    /// Builds the entrance and exit storyboards for a style's toast motion. Every child is left
    /// untargeted with explicit From/To, so the toast service binds it to the slide host exactly as
    /// it binds a theme storyboard. The paths match the host's transform group from
    /// <see cref="ToastSurfaceFactory.BuildSlideHost"/>: scale at index 0, translate at index 1.
    /// </summary>
    internal static class ToastMotionStoryboardFactory
    {
        /// <summary>The scale a zoom starts from on entry and ends at on exit.</summary>
        public const double ZoomFromScale = 0.85;

        private const double OvershootAmplitude = 0.35;

        public static ToastMotionPlan Resolve(NotificationSurfaceStyle surface)
        {
            if (surface == null || !surface.HasCustomMotion)
            {
                return null;
            }

            var entrance = surface.EntranceMotion ?? ToastMotion.Slide;
            return new ToastMotionPlan
            {
                Entrance = entrance,
                Exit = surface.ExitMotion ?? entrance,
                Feel = surface.MotionFeel,
                SpeedScale = SpeedScale(surface.MotionSpeed)
            };
        }

        public static double SpeedScale(ToastMotionSpeed? speed)
        {
            switch (speed)
            {
                case ToastMotionSpeed.Quick:
                    return 0.7;
                case ToastMotionSpeed.Relaxed:
                    return 1.5;
                default:
                    return 1d;
            }
        }

        /// <summary>The motion's duration in ms; 0 for <see cref="ToastMotion.None"/>.</summary>
        public static double DurationMs(ToastMotion motion, double baseMs, double speedScale)
        {
            return motion == ToastMotion.None ? 0d : Math.Round(baseMs * speedScale);
        }

        /// <summary>Whether the motion moves the card and so needs travel room in the window.</summary>
        public static bool Travels(ToastMotion motion)
        {
            return motion == ToastMotion.Slide || motion == ToastMotion.SlideSide;
        }

        /// <summary>
        /// Where the card sits off screen for this motion, relative to its resting place, in DIPs.
        /// A vertical slide leaves through the top or bottom edge the position is on; a side slide
        /// leaves through the side the position is on, and slides vertically at a centered
        /// position, which is against neither side. Zero for motions that stay in place.
        /// </summary>
        public static Vector TravelOffset(
            ToastMotion motion, ToastScreenCorner position, double verticalDip, double horizontalDip)
        {
            var horizontal = position.Horizontal();
            if (motion == ToastMotion.SlideSide && horizontal != ToastHorizontalAlignment.Center)
            {
                return new Vector(
                    horizontal == ToastHorizontalAlignment.Right ? horizontalDip : -horizontalDip, 0d);
            }

            if (motion == ToastMotion.Slide || motion == ToastMotion.SlideSide)
            {
                return new Vector(0d, position.IsBottom() ? verticalDip : -verticalDip);
            }

            return new Vector(0d, 0d);
        }

        /// <summary>
        /// The storyboard for one entrance (<paramref name="entering"/>) or exit. An entrance runs
        /// from <paramref name="offset"/>, transparent or shrunk to the resting card; an exit runs
        /// the same shape backwards. Null for <see cref="ToastMotion.None"/> or a zero duration.
        /// </summary>
        public static Storyboard Build(
            ToastMotion motion, bool entering, ToastMotionFeel? feel, double durationMs, Vector offset)
        {
            if (motion == ToastMotion.None || durationMs <= 0)
            {
                return null;
            }

            var duration = new Duration(TimeSpan.FromMilliseconds(durationMs));
            var storyboard = new Storyboard();
            var motionEase = MotionEase(entering, feel);
            switch (motion)
            {
                case ToastMotion.Slide:
                case ToastMotion.SlideSide:
                    // The axis follows the offset rather than the motion: a side slide at a
                    // centered position travels vertically (see TravelOffset).
                    var horizontal = offset.X != 0d;
                    var travel = horizontal ? offset.X : offset.Y;
                    storyboard.Children.Add(Animate(
                        TranslatePath(horizontal ? TranslateTransform.XProperty : TranslateTransform.YProperty),
                        entering ? travel : 0d, entering ? 0d : travel, duration, motionEase));
                    break;
                case ToastMotion.Fade:
                    storyboard.Children.Add(Animate(new PropertyPath(UIElement.OpacityProperty),
                        entering ? 0d : 1d, entering ? 1d : 0d, duration, FadeEase(entering)));
                    break;
                case ToastMotion.Zoom:
                    var from = entering ? ZoomFromScale : 1d;
                    var to = entering ? 1d : ZoomFromScale;
                    storyboard.Children.Add(Animate(ScalePath(ScaleTransform.ScaleXProperty), from, to, duration, motionEase));
                    storyboard.Children.Add(Animate(ScalePath(ScaleTransform.ScaleYProperty), from, to, duration, motionEase));
                    storyboard.Children.Add(Animate(new PropertyPath(UIElement.OpacityProperty),
                        entering ? 0d : 1d, entering ? 1d : 0d, duration, FadeEase(entering)));
                    break;
            }

            return storyboard;
        }

        /// <summary>
        /// Easing for movement and scale. The default entrance overshoots and the default exit
        /// accelerates away, matching the built-in slide; Smooth drops the overshoot and Bouncy
        /// adds a small wind-up to the exit.
        /// </summary>
        private static IEasingFunction MotionEase(bool entering, ToastMotionFeel? feel)
        {
            var mode = entering ? EasingMode.EaseOut : EasingMode.EaseIn;
            var bouncy = entering ? feel != ToastMotionFeel.Smooth : feel == ToastMotionFeel.Bouncy;
            return bouncy
                ? (IEasingFunction)new BackEase { EasingMode = mode, Amplitude = OvershootAmplitude }
                : new CubicEase { EasingMode = mode };
        }

        // Opacity never overshoots: an eased value past 1 or below 0 has nothing to show.
        private static IEasingFunction FadeEase(bool entering)
        {
            return new CubicEase { EasingMode = entering ? EasingMode.EaseOut : EasingMode.EaseIn };
        }

        private static DoubleAnimation Animate(
            PropertyPath path, double from, double to, Duration duration, IEasingFunction ease)
        {
            var animation = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = duration,
                EasingFunction = ease,
                // The card holds where the motion left it until the service stops the storyboard:
                // the exit's end is off screen or transparent, and the window closes after it.
                FillBehavior = FillBehavior.HoldEnd,
            };
            Storyboard.SetTargetProperty(animation, path);
            return animation;
        }

        // Built with the dependency properties supplied directly; see
        // ToastNotificationService.BuildSlidePath for why these are never parsed from text.
        private static PropertyPath TranslatePath(DependencyProperty axis)
        {
            return new PropertyPath(
                "(0).(1)[1].(2)", UIElement.RenderTransformProperty, TransformGroup.ChildrenProperty, axis);
        }

        private static PropertyPath ScalePath(DependencyProperty axis)
        {
            return new PropertyPath(
                "(0).(1)[0].(2)", UIElement.RenderTransformProperty, TransformGroup.ChildrenProperty, axis);
        }
    }
}
