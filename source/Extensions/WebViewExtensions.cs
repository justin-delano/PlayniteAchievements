using Playnite.SDK;
using Playnite.SDK.Events; // Required for WebViewLoadingChangedEventArgs
using System.Threading.Tasks;

namespace PlayniteAchievements.Extensions
{
    public static class WebViewExtensions
    {
        public static async Task NavigateAndWaitAsync(this IWebView webView, string url)
        {
            var tcs = new TaskCompletionSource<bool>();

            void OnLoadingChanged(object sender, WebViewLoadingChangedEventArgs e)
            {
                if (!e.IsLoading)
                {
                    webView.LoadingChanged -= OnLoadingChanged;
                    tcs.TrySetResult(true);
                }
            }

            webView.LoadingChanged += OnLoadingChanged;

            try
            {
                webView.Navigate(url);
                await Task.WhenAny(tcs.Task, Task.Delay(15000));
            }
            finally
            {
                webView.LoadingChanged -= OnLoadingChanged;
            }
        }
    }
}
