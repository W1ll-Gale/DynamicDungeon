using System;
using UnityEngine;

namespace DynamicDungeon.Runtime.Graph
{
    [Serializable]
    public sealed class SerializedParameter
    {
        public string Name = string.Empty;
        public string Value = string.Empty;
        public UnityEngine.Object ObjectReference;

        public SerializedParameter()
        {
        }

        public SerializedParameter(string name, string value)
        {
            Name = name ?? string.Empty;
            Value = value ?? string.Empty;
        }

        public SerializedParameter(string name, string value, UnityEngine.Object objectReference)
        {
            Name = name ?? string.Empty;
            Value = value ?? string.Empty;
            ObjectReference = objectReference;
        }
    }
}
