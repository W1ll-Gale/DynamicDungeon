using System;
using UnityEngine;

[Serializable]
public sealed class PortConnection
{
    [SerializeField] private string _connectionId;
    [SerializeField] private string _outputNodeId;
    [SerializeField] private string _outputPortId;
    [SerializeField] private string _inputNodeId;
    [SerializeField] private string _inputPortId;

    public string ConnectionId => _connectionId;
    public string OutputNodeId => _outputNodeId;
    public string OutputPortId => _outputPortId;
    public string InputNodeId => _inputNodeId;
    public string InputPortId => _inputPortId;

    public PortConnection(string outputNodeId, string outputPortId, string inputNodeId, string inputPortId)
    {
        _connectionId = Guid.NewGuid().ToString();
        _outputNodeId = outputNodeId;
        _outputPortId = outputPortId;
        _inputNodeId = inputNodeId;
        _inputPortId = inputPortId;
    }

    public bool Involves(string nodeId) => _outputNodeId == nodeId || _inputNodeId == nodeId;

    public override string ToString() => $"[Connection {_outputNodeId}:{_outputPortId} → {_inputNodeId}:{_inputPortId}]";
}