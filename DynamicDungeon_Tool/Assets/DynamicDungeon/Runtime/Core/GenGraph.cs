using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewGenGraph", menuName = "DynamicDungeon/Generation Graph")]
public sealed class GenGraph : ScriptableObject
{
    [Header("World Defaults")]
    [SerializeField, Min(1)] private int _defaultWidth = 128;
    [SerializeField, Min(1)] private int _defaultHeight = 128;
    [SerializeField] private long _defaultSeed = 0L;
    [SerializeField] private bool _randomizeSeedByDefault = true;

    [Header("Graph Layers")]
    [SerializeField] private List<GraphLayerDefinition> _layers = new List<GraphLayerDefinition>();

    [Header("Rendering")]
    [SerializeField] private TileRulesetAsset _tileRuleset;

    [Header("Graph Data")]
    [SerializeField] private List<GenNodeBase> _nodes = new List<GenNodeBase>();
    [SerializeField] private List<PortConnection> _connections = new List<PortConnection>();

    public int DefaultWidth => Mathf.Max(1, _defaultWidth);
    public int DefaultHeight => Mathf.Max(1, _defaultHeight);
    public long DefaultSeed => _defaultSeed;
    public bool RandomizeSeedByDefault => _randomizeSeedByDefault;
    public IReadOnlyList<GraphLayerDefinition> Layers => _layers;
    public TileRulesetAsset TileRuleset => _tileRuleset;

    public IReadOnlyList<GenNodeBase> Nodes => _nodes;
    public IReadOnlyList<PortConnection> Connections => _connections;

    private void OnEnable()
    {
        EnsureLayersValid();
    }

    private void OnValidate()
    {
        EnsureLayersValid();
    }

