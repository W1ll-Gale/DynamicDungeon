using System;

namespace DynamicDungeon.Runtime.Component
{
    [Serializable]
    public sealed class ExposedPropertyOverride
    {
        public string PropertyName = string.Empty;

        public string OverrideValue = "0";
    }
}
