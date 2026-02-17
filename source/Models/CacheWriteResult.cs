using System;

namespace PlayniteAchievements.Models
{
    public sealed class CacheWriteResult
    {
        public bool Success { get; private set; }
        public string Key { get; private set; }
        public string ErrorCode { get; private set; }
        public string ErrorMessage { get; private set; }
        public Exception Exception { get; private set; }
        public DateTime? WrittenUtc { get; private set; }

        public static CacheWriteResult CreateSuccess(string key, DateTime writtenUtc)
        {
            return new CacheWriteResult
            {
                Success = true,
                Key = key,
                WrittenUtc = writtenUtc
            };
        }

        public static CacheWriteResult CreateFailure(string key, string errorCode, string errorMessage, Exception exception = null)
        {
            return new CacheWriteResult
            {
                Success = false,
                Key = key,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
                Exception = exception
            };
        }
    }
}
