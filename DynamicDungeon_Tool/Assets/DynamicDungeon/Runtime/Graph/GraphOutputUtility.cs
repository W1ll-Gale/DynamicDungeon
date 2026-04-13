using System;
using System.Collections.Generic;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Nodes;
using UnityEngine;

namespace DynamicDungeon.Runtime.Graph
{
    public static class GraphOutputUtility
    {
        public const string OutputNodeDisplayName = "Output";
        public const string OutputInputPortName = "LogicalIds";
        public const string LegacyOutputPortName = "LogicalIds";

        public static string OutputNodeTypeName
        {
            get
            {
                return typeof(TilemapOutputNode).FullName;
            }
        }

        public static bool IsOutputNodeType(Type nodeType)
        {
            return nodeType == typeof(TilemapOutputNode);
        }

        public static bool IsOutputNodeTypeName(string nodeTypeName)
        {
            return string.Equals(nodeTypeName, OutputNodeTypeName, StringComparison.Ordinal);
        }

        public static bool IsOutputNode(GenNodeData nodeData)
        {
            return nodeData != null && IsOutputNodeTypeName(nodeData.NodeTypeName);
        }

        public static GenNodeData FindOutputNode(GenGraph graph)
        {
            List<GenNodeData> nodes = graph != null ? graph.Nodes : null;
            if (nodes == null)
            {
                return null;
            }

            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
            {
                GenNodeData nodeData = nodes[nodeIndex];
                if (IsOutputNode(nodeData))
                {
                    return nodeData;
                }
            }

            return null;
        }

        public static int CountOutputNodes(GenGraph graph)
        {
            List<GenNodeData> nodes = graph != null ? graph.Nodes : null;
            if (nodes == null)
            {
                return 0;
            }

            int count = 0;
            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
            {
                if (IsOutputNode(nodes[nodeIndex]))
                {
                    count++;
                }
            }

            return count;
        }

        public static GenNodeData EnsureSingleOutputNode(GenGraph graph, bool tryAutoConnectLegacyOutput)
        {
            if (graph == null)
            {
                return null;
            }

            if (graph.Nodes == null)
            {
                graph.Nodes = new List<GenNodeData>();
            }

            if (graph.Connections == null)
            {
                graph.Connections = new List<GenConnectionData>();
            }

            GenNodeData outputNode = null;
            List<string> duplicateOutputNodeIds = new List<string>();

            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < graph.Nodes.Count; nodeIndex++)
            {
                GenNodeData nodeData = graph.Nodes[nodeIndex];
                if (!IsOutputNode(nodeData))
                {
                    continue;
                }

                if (outputNode == null)
                {
                    outputNode = nodeData;
                }
                else if (!string.IsNullOrWhiteSpace(nodeData.NodeId))
                {
                    duplicateOutputNodeIds.Add(nodeData.NodeId);
                }
            }

            if (outputNode == null)
            {
                outputNode = CreateOutputNode(graph, ResolveDefaultOutputPosition(graph));
                graph.Nodes.Add(outputNode);
            }

            if (duplicateOutputNodeIds.Count > 0)
            {
                RemoveDuplicateOutputNodes(graph, outputNode.NodeId, duplicateOutputNodeIds);
            }

            outputNode.NodeTypeName = OutputNodeTypeName;
            if (string.IsNullOrWhiteSpace(outputNode.NodeName))
            {
                outputNode.NodeName = OutputNodeDisplayName;
            }

            ResetOutputNodePorts(outputNode);

            if (tryAutoConnectLegacyOutput && !HasIncomingConnection(graph, outputNode.NodeId, OutputInputPortName))
            {
                string sourceNodeId;
                string sourcePortName;
                if (TryFindLegacyOutputCandidate(graph, outputNode.NodeId, out sourceNodeId, out sourcePortName))
                {
                    graph.AddConnection(sourceNodeId, sourcePortName, outputNode.NodeId, OutputInputPortName);

                    GenNodeData sourceNode = graph.GetNode(sourceNodeId);
                    if (sourceNode != null && outputNode.Position == Vector2.zero)
                    {
                        outputNode.Position = sourceNode.Position + new Vector2(260.0f, 0.0f);
                    }
                }
            }

            return outputNode;
        }

        public static bool TryUpgradeToCurrentSchema(GenGraph graph, out bool changed, out string errorMessage)
        {
            changed = false;
            errorMessage = null;

            if (graph == null)
            {
                errorMessage = "Graph cannot be null.";
                return false;
            }

            MigrationResult migrationResult = GraphMigrationRunner.RunMigrations(graph, DefaultGraphMigrations.All);
            if (!migrationResult.Success)
            {
                errorMessage = migrationResult.ErrorMessage;
                return false;
            }

            changed = migrationResult.FromVersion != migrationResult.ToVersion;
            return true;
        }

        private static GenNodeData CreateOutputNode(GenGraph graph, Vector2 position)
        {
            GenNodeData nodeData = new GenNodeData(GenerateOutputNodeId(graph), OutputNodeTypeName, OutputNodeDisplayName, position);
            ResetOutputNodePorts(nodeData);
            return nodeData;
        }

