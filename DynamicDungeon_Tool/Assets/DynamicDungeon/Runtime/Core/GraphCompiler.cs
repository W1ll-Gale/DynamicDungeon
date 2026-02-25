using System.Collections.Generic;

public sealed class GraphCompiler
{
    private readonly GenGraph _graph;

    public GraphCompiler(GenGraph graph)
    {
        _graph = graph;
    }

    public GraphCompileResult Compile()
    {
        List<GraphDiagnostic> diagnostics = new List<GraphDiagnostic>();
        List<GenNodeBase> orderedNodes = new List<GenNodeBase>();

        if (_graph == null)
        {
            diagnostics.Add(new GraphDiagnostic(GraphDiagnosticSeverity.Error, "Graph is null."));
            return GraphCompileResult.Failure(diagnostics);
        }

        Dictionary<string, GenNodeBase> nodeLookup = new Dictionary<string, GenNodeBase>();
        Dictionary<string, int> inDegree = new Dictionary<string, int>();
        Dictionary<string, List<string>> adjacency = new Dictionary<string, List<string>>();
        Dictionary<string, int> inputConnectionCounts = new Dictionary<string, int>();
        int graphOutputCount = 0;

        foreach (GenNodeBase node in _graph.Nodes)
        {
            if (node == null)
            {
                diagnostics.Add(new GraphDiagnostic(GraphDiagnosticSeverity.Warning, "Graph contains a null node reference."));
                continue;
            }

            if (nodeLookup.ContainsKey(node.NodeId))
            {
                diagnostics.Add(new GraphDiagnostic(
                    GraphDiagnosticSeverity.Error,
                    $"Duplicate node id '{node.NodeId}' detected.",
                    node.NodeId));
                continue;
            }

            nodeLookup[node.NodeId] = node;
            inDegree[node.NodeId] = 0;
            adjacency[node.NodeId] = new List<string>();

            if (node is GraphOutputNode)
                graphOutputCount++;
        }

        if (nodeLookup.Count == 0)
        {
            diagnostics.Add(new GraphDiagnostic(GraphDiagnosticSeverity.Error, "Graph has no executable nodes."));
            return GraphCompileResult.Failure(diagnostics);
        }

        if (graphOutputCount == 0)
        {
            diagnostics.Add(new GraphDiagnostic(
                GraphDiagnosticSeverity.Error,
                "Graph must contain one Graph Output node."));
        }
        else if (graphOutputCount > 1)
        {
            diagnostics.Add(new GraphDiagnostic(
                GraphDiagnosticSeverity.Error,
                "Graph currently supports exactly one Graph Output node."));
        }

        foreach (PortConnection connection in _graph.Connections)
        {
            if (connection == null)
            {
                diagnostics.Add(new GraphDiagnostic(GraphDiagnosticSeverity.Warning, "Graph contains a null connection reference."));
                continue;
            }

            if (!nodeLookup.TryGetValue(connection.OutputNodeId, out GenNodeBase outputNode))
            {
                diagnostics.Add(new GraphDiagnostic(
                    GraphDiagnosticSeverity.Error,
                    $"Connection references missing output node '{connection.OutputNodeId}'.",
                    connection.OutputNodeId,
                    connection.ConnectionId));
                continue;
            }

            if (!nodeLookup.TryGetValue(connection.InputNodeId, out GenNodeBase inputNode))
            {
                diagnostics.Add(new GraphDiagnostic(
                    GraphDiagnosticSeverity.Error,
                    $"Connection references missing input node '{connection.InputNodeId}'.",
                    connection.InputNodeId,
                    connection.ConnectionId));
                continue;
            }

            NodePort outputPort = outputNode.GetOutputPortById(connection.OutputPortId);
            NodePort inputPort = inputNode.GetInputPortById(connection.InputPortId);

            if (outputPort == null)
            {
                diagnostics.Add(new GraphDiagnostic(
                    GraphDiagnosticSeverity.Error,
                    $"Output port '{connection.OutputPortId}' not found on node '{outputNode.NodeTitle}'.",
                    outputNode.NodeId,
                    connection.ConnectionId));
                continue;
            }

            if (inputPort == null)
            {
                diagnostics.Add(new GraphDiagnostic(
                    GraphDiagnosticSeverity.Error,
                    $"Input port '{connection.InputPortId}' not found on node '{inputNode.NodeTitle}'.",
                    inputNode.NodeId,
                    connection.ConnectionId));
                continue;
            }

            if (outputPort.DataKind != inputPort.DataKind)
            {
                diagnostics.Add(new GraphDiagnostic(
                    GraphDiagnosticSeverity.Error,
                    $"Cannot connect '{outputNode.NodeTitle}:{outputPort.PortName}' ({outputPort.DataKind}) to " +
                    $"'{inputNode.NodeTitle}:{inputPort.PortName}' ({inputPort.DataKind}).",
                    inputNode.NodeId,
                    connection.ConnectionId));
                continue;
            }

            string inputKey = $"{inputNode.NodeId}|{inputPort.PortId}";
            inputConnectionCounts.TryGetValue(inputKey, out int existingCount);
            inputConnectionCounts[inputKey] = existingCount + 1;

            if (inputPort.Capacity == PortCapacity.Single && inputConnectionCounts[inputKey] > 1)
            {
                diagnostics.Add(new GraphDiagnostic(
                    GraphDiagnosticSeverity.Error,
                    $"Input port '{inputNode.NodeTitle}:{inputPort.PortName}' only accepts a single connection.",
                    inputNode.NodeId,
                    connection.ConnectionId));
            }

            adjacency[connection.OutputNodeId].Add(connection.InputNodeId);
            inDegree[connection.InputNodeId]++;
        }

        foreach (GenNodeBase node in nodeLookup.Values)
        {
            foreach (NodePort inputPort in node.InputPorts)
            {
                if (!inputPort.Required) continue;

                string inputKey = $"{node.NodeId}|{inputPort.PortId}";
                if (!inputConnectionCounts.ContainsKey(inputKey))
                {
                    diagnostics.Add(new GraphDiagnostic(
                        GraphDiagnosticSeverity.Error,
                        $"Required input '{node.NodeTitle}:{inputPort.PortName}' is unconnected.",
                        node.NodeId));
                }
            }
        }

        Queue<string> queue = new Queue<string>();
        foreach (KeyValuePair<string, int> pair in inDegree)
        {
            if (pair.Value == 0)
                queue.Enqueue(pair.Key);
        }

        while (queue.Count > 0)
        {
            string currentNodeId = queue.Dequeue();
            orderedNodes.Add(nodeLookup[currentNodeId]);

            foreach (string neighbourId in adjacency[currentNodeId])
            {
                inDegree[neighbourId]--;
                if (inDegree[neighbourId] == 0)
                    queue.Enqueue(neighbourId);
            }
        }

        if (orderedNodes.Count < nodeLookup.Count)
        {
            diagnostics.Add(new GraphDiagnostic(
                GraphDiagnosticSeverity.Error,
                "Cycle detected in generation graph."));
        }

        bool hasErrors = false;
        for (int index = 0; index < diagnostics.Count; index++)
        {
            if (diagnostics[index].Severity == GraphDiagnosticSeverity.Error)
            {
                hasErrors = true;
                break;
            }
        }

        return hasErrors
            ? GraphCompileResult.Failure(diagnostics, orderedNodes)
            : GraphCompileResult.Success(orderedNodes, diagnostics);
    }
}
