using System;

namespace DynamicDungeon.Runtime.Core
{
    public readonly struct NodePortDefinition
    {
        public readonly string Name;
        public readonly PortDirection Direction;
        public readonly ChannelType Type;
        public readonly PortCapacity Capacity;
        public readonly bool Required;

        public NodePortDefinition(string name, PortDirection direction, ChannelType type, PortCapacity capacity = PortCapacity.Single, bool required = false)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Port name must be non-empty.", nameof(name));
            }

            Name = name;
            Direction = direction;
            Type = type;
            Capacity = capacity;
            Required = direction == PortDirection.Input && required;
        }
    }
}
