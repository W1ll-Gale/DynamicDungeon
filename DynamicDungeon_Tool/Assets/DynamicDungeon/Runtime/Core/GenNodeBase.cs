using System;
using System.Collections.Generic;
using UnityEngine;

public abstract class GenNodeBase : ScriptableObject
{
    [SerializeField, HideInInspector] private string _nodeId;
    [SerializeField, HideInInspector] private Vector2 _editorPosition;
    [SerializeField, HideInInspector] private List<NodePort> _inputPorts = new List<NodePort>();
    [SerializeField, HideInInspector] private List<NodePort> _outputPorts = new List<NodePort>();

    private bool _portsInitialised;

    public string NodeId => _nodeId;

    public Vector2 EditorPosition
    {
        get => _editorPosition;
        set => _editorPosition = value;
    }

    public abstract string NodeTitle { get; }
    public abstract string NodeCategory { get; }
    public virtual string NodeDescription => string.Empty;
    public virtual string PreferredPreviewPortName => string.Empty;
    public virtual string PreferredPreviewInputPortName => string.Empty;

    public IReadOnlyList<NodePort> InputPorts => EnsurePorts()._inputPorts;
    public IReadOnlyList<NodePort> OutputPorts => EnsurePorts()._outputPorts;

    protected virtual void OnEnable()
    {
        if (string.IsNullOrEmpty(_nodeId))
            _nodeId = Guid.NewGuid().ToString();

        _portsInitialised = false;
    }

    protected abstract void DefinePorts();

    protected NodePort AddInputPort(
        string portName,
        PortDataKind dataKind,
        PortCapacity capacity = PortCapacity.Single,
        bool required = true,
        string tooltip = "")
    {
        NodePort port = new NodePort(portName, PortDirection.Input, dataKind, capacity, required, tooltip);
        _inputPorts.Add(port);
        return port;
    }

    protected NodePort AddOutputPort(
        string portName,
        PortDataKind dataKind,
        PortCapacity capacity = PortCapacity.Single,
        string tooltip = "")
    {
        NodePort port = new NodePort(portName, PortDirection.Output, dataKind, capacity, false, tooltip);
        _outputPorts.Add(port);
        return port;
    }

    public abstract NodeExecutionResult Execute(NodeExecutionContext context);

    public bool IsSourceNode => EnsurePorts()._inputPorts.Count == 0;
    public bool IsSinkNode => EnsurePorts()._outputPorts.Count == 0;

    public NodePort GetInputPortById(string portId)
    {
        foreach (NodePort port in EnsurePorts()._inputPorts)
            if (port.PortId == portId) return port;
        return null;
    }

    public NodePort GetOutputPortById(string portId)
    {
        foreach (NodePort port in EnsurePorts()._outputPorts)
            if (port.PortId == portId) return port;
        return null;
    }

    public bool TryGetInputPort(string portName, out NodePort port)
    {
        foreach (NodePort candidate in EnsurePorts()._inputPorts)
        {
            if (candidate.PortName == portName)
            {
                port = candidate;
                return true;
            }
        }

        port = null;
        return false;
    }

    public bool TryGetOutputPort(string portName, out NodePort port)
    {
        foreach (NodePort candidate in EnsurePorts()._outputPorts)
        {
            if (candidate.PortName == portName)
            {
                port = candidate;
                return true;
            }
        }

        port = null;
        return false;
    }

    private GenNodeBase EnsurePorts()
    {
        if (_portsInitialised) return this;

        _inputPorts.Clear();
        _outputPorts.Clear();
        DefinePorts();
        _portsInitialised = true;

        return this;
    }
}
