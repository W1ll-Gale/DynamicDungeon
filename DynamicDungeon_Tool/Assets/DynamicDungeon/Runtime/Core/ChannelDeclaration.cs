using System;

namespace DynamicDungeon.Runtime.Core
{
    public readonly struct ChannelDeclaration
    {
        public readonly string ChannelName;
        public readonly ChannelType Type;
        public readonly bool IsWrite;

        public ChannelDeclaration(string channelName, ChannelType type, bool isWrite)
        {
            if (string.IsNullOrWhiteSpace(channelName))
            {
                throw new ArgumentException("Channel name must be non-empty.", nameof(channelName));
            }

            ChannelName = channelName;
            Type = type;
            IsWrite = isWrite;
        }
    }
}
