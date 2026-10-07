using System;
using System.Windows;
using System.Windows.Input;

namespace PlayniteAchievements.Views.Helpers
{
    /// <summary>Carries the image source a pick target received.</summary>
    public sealed class ImagePickedEventArgs : RoutedEventArgs
    {
        public ImagePickedEventArgs(RoutedEvent routedEvent, object source, string imageSource)
            : base(routedEvent, source)
        {
            ImageSource = imageSource;
        }

        /// <summary>A local image file path or an http(s) URL.</summary>
        public string ImageSource { get; }
    }

    /// <summary>
    /// Turns an image slot's thumbnail into its input: it takes keyboard focus on click, accepts
    /// a dropped file or browser image, and takes Ctrl+V or Shift+Insert from the clipboard.
    /// Every accepted input raises <see cref="PickedEvent"/> with the same image source, so a
    /// slot handles drop and paste in one place. Payload parsing is
    /// <see cref="ImageDropHelper.TryGetImageSource"/>.
    /// </summary>
    public static class ImagePickTarget
    {
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(ImagePickTarget),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static readonly RoutedEvent PickedEvent =
            EventManager.RegisterRoutedEvent(
                "Picked",
                RoutingStrategy.Bubble,
                typeof(EventHandler<ImagePickedEventArgs>),
                typeof(ImagePickTarget));

        public static bool GetIsEnabled(DependencyObject element) =>
            (bool)element.GetValue(IsEnabledProperty);

        public static void SetIsEnabled(DependencyObject element, bool value) =>
            element.SetValue(IsEnabledProperty, value);

        public static void AddPickedHandler(DependencyObject element, EventHandler<ImagePickedEventArgs> handler) =>
            (element as UIElement)?.AddHandler(PickedEvent, handler);

        public static void RemovePickedHandler(DependencyObject element, EventHandler<ImagePickedEventArgs> handler) =>
            (element as UIElement)?.RemoveHandler(PickedEvent, handler);

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is UIElement element))
            {
                return;
            }

            element.PreviewDragOver -= OnPreviewDragOver;
            element.PreviewDrop -= OnPreviewDrop;
            element.PreviewKeyDown -= OnPreviewKeyDown;
            element.MouseLeftButtonDown -= OnMouseLeftButtonDown;

            var enabled = (bool)e.NewValue;
            element.Focusable = enabled;
            element.AllowDrop = enabled;
            if (!enabled)
            {
                return;
            }

            element.PreviewDragOver += OnPreviewDragOver;
            element.PreviewDrop += OnPreviewDrop;
            element.PreviewKeyDown += OnPreviewKeyDown;
            element.MouseLeftButtonDown += OnMouseLeftButtonDown;
        }

        private static void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is UIElement element && element.Focus())
            {
                e.Handled = true;
            }
        }

        private static void OnPreviewDragOver(object sender, DragEventArgs e)
        {
            e.Effects = ImageDropHelper.TryGetImageSource(e.Data, out _)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private static void OnPreviewDrop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (ImageDropHelper.TryGetImageSource(e.Data, out var imageSource))
            {
                Raise(sender as UIElement, imageSource);
            }
        }

        private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            var modifiers = Keyboard.Modifiers;
            var isPaste = (e.Key == Key.V && modifiers == ModifierKeys.Control) ||
                          (e.Key == Key.Insert && modifiers == ModifierKeys.Shift);
            if (!isPaste)
            {
                return;
            }

            e.Handled = true;
            if (ImageDropHelper.TryGetClipboardImageSource(out var imageSource))
            {
                Raise(sender as UIElement, imageSource);
            }
        }

        private static void Raise(UIElement element, string imageSource)
        {
            element?.RaiseEvent(new ImagePickedEventArgs(PickedEvent, element, imageSource));
        }
    }
}
