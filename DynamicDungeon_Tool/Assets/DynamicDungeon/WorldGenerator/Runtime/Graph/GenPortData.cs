using System;
using DynamicDungeon.Runtime.Core;

namespace DynamicDungeon.Runtime.Graph
{
    [Serializable]
    public sealed class GenPortData
    {
        public string PortName = string.Empty;
        public string DisplayName = string.Empty;
        public PortDirection Direction;
        public ChannelType Type;

        public GenPortData()
        {
        }

        public GenPortData(string portName, PortDirection direction, ChannelType type, string displayName = null)
        {
            PortName = portName ?? string.Empty;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? PortName : displayName;
            Direction = direction;
            Type = type;
        }
    }
}
