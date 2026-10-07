using System;
using System.Diagnostics;
using System.Windows.Threading;
using Playnite.SDK;

namespace PlayniteAchievements.Common
{
    /// <summary>
    /// Tracing only: counts the rows a grid prepares in one burst (one layout pass, as after a
    /// filter swaps the rows) and logs the count once the burst ends, as
    /// <c>[GridRows] grid= rows= spanMs=</c>. Rows are prepared inside the render pass, which no
    /// scope covers, so the count is what ties that pass's time to a grid. The span runs from
    /// the first to the last row and also holds any other work between them.
    /// </summary>
    public sealed class RowPrepareCounter
    {
        private readonly ILogger _logger;
        private readonly string _grid;
        private readonly Stopwatch _span = new Stopwatch();
        private int _rows;
        private long _lastMs;

        // Rows are prepared and measured one after another, so the gap before each row is
        // roughly what the row before it cost.
        private readonly System.Text.StringBuilder _gaps = new System.Text.StringBuilder();

        public RowPrepareCounter(ILogger logger, string grid)
        {
            _logger = logger;
            _grid = grid;
        }

        public void RowPrepared(Dispatcher dispatcher)
        {
            if (!PerfScope.PerfTracingEnabled || dispatcher == null)
            {
                return;
            }

            if (_rows == 0)
            {
                _span.Restart();
                // Background runs after the render pass that prepares the rows.
                dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Flush));
            }

            var now = _span.ElapsedMilliseconds;
            if (_rows > 0)
            {
                _gaps.Append(_gaps.Length > 0 ? "," : string.Empty).Append(now - _lastMs);
            }

            _rows++;
            _lastMs = now;
        }

        private void Flush()
        {
            if (_rows > 0)
            {
                _logger?.Debug($"[GridRows] grid={_grid} rows={_rows} spanMs={_lastMs} gapsMs={_gaps}");
            }

            _rows = 0;
            _gaps.Clear();
            _span.Reset();
        }
    }
}
