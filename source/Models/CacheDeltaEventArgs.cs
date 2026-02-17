using System;

namespace PlayniteAchievements.Models
{
    public enum CacheDeltaOperationType
    {
        Upsert = 0,
        Remove = 1,
        FullReset = 2
    }

    public sealed class CacheDeltaEventArgs : EventArgs
    {
        public string Key { get; }
        public CacheDeltaOperationType OperationType { get; }
        public DateTime OccurredUtc { get; }

        public bool IsFullReset => OperationType == CacheDeltaOperationType.FullReset;

        public CacheDeltaEventArgs(string key, CacheDeltaOperationType operationType, DateTime occurredUtc)
        {
            Key = key;
            OperationType = operationType;
            OccurredUtc = occurredUtc;
        }
    }
}
