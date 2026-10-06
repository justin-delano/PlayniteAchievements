using System;
using System.Threading;
using PlayniteAchievements.Services.Images.Webm;
using PlayniteAchievements.Services.Logging;
using Playnite.SDK;

namespace PlayniteAchievements.Services.Images
{
    /// <summary>
    /// Reports whether this machine can decode WebM. The VP8 and VP9 decoders come from Windows
    /// (Media Foundation transforms that the Web Media and VP9 Video Extensions provide), so the
    /// answer varies per machine and is measured once rather than assumed.
    /// </summary>
    internal static class WebmCodecProbe
    {
        private static readonly ILogger Logger = PluginLogger.GetLogger(nameof(WebmCodecProbe));

        private static readonly Lazy<bool> Vp8 =
            new Lazy<bool>(() => Probe(WebmCodec.Vp8), LazyThreadSafetyMode.ExecutionAndPublication);

        private static readonly Lazy<bool> Vp9 =
            new Lazy<bool>(() => Probe(WebmCodec.Vp9), LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>
        /// Forces the reported answer regardless of the real decoder state. Exists so the
        /// decoder-absent behavior is reachable on a machine that has the decoders; leave null
        /// outside tests and diagnostics.
        /// </summary>
        internal static bool? SupportOverride { get; set; }

        /// <summary>True when either decoder is present, so WebM can be offered as a format.</summary>
        internal static bool IsSupported => SupportOverride ?? (Vp9.Value || Vp8.Value);

        internal static bool IsCodecSupported(WebmCodec codec)
        {
            return SupportOverride ?? (codec == WebmCodec.Vp8 ? Vp8.Value : Vp9.Value);
        }

        private static bool Probe(WebmCodec codec)
        {
            try
            {
                var available = WebmVideoDecoder.IsAvailable(codec);
                Logger?.Info($"[Webm] {codec} decoding is {(available ? "available" : "unavailable")}.");
                return available;
            }
            catch (Exception ex)
            {
                Logger?.Info($"[Webm] {codec} decoding is unavailable on this machine ({ex.GetType().Name}).");
                return false;
            }
        }
    }
}
