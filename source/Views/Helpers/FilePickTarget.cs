using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using PlayniteAchievements.Services.Sound;

namespace PlayniteAchievements.Views.Helpers
{
    /// <summary>What a <see cref="FilePickTarget"/> accepts.</summary>
    public enum FilePickKind
    {
        /// <summary>A decodable image file or an http(s) URL; see <see cref="ImageDropHelper.TryGetImageSource"/>.</summary>
        Image,

        /// <summary>A local file in a supported unlock sound format.</summary>
        Sound
    }

    /// <summary>Carries the file or URL a pick target received.</summary>
    public sealed class FilePickedEventArgs : RoutedEventArgs
    {
        public FilePickedEventArgs(RoutedEvent routedEvent, object source, string pickedSource)
            : base(routedEvent, source)
        {
            PickedSource = pickedSource;
        }

        /// <summary>A local file path, or for <see cref="FilePickKind.Image"/> an http(s) URL.</summary>
        public string PickedSource { get; }
    }

    /// <summary>
    /// Turns a slot's preview into its input: it takes keyboard focus on click, accepts a dropped
    /// file (or browser image), and takes Ctrl+V or Shift+Insert from the clipboard. Every
    /// accepted input raises <see cref="PickedEvent"/> with the same source, so a slot handles drop
    /// and paste in one place. <see cref="KindProperty"/> decides what is accepted.
    /// </summary>
    public static class FilePickTarget
    {
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(FilePickTarget),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static readonly DependencyProperty KindProperty =
            DependencyProperty.RegisterAttached(
                "Kind",
                typeof(FilePickKind),
                typeof(FilePickTarget),
                new PropertyMetadata(FilePickKind.Image));

        public static readonly RoutedEvent PickedEvent =
            EventManager.RegisterRoutedEvent(
                "Picked",
                RoutingStrategy.Bubble,
                typeof(EventHandler<FilePickedEventArgs>),
                typeof(FilePickTarget));

        public static bool GetIsEnabled(DependencyObject element) =>
            (bool)element.GetValue(IsEnabledProperty);

        public static void SetIsEnabled(DependencyObject element, bool value) =>
            element.SetValue(IsEnabledProperty, value);

        public static FilePickKind GetKind(DependencyObject element) =>
            (FilePickKind)element.GetValue(KindProperty);

        public static void SetKind(DependencyObject element, FilePickKind value) =>
            element.SetValue(KindProperty, value);

        public static void AddPickedHandler(DependencyObject element, EventHandler<FilePickedEventArgs> handler) =>
            (element as UIElement)?.AddHandler(PickedEvent, handler);

        public static void RemovePickedHandler(DependencyObject element, EventHandler<FilePickedEventArgs> handler) =>
            (element as UIElement)?.RemoveHandler(PickedEvent, handler);

        /// <summary>The first sound file a drop or paste payload carries: a dropped file, or text naming one.</summary>
        public static bool TryGetSoundFile(IDataObject data, out string path)
        {
            path = null;
            if (data == null)
            {
                return false;
            }

            try
            {
                if (data.GetDataPresent(DataFormats.FileDrop))
                {
                    path = (data.GetData(DataFormats.FileDrop) as string[])?.FirstOrDefault(IsSoundFile);
                    return path != null;
                }

                var text = (data.GetData(DataFormats.UnicodeText) as string ?? string.Empty).Trim().Trim('"');
                if (IsSoundFile(text))
                {
                    path = text;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool IsSoundFile(string path)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(path) &&
                       File.Exists(path) &&
                       UnlockSoundPortableStore.IsSupportedExtension(Path.GetExtension(path));
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static bool TryGetSource(DependencyObject element, IDataObject data, out string source)
        {
            return GetKind(element) == FilePickKind.Sound
                ? TryGetSoundFile(data, out source)
                : ImageDropHelper.TryGetImageSource(data, out source);
        }

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
            e.Effects = TryGetSource(sender as DependencyObject, e.Data, out _)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private static void OnPreviewDrop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (TryGetSource(sender as DependencyObject, e.Data, out var source))
            {
                Raise(sender as UIElement, source);
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
            IDataObject clipboard;
            try
            {
                clipboard = Clipboard.GetDataObject();
            }
            catch
            {
                return;
            }

            if (TryGetSource(sender as DependencyObject, clipboard, out var source))
            {
                Raise(sender as UIElement, source);
            }
        }

        private static void Raise(UIElement element, string source)
        {
            element?.RaiseEvent(new FilePickedEventArgs(PickedEvent, element, source));
        }
    }
}
