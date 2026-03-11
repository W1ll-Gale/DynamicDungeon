using System;

namespace DynamicDungeon.Runtime.Graph
{
    [Serializable]
    public sealed class SerializedParameter
    {
        public string Name = string.Empty;
        public string Value = string.Empty;

        public SerializedParameter()
        {
        }

        public SerializedParameter(string name, string value)
        {
            Name = name ?? string.Empty;
            Value = value ?? string.Empty;
        }
    }
}
