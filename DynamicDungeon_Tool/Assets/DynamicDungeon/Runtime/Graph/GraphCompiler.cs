using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using DynamicDungeon.Runtime.Core;
using UnityEngine;

namespace DynamicDungeon.Runtime.Graph
{
    public static class GraphCompiler
    {
        private sealed class CompiledNodeInfo
        {
            public readonly GenNodeData NodeData;
            public readonly IGenNode Node;
            public readonly Dictionary<string, NodePortDefinition> PortsByName;

            public CompiledNodeInfo(GenNodeData nodeData, IGenNode node, Dictionary<string, NodePortDefinition> portsByName)
            {
                NodeData = nodeData;
                Node = node;
                PortsByName = portsByName;
            }
        }

        private sealed class ValidatedConnection
        {
            public readonly CompiledNodeInfo FromNode;
            public readonly NodePortDefinition FromPort;
            public readonly CompiledNodeInfo ToNode;
            public readonly NodePortDefinition ToPort;

            public ValidatedConnection(CompiledNodeInfo fromNode, NodePortDefinition fromPort, CompiledNodeInfo toNode, NodePortDefinition toPort)
            {
                FromNode = fromNode;
                FromPort = fromPort;
                ToNode = toNode;
                ToPort = toPort;
            }
        }

        private sealed class ConnectionValidationResult
        {
            public readonly List<ValidatedConnection> Connections;
            public readonly Dictionary<string, int> IncomingCountsByPortKey;

            public ConnectionValidationResult(List<ValidatedConnection> connections, Dictionary<string, int> incomingCountsByPortKey)
            {
                Connections = connections;
                IncomingCountsByPortKey = incomingCountsByPortKey;
            }
        }

        private sealed class ConstructorMatch
        {
            public readonly ConstructorInfo Constructor;
            public readonly object[] Arguments;
            public readonly int Score;

            public ConstructorMatch(ConstructorInfo constructor, object[] arguments, int score)
            {
                Constructor = constructor;
                Arguments = arguments;
                Score = score;
            }
        }

        private enum VisitState
        {
            Unvisited,
            Visiting,
            Visited
        }

        public static GraphCompileResult Compile(GenGraph graph)
        {
            return CompileInternal(graph, false);
        }

        public static GraphCompileResult CompileForPreview(GenGraph graph)
        {
            return CompileInternal(graph, true);
        }

        private static GraphCompileResult CompileInternal(GenGraph graph, bool includeDisconnectedNodes)
        {
            List<GraphDiagnostic> diagnostics = new List<GraphDiagnostic>();

            if (graph == null)
            {
                diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Graph cannot be null.", null, null));
                return new GraphCompileResult(false, diagnostics, null, string.Empty, false);
            }

            if (graph.WorldWidth <= 0)
            {
                diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Graph world width must be greater than zero.", null, null));
            }

            if (graph.WorldHeight <= 0)
            {
                diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Graph world height must be greater than zero.", null, null));
            }

            List<GenNodeData> nodeDataList = graph.Nodes ?? new List<GenNodeData>();
            List<GenConnectionData> connectionDataList = graph.Connections ?? new List<GenConnectionData>();
            GenNodeData outputNodeData = FindOutputNodeData(nodeDataList, diagnostics);

            if (HasErrors(diagnostics))
            {
                return new GraphCompileResult(false, diagnostics, null, string.Empty, false);
            }

            List<GenNodeData> reachableNodeDataList;
            List<GenConnectionData> reachableConnectionDataList;

            if (includeDisconnectedNodes)
            {
                reachableNodeDataList = new List<GenNodeData>(nodeDataList);
                reachableConnectionDataList = new List<GenConnectionData>(connectionDataList);
            }
            else
            {
                HashSet<string> reachableNodeIds = BuildReachableNodeIds(outputNodeData.NodeId, connectionDataList);
                reachableNodeDataList = FilterReachableNodes(nodeDataList, reachableNodeIds);
                reachableConnectionDataList = FilterReachableConnections(connectionDataList, reachableNodeIds);
            }

            List<CompiledNodeInfo> compiledNodes = new List<CompiledNodeInfo>(reachableNodeDataList.Count);
            Dictionary<string, CompiledNodeInfo> nodesById = new Dictionary<string, CompiledNodeInfo>(reachableNodeDataList.Count, StringComparer.Ordinal);

            CompileNodes(reachableNodeDataList, diagnostics, compiledNodes, nodesById);

            if (HasErrors(diagnostics))
            {
                return new GraphCompileResult(false, diagnostics, null, string.Empty, false);
            }

            ConnectionValidationResult connectionValidation = ValidateConnections(reachableConnectionDataList, diagnostics, nodesById);
            ApplyInputConnectionBindings(compiledNodes, connectionValidation.Connections);
            bool hasConnectedOutput;
            string outputChannelName = ResolveOutputChannelName(connectionValidation.Connections, outputNodeData.NodeId, out hasConnectedOutput);
            ValidateRequiredInputs(compiledNodes, connectionValidation.IncomingCountsByPortKey, diagnostics, outputNodeData.NodeId, hasConnectedOutput);
            ValidateChannelOwnership(compiledNodes, diagnostics);

            List<CompiledNodeInfo> orderedNodes = TopologicallySort(compiledNodes, connectionValidation.Connections, diagnostics);

            if (HasErrors(diagnostics))
            {
                return new GraphCompileResult(false, diagnostics, null, outputChannelName, hasConnectedOutput);
            }

            try
            {
                List<IGenNode> orderedRuntimeNodes = new List<IGenNode>(orderedNodes.Count);

                int nodeIndex;
                for (nodeIndex = 0; nodeIndex < orderedNodes.Count; nodeIndex++)
                {
                    orderedRuntimeNodes.Add(orderedNodes[nodeIndex].Node);
                }

                ExecutionPlan plan = ExecutionPlan.Build(orderedRuntimeNodes, graph.WorldWidth, graph.WorldHeight, graph.DefaultSeed);
                return new GraphCompileResult(true, diagnostics, plan, outputChannelName, hasConnectedOutput);
            }
            catch (Exception exception)
            {
                diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Execution plan build failed: " + exception.Message, null, null));
                return new GraphCompileResult(false, diagnostics, null, outputChannelName, hasConnectedOutput);
            }
        }

