namespace PlayniteAchievements.Models.Settings
{
    public sealed class ResourceOverrideDescriptor
    {
        public ResourceOverrideDescriptor(
            string resourceKey,
            string displayName,
            ResourceOverrideValueKind valueKind,
            string playniteResourceKey)
        {
            ResourceKey = resourceKey;
            DisplayName = displayName;
            ValueKind = valueKind;
            PlayniteResourceKey = playniteResourceKey;
        }

        public string ResourceKey { get; }
        public string DisplayName { get; }
        public ResourceOverrideValueKind ValueKind { get; }
        public string PlayniteResourceKey { get; }
    }
}
