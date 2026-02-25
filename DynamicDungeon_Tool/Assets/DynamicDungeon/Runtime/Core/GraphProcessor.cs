using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class GraphProcessor
{
    private readonly GenGraph _graph;

    public GraphProcessor(GenGraph graph)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
    }

    public GraphProcessorResult Execute(GraphExecutionContext executionContext = null)
    {
        executionContext ??= GraphExecutionContext.FromGraph(_graph);

        GraphCompileResult compileResult = new GraphCompiler(_graph).Compile();
        if (!compileResult.IsSuccess)
            return GraphProcessorResult.Failure(compileResult.BuildSummary(), compileResult.Diagnostics);

        Dictionary<string, Dictionary<string, NodeValue>> outputCache =
            new Dictionary<string, Dictionary<string, NodeValue>>();
        NodeValue primaryGraphOutput = null;

        foreach (GenNodeBase node in compileResult.ExecutionOrder)
        {
            Dictionary<string, NodeValue> inputs = CollectInputs(node, outputCache);
            NodeExecutionContext nodeContext = new NodeExecutionContext(executionContext, node, inputs);

            NodeExecutionResult executionResult;
            try
            {
                executionResult = node.Execute(nodeContext) ?? NodeExecutionResult.Empty;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return GraphProcessorResult.Failure(
                    $"[GraphProcessor] Node '{node.NodeTitle}' ({node.NodeId}) threw an exception: {ex.Message}",
                    compileResult.Diagnostics);
            }

            Dictionary<string, NodeValue> outputs = new Dictionary<string, NodeValue>(executionResult.Outputs);
            outputCache[node.NodeId] = outputs;

            if (node is GraphOutputNode &&
                inputs.TryGetValue(GraphOutputNode.WorldInputPortName, out NodeValue outputValue))
            {
                primaryGraphOutput = outputValue;
            }
        }

        Dictionary<string, IReadOnlyDictionary<string, NodeValue>> finalOutputs =
            new Dictionary<string, IReadOnlyDictionary<string, NodeValue>>();

        foreach (KeyValuePair<string, Dictionary<string, NodeValue>> pair in outputCache)
            finalOutputs[pair.Key] = pair.Value;

        return GraphProcessorResult.Success(finalOutputs, primaryGraphOutput, compileResult.Diagnostics);
    }

    private Dictionary<string, NodeValue> CollectInputs(
        GenNodeBase node,
        Dictionary<string, Dictionary<string, NodeValue>> outputCache)
    {
        Dictionary<string, NodeValue> inputs = new Dictionary<string, NodeValue>();
        List<PortConnection> incomingConnections = _graph.GetConnectionsToNode(node.NodeId);

        foreach (PortConnection connection in incomingConnections)
        {
            if (!outputCache.TryGetValue(connection.OutputNodeId, out Dictionary<string, NodeValue> upstreamOutputs))
                continue;

            GenNodeBase upstreamNode = _graph.FindNodeById(connection.OutputNodeId);
            if (upstreamNode == null) continue;

            NodePort outputPort = upstreamNode.GetOutputPortById(connection.OutputPortId);
            NodePort inputPort = node.GetInputPortById(connection.InputPortId);
            if (outputPort == null || inputPort == null) continue;

            if (!upstreamOutputs.TryGetValue(outputPort.PortName, out NodeValue value))
                continue;

            if (inputPort.Capacity == PortCapacity.Multi)
            {
                int suffix = 0;
                string key = inputPort.PortName;
                while (inputs.ContainsKey(key))
                {
                    suffix++;
                    key = $"{inputPort.PortName}_{suffix}";
                }
                inputs[key] = value;
            }
            else
            {
                inputs[inputPort.PortName] = value;
            }
        }

        return inputs;
    }
}
