using DynamicDungeon.Runtime.Core;

namespace DynamicDungeon.Runtime.Graph
{
    public static class CastRegistry
    {
        public static bool HasImplicitCast(ChannelType fromType, ChannelType toType)
        {
            return false;
        }
    }
}
