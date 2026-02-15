using System;

namespace PlayniteAchievements.Providers.GOG
{
    public enum GogAuthOutcome
    {
        Authenticated,
        AlreadyAuthenticated,
        NotAuthenticated,
        Cancelled,
        TimedOut,
        Failed,
        ProbeFailed
    }

    public enum GogAuthProgressStep
    {
        CheckingExistingSession,
        OpeningLoginWindow,
        WaitingForUserLogin,
        VerifyingSession,
        Completed,
        Failed
    }

    public sealed class GogAuthResult
    {
        public GogAuthOutcome Outcome { get; set; }

        public string MessageKey { get; set; }

        public string UserId { get; set; }

        public DateTime? TokenExpiryUtc { get; set; }

        public bool WindowOpened { get; set; }

        public bool IsSuccess =>
            Outcome == GogAuthOutcome.Authenticated ||
            Outcome == GogAuthOutcome.AlreadyAuthenticated;

        public static GogAuthResult Create(
            GogAuthOutcome outcome,
            string messageKey,
            string userId = null,
            DateTime? tokenExpiryUtc = null,
            bool windowOpened = false)
        {
            return new GogAuthResult
            {
                Outcome = outcome,
                MessageKey = messageKey,
                UserId = userId,
                TokenExpiryUtc = tokenExpiryUtc,
                WindowOpened = windowOpened
            };
        }
    }

    public interface IGogTokenProvider
    {
        string GetAccessToken();
    }

    /// <summary>
    /// Exception thrown when authentication is required.
    /// </summary>
    public class AuthRequiredException : Exception
    {
        public AuthRequiredException(string message) : base(message) { }
    }
}
