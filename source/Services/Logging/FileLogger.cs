using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using Playnite.SDK;

namespace PlayniteAchievements.Services.Logging
{
    /// <summary>
    /// Thread-safe file logger that writes log entries to a dedicated log file.
    /// Uses a background thread to avoid blocking the calling thread during writes.
    /// </summary>
    public class FileLogger : ILogger, IDisposable
    {
        private readonly BlockingCollection<LogEntry> _logQueue;
        private readonly Thread _writerThread;
        private readonly string _logFilePath;
        private readonly long _maxFileSizeBytes;
        private readonly object _fileLock = new object();
        private bool _disposed;
        private bool _isShuttingDown;

        private const long DefaultMaxFileSize = 5 * 1024 * 1024; // 5 MB

        public FileLogger(string logDirectory, string fileName = "playniteachievements.log", long maxFileSizeBytes = DefaultMaxFileSize)
        {
            _maxFileSizeBytes = maxFileSizeBytes;
            _logQueue = new BlockingCollection<LogEntry>(1000);

            // Ensure directory exists
            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            _logFilePath = Path.Combine(logDirectory, fileName);

            // Rotate if file already exceeds max size
            RotateIfNeeded();

            // Start background writer thread
            _writerThread = new Thread(WriteLoop)
            {
                IsBackground = true,
                Name = "PlayniteAchievements-FileLogger"
            };
            _writerThread.Start();
        }

        public string LogFilePath => _logFilePath;

        public void Debug(string message)
        {
            EnqueueLog(LogLevel.Debug, message);
        }

        public void Debug(Exception exception, string message)
        {
            EnqueueLog(LogLevel.Debug, $"{message}{Environment.NewLine}{FormatException(exception)}");
        }

        public void Trace(string message)
        {
            // Trace is more verbose than debug, treat it as debug
            EnqueueLog(LogLevel.Debug, $"[TRACE] {message}");
        }

        public void Trace(Exception exception, string message)
        {
            EnqueueLog(LogLevel.Debug, $"[TRACE] {message}{Environment.NewLine}{FormatException(exception)}");
        }

        public void Info(string message)
        {
            EnqueueLog(LogLevel.Info, message);
        }

        public void Info(Exception exception, string message)
        {
            EnqueueLog(LogLevel.Info, $"{message}{Environment.NewLine}{FormatException(exception)}");
        }

        public void Warn(string message)
        {
            EnqueueLog(LogLevel.Warn, message);
        }

        public void Warn(Exception exception, string message)
        {
            EnqueueLog(LogLevel.Warn, $"{message}{Environment.NewLine}{FormatException(exception)}");
        }

        public void Error(string message)
        {
            EnqueueLog(LogLevel.Error, message);
        }

        public void Error(Exception exception, string message)
        {
            EnqueueLog(LogLevel.Error, $"{message}{Environment.NewLine}{FormatException(exception)}");
        }

        private void EnqueueLog(LogLevel level, string message)
        {
            if (_disposed || _isShuttingDown)
            {
                return;
            }

            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message
            };

            // Try to add without blocking; if queue is full, drop the message
            if (!_logQueue.TryAdd(entry, TimeSpan.FromMilliseconds(10)))
            {
                // Queue is full, skip this log entry to avoid blocking
            }
        }

        private void WriteLoop()
        {
            var sb = new StringBuilder();

            try
            {
                foreach (var entry in _logQueue.GetConsumingEnumerable())
                {
                    if (_isShuttingDown && _logQueue.Count == 0)
                    {
                        break;
                    }

                    try
                    {
                        sb.Clear();
                        sb.Append('[');
                        sb.Append(entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                        sb.Append("] [");
                        sb.Append(entry.Level.ToString().ToUpperInvariant());
                        sb.Append("] ");
                        sb.AppendLine(entry.Message);

                        var line = sb.ToString();

                        lock (_fileLock)
                        {
                            File.AppendAllText(_logFilePath, line);
                        }

                        // Check for rotation after write
                        RotateIfNeeded();
                    }
                    catch
                    {
                        // Ignore write errors to prevent crashing the application
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
            catch
            {
                // Unexpected error in writer thread
            }
        }

        private void RotateIfNeeded()
        {
            try
            {
                lock (_fileLock)
                {
                    if (!File.Exists(_logFilePath))
                    {
                        return;
                    }

                    var fileInfo = new FileInfo(_logFilePath);
                    if (fileInfo.Length >= _maxFileSizeBytes)
                    {
                        var backupPath = _logFilePath + ".bak";

                        // Delete existing backup
                        if (File.Exists(backupPath))
                        {
                            File.Delete(backupPath);
                        }

                        // Move current log to backup
                        File.Move(_logFilePath, backupPath);
                    }
                }
            }
            catch
            {
                // Ignore rotation errors
            }
        }

        private static string FormatException(Exception exception)
        {
            if (exception == null)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            sb.Append(exception.GetType().Name);
            sb.Append(": ");
            sb.AppendLine(exception.Message);

            if (!string.IsNullOrWhiteSpace(exception.StackTrace))
            {
                sb.AppendLine(exception.StackTrace);
            }

            if (exception.InnerException != null)
            {
                sb.AppendLine("--- Inner Exception ---");
                sb.Append(FormatException(exception.InnerException));
            }

            return sb.ToString();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _isShuttingDown = true;

            try
            {
                // Signal the writer thread to finish
                _logQueue.CompleteAdding();

                // Wait for the writer thread to finish (with timeout)
                if (!_writerThread.Join(TimeSpan.FromSeconds(5)))
                {
                    // Thread didn't finish in time, but we tried
                }
            }
            catch
            {
                // Ignore cleanup errors
            }

            try
            {
                _logQueue?.Dispose();
            }
            catch
            {
                // Ignore disposal errors
            }
        }

        private class LogEntry
        {
            public DateTime Timestamp { get; set; }
            public LogLevel Level { get; set; }
            public string Message { get; set; }
        }

        private enum LogLevel
        {
            Debug,
            Info,
            Warn,
            Error
        }
    }
}
