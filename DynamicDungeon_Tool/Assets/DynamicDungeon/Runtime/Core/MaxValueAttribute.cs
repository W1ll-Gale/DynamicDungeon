using System;

namespace DynamicDungeon.Runtime.Core
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class MaxValueAttribute : Attribute
    {
        public MaxValueAttribute(float value)
        {
            Value = value;
        }

        public float Value { get; }
    }
}
