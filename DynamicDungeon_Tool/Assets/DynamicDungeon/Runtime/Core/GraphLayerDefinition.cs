using System;
using UnityEngine;

[Serializable]
public sealed class GraphLayerDefinition
{
    [SerializeField, HideInInspector] private string _layerId;
    [SerializeField] private string _displayName = "Layer";
    [SerializeField] private PortDataKind _kind = PortDataKind.FloatLayer;

    public string LayerId => _layerId;
    public string DisplayName => string.IsNullOrWhiteSpace(_displayName) ? "Layer" : _displayName;
    public PortDataKind Kind => _kind;

    public GraphLayerDefinition(string displayName, PortDataKind kind)
    {
        _layerId = Guid.NewGuid().ToString("N");
        _displayName = string.IsNullOrWhiteSpace(displayName) ? "Layer" : displayName.Trim();
        _kind = kind;
    }

    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(_layerId))
            _layerId = Guid.NewGuid().ToString("N");

        if (string.IsNullOrWhiteSpace(_displayName))
            _displayName = "Layer";
    }

    public void Rename(string displayName)
    {
        _displayName = string.IsNullOrWhiteSpace(displayName) ? "Layer" : displayName.Trim();
    }

    public void SetKind(PortDataKind kind)
    {
        _kind = kind;
    }
}
