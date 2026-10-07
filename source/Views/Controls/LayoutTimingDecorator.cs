using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using PlayniteAchievements.Common;
using PlayniteAchievements.Services.Logging;

namespace PlayniteAchievements.Views.Controls
{
    /// <summary>
    /// Diagnostic: logs how long its subtree takes to measure and arrange, so a layout pass can be
    /// attributed to one part of a window. Only logs while perf tracing is on.
    /// </summary>
    public sealed class LayoutTimingDecorator : Decorator
    {
        public string Label { get; set; }

        protected override Size MeasureOverride(Size constraint)
        {
            if (!PerfScope.PerfTracingEnabled)
            {
                return base.MeasureOverride(constraint);
            }

            var watch = Stopwatch.StartNew();
            var size = base.MeasureOverride(constraint);
            Report("measure", watch.Elapsed.TotalMilliseconds);
            return size;
        }

        protected override Size ArrangeOverride(Size arrangeSize)
        {
            if (!PerfScope.PerfTracingEnabled)
            {
                return base.ArrangeOverride(arrangeSize);
            }

            var watch = Stopwatch.StartNew();
            var size = base.ArrangeOverride(arrangeSize);
            Report("arrange", watch.Elapsed.TotalMilliseconds);
            return size;
        }

        private void Report(string phase, double ms)
        {
            if (ms >= 5)
            {
                PluginLogger.GetLogger(nameof(LayoutTimingDecorator)).Debug(
                    $"[LayoutTiming] {Label} {phase} ms={ms:F0}");
            }
        }
    }
}
