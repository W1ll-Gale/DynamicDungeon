using System;
using System.Collections.Generic;
using System.Globalization;
using DynamicDungeon.Runtime.Biome;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Nodes;
using DynamicDungeon.Runtime.Placement;
#if UNITY_EDITOR
using UnityEditor;
#endif
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
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
            public readonly string SourceChannelName;
            public readonly CompiledNodeInfo ToNode;
            public readonly NodePortDefinition ToPort;
            public readonly CastMode CastMode;
            public readonly string CastChannelName;

            public ValidatedConnection(CompiledNodeInfo fromNode, NodePortDefinition fromPort, string sourceChannelName, CompiledNodeInfo toNode, NodePortDefinition toPort, CastMode castMode, string castChannelName)
            {
                FromNode = fromNode;
                FromPort = fromPort;
                SourceChannelName = sourceChannelName;
                ToNode = toNode;
                ToPort = toPort;
                CastMode = castMode;
                CastChannelName = castChannelName;
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

        private sealed class ExpandedSubGraphBoundary
        {
            private readonly Dictionary<string, List<ExpandedTargetEndpoint>> _inputTargetsByPort = new Dictionary<string, List<ExpandedTargetEndpoint>>(StringComparer.Ordinal);
            private readonly Dictionary<string, List<ExpandedSourceEndpoint>> _outputSourcesByPort = new Dictionary<string, List<ExpandedSourceEndpoint>>(StringComparer.Ordinal);

            public readonly string NodeId;

            public ExpandedSubGraphBoundary(string nodeId)
            {
                NodeId = nodeId ?? string.Empty;
            }

            public void AddInputTarget(string portName, ExpandedTargetEndpoint target)
            {
                if (target == null)
                {
                    return;
                }

                List<ExpandedTargetEndpoint> targets;
                string safePortName = portName ?? string.Empty;
                if (!_inputTargetsByPort.TryGetValue(safePortName, out targets))
                {
                    targets = new List<ExpandedTargetEndpoint>();
                    _inputTargetsByPort.Add(safePortName, targets);
                }

                targets.Add(target);
            }

            public void AddOutputSource(string portName, ExpandedSourceEndpoint source)
            {
                if (source == null)
                {
                    return;
                }

                List<ExpandedSourceEndpoint> sources;
                string safePortName = portName ?? string.Empty;
                if (!_outputSourcesByPort.TryGetValue(safePortName, out sources))
                {
                    sources = new List<ExpandedSourceEndpoint>();
                    _outputSourcesByPort.Add(safePortName, sources);
                }

                sources.Add(source);
            }

            public IReadOnlyList<ExpandedTargetEndpoint> GetInputTargets(string portName)
            {
                List<ExpandedTargetEndpoint> targets;
                return _inputTargetsByPort.TryGetValue(portName ?? string.Empty, out targets)
                    ? targets
                    : Array.Empty<ExpandedTargetEndpoint>();
            }

            public IReadOnlyList<ExpandedSourceEndpoint> GetOutputSources(string portName)
            {
                List<ExpandedSourceEndpoint> sources;
                return _outputSourcesByPort.TryGetValue(portName ?? string.Empty, out sources)
                    ? sources
                    : Array.Empty<ExpandedSourceEndpoint>();
            }
        }

        private sealed class ExpandedSourceEndpoint
        {
            public readonly string NodeId;
            public readonly string PortName;
            public readonly CastMode CastMode;
            public readonly string PassthroughInputPort;

            public bool IsPassthrough => !string.IsNullOrWhiteSpace(PassthroughInputPort);

            private ExpandedSourceEndpoint(string nodeId, string portName, CastMode castMode, string passthroughInputPort)
            {
                NodeId = nodeId ?? string.Empty;
                PortName = portName ?? string.Empty;
                CastMode = castMode;
                PassthroughInputPort = passthroughInputPort ?? string.Empty;
            }

            public static ExpandedSourceEndpoint CreateInternal(string nodeId, string portName, CastMode castMode)
            {
                return new ExpandedSourceEndpoint(nodeId, portName, castMode, string.Empty);
            }

            public static ExpandedSourceEndpoint CreatePassthrough(string inputPortName, CastMode castMode)
            {
                return new ExpandedSourceEndpoint(string.Empty, string.Empty, castMode, inputPortName);
            }

            public ExpandedSourceEndpoint WithCast(CastMode castMode)
            {
                return IsPassthrough
                    ? CreatePassthrough(PassthroughInputPort, castMode)
                    : CreateInternal(NodeId, PortName, castMode);
            }
        }

        private sealed class ExpandedTargetEndpoint
        {
            public readonly string NodeId;
            public readonly string PortName;
            public readonly CastMode CastMode;

            public ExpandedTargetEndpoint(string nodeId, string portName, CastMode castMode)
            {
                NodeId = nodeId ?? string.Empty;
                PortName = portName ?? string.Empty;
                CastMode = castMode;
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
            return CompileInternal(graph, true, false);
        }

        public static GraphCompileResult CompileForPreview(GenGraph graph)
        {
            return CompileInternal(graph, false, false);
        }

        private static GraphCompileResult CompileInternal(GenGraph graph, bool requireOutputNode, bool includeDisconnectedNodes)
        {
            List<GraphDiagnostic> diagnostics = new List<GraphDiagnostic>();

            if (graph == null)
            {
                diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Graph cannot be null.", null, null));
                return new GraphCompileResult(false, diagnostics, null, string.Empty, false);
            }

            string schemaErrorMessage;
            if (!GraphOutputUtility.TryValidateCurrentSchema(graph, out schemaErrorMessage))
            {
                diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, schemaErrorMessage, null, null));
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

            GenGraph compilationGraph;
            if (!TryExpandSubGraphs(graph, diagnostics, out compilationGraph))
            {
                return new GraphCompileResult(false, diagnostics, null, string.Empty, false);
            }

            List<GenNodeData> nodeDataList = compilationGraph.Nodes ?? new List<GenNodeData>();
            List<GenConnectionData> connectionDataList = compilationGraph.Connections ?? new List<GenConnectionData>();
            GenNodeData outputNodeData = FindOutputNodeData(nodeDataList, diagnostics, requireOutputNode);

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
                HashSet<string> reachableNodeIds = requireOutputNode
                    ? BuildReachableNodeIds(outputNodeData != null ? outputNodeData.NodeId : string.Empty, nodeDataList, connectionDataList)
                    : BuildPreviewReachableNodeIds(nodeDataList, connectionDataList);
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
            string outputNodeId = outputNodeData != null ? outputNodeData.NodeId : string.Empty;
            string outputChannelName = ResolveOutputChannelName(connectionValidation.Connections, outputNodeId, out hasConnectedOutput);
            ValidateRequiredInputs(compiledNodes, connectionValidation.IncomingCountsByPortKey, diagnostics, outputNodeId, hasConnectedOutput);
            ValidateChannelOwnership(compiledNodes, diagnostics);

            List<CompiledNodeInfo> orderedNodes = TopologicallySort(compiledNodes, connectionValidation.Connections, diagnostics);

            if (HasErrors(diagnostics))
            {
                return new GraphCompileResult(false, diagnostics, null, outputChannelName, hasConnectedOutput);
            }

            List<CompiledNodeInfo> allOrderedNodes = InsertImplicitCastNodes(orderedNodes, connectionValidation.Connections);
            BiomeAsset[] biomeChannelBiomes = ResolveBiomeChannelPalette(allOrderedNodes, diagnostics);
            PrefabStampPalette prefabPlacementPalette = ResolvePrefabPlacementPalette(allOrderedNodes, diagnostics);

            if (HasErrors(diagnostics))
            {
                return new GraphCompileResult(false, diagnostics, null, outputChannelName, hasConnectedOutput);
            }

            try
            {
                List<IGenNode> orderedRuntimeNodes = new List<IGenNode>(allOrderedNodes.Count);

                int nodeIndex;
                for (nodeIndex = 0; nodeIndex < allOrderedNodes.Count; nodeIndex++)
                {
                    orderedRuntimeNodes.Add(allOrderedNodes[nodeIndex].Node);
                }

                Dictionary<string, float> initialBlackboardValues = BuildExposedPropertyInitialValues(graph.ExposedProperties);
                ExecutionPlan plan = ExecutionPlan.Build(orderedRuntimeNodes, graph.WorldWidth, graph.WorldHeight, graph.DefaultSeed, initialBlackboardValues);
                plan.SetBiomeChannelBiomes(biomeChannelBiomes);
                plan.SetPrefabPlacementPalette(prefabPlacementPalette.Prefabs, prefabPlacementPalette.Templates);
                return new GraphCompileResult(true, diagnostics, plan, outputChannelName, hasConnectedOutput);
            }
            catch (Exception exception)
            {
                diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Execution plan build failed: " + exception.Message, null, null));
                return new GraphCompileResult(false, diagnostics, null, outputChannelName, hasConnectedOutput);
            }
        }

        private static bool TryExpandSubGraphs(GenGraph graph, List<GraphDiagnostic> diagnostics, out GenGraph expandedGraph)
        {
            expandedGraph = graph;

            if (!ContainsSubGraphNode(graph))
            {
                return true;
            }

            HashSet<int> activeGraphIds = new HashSet<int>();
            return TryExpandSubGraphsRecursive(graph, diagnostics, activeGraphIds, out expandedGraph);
        }

        private static bool TryExpandSubGraphsRecursive(GenGraph graph, List<GraphDiagnostic> diagnostics, HashSet<int> activeGraphIds, out GenGraph expandedGraph)
        {
            expandedGraph = null;

            if (graph == null)
            {
                diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Sub-graph expansion failed because a graph reference was null.", null, null));
                return false;
            }

            int graphId = graph.GetInstanceID();
            if (!activeGraphIds.Add(graphId))
            {
                diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Recursive sub-graph reference detected.", null, null));
                return false;
            }

            expandedGraph = CreateEmptyExpandedGraph(graph);
            List<GenNodeData> nodes = graph.Nodes ?? new List<GenNodeData>();
            List<GenConnectionData> connections = graph.Connections ?? new List<GenConnectionData>();
            HashSet<string> subGraphNodeIds = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, ExpandedSubGraphBoundary> expandedSubGraphs = new Dictionary<string, ExpandedSubGraphBoundary>(StringComparer.Ordinal);
            bool success = true;

            for (int nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
            {
                GenNodeData node = nodes[nodeIndex];
                if (node == null)
                {
                    continue;
                }

                if (IsSubGraphNodeData(node))
                {
                    subGraphNodeIds.Add(node.NodeId ?? string.Empty);
                    continue;
                }

                GenNodeData clonedNode = CloneNodeData(node, node.NodeId);
                if (!TryAddExpandedNode(expandedGraph, clonedNode, diagnostics))
                {
                    success = false;
                }
            }

            for (int connectionIndex = 0; connectionIndex < connections.Count; connectionIndex++)
            {
                GenConnectionData connection = connections[connectionIndex];
                if (connection == null)
                {
                    continue;
                }

                if (subGraphNodeIds.Contains(connection.FromNodeId ?? string.Empty) ||
                    subGraphNodeIds.Contains(connection.ToNodeId ?? string.Empty))
                {
                    continue;
                }

                expandedGraph.Connections.Add(CloneConnectionData(connection, connection.FromNodeId, connection.ToNodeId));
            }

            for (int nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
            {
                GenNodeData node = nodes[nodeIndex];
                if (node == null || !IsSubGraphNodeData(node))
                {
                    continue;
                }

                ExpandedSubGraphBoundary expandedSubGraph;
                if (!TryExpandSubGraphNode(graph, node, expandedGraph, diagnostics, activeGraphIds, out expandedSubGraph))
                {
                    success = false;
                }

                if (expandedSubGraph != null)
                {
                    expandedSubGraphs[node.NodeId ?? string.Empty] = expandedSubGraph;
                }
            }

            for (int connectionIndex = 0; connectionIndex < connections.Count; connectionIndex++)
            {
                GenConnectionData connection = connections[connectionIndex];
                if (connection == null)
                {
                    continue;
                }

                if (!subGraphNodeIds.Contains(connection.FromNodeId ?? string.Empty) &&
                    !subGraphNodeIds.Contains(connection.ToNodeId ?? string.Empty))
                {
                    continue;
                }

                if (!TryExpandParentSubGraphConnection(connections, expandedSubGraphs, expandedGraph, connection, diagnostics))
                {
                    success = false;
                }
            }

            activeGraphIds.Remove(graphId);
            return success;
        }

        private static bool TryExpandSubGraphNode(GenGraph parentGraph, GenNodeData subGraphNode, GenGraph expandedParent, List<GraphDiagnostic> diagnostics, HashSet<int> activeGraphIds, out ExpandedSubGraphBoundary expandedSubGraph)
        {
            expandedSubGraph = null;
            GenGraph nestedGraph = ResolveNestedGraph(subGraphNode);

            if (nestedGraph == null)
            {
                diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Sub-graph node '" + subGraphNode.NodeName + "' has no nested graph reference.", subGraphNode.NodeId, null));
                return false;
            }

            string schemaErrorMessage;
            if (!GraphOutputUtility.TryValidateCurrentSchema(nestedGraph, out schemaErrorMessage))
            {
                diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Nested graph on node '" + subGraphNode.NodeName + "' is invalid: " + schemaErrorMessage, subGraphNode.NodeId, null));
                return false;
            }

            GenGraph expandedNestedGraph;
            if (!TryExpandSubGraphsRecursive(nestedGraph, diagnostics, activeGraphIds, out expandedNestedGraph))
            {
                return false;
            }

            List<GenNodeData> nestedNodes = expandedNestedGraph.Nodes ?? new List<GenNodeData>();
            List<GenConnectionData> nestedConnections = expandedNestedGraph.Connections ?? new List<GenConnectionData>();
            HashSet<string> inputBoundaryNodeIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> outputBoundaryNodeIds = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, string> internalNodeIdMap = new Dictionary<string, string>(StringComparer.Ordinal);
            string nestedPrefix = (subGraphNode.NodeId ?? string.Empty) + "::";
            expandedSubGraph = new ExpandedSubGraphBoundary(subGraphNode.NodeId ?? string.Empty);
            bool success = true;

            for (int nodeIndex = 0; nodeIndex < nestedNodes.Count; nodeIndex++)
            {
                GenNodeData nestedNode = nestedNodes[nodeIndex];
                if (nestedNode == null)
                {
                    continue;
                }

                if (IsSubGraphInputNodeData(nestedNode))
                {
                    inputBoundaryNodeIds.Add(nestedNode.NodeId ?? string.Empty);
                    continue;
                }

                if (IsSubGraphOutputNodeData(nestedNode))
                {
                    outputBoundaryNodeIds.Add(nestedNode.NodeId ?? string.Empty);
                    continue;
                }

                string mappedNodeId = nestedPrefix + (nestedNode.NodeId ?? string.Empty);
                internalNodeIdMap[nestedNode.NodeId ?? string.Empty] = mappedNodeId;
                if (!TryAddExpandedNode(expandedParent, CloneNodeData(nestedNode, mappedNodeId), diagnostics))
                {
                    success = false;
                }
            }

            HashSet<string> producedOutputPorts = new HashSet<string>(StringComparer.Ordinal);

            for (int connectionIndex = 0; connectionIndex < nestedConnections.Count; connectionIndex++)
            {
                GenConnectionData nestedConnection = nestedConnections[connectionIndex];
                if (nestedConnection == null)
                {
                    continue;
                }

                bool fromInputBoundary = inputBoundaryNodeIds.Contains(nestedConnection.FromNodeId ?? string.Empty);
                bool toOutputBoundary = outputBoundaryNodeIds.Contains(nestedConnection.ToNodeId ?? string.Empty);

                if (fromInputBoundary && toOutputBoundary)
                {
                    producedOutputPorts.Add(nestedConnection.ToPortName ?? string.Empty);
                    expandedSubGraph.AddOutputSource(
                        nestedConnection.ToPortName,
                        ExpandedSourceEndpoint.CreatePassthrough(nestedConnection.FromPortName, nestedConnection.CastMode));
                    continue;
                }

                if (fromInputBoundary)
                {
                    string mappedTargetNodeId;
                    if (!internalNodeIdMap.TryGetValue(nestedConnection.ToNodeId ?? string.Empty, out mappedTargetNodeId))
                    {
                        diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Sub-graph input boundary connection targets an unknown internal node.", subGraphNode.NodeId, nestedConnection.FromPortName));
                        success = false;
                        continue;
                    }

                    expandedSubGraph.AddInputTarget(
                        nestedConnection.FromPortName,
                        new ExpandedTargetEndpoint(mappedTargetNodeId, nestedConnection.ToPortName, nestedConnection.CastMode));
                    continue;
                }

                if (toOutputBoundary)
                {
                    string mappedSourceNodeId;
                    if (!internalNodeIdMap.TryGetValue(nestedConnection.FromNodeId ?? string.Empty, out mappedSourceNodeId))
                    {
                        diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Sub-graph output boundary connection reads from an unknown internal node.", subGraphNode.NodeId, nestedConnection.ToPortName));
                        success = false;
                        continue;
                    }

                    producedOutputPorts.Add(nestedConnection.ToPortName ?? string.Empty);
                    expandedSubGraph.AddOutputSource(
                        nestedConnection.ToPortName,
                        ExpandedSourceEndpoint.CreateInternal(mappedSourceNodeId, nestedConnection.FromPortName, nestedConnection.CastMode));
                    continue;
                }

                string mappedFromNodeId;
                string mappedToNodeId;
                if (!internalNodeIdMap.TryGetValue(nestedConnection.FromNodeId ?? string.Empty, out mappedFromNodeId) ||
                    !internalNodeIdMap.TryGetValue(nestedConnection.ToNodeId ?? string.Empty, out mappedToNodeId))
                {
                    diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Sub-graph contains a connection that references an unknown node.", subGraphNode.NodeId, null));
                    success = false;
                    continue;
                }

                expandedParent.Connections.Add(CloneConnectionData(nestedConnection, mappedFromNodeId, mappedToNodeId));
            }

            return success;
        }

        private static bool TryExpandParentSubGraphConnection(
            IReadOnlyList<GenConnectionData> parentConnections,
            IReadOnlyDictionary<string, ExpandedSubGraphBoundary> expandedSubGraphs,
            GenGraph expandedParent,
            GenConnectionData parentConnection,
            List<GraphDiagnostic> diagnostics)
        {
            List<ExpandedSourceEndpoint> sources = ResolveParentConnectionSources(
                parentConnections,
                expandedSubGraphs,
                parentConnection,
                diagnostics,
                new HashSet<string>(StringComparer.Ordinal));
            List<ExpandedTargetEndpoint> targets = ResolveParentConnectionTargets(expandedSubGraphs, parentConnection, diagnostics);

            if (sources.Count == 0 || targets.Count == 0)
            {
                return false;
            }

            for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                ExpandedSourceEndpoint source = sources[sourceIndex];
                if (source.IsPassthrough)
                {
                    diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Sub-graph output port '" + parentConnection.FromPortName + "' resolves to an unexpanded passthrough input.", parentConnection.FromNodeId, parentConnection.FromPortName));
                    return false;
                }

                for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
                {
                    ExpandedTargetEndpoint target = targets[targetIndex];
                    GenConnectionData expandedConnection = new GenConnectionData(
                        source.NodeId,
                        source.PortName,
                        target.NodeId,
                        target.PortName);
                    expandedConnection.CastMode = ResolveBoundaryCast(parentConnection.CastMode, ResolveBoundaryCast(source.CastMode, target.CastMode));
                    expandedParent.Connections.Add(expandedConnection);
                }
            }

            return true;
        }

        private static List<ExpandedSourceEndpoint> ResolveParentConnectionSources(
            IReadOnlyList<GenConnectionData> parentConnections,
            IReadOnlyDictionary<string, ExpandedSubGraphBoundary> expandedSubGraphs,
            GenConnectionData parentConnection,
            List<GraphDiagnostic> diagnostics,
            HashSet<string> resolvingPassthroughInputs)
        {
            ExpandedSubGraphBoundary expandedSubGraph;
            if (!expandedSubGraphs.TryGetValue(parentConnection.FromNodeId ?? string.Empty, out expandedSubGraph))
            {
                return new List<ExpandedSourceEndpoint>
                {
                    ExpandedSourceEndpoint.CreateInternal(parentConnection.FromNodeId, parentConnection.FromPortName, CastMode.None)
                };
            }

            IReadOnlyList<ExpandedSourceEndpoint> wrapperSources = expandedSubGraph.GetOutputSources(parentConnection.FromPortName);
            if (wrapperSources.Count == 0)
            {
                diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Sub-graph output port '" + parentConnection.FromPortName + "' is connected in the parent graph but is not wired inside the nested graph.", parentConnection.FromNodeId, parentConnection.FromPortName));
                return new List<ExpandedSourceEndpoint>();
            }

            List<ExpandedSourceEndpoint> resolvedSources = new List<ExpandedSourceEndpoint>();
            for (int sourceIndex = 0; sourceIndex < wrapperSources.Count; sourceIndex++)
            {
                ExpandedSourceEndpoint wrapperSource = wrapperSources[sourceIndex];
                if (!wrapperSource.IsPassthrough)
                {
                    resolvedSources.Add(wrapperSource.WithCast(ResolveBoundaryCast(parentConnection.CastMode, wrapperSource.CastMode)));
                    continue;
                }

                string passthroughKey = (parentConnection.FromNodeId ?? string.Empty) + "\n" + (wrapperSource.PassthroughInputPort ?? string.Empty);
                if (!resolvingPassthroughInputs.Add(passthroughKey))
                {
                    diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Recursive sub-graph passthrough boundary detected.", parentConnection.FromNodeId, parentConnection.FromPortName));
                    continue;
                }

                bool foundIncoming = false;
                for (int connectionIndex = 0; connectionIndex < parentConnections.Count; connectionIndex++)
                {
                    GenConnectionData incomingConnection = parentConnections[connectionIndex];
                    if (incomingConnection == null ||
                        !string.Equals(incomingConnection.ToNodeId, parentConnection.FromNodeId, StringComparison.Ordinal) ||
                        !string.Equals(incomingConnection.ToPortName, wrapperSource.PassthroughInputPort, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    foundIncoming = true;
                    List<ExpandedSourceEndpoint> incomingSources = ResolveParentConnectionSources(
                        parentConnections,
                        expandedSubGraphs,
                        incomingConnection,
                        diagnostics,
                        resolvingPassthroughInputs);
                    for (int incomingIndex = 0; incomingIndex < incomingSources.Count; incomingIndex++)
                    {
                        ExpandedSourceEndpoint incomingSource = incomingSources[incomingIndex];
                        resolvedSources.Add(incomingSource.WithCast(ResolveBoundaryCast(parentConnection.CastMode, ResolveBoundaryCast(wrapperSource.CastMode, incomingSource.CastMode))));
                    }
                }

                if (!foundIncoming)
                {
                    diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Sub-graph passthrough output port '" + parentConnection.FromPortName + "' has no parent input connection for '" + wrapperSource.PassthroughInputPort + "'.", parentConnection.FromNodeId, parentConnection.FromPortName));
                }

                resolvingPassthroughInputs.Remove(passthroughKey);
            }

            return resolvedSources;
        }

        private static List<ExpandedTargetEndpoint> ResolveParentConnectionTargets(
            IReadOnlyDictionary<string, ExpandedSubGraphBoundary> expandedSubGraphs,
            GenConnectionData parentConnection,
            List<GraphDiagnostic> diagnostics)
        {
            ExpandedSubGraphBoundary expandedSubGraph;
            if (!expandedSubGraphs.TryGetValue(parentConnection.ToNodeId ?? string.Empty, out expandedSubGraph))
            {
                return new List<ExpandedTargetEndpoint>
                {
                    new ExpandedTargetEndpoint(parentConnection.ToNodeId, parentConnection.ToPortName, CastMode.None)
                };
            }

            IReadOnlyList<ExpandedTargetEndpoint> targets = expandedSubGraph.GetInputTargets(parentConnection.ToPortName);
            if (targets.Count == 0)
            {
                diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Sub-graph input port '" + parentConnection.ToPortName + "' is connected in the parent graph but is not wired inside the nested graph.", parentConnection.ToNodeId, parentConnection.ToPortName));
                return new List<ExpandedTargetEndpoint>();
            }

            List<ExpandedTargetEndpoint> resolvedTargets = new List<ExpandedTargetEndpoint>(targets.Count);
            for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
            {
                ExpandedTargetEndpoint target = targets[targetIndex];
                resolvedTargets.Add(new ExpandedTargetEndpoint(target.NodeId, target.PortName, ResolveBoundaryCast(parentConnection.CastMode, target.CastMode)));
            }

            return resolvedTargets;
        }

        private static void AddIncomingBoundaryConnections(GenGraph expandedParent, IReadOnlyList<GenConnectionData> incomingParentConnections, GenConnectionData nestedConnection, string mappedTargetNodeId)
        {
            for (int connectionIndex = 0; connectionIndex < incomingParentConnections.Count; connectionIndex++)
            {
                GenConnectionData parentConnection = incomingParentConnections[connectionIndex];
                if (!string.Equals(parentConnection.ToPortName, nestedConnection.FromPortName, StringComparison.Ordinal))
                {
                    continue;
                }

                GenConnectionData expandedConnection = new GenConnectionData(
                    parentConnection.FromNodeId,
                    parentConnection.FromPortName,
                    mappedTargetNodeId,
                    nestedConnection.ToPortName);
                expandedConnection.CastMode = ResolveBoundaryCast(parentConnection.CastMode, nestedConnection.CastMode);
                expandedParent.Connections.Add(expandedConnection);
            }
        }

        private static void AddOutgoingBoundaryConnections(GenGraph expandedParent, IReadOnlyList<GenConnectionData> outgoingParentConnections, GenConnectionData nestedConnection, string mappedSourceNodeId)
        {
            for (int connectionIndex = 0; connectionIndex < outgoingParentConnections.Count; connectionIndex++)
            {
                GenConnectionData parentConnection = outgoingParentConnections[connectionIndex];
                if (!string.Equals(parentConnection.FromPortName, nestedConnection.ToPortName, StringComparison.Ordinal))
                {
                    continue;
                }

                GenConnectionData expandedConnection = new GenConnectionData(
                    mappedSourceNodeId,
                    nestedConnection.FromPortName,
                    parentConnection.ToNodeId,
                    parentConnection.ToPortName);
                expandedConnection.CastMode = ResolveBoundaryCast(parentConnection.CastMode, nestedConnection.CastMode);
                expandedParent.Connections.Add(expandedConnection);
            }
        }

        private static void AddPassthroughBoundaryConnections(GenGraph expandedParent, IReadOnlyList<GenConnectionData> incomingParentConnections, IReadOnlyList<GenConnectionData> outgoingParentConnections, GenConnectionData nestedConnection)
        {
            for (int incomingIndex = 0; incomingIndex < incomingParentConnections.Count; incomingIndex++)
            {
                GenConnectionData incomingConnection = incomingParentConnections[incomingIndex];
                if (!string.Equals(incomingConnection.ToPortName, nestedConnection.FromPortName, StringComparison.Ordinal))
                {
                    continue;
                }

                for (int outgoingIndex = 0; outgoingIndex < outgoingParentConnections.Count; outgoingIndex++)
                {
                    GenConnectionData outgoingConnection = outgoingParentConnections[outgoingIndex];
                    if (!string.Equals(outgoingConnection.FromPortName, nestedConnection.ToPortName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    GenConnectionData expandedConnection = new GenConnectionData(
                        incomingConnection.FromNodeId,
                        incomingConnection.FromPortName,
                        outgoingConnection.ToNodeId,
                        outgoingConnection.ToPortName);
                    expandedConnection.CastMode = ResolveBoundaryCast(outgoingConnection.CastMode, incomingConnection.CastMode);
                    expandedParent.Connections.Add(expandedConnection);
                }
            }
        }

        private static CastMode ResolveBoundaryCast(CastMode parentCastMode, CastMode nestedCastMode)
        {
            return parentCastMode != CastMode.None ? parentCastMode : nestedCastMode;
        }

        private static bool ContainsSubGraphNode(GenGraph graph)
        {
            List<GenNodeData> nodes = graph != null ? graph.Nodes : null;
            if (nodes == null)
            {
                return false;
            }

            for (int nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
            {
                if (IsSubGraphNodeData(nodes[nodeIndex]))
                {
                    return true;
                }
            }

            return false;
        }

        private static GenGraph CreateEmptyExpandedGraph(GenGraph source)
        {
            GenGraph graph = ScriptableObject.CreateInstance<GenGraph>();
            graph.SchemaVersion = source.SchemaVersion;
            graph.WorldWidth = source.WorldWidth;
            graph.WorldHeight = source.WorldHeight;
            graph.DefaultSeed = source.DefaultSeed;
            graph.DefaultSeedMode = source.DefaultSeedMode;
            graph.MaxValidationRetries = source.MaxValidationRetries;
            graph.Biome = source.Biome;
            graph.TileSemanticRegistry = source.TileSemanticRegistry;
            graph.PromoteBlackboardToParentScope = source.PromoteBlackboardToParentScope;
            graph.ExposedProperties = source.ExposedProperties;
            return graph;
        }

        private static bool TryAddExpandedNode(GenGraph graph, GenNodeData node, List<GraphDiagnostic> diagnostics)
        {
            if (node == null)
            {
                return true;
            }

            if (graph.GetNode(node.NodeId) != null)
            {
                diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Duplicate node ID '" + node.NodeId + "' detected after sub-graph expansion.", node.NodeId, null));
                return false;
            }

            graph.Nodes.Add(node);
            return true;
        }

        private static GenGraph ResolveNestedGraph(GenNodeData nodeData)
        {
            GenGraph graphFromNodeInstance = ResolveNestedGraphFromNodeInstance(nodeData);
            if (graphFromNodeInstance != null)
            {
                return graphFromNodeInstance;
            }

            List<SerializedParameter> parameters = nodeData != null ? nodeData.Parameters : null;
            if (parameters == null)
            {
                return null;
            }

            for (int parameterIndex = 0; parameterIndex < parameters.Count; parameterIndex++)
            {
                SerializedParameter parameter = parameters[parameterIndex];
                if (parameter != null &&
                    string.Equals(parameter.Name, SubGraphNode.NestedGraphParameterName, StringComparison.OrdinalIgnoreCase))
                {
                    GenGraph nestedGraph = ResolveGraphReference(parameter);
                    if (nestedGraph != null)
                    {
                        return nestedGraph;
                    }
                }
            }

            return null;
        }

        private static GenGraph ResolveNestedGraphFromNodeInstance(GenNodeData nodeData)
        {
            if (nodeData == null)
            {
                return null;
            }

            string errorMessage;
            IGenNode nodeInstance;
            if (!GraphNodeInstantiationUtility.TryInstantiateNode(typeof(SubGraphNode), nodeData, out nodeInstance, out errorMessage))
            {
                return null;
            }

            SubGraphNode subGraphNode = nodeInstance as SubGraphNode;
            return subGraphNode != null ? subGraphNode.NestedGraph : null;
        }

        private static GenGraph ResolveGraphReference(SerializedParameter parameter)
        {
            if (parameter == null)
            {
                return null;
            }

            GenGraph referencedGraph = parameter.ObjectReference as GenGraph;
            if (referencedGraph != null)
            {
                return referencedGraph;
            }

#if UNITY_EDITOR
            string assetPath = ResolveGraphAssetPath(parameter.Value);
            if (!string.IsNullOrWhiteSpace(assetPath))
            {
                GenGraph loadedGraph = AssetDatabase.LoadAssetAtPath<GenGraph>(assetPath);
                if (loadedGraph != null)
                {
                    return loadedGraph;
                }

                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                return AssetDatabase.LoadAssetAtPath<GenGraph>(assetPath);
            }
#endif

            return null;
        }

#if UNITY_EDITOR
        private static string ResolveGraphAssetPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string trimmedValue = value.Trim();
            string guidPath = AssetDatabase.GUIDToAssetPath(trimmedValue);
            if (!string.IsNullOrWhiteSpace(guidPath))
            {
                return guidPath;
            }

            return string.Empty;
        }
#endif

        private static List<GenConnectionData> FindConnectionsToNode(IReadOnlyList<GenConnectionData> connections, string nodeId)
        {
            List<GenConnectionData> result = new List<GenConnectionData>();
            string safeNodeId = nodeId ?? string.Empty;
            for (int connectionIndex = 0; connectionIndex < connections.Count; connectionIndex++)
            {
                GenConnectionData connection = connections[connectionIndex];
                if (connection != null && string.Equals(connection.ToNodeId, safeNodeId, StringComparison.Ordinal))
                {
                    result.Add(connection);
                }
            }

            return result;
        }

        private static List<GenConnectionData> FindConnectionsFromNode(IReadOnlyList<GenConnectionData> connections, string nodeId)
        {
            List<GenConnectionData> result = new List<GenConnectionData>();
            string safeNodeId = nodeId ?? string.Empty;
            for (int connectionIndex = 0; connectionIndex < connections.Count; connectionIndex++)
            {
                GenConnectionData connection = connections[connectionIndex];
                if (connection != null && string.Equals(connection.FromNodeId, safeNodeId, StringComparison.Ordinal))
                {
                    result.Add(connection);
                }
            }

            return result;
        }

        private static GenNodeData CloneNodeData(GenNodeData source, string nodeId)
        {
            GenNodeData clone = new GenNodeData(
                nodeId ?? string.Empty,
                source.NodeTypeName,
                source.NodeName,
                source.Position);

            clone.Ports = ClonePorts(source.Ports);
            clone.Parameters = CloneParameters(source.Parameters);
            return clone;
        }

        private static List<GenPortData> ClonePorts(IReadOnlyList<GenPortData> source)
        {
            List<GenPortData> ports = new List<GenPortData>();
            if (source == null)
            {
                return ports;
            }

            for (int portIndex = 0; portIndex < source.Count; portIndex++)
            {
                GenPortData port = source[portIndex];
                if (port != null)
                {
                    ports.Add(new GenPortData(port.PortName, port.Direction, port.Type, port.DisplayName));
                }
            }

            return ports;
        }

        private static List<SerializedParameter> CloneParameters(IReadOnlyList<SerializedParameter> source)
        {
            List<SerializedParameter> parameters = new List<SerializedParameter>();
            if (source == null)
            {
                return parameters;
            }

            for (int parameterIndex = 0; parameterIndex < source.Count; parameterIndex++)
            {
                SerializedParameter parameter = source[parameterIndex];
                if (parameter != null)
                {
                    parameters.Add(new SerializedParameter(parameter.Name, parameter.Value, parameter.ObjectReference));
                }
            }

            return parameters;
        }

        private static GenConnectionData CloneConnectionData(GenConnectionData source, string fromNodeId, string toNodeId)
        {
            GenConnectionData clone = new GenConnectionData(
                fromNodeId,
                source.FromPortName,
                toNodeId,
                source.ToPortName);
            clone.CastMode = source.CastMode;
            return clone;
        }

        private static bool IsSubGraphNodeData(GenNodeData nodeData)
        {
            return nodeData != null && string.Equals(nodeData.NodeTypeName, typeof(SubGraphNode).FullName, StringComparison.Ordinal);
        }

        private static bool IsSubGraphInputNodeData(GenNodeData nodeData)
        {
            return nodeData != null && string.Equals(nodeData.NodeTypeName, typeof(SubGraphInputNode).FullName, StringComparison.Ordinal);
        }

        private static bool IsSubGraphOutputNodeData(GenNodeData nodeData)
        {
            return nodeData != null && string.Equals(nodeData.NodeTypeName, typeof(SubGraphOutputNode).FullName, StringComparison.Ordinal);
        }

        private static GenNodeData FindOutputNodeData(IReadOnlyList<GenNodeData> nodeDataList, List<GraphDiagnostic> diagnostics, bool requireOutputNode)
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

            if (requireOutputNode && outputNodeData == null)
            {
                diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Graph must contain an output node.", null, null));
            }

            return outputNodeData;
        }

        private static HashSet<string> BuildReachableNodeIds(string outputNodeId, IReadOnlyList<GenNodeData> nodeDataList, IReadOnlyList<GenConnectionData> connectionDataList)
        {
            HashSet<string> reachableNodeIds = new HashSet<string>(StringComparer.Ordinal);
            string resolvedOutputNodeId = outputNodeId ?? string.Empty;
            Queue<string> pendingNodeIds = new Queue<string>();

            if (!string.IsNullOrWhiteSpace(resolvedOutputNodeId))
            {
                reachableNodeIds.Add(resolvedOutputNodeId);
                pendingNodeIds.Enqueue(resolvedOutputNodeId);
            }

            EnqueueSharedChannelRoots(nodeDataList, reachableNodeIds, pendingNodeIds);

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

        private static HashSet<string> BuildPreviewReachableNodeIds(IReadOnlyList<GenNodeData> nodeDataList, IReadOnlyList<GenConnectionData> connectionDataList)
        {
            HashSet<string> reachableNodeIds = new HashSet<string>(StringComparer.Ordinal);
            Queue<string> pendingNodeIds = new Queue<string>();

            EnqueueSharedChannelRoots(nodeDataList, reachableNodeIds, pendingNodeIds);
            EnqueueStandalonePreviewRoots(nodeDataList, reachableNodeIds);

            int connectionIndex;
            for (connectionIndex = 0; connectionIndex < connectionDataList.Count; connectionIndex++)
            {
                GenConnectionData connectionData = connectionDataList[connectionIndex];
                if (connectionData == null)
                {
                    continue;
                }

                string fromNodeId = connectionData.FromNodeId ?? string.Empty;
                string toNodeId = connectionData.ToNodeId ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(fromNodeId))
                {
                    reachableNodeIds.Add(fromNodeId);
                }

                if (!string.IsNullOrWhiteSpace(toNodeId))
                {
                    reachableNodeIds.Add(toNodeId);
                }
            }

            return reachableNodeIds;
        }

        private static void EnqueueStandalonePreviewRoots(IReadOnlyList<GenNodeData> nodeDataList, HashSet<string> reachableNodeIds)
        {
            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < nodeDataList.Count; nodeIndex++)
            {
                GenNodeData nodeData = nodeDataList[nodeIndex];
                if (nodeData == null ||
                    string.IsNullOrWhiteSpace(nodeData.NodeId) ||
                    HasAnyInputPorts(nodeData.Ports))
                {
                    continue;
                }

                reachableNodeIds.Add(nodeData.NodeId);
            }
        }

        private static void EnqueueSharedChannelRoots(IReadOnlyList<GenNodeData> nodeDataList, HashSet<string> reachableNodeIds, Queue<string> pendingNodeIds)
        {
            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < nodeDataList.Count; nodeIndex++)
            {
                GenNodeData nodeData = nodeDataList[nodeIndex];
                if (nodeData == null ||
                    string.IsNullOrWhiteSpace(nodeData.NodeId) ||
                    (!WritesBiomeChannel(nodeData.Ports) &&
                     !WritesLogicalIdChannel(nodeData.Ports) &&
                     !WritesPrefabPlacementChannel(nodeData.Ports)))
                {
                    continue;
                }

                if (reachableNodeIds.Add(nodeData.NodeId))
                {
                    pendingNodeIds.Enqueue(nodeData.NodeId);
                }
            }
        }

        private static bool HasAnyInputPorts(IReadOnlyList<GenPortData> ports)
        {
            if (ports == null)
            {
                return false;
            }

            int portIndex;
            for (portIndex = 0; portIndex < ports.Count; portIndex++)
            {
                GenPortData port = ports[portIndex];
                if (port != null && port.Direction == PortDirection.Input)
                {
                    return true;
                }
            }

            return false;
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

                Type nodeType = GraphNodeInstantiationUtility.ResolveNodeType(nodeData.NodeTypeName);
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
                if (!GraphNodeInstantiationUtility.TryInstantiateNode(nodeType, nodeData, out nodeInstance, out instantiationError))
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
            string errorMessage;
            if (!GraphNodeInstantiationUtility.TryApplyParameters(nodeInstance, nodeData, out errorMessage))
            {
                diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, errorMessage, nodeData.NodeId, null));
                return false;
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
                AddPortAlias(portsByName, port.DisplayName, port);

                if (port.Direction == PortDirection.Output)
                {
                    AddPortAlias(portsByName, GraphPortNameUtility.CreateGeneratedOutputPortName(nodeInstance.NodeId, port.DisplayName), port);
                    AddPortAlias(portsByName, GraphPortNameUtility.CreateGeneratedOutputPortName(nodeInstance.NodeId, port.Name), port);
                }
            }

            return portsByName;
        }

        private static void AddPortAlias(Dictionary<string, NodePortDefinition> portsByName, string alias, NodePortDefinition port)
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                return;
            }

            if (portsByName.ContainsKey(alias))
            {
                return;
            }

            portsByName.Add(alias, port);
        }

        private static ConnectionValidationResult ValidateConnections(IReadOnlyList<GenConnectionData> connectionDataList, List<GraphDiagnostic> diagnostics, IReadOnlyDictionary<string, CompiledNodeInfo> nodesById)
        {
            List<ValidatedConnection> validatedConnections = new List<ValidatedConnection>(connectionDataList.Count);
            Dictionary<string, int> incomingCountsByPortKey = new Dictionary<string, int>(StringComparer.Ordinal);
            int implicitCastIndex = 0;

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

                CastMode defaultMode = CastMode.None;

                if (fromPort.Type != toPort.Type)
                {
                    if (!CastRegistry.CanCast(fromPort.Type, toPort.Type, out defaultMode))
                    {
                        diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Connection from '" + fromNode.Node.NodeName + "." + fromPort.Name + "' to '" + toNode.Node.NodeName + "." + toPort.Name + "' is type-incompatible.", toNode.Node.NodeId, toPort.Name));
                        continue;
                    }
                }

                string targetPortKey = CreatePortKey(toNode.Node.NodeId, toPort.Name);
                int existingIncomingCount;
                incomingCountsByPortKey.TryGetValue(targetPortKey, out existingIncomingCount);

                if (existingIncomingCount >= 1 && toPort.Capacity != PortCapacity.Multi)
                {
                    diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, "Input port '" + toPort.Name + "' on node '" + toNode.Node.NodeName + "' does not allow multiple incoming connections.", toNode.Node.NodeId, toPort.Name));
                    continue;
                }

                CastMode castMode;
                string castChannelName;

                if (fromPort.Type == toPort.Type)
                {
                    castMode = CastMode.None;
                    castChannelName = null;
                }
                else
                {
                    castMode = (connectionData.CastMode == CastMode.None) ? defaultMode : connectionData.CastMode;
                    connectionData.CastMode = castMode;
                    castChannelName = "__cast_" + implicitCastIndex.ToString(CultureInfo.InvariantCulture);
                    implicitCastIndex++;
                }

                string sourceChannelName = ResolveSourceChannelName(fromNode, fromPort);
                incomingCountsByPortKey[targetPortKey] = existingIncomingCount + 1;
                validatedConnections.Add(new ValidatedConnection(fromNode, fromPort, sourceChannelName, toNode, toPort, castMode, castChannelName));
            }

            return new ConnectionValidationResult(validatedConnections, incomingCountsByPortKey);
        }

        private static void ApplyInputConnectionBindings(IReadOnlyList<CompiledNodeInfo> compiledNodes, IReadOnlyList<ValidatedConnection> validatedConnections)
        {
            Dictionary<string, Dictionary<string, List<string>>> inputConnectionsByNodeId = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.Ordinal);

            int connectionIndex;
            for (connectionIndex = 0; connectionIndex < validatedConnections.Count; connectionIndex++)
            {
                ValidatedConnection connection = validatedConnections[connectionIndex];
                Dictionary<string, List<string>> nodeConnections;
                if (!inputConnectionsByNodeId.TryGetValue(connection.ToNode.Node.NodeId, out nodeConnections))
                {
                    nodeConnections = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                    inputConnectionsByNodeId.Add(connection.ToNode.Node.NodeId, nodeConnections);
                }

                List<string> portConnections;
                if (!nodeConnections.TryGetValue(connection.ToPort.Name, out portConnections))
                {
                    portConnections = new List<string>();
                    nodeConnections.Add(connection.ToPort.Name, portConnections);
                }

                // For cast connections the downstream node reads from the implicit cast channel.
                // For same-type connections it reads directly from the source port's channel.
                string sourceChannelName = (connection.CastMode != CastMode.None)
                    ? connection.CastChannelName
                    : connection.SourceChannelName;
                portConnections.Add(sourceChannelName);
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

                Dictionary<string, List<string>> nodeConnections;
                if (!inputConnectionsByNodeId.TryGetValue(compiledNode.Node.NodeId, out nodeConnections))
                {
                    nodeConnections = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                }

                InputConnectionMap connectionMap = new InputConnectionMap();
                foreach (KeyValuePair<string, List<string>> nodeConnection in nodeConnections)
                {
                    connectionMap.SetConnections(nodeConnection.Key, nodeConnection.Value);
                }

                inputConnectionReceiver.ReceiveInputConnections(connectionMap);
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
                return connection.CastMode == CastMode.None
                    ? connection.SourceChannelName
                    : connection.CastChannelName;
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

                    if (IsSharedChannel(declaration.Type, declaration.ChannelName))
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

            AddImplicitBiomeChannelOrdering(compiledNodes, validatedConnections, adjacency);
            AddImplicitLogicalIdChannelOrdering(compiledNodes, adjacency);
            AddImplicitPrefabPlacementChannelOrdering(compiledNodes, adjacency);

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

        private static void AddImplicitBiomeChannelOrdering(IReadOnlyList<CompiledNodeInfo> compiledNodes, IReadOnlyList<ValidatedConnection> validatedConnections, Dictionary<string, List<CompiledNodeInfo>> adjacency)
        {
            HashSet<string> nodesWithExplicitBiomeInput = new HashSet<string>(StringComparer.Ordinal);

            int connectionIndex;
            for (connectionIndex = 0; connectionIndex < validatedConnections.Count; connectionIndex++)
            {
                ValidatedConnection connection = validatedConnections[connectionIndex];
                if (connection == null ||
                    connection.ToNode == null ||
                    !string.Equals(connection.ToPort.Name, "Biome Input", StringComparison.Ordinal))
                {
                    continue;
                }

                nodesWithExplicitBiomeInput.Add(connection.ToNode.Node.NodeId);
            }

            CompiledNodeInfo previousBiomeWriter = null;

            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < compiledNodes.Count; nodeIndex++)
            {
                CompiledNodeInfo compiledNode = compiledNodes[nodeIndex];
                if (!WritesBiomeChannel(compiledNode.Node.ChannelDeclarations))
                {
                    continue;
                }

                if (previousBiomeWriter != null &&
                    !nodesWithExplicitBiomeInput.Contains(compiledNode.Node.NodeId))
                {
                    adjacency[previousBiomeWriter.Node.NodeId].Add(compiledNode);
                }

                previousBiomeWriter = compiledNode;
            }
        }

        private static void AddImplicitLogicalIdChannelOrdering(IReadOnlyList<CompiledNodeInfo> compiledNodes, Dictionary<string, List<CompiledNodeInfo>> adjacency)
        {
            CompiledNodeInfo previousLogicalIdWriter = null;

            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < compiledNodes.Count; nodeIndex++)
            {
                CompiledNodeInfo compiledNode = compiledNodes[nodeIndex];
                if (!WritesLogicalIdChannel(compiledNode.Node.ChannelDeclarations))
                {
                    continue;
                }

                if (previousLogicalIdWriter != null)
                {
                    adjacency[previousLogicalIdWriter.Node.NodeId].Add(compiledNode);
                }

                previousLogicalIdWriter = compiledNode;
            }
        }

        private static void AddImplicitPrefabPlacementChannelOrdering(IReadOnlyList<CompiledNodeInfo> compiledNodes, Dictionary<string, List<CompiledNodeInfo>> adjacency)
        {
            CompiledNodeInfo previousPlacementWriter = null;

            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < compiledNodes.Count; nodeIndex++)
            {
                CompiledNodeInfo compiledNode = compiledNodes[nodeIndex];
                if (!WritesPrefabPlacementChannel(compiledNode.Node.ChannelDeclarations))
                {
                    continue;
                }

                if (previousPlacementWriter != null)
                {
                    adjacency[previousPlacementWriter.Node.NodeId].Add(compiledNode);
                }

                previousPlacementWriter = compiledNode;
            }
        }

        private static BiomeAsset[] ResolveBiomeChannelPalette(IReadOnlyList<CompiledNodeInfo> orderedNodes, List<GraphDiagnostic> diagnostics)
        {
            BiomeChannelPalette palette = new BiomeChannelPalette();

            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < orderedNodes.Count; nodeIndex++)
            {
                CompiledNodeInfo compiledNode = orderedNodes[nodeIndex];
                IBiomeChannelNode biomeChannelNode = compiledNode.Node as IBiomeChannelNode;
                if (biomeChannelNode == null)
                {
                    continue;
                }

                string errorMessage;
                if (!biomeChannelNode.ResolveBiomePalette(palette, out errorMessage))
                {
                    diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, errorMessage, compiledNode.Node.NodeId, null));
                }
            }

            return palette.ToArray();
        }

        private static PrefabStampPalette ResolvePrefabPlacementPalette(IReadOnlyList<CompiledNodeInfo> orderedNodes, List<GraphDiagnostic> diagnostics)
        {
            PrefabStampPalette palette = new PrefabStampPalette();

            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < orderedNodes.Count; nodeIndex++)
            {
                CompiledNodeInfo compiledNode = orderedNodes[nodeIndex];
                IPrefabPlacementNode prefabPlacementNode = compiledNode.Node as IPrefabPlacementNode;
                if (prefabPlacementNode == null)
                {
                    continue;
                }

                string errorMessage;
                if (!prefabPlacementNode.ResolvePrefabPalette(palette, out errorMessage))
                {
                    diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, errorMessage, compiledNode.Node.NodeId, null));
                }
            }

            return palette;
        }

        private static bool WritesBiomeChannel(IReadOnlyList<GenPortData> ports)
        {
            if (ports == null)
            {
                return false;
            }

            int portIndex;
            for (portIndex = 0; portIndex < ports.Count; portIndex++)
            {
                GenPortData port = ports[portIndex];
                if (port != null &&
                    port.Direction == PortDirection.Output &&
                    port.Type == ChannelType.Int &&
                    BiomeChannelUtility.IsBiomeChannel(port.PortName))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool WritesBiomeChannel(IReadOnlyList<ChannelDeclaration> channelDeclarations)
        {
            if (channelDeclarations == null)
            {
                return false;
            }

            int declarationIndex;
            for (declarationIndex = 0; declarationIndex < channelDeclarations.Count; declarationIndex++)
            {
                ChannelDeclaration declaration = channelDeclarations[declarationIndex];
                if (declaration.IsWrite &&
                    declaration.Type == ChannelType.Int &&
                    BiomeChannelUtility.IsBiomeChannel(declaration.ChannelName))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool WritesLogicalIdChannel(IReadOnlyList<GenPortData> ports)
        {
            if (ports == null)
            {
                return false;
            }

            int portIndex;
            for (portIndex = 0; portIndex < ports.Count; portIndex++)
            {
                GenPortData port = ports[portIndex];
                if (port != null &&
                    port.Direction == PortDirection.Output &&
                    port.Type == ChannelType.Int &&
                    string.Equals(port.PortName, GraphOutputUtility.OutputInputPortName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool WritesLogicalIdChannel(IReadOnlyList<ChannelDeclaration> channelDeclarations)
        {
            if (channelDeclarations == null)
            {
                return false;
            }

            int declarationIndex;
            for (declarationIndex = 0; declarationIndex < channelDeclarations.Count; declarationIndex++)
            {
                ChannelDeclaration declaration = channelDeclarations[declarationIndex];
                if (declaration.IsWrite &&
                    declaration.Type == ChannelType.Int &&
                    string.Equals(declaration.ChannelName, GraphOutputUtility.OutputInputPortName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool WritesPrefabPlacementChannel(IReadOnlyList<ChannelDeclaration> channelDeclarations)
        {
            if (channelDeclarations == null)
            {
                return false;
            }

            int declarationIndex;
            for (declarationIndex = 0; declarationIndex < channelDeclarations.Count; declarationIndex++)
            {
                ChannelDeclaration declaration = channelDeclarations[declarationIndex];
                if (declaration.IsWrite &&
                    declaration.Type == ChannelType.PrefabPlacementList &&
                    string.Equals(declaration.ChannelName, PrefabPlacementChannelUtility.ChannelName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool WritesPrefabPlacementChannel(IReadOnlyList<GenPortData> ports)
        {
            if (ports == null)
            {
                return false;
            }

            int portIndex;
            for (portIndex = 0; portIndex < ports.Count; portIndex++)
            {
                GenPortData port = ports[portIndex];
                if (port != null &&
                    port.Direction == PortDirection.Output &&
                    port.Type == ChannelType.PrefabPlacementList &&
                    string.Equals(port.PortName, PrefabPlacementChannelUtility.ChannelName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsSharedChannel(ChannelType channelType, string channelName)
        {
            if (channelType == ChannelType.Int)
            {
                return BiomeChannelUtility.IsBiomeChannel(channelName) ||
                       string.Equals(channelName, GraphOutputUtility.OutputInputPortName, StringComparison.Ordinal);
            }

            if (channelType == ChannelType.PrefabPlacementList)
            {
                return string.Equals(channelName, PrefabPlacementChannelUtility.ChannelName, StringComparison.Ordinal);
            }

            return false;
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

        private static Dictionary<string, float> BuildExposedPropertyInitialValues(IReadOnlyList<ExposedProperty> exposedProperties)
        {
            if (exposedProperties == null || exposedProperties.Count == 0)
            {
                return null;
            }

            Dictionary<string, float> initialValues = new Dictionary<string, float>(exposedProperties.Count, StringComparer.Ordinal);

            int index;
            for (index = 0; index < exposedProperties.Count; index++)
            {
                ExposedProperty property = exposedProperties[index];
                if (property == null)
                {
                    continue;
                }

                string runtimeKey = GetExposedPropertyRuntimeKey(property);
                if (string.IsNullOrWhiteSpace(runtimeKey) || initialValues.ContainsKey(runtimeKey))
                {
                    continue;
                }

                float floatValue = 0.0f;
                if (property.Type == ChannelType.Float)
                {
                    float parsedFloat;
                    if (float.TryParse(property.DefaultValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsedFloat))
                    {
                        floatValue = parsedFloat;
                    }
                }
                else if (property.Type == ChannelType.Int)
                {
                    int parsedInt;
                    if (int.TryParse(property.DefaultValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedInt))
                    {
                        floatValue = (float)parsedInt;
                    }
                }

                initialValues[runtimeKey] = floatValue;
            }

            return initialValues.Count > 0 ? initialValues : null;
        }

        private static string GetExposedPropertyRuntimeKey(ExposedProperty property)
        {
            if (property == null)
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(property.PropertyId)
                ? (property.PropertyName ?? string.Empty)
                : property.PropertyId;
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

        private static string ResolveSourceChannelName(CompiledNodeInfo fromNode, NodePortDefinition fromPort)
        {
            IReadOnlyList<ChannelDeclaration> channelDeclarations = fromNode.Node.ChannelDeclarations;

            int declarationIndex;
            for (declarationIndex = 0; declarationIndex < channelDeclarations.Count; declarationIndex++)
            {
                ChannelDeclaration declaration = channelDeclarations[declarationIndex];
                if (!declaration.IsWrite || declaration.Type != fromPort.Type)
                {
                    continue;
                }

                if (GraphPortNameUtility.PortMatchesName(fromNode.Node.NodeId, fromPort, declaration.ChannelName))
                {
                    return declaration.ChannelName;
                }
            }

            return fromPort.Name;
        }

        private static List<CompiledNodeInfo> InsertImplicitCastNodes(List<CompiledNodeInfo> sortedNodes, IReadOnlyList<ValidatedConnection> validatedConnections)
        {
            Dictionary<string, List<ImplicitCastNode>> castNodesBeforeNode = new Dictionary<string, List<ImplicitCastNode>>(StringComparer.Ordinal);

            int connectionIndex;
            for (connectionIndex = 0; connectionIndex < validatedConnections.Count; connectionIndex++)
            {
                ValidatedConnection connection = validatedConnections[connectionIndex];
                if (connection.CastMode == CastMode.None)
                {
                    continue;
                }

                ImplicitCastNode castNode = new ImplicitCastNode(
                    connection.CastChannelName,
                    connection.SourceChannelName,
                    connection.FromPort.Type,
                    connection.CastChannelName,
                    connection.ToPort.Type,
                    connection.CastMode);

                string destNodeId = connection.ToNode.Node.NodeId;
                List<ImplicitCastNode> castNodesForDest;
                if (!castNodesBeforeNode.TryGetValue(destNodeId, out castNodesForDest))
                {
                    castNodesForDest = new List<ImplicitCastNode>();
                    castNodesBeforeNode.Add(destNodeId, castNodesForDest);
                }

                castNodesForDest.Add(castNode);
            }

            if (castNodesBeforeNode.Count == 0)
            {
                return sortedNodes;
            }

            List<CompiledNodeInfo> result = new List<CompiledNodeInfo>(sortedNodes.Count + castNodesBeforeNode.Count);

            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < sortedNodes.Count; nodeIndex++)
            {
                CompiledNodeInfo compiledNode = sortedNodes[nodeIndex];

                List<ImplicitCastNode> castNodesForThis;
                if (castNodesBeforeNode.TryGetValue(compiledNode.Node.NodeId, out castNodesForThis))
                {
                    int castNodeIndex;
                    for (castNodeIndex = 0; castNodeIndex < castNodesForThis.Count; castNodeIndex++)
                    {
                        result.Add(new CompiledNodeInfo(null, castNodesForThis[castNodeIndex], new Dictionary<string, NodePortDefinition>(StringComparer.Ordinal)));
                    }
                }

                result.Add(compiledNode);
            }

            return result;
        }

        private sealed class ImplicitCastNode : IGenNode
        {
            private const int DefaultBatchSize = 64;
            private const string ImplicitCastNodeName = "Implicit Cast";

            private static readonly NodePortDefinition[] _noPorts = Array.Empty<NodePortDefinition>();
            private static readonly BlackboardKey[] _noBlackboardDeclarations = Array.Empty<BlackboardKey>();

            private readonly string _nodeId;
            private readonly string _sourceChannelName;
            private readonly string _outputChannelName;
            private readonly CastMode _castMode;
            private readonly ChannelDeclaration[] _channelDeclarations;

            public IReadOnlyList<NodePortDefinition> Ports
            {
                get
                {
                    return _noPorts;
                }
            }

            public IReadOnlyList<ChannelDeclaration> ChannelDeclarations
            {
                get
                {
                    return _channelDeclarations;
                }
            }

            public IReadOnlyList<BlackboardKey> BlackboardDeclarations
            {
                get
                {
                    return _noBlackboardDeclarations;
                }
            }

            public string NodeId
            {
                get
                {
                    return _nodeId;
                }
            }

            public string NodeName
            {
                get
                {
                    return ImplicitCastNodeName;
                }
            }

            public ImplicitCastNode(string nodeId, string sourceChannelName, ChannelType sourceType, string outputChannelName, ChannelType outputType, CastMode castMode)
            {
                _nodeId = nodeId;
                _sourceChannelName = sourceChannelName;
                _outputChannelName = outputChannelName;
                _castMode = castMode;
                _channelDeclarations = new[]
                {
                    new ChannelDeclaration(sourceChannelName, sourceType, false),
                    new ChannelDeclaration(outputChannelName, outputType, true)
                };
            }

            public JobHandle Schedule(NodeExecutionContext context)
            {
                if (_castMode == CastMode.FloatToIntFloor)
                {
                    NativeArray<float> source = context.GetFloatChannel(_sourceChannelName);
                    NativeArray<int> output = context.GetIntChannel(_outputChannelName);
                    FloatToIntFloorJob floorJob = new FloatToIntFloorJob { Source = source, Output = output };
                    return floorJob.Schedule(source.Length, DefaultBatchSize, context.InputDependency);
                }

                if (_castMode == CastMode.FloatToIntRound)
                {
                    NativeArray<float> source = context.GetFloatChannel(_sourceChannelName);
                    NativeArray<int> output = context.GetIntChannel(_outputChannelName);
                    FloatToIntRoundJob roundJob = new FloatToIntRoundJob { Source = source, Output = output };
                    return roundJob.Schedule(source.Length, DefaultBatchSize, context.InputDependency);
                }

                if (_castMode == CastMode.FloatToBoolMask)
                {
                    NativeArray<float> source = context.GetFloatChannel(_sourceChannelName);
                    NativeArray<byte> output = context.GetBoolMaskChannel(_outputChannelName);
                    FloatToBoolMaskJob boolJob = new FloatToBoolMaskJob { Source = source, Output = output };
                    return boolJob.Schedule(source.Length, DefaultBatchSize, context.InputDependency);
                }

                if (_castMode == CastMode.IntToBoolMask)
                {
                    NativeArray<int> source = context.GetIntChannel(_sourceChannelName);
                    NativeArray<byte> output = context.GetBoolMaskChannel(_outputChannelName);
                    IntToBoolMaskJob intBoolJob = new IntToBoolMaskJob { Source = source, Output = output };
                    return intBoolJob.Schedule(source.Length, DefaultBatchSize, context.InputDependency);
                }

                throw new InvalidOperationException("Implicit cast node has unrecognised cast mode '" + _castMode + "'.");
            }

            [BurstCompile]
            private struct FloatToIntFloorJob : IJobParallelFor
            {
                [ReadOnly]
                public NativeArray<float> Source;

                public NativeArray<int> Output;

                public void Execute(int index)
                {
                    Output[index] = (int)math.floor(Source[index]);
                }
            }

            [BurstCompile]
            private struct FloatToIntRoundJob : IJobParallelFor
            {
                [ReadOnly]
                public NativeArray<float> Source;

                public NativeArray<int> Output;

                // Rounds half toward positive infinity: floor(x + 0.5f).
                // 0.5 rounds to 1, -0.5 rounds to 0.
                public void Execute(int index)
                {
                    Output[index] = (int)math.floor(Source[index] + 0.5f);
                }
            }

            [BurstCompile]
            private struct FloatToBoolMaskJob : IJobParallelFor
            {
                [ReadOnly]
                public NativeArray<float> Source;

                public NativeArray<byte> Output;

                public void Execute(int index)
                {
                    Output[index] = Source[index] > 0.5f ? (byte)1 : (byte)0;
                }
            }

            [BurstCompile]
            private struct IntToBoolMaskJob : IJobParallelFor
            {
                [ReadOnly]
                public NativeArray<int> Source;

                public NativeArray<byte> Output;

                public void Execute(int index)
                {
                    Output[index] = Source[index] != 0 ? (byte)1 : (byte)0;
                }
            }
        }
    }
}
