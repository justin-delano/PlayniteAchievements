using System;

namespace PlayniteAchievements.Services
{
    public sealed class CachePersistenceException : Exception
    {
        public string CacheKey { get; }
        public string ProviderName { get; }
        public string ErrorCode { get; }

        public CachePersistenceException(
            string cacheKey,
            string providerName,
            string errorCode,
            string message,
            Exception innerException = null)
            : base(message, innerException)
        {
            CacheKey = cacheKey;
            ProviderName = providerName;
            ErrorCode = errorCode;
        }
    }
}
