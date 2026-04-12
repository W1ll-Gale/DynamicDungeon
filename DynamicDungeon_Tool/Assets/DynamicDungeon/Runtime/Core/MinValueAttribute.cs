using System;

namespace DynamicDungeon.Runtime.Core
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class MinValueAttribute : Attribute
    {
        public MinValueAttribute(float value)
        {
            Value = value;
        }

        public float Value { get; }
    }
}
