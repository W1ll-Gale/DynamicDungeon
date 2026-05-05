using System;
using DynamicDungeon.Runtime.Core;

namespace DynamicDungeon.Runtime.Graph
{
    [Serializable]
    public sealed class ExposedProperty
    {
        public string PropertyId = string.Empty;

        public string PropertyName = string.Empty;

        public ChannelType Type = ChannelType.Float;

        public string DefaultValue = "0";

        public string Description = string.Empty;
    }
}
