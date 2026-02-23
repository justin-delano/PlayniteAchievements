using System;
using System.Collections.Generic;

namespace PlayniteAchievements.Providers.Xbox.Models
{
    /// <summary>
    /// Achievement model for Xbox 360 games.
    /// Uses contract version 1 of the achievements API.
    /// </summary>
    public class Xbox360Achievement
    {
        public int id { get; set; }
        public int titleId { get; set; }
        public string name { get; set; }
        public long sequence { get; set; }
        public int flags { get; set; }
        public bool unlockedOnline { get; set; }
        public bool unlocked { get; set; }
        public bool isSecret { get; set; }
        public int platform { get; set; }
        public int gamerscore { get; set; }
        public int imageId { get; set; }
        public string description { get; set; }
        public string lockedDescription { get; set; }
        public int type { get; set; }
        public bool isRevoked { get; set; }
        public DateTime timeUnlocked { get; set; }
    }

    /// <summary>
    /// Response from Xbox 360 achievements API.
    /// </summary>
    public class Xbox360AchievementResponse
    {
        public List<Xbox360Achievement> achievements { get; set; }
        public XboxPagingInfo pagingInfo { get; set; }
        public DateTime version { get; set; }
    }
}
