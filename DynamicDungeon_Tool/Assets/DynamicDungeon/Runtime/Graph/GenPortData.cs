using System;
using DynamicDungeon.Runtime.Core;

namespace DynamicDungeon.Runtime.Graph
{
    [Serializable]
    public sealed class GenPortData
    {
        public string PortName = string.Empty;
        public PortDirection Direction;
        public ChannelType Type;

        public GenPortData()
        {
        }

        public GenPortData(string portName, PortDirection direction, ChannelType type)
        {
            PortName = portName ?? string.Empty;
            Direction = direction;
            Type = type;
        }
    }
}
