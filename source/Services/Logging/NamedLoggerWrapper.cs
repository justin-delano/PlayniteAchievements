using System;
using Playnite.SDK;

namespace PlayniteAchievements.Services.Logging
{
    /// <summary>
    /// Wraps a FileLogger and prepends a logger name prefix to all messages.
    /// </summary>
    public class NamedLoggerWrapper : ILogger
    {
        private readonly FileLogger _innerLogger;
        private readonly string _loggerName;

        public NamedLoggerWrapper(FileLogger innerLogger, string loggerName)
        {
            _innerLogger = innerLogger ?? throw new ArgumentNullException(nameof(innerLogger));
            _loggerName = loggerName ?? throw new ArgumentNullException(nameof(loggerName));
        }

        public void Debug(string message)
        {
            _innerLogger.Debug($"[{_loggerName}] {message}");
        }

        public void Debug(Exception exception, string message)
        {
            _innerLogger.Debug(exception, $"[{_loggerName}] {message}");
        }

        public void Trace(string message)
        {
            _innerLogger.Trace($"[{_loggerName}] {message}");
        }

        public void Trace(Exception exception, string message)
        {
            _innerLogger.Trace(exception, $"[{_loggerName}] {message}");
        }

        public void Info(string message)
        {
            _innerLogger.Info($"[{_loggerName}] {message}");
        }

        public void Info(Exception exception, string message)
        {
            _innerLogger.Info(exception, $"[{_loggerName}] {message}");
        }

        public void Warn(string message)
        {
            _innerLogger.Warn($"[{_loggerName}] {message}");
        }

        public void Warn(Exception exception, string message)
        {
            _innerLogger.Warn(exception, $"[{_loggerName}] {message}");
        }

        public void Error(string message)
        {
            _innerLogger.Error($"[{_loggerName}] {message}");
        }

        public void Error(Exception exception, string message)
        {
            _innerLogger.Error(exception, $"[{_loggerName}] {message}");
        }
    }
}
