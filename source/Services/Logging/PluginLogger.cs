using System;
using System.IO;
using Playnite.SDK;

namespace PlayniteAchievements.Services.Logging
{
    /// <summary>
    /// Static accessor class providing logger factory methods for the PlayniteAchievements plugin.
    /// All logs are written to a dedicated log file in the extension data folder.
    /// </summary>
    public static class PluginLogger
    {
        private static FileLogger _fileLogger;
        private static string _logDirectory;
        private static readonly object _lock = new object();

        /// <summary>
        /// Gets the directory where log files are stored.
        /// Returns null if Initialize has not been called.
        /// </summary>
        public static string LogDirectory => _logDirectory;

        /// <summary>
        /// Gets the full path to the log file.
        /// Returns null if Initialize has not been called.
        /// </summary>
        public static string LogFilePath => _fileLogger?.LogFilePath;

        /// <summary>
        /// Initializes the logging system with the specified extension data path.
        /// Must be called before any logging can occur.
        /// </summary>
        /// <param name="extensionDataPath">The plugin's extension data path from GetPluginUserDataPath().</param>
        public static void Initialize(string extensionDataPath)
        {
            lock (_lock)
            {
                if (_fileLogger != null)
                {
                    return; // Already initialized
                }

                // Use the extension data path directly for logs
                _logDirectory = extensionDataPath;

                // Ensure directory exists
                if (!Directory.Exists(_logDirectory))
                {
                    Directory.CreateDirectory(_logDirectory);
                }

                _fileLogger = new FileLogger(_logDirectory);
            }
        }

        /// <summary>
        /// Gets a logger with the specified name. All messages from this logger will be prefixed with [name].
        /// </summary>
        /// <param name="name">The name to prefix log messages with (typically the class name).</param>
        /// <returns>An ILogger instance that prefixes messages with the given name.</returns>
        public static ILogger GetLogger(string name)
        {
            lock (_lock)
            {
                if (_fileLogger == null)
                {
                    // Log system not initialized - return a no-op logger to prevent crashes
                    // This shouldn't happen in normal operation, but provides safety
                    return new NullLogger();
                }

                return new NamedLoggerWrapper(_fileLogger, name);
            }
        }

        /// <summary>
        /// Shuts down the logging system, ensuring all queued messages are written.
        /// Should be called during plugin shutdown.
        /// </summary>
        public static void Shutdown()
        {
            lock (_lock)
            {
                if (_fileLogger != null)
                {
                    _fileLogger.Dispose();
                    _fileLogger = null;
                }
            }
        }

        /// <summary>
        /// A no-op logger that discards all messages.
        /// Used as a fallback when the logging system is not initialized.
        /// </summary>
        private class NullLogger : ILogger
        {
            public void Debug(string message) { }
            public void Debug(Exception exception, string message) { }
            public void Trace(string message) { }
            public void Trace(Exception exception, string message) { }
            public void Info(string message) { }
            public void Info(Exception exception, string message) { }
            public void Warn(string message) { }
            public void Warn(Exception exception, string message) { }
            public void Error(string message) { }
            public void Error(Exception exception, string message) { }
        }
    }
}
