using System;
using System.Collections.Generic;
using System.Text;

namespace PlayniteAchievements.Providers.Steam.Models
{
    public class SteamPlayerSummaries
    {
        public string SteamId { get; set; }
        public int CommunityVisibilityState { get; set; }
        public int ProfileState { get; set; }
        public string PersonaName { get; set; }
        public string ProfileUrl { get; set; }
        public string Avatar { get; set; }
        public string AvatarMedium { get; set; }
        public string AvatarFull { get; set; }
        public string AvatarHash { get; set; }
        public DateTime LastLogoff { get; set; }
        public int PersonaState { get; set; }
        public string PrimaryClanId { get; set; }
        public DateTime TimeCreated { get; set; }
        public int PersonaStateFlags { get; set; }
        public string LocCountryCode { get; set; }
    }
}
