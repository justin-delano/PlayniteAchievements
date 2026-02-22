using System;

namespace PlayniteAchievements.Providers.PSN
{
    public enum PsnAuthOutcome
    {
        Authenticated,
        AlreadyAuthenticated,
        NotAuthenticated,
        Cancelled,
        TimedOut,
        Failed,
        ProbeFailed
    }

    public enum PsnAuthProgressStep
    {
        CheckingExistingSession,
        OpeningLoginWindow,
        WaitingForUserLogin,
        Completed,
        Failed
    }

    public sealed class PsnAuthResult
    {
        public PsnAuthOutcome Outcome { get; set; }

        public string MessageKey { get; set; }

        public bool WindowOpened { get; set; }

        public bool IsSuccess =>
            Outcome == PsnAuthOutcome.Authenticated ||
            Outcome == PsnAuthOutcome.AlreadyAuthenticated;

        public static PsnAuthResult Create(
            PsnAuthOutcome outcome,
            string messageKey,
            bool windowOpened = false)
        {
            return new PsnAuthResult
            {
                Outcome = outcome,
                MessageKey = messageKey,
                WindowOpened = windowOpened
            };
        }
    }

    public class PsnAuthRequiredException : Exception
    {
        public PsnAuthRequiredException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Mobile OAuth tokens from Sony account authentication.
    /// </summary>
    public class MobileTokens
    {
        public string access_token { get; set; }
        public string token_type { get; set; }
        public int expires_in { get; set; }
        public string refresh_token { get; set; }
        public int refresh_token_expires_in { get; set; }
        public string scope { get; set; }
    }
}