        private static GenNodeData FindOutputNodeData(IReadOnlyList<GenNodeData> nodeDataList, List<GraphDiagnostic> diagnostics)
        {
            GenNodeData outputNodeData = null;

            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < nodeDataList.Count; nodeIndex++)
            {
                GenNodeData nodeData = nodeDataList[nodeIndex];
                if (!GraphOutputUtility.IsOutputNode(nodeData))
                {
                    continue;
                }

                if (outputNodeData != null)
                {
                    diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Graph contains more than one output node.", nodeData != null ? nodeData.NodeId : null, null));
                    return null;
                }

                outputNodeData = nodeData;
            }

            if (outputNodeData == null)
            {
                diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Graph must contain an output node.", null, null));
            }

            return outputNodeData;
        }

        private static HashSet<string> BuildReachableNodeIds(string outputNodeId, IReadOnlyList<GenConnectionData> connectionDataList)
        {
            HashSet<string> reachableNodeIds = new HashSet<string>(StringComparer.Ordinal);
            string resolvedOutputNodeId = outputNodeId ?? string.Empty;
            Queue<string> pendingNodeIds = new Queue<string>();
            reachableNodeIds.Add(resolvedOutputNodeId);
            pendingNodeIds.Enqueue(resolvedOutputNodeId);

            while (pendingNodeIds.Count > 0)
            {
                string targetNodeId = pendingNodeIds.Dequeue();

                int connectionIndex;
                for (connectionIndex = 0; connectionIndex < connectionDataList.Count; connectionIndex++)
                {
                    GenConnectionData connectionData = connectionDataList[connectionIndex];
                    if (connectionData == null ||
                        !string.Equals(connectionData.ToNodeId, targetNodeId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string sourceNodeId = connectionData.FromNodeId ?? string.Empty;
                    if (reachableNodeIds.Add(sourceNodeId))
                    {
                        pendingNodeIds.Enqueue(sourceNodeId);
                    }
                }
            }

            return reachableNodeIds;
        }

        private static List<GenNodeData> FilterReachableNodes(IReadOnlyList<GenNodeData> nodeDataList, HashSet<string> reachableNodeIds)
        {
            List<GenNodeData> reachableNodes = new List<GenNodeData>();

            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < nodeDataList.Count; nodeIndex++)
            {
                GenNodeData nodeData = nodeDataList[nodeIndex];
                if (nodeData != null && reachableNodeIds.Contains(nodeData.NodeId ?? string.Empty))
                {
                    reachableNodes.Add(nodeData);
                }
            }

            return reachableNodes;
        }

        private static List<GenConnectionData> FilterReachableConnections(IReadOnlyList<GenConnectionData> connectionDataList, HashSet<string> reachableNodeIds)
        {
            List<GenConnectionData> reachableConnections = new List<GenConnectionData>();

            int connectionIndex;
            for (connectionIndex = 0; connectionIndex < connectionDataList.Count; connectionIndex++)
            {
                GenConnectionData connectionData = connectionDataList[connectionIndex];
                if (connectionData == null)
                {
                    continue;
                }

                if (reachableNodeIds.Contains(connectionData.FromNodeId ?? string.Empty) &&
                    reachableNodeIds.Contains(connectionData.ToNodeId ?? string.Empty))
                {
                    reachableConnections.Add(connectionData);
                }
            }

            return reachableConnections;
        }

        private static void CompileNodes(IReadOnlyList<GenNodeData> nodeDataList, List<GraphDiagnostic> diagnostics, List<CompiledNodeInfo> compiledNodes, Dictionary<string, CompiledNodeInfo> nodesById)
        {
            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < nodeDataList.Count; nodeIndex++)
            {
                GenNodeData nodeData = nodeDataList[nodeIndex];
                if (nodeData == null)
                {
                    diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Graph contains a null node entry.", null, null));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(nodeData.NodeId))
                {
                    diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Node ID must be non-empty.", null, null));
                    continue;
                }

                if (nodesById.ContainsKey(nodeData.NodeId))
                {
                    diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Duplicate node ID '" + nodeData.NodeId + "' detected in the graph.", nodeData.NodeId, null));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(nodeData.NodeTypeName))
                {
                    diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Node '" + nodeData.NodeId + "' does not specify a node type name.", nodeData.NodeId, null));
                    continue;
                }

                Type nodeType = ResolveNodeType(nodeData.NodeTypeName);
                if (nodeType == null)
                {
                    diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Node type '" + nodeData.NodeTypeName + "' could not be found.", nodeData.NodeId, null));
                    continue;
                }

                if (!typeof(IGenNode).IsAssignableFrom(nodeType) || nodeType.IsAbstract)
                {
                    diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Node type '" + nodeData.NodeTypeName + "' does not implement IGenNode.", nodeData.NodeId, null));
                    continue;
                }

                string instantiationError;
                IGenNode nodeInstance;
                if (!TryInstantiateNode(nodeType, nodeData, out nodeInstance, out instantiationError))
                {
                    diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, instantiationError, nodeData.NodeId, null));
                    continue;
                }

                if (!ApplyParameters(nodeInstance, nodeData, diagnostics))
                {
                    continue;
                }

                Dictionary<string, NodePortDefinition> portsByName = BuildPortLookup(nodeInstance, diagnostics);
                if (portsByName == null)
                {
                    continue;
                }

                CompiledNodeInfo compiledNode = new CompiledNodeInfo(nodeData, nodeInstance, portsByName);
                compiledNodes.Add(compiledNode);
                nodesById.Add(nodeData.NodeId, compiledNode);
            }
        }

