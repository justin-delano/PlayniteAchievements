using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Navigation;

namespace PlayniteAchievements.Views.Helpers
{
    public static class AchievementNoteInlineFormatter
    {
        public static readonly DependencyProperty FormattedTextProperty =
            DependencyProperty.RegisterAttached(
                "FormattedText",
                typeof(string),
                typeof(AchievementNoteInlineFormatter),
                new PropertyMetadata(string.Empty, OnFormattedTextChanged));

        public static string GetFormattedText(DependencyObject element)
        {
            return (string)element.GetValue(FormattedTextProperty);
        }

        public static void SetFormattedText(DependencyObject element, string value)
        {
            element.SetValue(FormattedTextProperty, value ?? string.Empty);
        }

        public static void ApplyFormattedText(TextBlock textBlock, string value)
        {
            if (textBlock == null)
            {
                return;
            }

            textBlock.Inlines.Clear();

            var text = (value ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                {
                    textBlock.Inlines.Add(new LineBreak());
                }

                AppendFormattedText(textBlock.Inlines, lines[i], bold: false, italic: false, underline: false);
            }
        }

        private static void OnFormattedTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextBlock textBlock)
            {
                ApplyFormattedText(textBlock, e.NewValue as string);
            }
        }

        private static void AppendFormattedText(
            InlineCollection inlines,
            string text,
            bool bold,
            bool italic,
            bool underline)
        {
            if (inlines == null || string.IsNullOrEmpty(text))
            {
                return;
            }

            var index = 0;
            while (index < text.Length)
            {
                var marker = FindNextMarker(text, index);
                if (marker.Start < 0)
                {
                    AddRun(inlines, text.Substring(index), bold, italic, underline);
                    return;
                }

                if (marker.Start > index)
                {
                    AddRun(inlines, text.Substring(index, marker.Start - index), bold, italic, underline);
                }

                var contentStart = marker.Start + marker.Token.Length;
                var end = text.IndexOf(marker.Token, contentStart, StringComparison.Ordinal);
                if (end < 0)
                {
                    AddRun(inlines, text.Substring(marker.Start), bold, italic, underline);
                    return;
                }

                AppendFormattedText(
                    inlines,
                    text.Substring(contentStart, end - contentStart),
                    bold || marker.Kind == MarkerKind.Bold,
                    italic || marker.Kind == MarkerKind.Italic,
                    underline || marker.Kind == MarkerKind.Underline);

                index = end + marker.Token.Length;
            }
        }

        private static Marker FindNextMarker(string text, int startIndex)
        {
            for (var i = startIndex; i < text.Length; i++)
            {
                if (i + 1 < text.Length &&
                    text[i] == '*' &&
                    text[i + 1] == '*')
                {
                    return new Marker(i, "**", MarkerKind.Bold);
                }

                if (i + 1 < text.Length &&
                    text[i] == '_' &&
                    text[i + 1] == '_')
                {
                    return new Marker(i, "__", MarkerKind.Underline);
                }

                if (text[i] == '*')
                {
                    return new Marker(i, "*", MarkerKind.Italic);
                }
            }

            return new Marker(-1, string.Empty, MarkerKind.Italic);
        }

        private static void AddRun(
            InlineCollection inlines,
            string text,
            bool bold,
            bool italic,
            bool underline)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var index = 0;
            while (index < text.Length)
            {
                var link = FindNextLink(text, index);
                if (link.Start < 0)
                {
                    AddTextRun(inlines, text.Substring(index), bold, italic, underline);
                    return;
                }

                if (link.Start > index)
                {
                    AddTextRun(inlines, text.Substring(index, link.Start - index), bold, italic, underline);
                }

                AddHyperlink(inlines, link.DisplayText, link.NavigateUri, bold, italic, underline);
                index = link.End;
            }
        }

        private static void AddTextRun(
            InlineCollection inlines,
            string text,
            bool bold,
            bool italic,
            bool underline)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var run = new Run(text);
            ApplyTextStyle(run, bold, italic, underline);
            inlines.Add(run);
        }

        private static void AddHyperlink(
            InlineCollection inlines,
            string displayText,
            Uri navigateUri,
            bool bold,
            bool italic,
            bool underline)
        {
            if (string.IsNullOrWhiteSpace(displayText) || navigateUri == null)
            {
                return;
            }

            var run = new Run(displayText);
            ApplyTextStyle(run, bold, italic, underline);

            var hyperlink = new Hyperlink(run)
            {
                NavigateUri = navigateUri,
                ToolTip = navigateUri.AbsoluteUri
            };
            hyperlink.RequestNavigate += Hyperlink_RequestNavigate;
            inlines.Add(hyperlink);
        }

        private static void ApplyTextStyle(Run run, bool bold, bool italic, bool underline)
        {
            if (run == null)
            {
                return;
            }

            if (bold)
            {
                run.FontWeight = FontWeights.Bold;
            }

            if (italic)
            {
                run.FontStyle = FontStyles.Italic;
            }

            if (underline)
            {
                run.TextDecorations = TextDecorations.Underline;
            }
        }

        private static void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            if (e?.Uri == null)
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
                {
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch
            {
                // Navigation failures should not close or corrupt the note dialog.
            }
        }

        private static LinkMatch FindNextLink(string text, int startIndex)
        {
            for (var i = startIndex; i < text.Length; i++)
            {
                if (text[i] == '[' && TryParseMarkdownLink(text, i, out var markdownLink))
                {
                    return markdownLink;
                }

                if (IsRawUrlStart(text, i) && TryParseRawUrl(text, i, out var rawLink))
                {
                    return rawLink;
                }
            }

            return new LinkMatch(-1, -1, string.Empty, null);
        }

        private static bool TryParseMarkdownLink(string text, int startIndex, out LinkMatch match)
        {
            match = default;

            var labelEnd = text.IndexOf(']', startIndex + 1);
            if (labelEnd <= startIndex + 1 ||
                labelEnd + 1 >= text.Length ||
                text[labelEnd + 1] != '(')
            {
                return false;
            }

            var urlStart = labelEnd + 2;
            var urlEnd = text.IndexOf(')', urlStart);
            if (urlEnd <= urlStart)
            {
                return false;
            }

            var label = text.Substring(startIndex + 1, labelEnd - startIndex - 1);
            var urlText = text.Substring(urlStart, urlEnd - urlStart);
            if (!TryCreateNavigateUri(urlText, out var uri))
            {
                return false;
            }

            match = new LinkMatch(startIndex, urlEnd + 1, label, uri);
            return true;
        }

        private static bool TryParseRawUrl(string text, int startIndex, out LinkMatch match)
        {
            match = default;

            var end = startIndex;
            while (end < text.Length && !char.IsWhiteSpace(text[end]))
            {
                end++;
            }

            var urlText = text.Substring(startIndex, end - startIndex);
            while (urlText.Length > 0 && IsTrailingUrlPunctuation(urlText[urlText.Length - 1]))
            {
                urlText = urlText.Substring(0, urlText.Length - 1);
                end--;
            }

            if (!TryCreateNavigateUri(urlText, out var uri))
            {
                return false;
            }

            match = new LinkMatch(startIndex, end, urlText, uri);
            return true;
        }

        private static bool IsRawUrlStart(string text, int index)
        {
            return text.IndexOf("http://", index, StringComparison.OrdinalIgnoreCase) == index ||
                   text.IndexOf("https://", index, StringComparison.OrdinalIgnoreCase) == index;
        }

        private static bool TryCreateNavigateUri(string value, out Uri uri)
        {
            uri = null;
            var candidate = (value ?? string.Empty).Trim();
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var parsed))
            {
                return false;
            }

            if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            uri = parsed;
            return true;
        }

        private static bool IsTrailingUrlPunctuation(char value)
        {
            return value == '.' ||
                   value == ',' ||
                   value == ';' ||
                   value == ':' ||
                   value == '!' ||
                   value == '?' ||
                   value == ')' ||
                   value == ']' ||
                   value == '}';
        }

        private enum MarkerKind
        {
            Bold,
            Italic,
            Underline
        }

        private readonly struct Marker
        {
            public Marker(int start, string token, MarkerKind kind)
            {
                Start = start;
                Token = token;
                Kind = kind;
            }

            public int Start { get; }

            public string Token { get; }

            public MarkerKind Kind { get; }
        }

        private readonly struct LinkMatch
        {
            public LinkMatch(int start, int end, string displayText, Uri navigateUri)
            {
                Start = start;
                End = end;
                DisplayText = displayText;
                NavigateUri = navigateUri;
            }

            public int Start { get; }

            public int End { get; }

            public string DisplayText { get; }

            public Uri NavigateUri { get; }
        }
    }
}
