namespace DynamicDungeon.Runtime.Biome
{
    public static class BiomeChannelUtility
    {
        public const string ChannelName = "BiomeChannel";
        public const int UnassignedBiomeIndex = -1;

        public static bool IsBiomeChannel(string channelName)
        {
            return string.Equals(channelName, ChannelName, System.StringComparison.Ordinal);
        }
    }
}