        private static bool ApplyParameters(IGenNode nodeInstance, GenNodeData nodeData, List<GraphDiagnostic> diagnostics)
        {
            IParameterReceiver parameterReceiver = nodeInstance as IParameterReceiver;
            if (parameterReceiver == null)
            {
                return true;
            }

            List<SerializedParameter> parameters = nodeData.Parameters ?? new List<SerializedParameter>();

            int parameterIndex;
            for (parameterIndex = 0; parameterIndex < parameters.Count; parameterIndex++)
            {
                SerializedParameter parameter = parameters[parameterIndex];
                if (parameter == null || string.IsNullOrWhiteSpace(parameter.Name))
                {
                    continue;
                }

                try
                {
                    parameterReceiver.ReceiveParameter(parameter.Name, parameter.Value ?? string.Empty);
                }
                catch (Exception exception)
                {
                    diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Failed to apply parameter '" + parameter.Name + "' to node '" + nodeData.NodeName + "': " + exception.Message, nodeData.NodeId, null));
                    return false;
                }
            }

            return true;
        }

        private static Dictionary<string, NodePortDefinition> BuildPortLookup(IGenNode nodeInstance, List<GraphDiagnostic> diagnostics)
        {
            Dictionary<string, NodePortDefinition> portsByName = new Dictionary<string, NodePortDefinition>(StringComparer.Ordinal);
            IReadOnlyList<NodePortDefinition> ports = nodeInstance.Ports;

            int portIndex;
            for (portIndex = 0; portIndex < ports.Count; portIndex++)
            {
                NodePortDefinition port = ports[portIndex];
                if (portsByName.ContainsKey(port.Name))
                {
                    diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Node '" + nodeInstance.NodeName + "' declares duplicate port name '" + port.Name + "'.", nodeInstance.NodeId, port.Name));
                    return null;
                }

                portsByName.Add(port.Name, port);
            }

            return portsByName;
        }