    public void AddNode(GenNodeBase node)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));
        if (_nodes.Contains(node))
        {
            Debug.LogWarning($"[GenGraph] Node '{node.NodeId}' is already in the graph.");
            return;
        }

        _nodes.Add(node);
    }

    public void SetTileRuleset(TileRulesetAsset tileRuleset)
    {
        _tileRuleset = tileRuleset;
    }

    public GraphLayerDefinition AddLayer(string displayName, PortDataKind kind)
    {
        string uniqueName = MakeUniqueLayerName(displayName, kind);
        GraphLayerDefinition created = new GraphLayerDefinition(uniqueName, kind);
        _layers.Add(created);
        return created;
    }

    public GraphLayerDefinition EnsureLayer(string displayName, PortDataKind kind)
    {
        string trimmedName = string.IsNullOrWhiteSpace(displayName) ? "Layer" : displayName.Trim();
        EnsureLayersValid();

        foreach (GraphLayerDefinition layer in _layers)
        {
            if (layer == null) continue;
            if (layer.Kind == kind && string.Equals(layer.DisplayName, trimmedName, StringComparison.Ordinal))
                return layer;
        }

        return AddLayer(trimmedName, kind);
    }

    public bool RemoveLayer(string layerId)
    {
        if (string.IsNullOrWhiteSpace(layerId))
            return false;

        return _layers.RemoveAll(layer => layer != null && layer.LayerId == layerId) > 0;
    }

    public bool MoveLayer(int fromIndex, int toIndex)
    {
        EnsureLayersValid();

        if (fromIndex < 0 || fromIndex >= _layers.Count)
            return false;

        if (toIndex < 0 || toIndex >= _layers.Count)
            return false;

        if (fromIndex == toIndex)
            return false;

        GraphLayerDefinition layer = _layers[fromIndex];
        _layers.RemoveAt(fromIndex);
        _layers.Insert(toIndex, layer);
        return true;
    }

    public bool TryRenameLayer(string layerId, string displayName)
    {
        if (!TryGetLayer(layerId, out GraphLayerDefinition layer))
            return false;

        layer.Rename(MakeUniqueLayerNameForExisting(layerId, displayName));
        return true;
    }

    public bool TrySetLayerKind(string layerId, PortDataKind kind)
    {
        if (!TryGetLayer(layerId, out GraphLayerDefinition layer))
            return false;

        layer.SetKind(kind);
        return true;
    }

    public bool TryGetLayer(string layerId, out GraphLayerDefinition layer)
    {
        EnsureLayersValid();

        foreach (GraphLayerDefinition candidate in _layers)
        {
            if (candidate != null && candidate.LayerId == layerId)
            {
                layer = candidate;
                return true;
            }
        }

        layer = null;
        return false;
    }

    public List<GraphLayerDefinition> GetLayersOfKind(PortDataKind kind)
    {
        EnsureLayersValid();
        List<GraphLayerDefinition> results = new List<GraphLayerDefinition>();
        foreach (GraphLayerDefinition layer in _layers)
        {
            if (layer != null && layer.Kind == kind)
                results.Add(layer);
        }
        return results;
    }

    public string GetLayerDisplayName(string layerId, string fallback = "Unassigned Layer")
    {
        return TryGetLayer(layerId, out GraphLayerDefinition layer) ? layer.DisplayName : fallback;
    }

    public void RemoveNode(GenNodeBase node)
    {
        if (node == null) return;

        _connections.RemoveAll(connection => connection.Involves(node.NodeId));
        _nodes.Remove(node);
    }

    public GenNodeBase FindNodeById(string nodeId)
    {
        foreach (GenNodeBase node in _nodes)
            if (node != null && node.NodeId == nodeId) return node;
        return null;
    }

    public PortConnection AddConnection(
        string outputNodeId,
        string outputPortId,
        string inputNodeId,
        string inputPortId)
    {
        GenNodeBase outputNode = FindNodeById(outputNodeId);
        GenNodeBase inputNode = FindNodeById(inputNodeId);

        if (outputNode == null)
        {
            Debug.LogError($"[GenGraph] Cannot connect: output node '{outputNodeId}' not found.");
            return null;
        }

        if (inputNode == null)
        {
            Debug.LogError($"[GenGraph] Cannot connect: input node '{inputNodeId}' not found.");
            return null;
        }

        NodePort outputPort = outputNode.GetOutputPortById(outputPortId);
        NodePort inputPort = inputNode.GetInputPortById(inputPortId);

        if (outputPort == null)
        {
            Debug.LogError($"[GenGraph] Output port '{outputPortId}' not found on '{outputNode.NodeTitle}'.");
            return null;
        }

        if (inputPort == null)
        {
            Debug.LogError($"[GenGraph] Input port '{inputPortId}' not found on '{inputNode.NodeTitle}'.");
            return null;
        }

        if (outputPort.DataKind != inputPort.DataKind)
        {
            Debug.LogError(
                $"[GenGraph] Cannot connect '{outputNode.NodeTitle}:{outputPort.PortName}' ({outputPort.DataKind}) " +
                $"to '{inputNode.NodeTitle}:{inputPort.PortName}' ({inputPort.DataKind}).");
            return null;
        }

        if (inputPort.Capacity == PortCapacity.Single)
            _connections.RemoveAll(connection => connection.InputNodeId == inputNodeId && connection.InputPortId == inputPortId);

        PortConnection existing = _connections.Find(connection =>
            connection.OutputNodeId == outputNodeId &&
            connection.OutputPortId == outputPortId &&
            connection.InputNodeId == inputNodeId &&
            connection.InputPortId == inputPortId);

        if (existing != null) return existing;

        PortConnection created = new PortConnection(outputNodeId, outputPortId, inputNodeId, inputPortId);
        _connections.Add(created);
        return created;
    }

    public void RemoveConnection(string connectionId)
        => _connections.RemoveAll(connection => connection.ConnectionId == connectionId);

    public void RemoveConnection(PortConnection connection)
    {
        if (connection != null) RemoveConnection(connection.ConnectionId);
    }

    public List<PortConnection> GetConnectionsToNode(string nodeId)
    {
        List<PortConnection> result = new List<PortConnection>();
        foreach (PortConnection connection in _connections)
        {
            if (connection.InputNodeId == nodeId) result.Add(connection);
        }
        return result;
    }

    public List<PortConnection> GetConnectionsFromNode(string nodeId)
    {
        List<PortConnection> result = new List<PortConnection>();
        foreach (PortConnection connection in _connections)
        {
            if (connection.OutputNodeId == nodeId) result.Add(connection);
        }
        return result;
    }

    public void ClearAll()
    {
        _nodes.Clear();
        _connections.Clear();
    }

    private void EnsureLayersValid()
    {
        HashSet<string> usedNames = new HashSet<string>(StringComparer.Ordinal);

        for (int index = _layers.Count - 1; index >= 0; index--)
        {
            GraphLayerDefinition layer = _layers[index];
            if (layer == null)
            {
                _layers.RemoveAt(index);
                continue;
            }

            layer.EnsureValid();
        }

        foreach (GraphLayerDefinition layer in _layers)
        {
            string uniqueName = layer.DisplayName;
            int suffix = 2;
            while (!usedNames.Add(uniqueName))
            {
                uniqueName = $"{layer.DisplayName} {suffix}";
                suffix++;
            }

            if (!string.Equals(uniqueName, layer.DisplayName, StringComparison.Ordinal))
                layer.Rename(uniqueName);
        }
    }

    private string MakeUniqueLayerName(string displayName, PortDataKind kind)
    {
        EnsureLayersValid();

        string baseName = string.IsNullOrWhiteSpace(displayName)
            ? kind.ToString()
            : displayName.Trim();

        string candidate = baseName;
        int suffix = 2;

        while (_layers.Exists(layer => layer != null && string.Equals(layer.DisplayName, candidate, StringComparison.Ordinal)))
        {
            candidate = $"{baseName} {suffix}";
            suffix++;
        }

        return candidate;
    }

    private string MakeUniqueLayerNameForExisting(string layerId, string displayName)
    {
        EnsureLayersValid();

        string baseName = string.IsNullOrWhiteSpace(displayName) ? "Layer" : displayName.Trim();
        string candidate = baseName;
        int suffix = 2;

        while (_layers.Exists(layer =>
                   layer != null &&
                   layer.LayerId != layerId &&
                   string.Equals(layer.DisplayName, candidate, StringComparison.Ordinal)))
        {
            candidate = $"{baseName} {suffix}";
            suffix++;
        }

        return candidate;
    }
}
