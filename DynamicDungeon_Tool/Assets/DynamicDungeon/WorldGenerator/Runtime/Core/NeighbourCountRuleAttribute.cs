using System;

namespace DynamicDungeon.Runtime.Core
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class NeighbourCountRuleAttribute : Attribute
    {
    }
}
