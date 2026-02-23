using System;

namespace PlayniteAchievements.Providers.Xbox
{
    /// <summary>
    /// Outcome of an Xbox authentication attempt.
    /// </summary>
    public enum XboxAuthOutcome
    {
        /// <summary>
        /// Authentication was already valid, no action needed.
        /// </summary>
        AlreadyAuthenticated,

        /// <summary>
        /// Authentication completed successfully via login flow.
        /// </summary>
        Authenticated,

        /// <summary>
        /// User is not authenticated.
        /// </summary>
        NotAuthenticated,

        /// <summary>
        /// Authentication timed out waiting for user action.
        /// </summary>
        TimedOut,

        /// <summary>
        /// Authentication was cancelled by user.
        /// </summary>
        Cancelled,

        /// <summary>
        /// Authentication failed due to an error.
        /// </summary>
        Failed,

        /// <summary>
        /// Probe to check authentication status failed.
        /// </summary>
        ProbeFailed
    }

    /// <summary>
    /// Progress steps during Xbox authentication flow.
    /// </summary>
    public enum XboxAuthProgressStep
    {
        CheckingExistingSession,
        OpeningLoginWindow,
        WaitingForUserLogin,
        Completed,
        Failed
    }

    /// <summary>
    /// Result of an Xbox authentication operation.
    /// </summary>
    public class XboxAuthResult
    {
        public XboxAuthOutcome Outcome { get; private set; }
        public string MessageKey { get; private set; }
        public bool WindowOpened { get; private set; }

        private XboxAuthResult() { }

        public static XboxAuthResult Create(XboxAuthOutcome outcome, string messageKey, bool windowOpened)
        {
            return new XboxAuthResult
            {
                Outcome = outcome,
                MessageKey = messageKey,
                WindowOpened = windowOpened
            };
        }
    }

    /// <summary>
    /// Exception thrown when Xbox authentication is required but not configured.
    /// </summary>
    public class XboxAuthRequiredException : Exception
    {
        public XboxAuthRequiredException(string message) : base(message) { }
    }
}