        private static string GenerateOutputNodeId(GenGraph graph)
        {
            string candidateId = "graph-output-node";
            if (graph.GetNode(candidateId) == null)
            {
                return candidateId;
            }

            return Guid.NewGuid().ToString();
        }

        private static void ResetOutputNodePorts(GenNodeData nodeData)
        {
            if (nodeData.Ports == null)
            {
                nodeData.Ports = new List<GenPortData>();
            }
            else
            {
                nodeData.Ports.Clear();
            }

            nodeData.Ports.Add(new GenPortData(OutputInputPortName, PortDirection.Input, ChannelType.Int));
        }

        private static Vector2 ResolveDefaultOutputPosition(GenGraph graph)
        {
            float maxX = 0.0f;
            float y = 0.0f;
            bool foundNode = false;

            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < graph.Nodes.Count; nodeIndex++)
            {
                GenNodeData nodeData = graph.Nodes[nodeIndex];
                if (nodeData == null || IsOutputNode(nodeData))
                {
                    continue;
                }

                foundNode = true;
                if (nodeData.Position.x > maxX)
                {
                    maxX = nodeData.Position.x;
                    y = nodeData.Position.y;
                }
            }

            return foundNode ? new Vector2(maxX + 260.0f, y) : new Vector2(600.0f, 0.0f);
        }

        private static bool HasIncomingConnection(GenGraph graph, string nodeId, string portName)
        {
            int connectionIndex;
            for (connectionIndex = 0; connectionIndex < graph.Connections.Count; connectionIndex++)
            {
                GenConnectionData connectionData = graph.Connections[connectionIndex];
                if (connectionData == null)
                {
                    continue;
                }

                if (string.Equals(connectionData.ToNodeId, nodeId, StringComparison.Ordinal) &&
                    string.Equals(connectionData.ToPortName, portName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryFindLegacyOutputCandidate(GenGraph graph, string outputNodeId, out string sourceNodeId, out string sourcePortName)
        {
            sourceNodeId = null;
            sourcePortName = null;

            int candidateCount = 0;

            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < graph.Nodes.Count; nodeIndex++)
            {
                GenNodeData nodeData = graph.Nodes[nodeIndex];
                if (nodeData == null || IsOutputNode(nodeData) || nodeData.Ports == null)
                {
                    continue;
                }

                int portIndex;
                for (portIndex = 0; portIndex < nodeData.Ports.Count; portIndex++)
                {
                    GenPortData portData = nodeData.Ports[portIndex];
                    if (portData == null ||
                        portData.Direction != PortDirection.Output ||
                        portData.Type != ChannelType.Int ||
                        !string.Equals(portData.PortName, LegacyOutputPortName, StringComparison.Ordinal) ||
                        HasOutgoingConnection(graph, nodeData.NodeId, portData.PortName, outputNodeId))
                    {
                        continue;
                    }

                    sourceNodeId = nodeData.NodeId;
                    sourcePortName = portData.PortName;
                    candidateCount++;
                }
            }

            if (candidateCount == 1)
            {
                return true;
            }

            sourceNodeId = null;
            sourcePortName = null;
            return false;
        }

        private static bool HasOutgoingConnection(GenGraph graph, string nodeId, string portName, string outputNodeId)
        {
            int connectionIndex;
            for (connectionIndex = 0; connectionIndex < graph.Connections.Count; connectionIndex++)
            {
                GenConnectionData connectionData = graph.Connections[connectionIndex];
                if (connectionData == null)
                {
                    continue;
                }

                if (!string.Equals(connectionData.FromNodeId, nodeId, StringComparison.Ordinal) ||
                    !string.Equals(connectionData.FromPortName, portName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.Equals(connectionData.ToNodeId, outputNodeId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void RemoveDuplicateOutputNodes(GenGraph graph, string primaryOutputNodeId, IReadOnlyList<string> duplicateOutputNodeIds)
        {
            int connectionIndex;
            for (connectionIndex = graph.Connections.Count - 1; connectionIndex >= 0; connectionIndex--)
            {
                GenConnectionData connectionData = graph.Connections[connectionIndex];
                if (connectionData == null)
                {
                    continue;
                }

                int duplicateIndex;
                for (duplicateIndex = 0; duplicateIndex < duplicateOutputNodeIds.Count; duplicateIndex++)
                {
                    string duplicateNodeId = duplicateOutputNodeIds[duplicateIndex];
                    if (string.Equals(connectionData.FromNodeId, duplicateNodeId, StringComparison.Ordinal) ||
                        string.Equals(connectionData.ToNodeId, duplicateNodeId, StringComparison.Ordinal))
                    {
                        graph.Connections.RemoveAt(connectionIndex);
                        break;
                    }
                }
            }

            int nodeIndex;
            for (nodeIndex = graph.Nodes.Count - 1; nodeIndex >= 0; nodeIndex--)
            {
                GenNodeData nodeData = graph.Nodes[nodeIndex];
                if (nodeData == null ||
                    !IsOutputNode(nodeData) ||
                    string.Equals(nodeData.NodeId, primaryOutputNodeId, StringComparison.Ordinal))
                {
                    continue;
                }

                graph.Nodes.RemoveAt(nodeIndex);
            }
        }
    }
}
