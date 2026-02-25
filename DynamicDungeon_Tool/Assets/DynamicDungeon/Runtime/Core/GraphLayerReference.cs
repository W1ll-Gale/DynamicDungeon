using System;
using UnityEngine;

[Serializable]
public struct GraphLayerReference
{
    [SerializeField] private string _layerId;

    public string LayerId => _layerId;
    public bool IsAssigned => !string.IsNullOrWhiteSpace(_layerId);

    public GraphLayerReference(string layerId)
    {
        _layerId = layerId;
    }
}

[AttributeUsage(AttributeTargets.Field)]
public sealed class GraphLayerReferenceAttribute : PropertyAttribute
{
    public PortDataKind ExpectedKind { get; }
    public bool AllowNone { get; }

    public GraphLayerReferenceAttribute(PortDataKind expectedKind, bool allowNone = true)
    {
        ExpectedKind = expectedKind;
        AllowNone = allowNone;
    }
}
