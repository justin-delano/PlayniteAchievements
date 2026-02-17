using System;
using System.Threading;
using System.Threading.Tasks;

namespace PlayniteAchievements.Providers.Epic
{
    public enum EpicAuthOutcome
    {
        Authenticated,
        AlreadyAuthenticated,
        NotAuthenticated,
        Cancelled,
        TimedOut,
        Failed,
        ProbeFailed
    }

    public enum EpicAuthProgressStep
    {
        CheckingExistingSession,
        OpeningLoginWindow,
        WaitingForUserLogin,
        VerifyingSession,
        Completed,
        Failed
    }

    public enum EpicAuthFlow
    {
        AutoFallback,
        EmbeddedWindow,
        SystemBrowser
    }

    public sealed class EpicAuthResult
    {
        public EpicAuthOutcome Outcome { get; set; }

        public string MessageKey { get; set; }

        public string AccountId { get; set; }

        public bool WindowOpened { get; set; }

        public bool IsSuccess =>
            Outcome == EpicAuthOutcome.Authenticated ||
            Outcome == EpicAuthOutcome.AlreadyAuthenticated;

        public static EpicAuthResult Create(
            EpicAuthOutcome outcome,
            string messageKey,
            string accountId = null,
            bool windowOpened = false)
        {
            return new EpicAuthResult
            {
                Outcome = outcome,
                MessageKey = messageKey,
                AccountId = accountId,
                WindowOpened = windowOpened
            };
        }
    }

    public interface IEpicSessionProvider
    {
        string GetAccountId();

        Task<string> GetAccessTokenAsync(CancellationToken ct);

        Task<bool> TryRefreshTokenAsync(CancellationToken ct);
    }

    public class EpicAuthRequiredException : Exception
    {
        public EpicAuthRequiredException(string message) : base(message) { }
    }
}