        private static ConnectionValidationResult ValidateConnections(IReadOnlyList<GenConnectionData> connectionDataList, List<GraphDiagnostic> diagnostics, IReadOnlyDictionary<string, CompiledNodeInfo> nodesById)
        {
            List<ValidatedConnection> validatedConnections = new List<ValidatedConnection>(connectionDataList.Count);
            Dictionary<string, int> incomingCountsByPortKey = new Dictionary<string, int>(StringComparer.Ordinal);

            int connectionIndex;
            for (connectionIndex = 0; connectionIndex < connectionDataList.Count; connectionIndex++)
            {
                GenConnectionData connectionData = connectionDataList[connectionIndex];
                if (connectionData == null)
                {
                    diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Graph contains a null connection entry.", null, null));
                    continue;
                }

                CompiledNodeInfo fromNode;
                if (!nodesById.TryGetValue(connectionData.FromNodeId ?? string.Empty, out fromNode))
                {
                    diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Connection references unknown source node '" + connectionData.FromNodeId + "'.", connectionData.FromNodeId, connectionData.FromPortName));
                    continue;
                }

                CompiledNodeInfo toNode;
                if (!nodesById.TryGetValue(connectionData.ToNodeId ?? string.Empty, out toNode))
                {
                    diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Connection references unknown target node '" + connectionData.ToNodeId + "'.", connectionData.ToNodeId, connectionData.ToPortName));
                    continue;
                }

                NodePortDefinition fromPort;
                if (!fromNode.PortsByName.TryGetValue(connectionData.FromPortName ?? string.Empty, out fromPort))
                {
                    diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Source port '" + connectionData.FromPortName + "' does not exist on node '" + fromNode.Node.NodeName + "'.", fromNode.Node.NodeId, connectionData.FromPortName));
                    continue;
                }

                if (fromPort.Direction != PortDirection.Output)
                {
                    diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Source port '" + fromPort.Name + "' on node '" + fromNode.Node.NodeName + "' is not an output port.", fromNode.Node.NodeId, fromPort.Name));
                    continue;
                }

                NodePortDefinition toPort;
                if (!toNode.PortsByName.TryGetValue(connectionData.ToPortName ?? string.Empty, out toPort))
                {
                    diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Target port '" + connectionData.ToPortName + "' does not exist on node '" + toNode.Node.NodeName + "'.", toNode.Node.NodeId, connectionData.ToPortName));
                    continue;
                }

                if (toPort.Direction != PortDirection.Input)
                {
                    diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Target port '" + toPort.Name + "' on node '" + toNode.Node.NodeName + "' is not an input port.", toNode.Node.NodeId, toPort.Name));
                    continue;
                }

                if (fromPort.Type != toPort.Type && !CastRegistry.CanCast(fromPort.Type, toPort.Type))
                {
                    diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Connection from '" + fromNode.Node.NodeName + "." + fromPort.Name + "' to '" + toNode.Node.NodeName + "." + toPort.Name + "' is type-incompatible.", toNode.Node.NodeId, toPort.Name));
                    continue;
                }

                string targetPortKey = CreatePortKey(toNode.Node.NodeId, toPort.Name);
                int existingIncomingCount;
                incomingCountsByPortKey.TryGetValue(targetPortKey, out existingIncomingCount);

                if (existingIncomingCount >= 1 && toPort.Capacity != PortCapacity.Multi)
                {
                    diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Input port '" + toPort.Name + "' on node '" + toNode.Node.NodeName + "' does not allow multiple incoming connections.", toNode.Node.NodeId, toPort.Name));
                    continue;
                }

                incomingCountsByPortKey[targetPortKey] = existingIncomingCount + 1;
                validatedConnections.Add(new ValidatedConnection(fromNode, fromPort, toNode, toPort));
            }

            return new ConnectionValidationResult(validatedConnections, incomingCountsByPortKey);
        }

