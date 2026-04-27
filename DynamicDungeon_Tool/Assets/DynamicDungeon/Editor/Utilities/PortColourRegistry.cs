using System;
using DynamicDungeon.Runtime.Core;
using UnityEngine;

namespace DynamicDungeon.Editor.Utilities
{
    public static class PortColourRegistry
    {
        private static readonly Color FloatColour = new Color(0.2f, 0.8f, 0.2f, 1.0f);
        private static readonly Color IntColour = new Color(0.2f, 0.4f, 0.9f, 1.0f);
        private static readonly Color BoolMaskColour = new Color(0.9f, 0.5f, 0.1f, 1.0f);
        private static readonly Color PointListColour = new Color(0.85f, 0.2f, 0.5f, 1.0f);
        private static readonly Color WorldColour = new Color(0.9f, 0.8f, 0.1f, 1.0f);
        private static readonly Color UnknownColour = new Color(0.5f, 0.5f, 0.5f, 1.0f);

        public static Color GetColour(ChannelType channelType)
        {
            string channelTypeName = Enum.GetName(typeof(ChannelType), channelType) ?? string.Empty;
            if (string.Equals(channelTypeName, "World", StringComparison.Ordinal))
            {
                return WorldColour;
            }

            switch (channelType)
            {
                case ChannelType.Float:
                    return FloatColour;

                case ChannelType.Int:
                    return IntColour;

                case ChannelType.BoolMask:
                    return BoolMaskColour;
                case ChannelType.PointList:
                    return PointListColour;

                default:
                    return UnknownColour;
            }
        }
    }
}
