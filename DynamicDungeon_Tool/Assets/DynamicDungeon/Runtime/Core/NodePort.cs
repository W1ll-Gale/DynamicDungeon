using System;
using UnityEngine;

public enum PortDirection
{
    Input,
    Output
}

public enum PortCapacity
{
    Single,
    Multi
}

public enum PortDataKind
{
    World,
    FloatLayer,
    IntLayer,
    BoolMask,
    MarkerSet,
    ValidationReport
}

[Serializable]
public sealed class NodePort
{
    [SerializeField] private string _portId;
    [SerializeField] private string _portName;
    [SerializeField] private string _tooltip;
    [SerializeField] private PortDirection _direction;
    [SerializeField] private PortCapacity _capacity;
    [SerializeField] private PortDataKind _dataKind;
    [SerializeField] private bool _required;

    public string PortId => _portId;
    public string PortName => _portName;
    public string Tooltip => _tooltip;
    public PortDirection Direction => _direction;
    public PortCapacity Capacity => _capacity;
    public PortDataKind DataKind => _dataKind;
    public bool Required => _required;

    public NodePort(
        string portName,
        PortDirection direction,
        PortDataKind dataKind,
        PortCapacity capacity = PortCapacity.Single,
        bool required = false,
        string tooltip = "")
    {
        if (string.IsNullOrWhiteSpace(portName))
            throw new ArgumentException("Port name cannot be null or whitespace.", nameof(portName));

        _portName = portName.Trim();
        _direction = direction;
        _dataKind = dataKind;
        _capacity = capacity;
        _required = required;
        _tooltip = tooltip ?? string.Empty;
        _portId = $"{direction}:{dataKind}:{_portName}";
    }

    public override string ToString()
        => $"[Port:{_portName} {_direction} {_dataKind} {_capacity}]";
}