        private static void ApplyInputConnectionBindings(IReadOnlyList<CompiledNodeInfo> compiledNodes, IReadOnlyList<ValidatedConnection> validatedConnections)
        {
            Dictionary<string, Dictionary<string, string>> inputConnectionsByNodeId = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

            int connectionIndex;
            for (connectionIndex = 0; connectionIndex < validatedConnections.Count; connectionIndex++)
            {
                ValidatedConnection connection = validatedConnections[connectionIndex];
                Dictionary<string, string> nodeConnections;
                if (!inputConnectionsByNodeId.TryGetValue(connection.ToNode.Node.NodeId, out nodeConnections))
                {
                    nodeConnections = new Dictionary<string, string>(StringComparer.Ordinal);
                    inputConnectionsByNodeId.Add(connection.ToNode.Node.NodeId, nodeConnections);
                }

                if (!nodeConnections.ContainsKey(connection.ToPort.Name))
                {
                    nodeConnections.Add(connection.ToPort.Name, connection.FromPort.Name);
                }
            }

            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < compiledNodes.Count; nodeIndex++)
            {
                CompiledNodeInfo compiledNode = compiledNodes[nodeIndex];
                IInputConnectionReceiver inputConnectionReceiver = compiledNode.Node as IInputConnectionReceiver;
                if (inputConnectionReceiver == null)
                {
                    continue;
                }

                Dictionary<string, string> nodeConnections;
                if (!inputConnectionsByNodeId.TryGetValue(compiledNode.Node.NodeId, out nodeConnections))
                {
                    nodeConnections = new Dictionary<string, string>(StringComparer.Ordinal);
                }

                inputConnectionReceiver.ReceiveInputConnections(nodeConnections);
            }
        }

        private static void ValidateRequiredInputs(IReadOnlyList<CompiledNodeInfo> compiledNodes, IReadOnlyDictionary<string, int> incomingCountsByPortKey, List<GraphDiagnostic> diagnostics, string outputNodeId, bool hasConnectedOutput)
        {
            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < compiledNodes.Count; nodeIndex++)
            {
                CompiledNodeInfo compiledNode = compiledNodes[nodeIndex];
                IReadOnlyList<NodePortDefinition> ports = compiledNode.Node.Ports;

                int portIndex;
                for (portIndex = 0; portIndex < ports.Count; portIndex++)
                {
                    NodePortDefinition port = ports[portIndex];
                    if (port.Direction != PortDirection.Input || !port.Required)
                    {
                        continue;
                    }

                    string portKey = CreatePortKey(compiledNode.Node.NodeId, port.Name);
                    int connectionCount;
                    incomingCountsByPortKey.TryGetValue(portKey, out connectionCount);

                    if (!hasConnectedOutput &&
                        string.Equals(compiledNode.Node.NodeId, outputNodeId, StringComparison.Ordinal) &&
                        string.Equals(port.Name, GraphOutputUtility.OutputInputPortName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (connectionCount == 0)
                    {
                        diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Required input port '" + port.Name + "' on node '" + compiledNode.Node.NodeName + "' is not connected.", compiledNode.Node.NodeId, port.Name));
                    }
                }
            }
        }

        private static string ResolveOutputChannelName(IReadOnlyList<ValidatedConnection> validatedConnections, string outputNodeId, out bool hasConnectedOutput)
        {
            int connectionIndex;
            for (connectionIndex = 0; connectionIndex < validatedConnections.Count; connectionIndex++)
            {
                ValidatedConnection connection = validatedConnections[connectionIndex];
                if (!string.Equals(connection.ToNode.Node.NodeId, outputNodeId, StringComparison.Ordinal) ||
                    !string.Equals(connection.ToPort.Name, GraphOutputUtility.OutputInputPortName, StringComparison.Ordinal))
                {
                    continue;
                }

                hasConnectedOutput = true;
                return connection.FromPort.Name;
            }

            hasConnectedOutput = false;
            return string.Empty;
        }

        private static void ValidateChannelOwnership(IReadOnlyList<CompiledNodeInfo> compiledNodes, List<GraphDiagnostic> diagnostics)
        {
            Dictionary<string, CompiledNodeInfo> ownersByChannelName = new Dictionary<string, CompiledNodeInfo>(StringComparer.Ordinal);

            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < compiledNodes.Count; nodeIndex++)
            {
                CompiledNodeInfo compiledNode = compiledNodes[nodeIndex];
                IReadOnlyList<ChannelDeclaration> declarations = compiledNode.Node.ChannelDeclarations;

                int declarationIndex;
                for (declarationIndex = 0; declarationIndex < declarations.Count; declarationIndex++)
                {
                    ChannelDeclaration declaration = declarations[declarationIndex];
                    if (!declaration.IsWrite)
                    {
                        continue;
                    }

                    CompiledNodeInfo existingOwner;
                    if (ownersByChannelName.TryGetValue(declaration.ChannelName, out existingOwner))
                    {
                        diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Channel '" + declaration.ChannelName + "' is owned by both '" + existingOwner.Node.NodeName + "' and '" + compiledNode.Node.NodeName + "'.", compiledNode.Node.NodeId, null));
                        continue;
                    }

                    ownersByChannelName.Add(declaration.ChannelName, compiledNode);
                }
            }
        }

        private static List<CompiledNodeInfo> TopologicallySort(IReadOnlyList<CompiledNodeInfo> compiledNodes, IReadOnlyList<ValidatedConnection> validatedConnections, List<GraphDiagnostic> diagnostics)
        {
            Dictionary<string, List<CompiledNodeInfo>> adjacency = new Dictionary<string, List<CompiledNodeInfo>>(compiledNodes.Count, StringComparer.Ordinal);
            Dictionary<string, VisitState> visitStates = new Dictionary<string, VisitState>(compiledNodes.Count, StringComparer.Ordinal);
            List<CompiledNodeInfo> orderedNodes = new List<CompiledNodeInfo>(compiledNodes.Count);

            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < compiledNodes.Count; nodeIndex++)
            {
                CompiledNodeInfo compiledNode = compiledNodes[nodeIndex];
                adjacency.Add(compiledNode.Node.NodeId, new List<CompiledNodeInfo>());
                visitStates.Add(compiledNode.Node.NodeId, VisitState.Unvisited);
            }

            int connectionIndex;
            for (connectionIndex = 0; connectionIndex < validatedConnections.Count; connectionIndex++)
            {
                ValidatedConnection connection = validatedConnections[connectionIndex];
                adjacency[connection.FromNode.Node.NodeId].Add(connection.ToNode);
            }

            for (nodeIndex = 0; nodeIndex < compiledNodes.Count; nodeIndex++)
            {
                CompiledNodeInfo compiledNode = compiledNodes[nodeIndex];
                if (visitStates[compiledNode.Node.NodeId] == VisitState.Unvisited)
                {
                    DepthFirstVisit(compiledNode, adjacency, visitStates, orderedNodes, diagnostics);
                }
            }

            orderedNodes.Reverse();
            return orderedNodes;
        }

        private static void DepthFirstVisit(CompiledNodeInfo compiledNode, IReadOnlyDictionary<string, List<CompiledNodeInfo>> adjacency, Dictionary<string, VisitState> visitStates, List<CompiledNodeInfo> orderedNodes, List<GraphDiagnostic> diagnostics)
        {
            visitStates[compiledNode.Node.NodeId] = VisitState.Visiting;

            List<CompiledNodeInfo> neighbours = adjacency[compiledNode.Node.NodeId];
            int neighbourIndex;
            for (neighbourIndex = 0; neighbourIndex < neighbours.Count; neighbourIndex++)
            {
                CompiledNodeInfo neighbour = neighbours[neighbourIndex];
                VisitState neighbourState = visitStates[neighbour.Node.NodeId];

                if (neighbourState == VisitState.Visiting)
                {
                    diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Cycle detected between nodes '" + compiledNode.Node.NodeName + "' and '" + neighbour.Node.NodeName + "'.", neighbour.Node.NodeId, null));
                    continue;
                }

                if (neighbourState == VisitState.Unvisited)
                {
                    DepthFirstVisit(neighbour, adjacency, visitStates, orderedNodes, diagnostics);
                }
            }

            visitStates[compiledNode.Node.NodeId] = VisitState.Visited;
            orderedNodes.Add(compiledNode);
        }

        private static bool TryInstantiateNode(Type nodeType, GenNodeData nodeData, out IGenNode nodeInstance, out string errorMessage)
        {
            ConstructorInfo[] constructors = nodeType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            ConstructorMatch bestMatch = null;

            int constructorIndex;
            for (constructorIndex = 0; constructorIndex < constructors.Length; constructorIndex++)
            {
                ConstructorMatch constructorMatch;
                if (TryBindConstructor(constructors[constructorIndex], nodeData, out constructorMatch))
                {
                    if (bestMatch == null || constructorMatch.Score > bestMatch.Score)
                    {
                        bestMatch = constructorMatch;
                    }
                }
            }

            if (bestMatch == null)
            {
                nodeInstance = null;
                errorMessage = "Node type '" + nodeType.FullName + "' could not be instantiated from graph data. Provide a compatible constructor or parameterless constructor.";
                return false;
            }

            try
            {
                nodeInstance = (IGenNode)bestMatch.Constructor.Invoke(bestMatch.Arguments);
                errorMessage = null;
                return true;
            }
            catch (TargetInvocationException exception)
            {
                Exception innerException = exception.InnerException ?? exception;
                nodeInstance = null;
                errorMessage = "Node type '" + nodeType.FullName + "' failed during construction: " + innerException.Message;
                return false;
            }
            catch (Exception exception)
            {
                nodeInstance = null;
                errorMessage = "Node type '" + nodeType.FullName + "' failed during construction: " + exception.Message;
                return false;
            }
        }

        private static bool TryBindConstructor(ConstructorInfo constructor, GenNodeData nodeData, out ConstructorMatch constructorMatch)
        {
            ParameterInfo[] parameters = constructor.GetParameters();
            object[] arguments = new object[parameters.Length];
            int score = 0;

            int parameterIndex;
            for (parameterIndex = 0; parameterIndex < parameters.Length; parameterIndex++)
            {
                ParameterInfo parameter = parameters[parameterIndex];

                object argumentValue;
                if (TryGetSpecialArgumentValue(parameter, nodeData, out argumentValue))
                {
                    arguments[parameterIndex] = argumentValue;
                    score += 2;
                    continue;
                }

                if (TryGetSerialisedParameterValue(nodeData.Parameters, parameter, out argumentValue))
                {
                    arguments[parameterIndex] = argumentValue;
                    score += 1;
                    continue;
                }

                if (TryGetPortDerivedArgumentValue(nodeData.Ports, parameter, out argumentValue))
                {
                    arguments[parameterIndex] = argumentValue;
                    score += 1;
                    continue;
                }

                if (parameter.ParameterType == typeof(Vector2))
                {
                    arguments[parameterIndex] = Vector2.zero;
                    continue;
                }

                if (parameter.HasDefaultValue)
                {
                    arguments[parameterIndex] = parameter.DefaultValue;
                    continue;
                }

                constructorMatch = null;
                return false;
            }

            constructorMatch = new ConstructorMatch(constructor, arguments, score + parameters.Length);
            return true;
        }

        private static bool TryGetSpecialArgumentValue(ParameterInfo parameter, GenNodeData nodeData, out object argumentValue)
        {
            string parameterName = parameter.Name ?? string.Empty;

            if (parameter.ParameterType == typeof(string) && string.Equals(parameterName, "nodeId", StringComparison.OrdinalIgnoreCase))
            {
                argumentValue = nodeData.NodeId ?? string.Empty;
                return true;
            }

            if (parameter.ParameterType == typeof(string) &&
                (string.Equals(parameterName, "nodeName", StringComparison.OrdinalIgnoreCase) || string.Equals(parameterName, "displayName", StringComparison.OrdinalIgnoreCase)))
            {
                argumentValue = nodeData.NodeName ?? string.Empty;
                return true;
            }

            if (parameter.ParameterType == typeof(Vector2) && string.Equals(parameterName, "position", StringComparison.OrdinalIgnoreCase))
            {
                argumentValue = nodeData.Position;
                return true;
            }

            argumentValue = null;
            return false;
        }

        private static bool TryGetSerialisedParameterValue(IReadOnlyList<SerializedParameter> parameters, ParameterInfo parameter, out object argumentValue)
        {
            IReadOnlyList<SerializedParameter> safeParameters = parameters ?? Array.Empty<SerializedParameter>();
            string parameterName = parameter.Name ?? string.Empty;

            int parameterIndex;
            for (parameterIndex = 0; parameterIndex < safeParameters.Count; parameterIndex++)
            {
                SerializedParameter serialisedParameter = safeParameters[parameterIndex];
                if (serialisedParameter == null || !string.Equals(serialisedParameter.Name, parameterName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return TryParseArgumentValue(parameter.ParameterType, serialisedParameter.Value, out argumentValue);
            }

            argumentValue = null;
            return false;
        }

        private static bool TryGetPortDerivedArgumentValue(IReadOnlyList<GenPortData> ports, ParameterInfo parameter, out object argumentValue)
        {
            GenPortData resolvedPort;
            if (!TryResolvePortForParameter(ports, parameter.Name ?? string.Empty, out resolvedPort))
            {
                argumentValue = null;
                return false;
            }

            if (parameter.ParameterType == typeof(string))
            {
                argumentValue = resolvedPort.PortName ?? string.Empty;
                return true;
            }

            if (parameter.ParameterType == typeof(ChannelType))
            {
                argumentValue = resolvedPort.Type;
                return true;
            }

            argumentValue = null;
            return false;
        }

        private static PortDirection? GetPreferredDirection(string parameterName)
        {
            if (parameterName.StartsWith("input", StringComparison.OrdinalIgnoreCase))
            {
                return PortDirection.Input;
            }

            if (parameterName.StartsWith("output", StringComparison.OrdinalIgnoreCase) || parameterName.StartsWith("from", StringComparison.OrdinalIgnoreCase))
            {
                return PortDirection.Output;
            }

            return null;
        }

        private static bool TryResolvePortForParameter(IReadOnlyList<GenPortData> ports, string parameterName, out GenPortData resolvedPort)
        {
            IReadOnlyList<GenPortData> safePorts = ports ?? Array.Empty<GenPortData>();
            PortDirection? preferredDirection = GetPreferredDirection(parameterName);
            string desiredPortName = GetDesiredPortName(parameterName);

            int portIndex;
            for (portIndex = 0; portIndex < safePorts.Count; portIndex++)
            {
                GenPortData port = safePorts[portIndex];
                if (port == null || string.IsNullOrWhiteSpace(port.PortName))
                {
                    continue;
                }

                if (preferredDirection.HasValue && port.Direction != preferredDirection.Value)
                {
                    continue;
                }

            if (!string.IsNullOrEmpty(desiredPortName) &&
                string.Equals(port.PortName, desiredPortName, StringComparison.OrdinalIgnoreCase))
            {
                resolvedPort = port;
                return true;
            }
        }

        return TryGetUniquePortByDirection(safePorts, preferredDirection, out resolvedPort);
    }

        private static string GetDesiredPortName(string parameterName)
        {
            string trimmedName = StripParameterSuffix(parameterName);
            string strippedPrefixName = StripDirectionPrefix(trimmedName);

            if (!string.IsNullOrEmpty(strippedPrefixName))
            {
                return strippedPrefixName;
            }

            if (!string.Equals(trimmedName, parameterName, StringComparison.OrdinalIgnoreCase))
            {
                return trimmedName;
            }

            return string.Empty;
        }

        private static string StripParameterSuffix(string parameterName)
        {
            if (parameterName.EndsWith("ChannelName", StringComparison.OrdinalIgnoreCase))
            {
                return parameterName.Substring(0, parameterName.Length - "ChannelName".Length);
            }

            if (parameterName.EndsWith("PortName", StringComparison.OrdinalIgnoreCase))
            {
                return parameterName.Substring(0, parameterName.Length - "PortName".Length);
            }

            if (parameterName.EndsWith("Type", StringComparison.OrdinalIgnoreCase))
            {
                return parameterName.Substring(0, parameterName.Length - "Type".Length);
            }

            return parameterName;
        }

        private static string StripDirectionPrefix(string value)
        {
            if (value.StartsWith("input", StringComparison.OrdinalIgnoreCase))
            {
                return value.Length == "input".Length ? string.Empty : value.Substring("input".Length);
            }

            if (value.StartsWith("output", StringComparison.OrdinalIgnoreCase))
            {
                return value.Length == "output".Length ? string.Empty : value.Substring("output".Length);
            }

            if (value.StartsWith("from", StringComparison.OrdinalIgnoreCase))
            {
                return value.Length == "from".Length ? string.Empty : value.Substring("from".Length);
            }

            if (value.StartsWith("to", StringComparison.OrdinalIgnoreCase))
            {
                return value.Length == "to".Length ? string.Empty : value.Substring("to".Length);
            }

            return value;
        }

        private static bool TryGetUniquePortByDirection(IReadOnlyList<GenPortData> ports, PortDirection? preferredDirection, out GenPortData resolvedPort)
        {
            GenPortData matchedPort = null;
            int matchCount = 0;

            int portIndex;
            for (portIndex = 0; portIndex < ports.Count; portIndex++)
            {
                GenPortData port = ports[portIndex];
                if (port == null || string.IsNullOrWhiteSpace(port.PortName))
                {
                    continue;
                }

                if (preferredDirection.HasValue && port.Direction != preferredDirection.Value)
                {
                    continue;
                }

                matchedPort = port;
                matchCount++;
            }

            if (matchCount == 1)
            {
                resolvedPort = matchedPort;
                return true;
            }

            resolvedPort = null;
            return false;
        }

        private static bool TryParseArgumentValue(Type targetType, string rawValue, out object argumentValue)
        {
            string safeValue = rawValue ?? string.Empty;

            if (targetType == typeof(string))
            {
                argumentValue = safeValue;
                return true;
            }

            if (targetType == typeof(int))
            {
                int parsedInt;
                if (int.TryParse(safeValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedInt))
                {
                    argumentValue = parsedInt;
                    return true;
                }

                argumentValue = null;
                return false;
            }

            if (targetType == typeof(long))
            {
                long parsedLong;
                if (long.TryParse(safeValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedLong))
                {
                    argumentValue = parsedLong;
                    return true;
                }

                argumentValue = null;
                return false;
            }

            if (targetType == typeof(float))
            {
                float parsedFloat;
                if (float.TryParse(safeValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsedFloat))
                {
                    argumentValue = parsedFloat;
                    return true;
                }

                argumentValue = null;
                return false;
            }

            if (targetType == typeof(double))
            {
                double parsedDouble;
                if (double.TryParse(safeValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsedDouble))
                {
                    argumentValue = parsedDouble;
                    return true;
                }

                argumentValue = null;
                return false;
            }

            if (targetType == typeof(bool))
            {
                bool parsedBool;
                if (bool.TryParse(safeValue, out parsedBool))
                {
                    argumentValue = parsedBool;
                    return true;
                }

                if (safeValue == "0")
                {
                    argumentValue = false;
                    return true;
                }

                if (safeValue == "1")
                {
                    argumentValue = true;
                    return true;
                }

                argumentValue = null;
                return false;
            }

            if (targetType == typeof(Vector2))
            {
                return TryParseVector2(safeValue, out argumentValue);
            }

            if (targetType.IsEnum)
            {
                try
                {
                    argumentValue = Enum.Parse(targetType, safeValue, true);
                    return true;
                }
                catch
                {
                    argumentValue = null;
                    return false;
                }
            }

            argumentValue = null;
            return false;
        }

        private static bool TryParseVector2(string rawValue, out object argumentValue)
        {
            string trimmedValue = rawValue.Trim();

            if (trimmedValue.Length == 0)
            {
                argumentValue = new Vector2(0.0f, 0.0f);
                return true;
            }

            string normalisedValue = trimmedValue.Replace("(", string.Empty).Replace(")", string.Empty);
            string[] parts = normalisedValue.Split(',');
            if (parts.Length == 2)
            {
                float xValue;
                float yValue;
                if (float.TryParse(parts[0].Trim(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out xValue) &&
                    float.TryParse(parts[1].Trim(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out yValue))
                {
                    argumentValue = new Vector2(xValue, yValue);
                    return true;
                }
            }

            float scalarValue;
            if (float.TryParse(trimmedValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out scalarValue))
            {
                argumentValue = new Vector2(scalarValue, scalarValue);
                return true;
            }

            try
            {
                Vector2 jsonVector = JsonUtility.FromJson<Vector2>(trimmedValue);
                if (!float.IsNaN(jsonVector.x) && !float.IsNaN(jsonVector.y))
                {
                    argumentValue = jsonVector;
                    return true;
                }
            }
            catch
            {
            }

            argumentValue = null;
            return false;
        }

        private static Type ResolveNodeType(string nodeTypeName)
        {
            Type resolvedType = Type.GetType(nodeTypeName, false);
            if (resolvedType != null)
            {
                return resolvedType;
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            int assemblyIndex;
            for (assemblyIndex = 0; assemblyIndex < assemblies.Length; assemblyIndex++)
            {
                Type[] types;
                try
                {
                    types = assemblies[assemblyIndex].GetTypes();
                }
                catch (ReflectionTypeLoadException exception)
                {
                    types = exception.Types;
                }

                int typeIndex;
                for (typeIndex = 0; typeIndex < types.Length; typeIndex++)
                {
                    Type candidateType = types[typeIndex];
                    if (candidateType != null && string.Equals(candidateType.FullName, nodeTypeName, StringComparison.Ordinal))
                    {
                        return candidateType;
                    }
                }
            }

            return null;
        }

        private static bool HasErrors(IReadOnlyList<GraphDiagnostic> diagnostics)
        {
            int diagnosticIndex;
            for (diagnosticIndex = 0; diagnosticIndex < diagnostics.Count; diagnosticIndex++)
            {
                if (diagnostics[diagnosticIndex].Severity == DiagnosticSeverity.Error)
                {
                    return true;
                }
            }

            return false;
        }

        private static string CreatePortKey(string nodeId, string portName)
        {
            return (nodeId ?? string.Empty) + "::" + (portName ?? string.Empty);
        }
    }
}
