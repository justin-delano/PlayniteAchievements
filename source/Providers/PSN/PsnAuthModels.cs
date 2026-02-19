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
        ProbeFailed,
        LibraryMissing
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
}
