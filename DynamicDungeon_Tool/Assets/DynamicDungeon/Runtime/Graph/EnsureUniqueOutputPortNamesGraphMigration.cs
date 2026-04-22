using System;
using System.Collections.Generic;
using DynamicDungeon.Runtime.Core;

namespace DynamicDungeon.Runtime.Graph
{
    public sealed class EnsureUniqueOutputPortNamesGraphMigration : IGraphMigration
    {
        public int FromVersion
        {
            get
            {
                return 2;
            }
        }

        public void Migrate(GenGraph graph)
        {
            if (graph == null || graph.Nodes == null)
            {
                return;
            }

            Dictionary<string, string> firstOwnerByPortName = new Dictionary<string, string>(StringComparer.Ordinal);
            HashSet<string> reservedPortNames = new HashSet<string>(StringComparer.Ordinal);

            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < graph.Nodes.Count; nodeIndex++)
            {
                GenNodeData node = graph.Nodes[nodeIndex];
                if (node == null || node.Ports == null)
                {
                    continue;
                }

                int portIndex;
                for (portIndex = 0; portIndex < node.Ports.Count; portIndex++)
                {
                    GenPortData port = node.Ports[portIndex];
                    if (port == null)
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(port.DisplayName))
                    {
                        port.DisplayName = port.PortName ?? string.Empty;
                    }

                    if (GraphOutputUtility.IsOutputNode(node) || port.Direction != PortDirection.Output || string.IsNullOrWhiteSpace(port.PortName))
                    {
                        reservedPortNames.Add(port.PortName ?? string.Empty);
                        continue;
                    }

                    string existingOwnerNodeId;
                    if (!firstOwnerByPortName.TryGetValue(port.PortName, out existingOwnerNodeId))
                    {
                        firstOwnerByPortName.Add(port.PortName, node.NodeId ?? string.Empty);
                        reservedPortNames.Add(port.PortName);
                        continue;
                    }

                    if (string.Equals(existingOwnerNodeId, node.NodeId, StringComparison.Ordinal))
                    {
                        reservedPortNames.Add(port.PortName);
                        continue;
                    }

                    string originalPortName = port.PortName;
                    string preferredDisplayName = string.IsNullOrWhiteSpace(port.DisplayName) ? originalPortName : port.DisplayName;
                    string candidatePortName = GraphPortNameUtility.CreateGeneratedOutputPortName(node.NodeId, preferredDisplayName);
                    string uniquePortName = EnsureUniquePortName(candidatePortName, reservedPortNames);

                    port.PortName = uniquePortName;
                    reservedPortNames.Add(uniquePortName);
                    RewriteOutgoingConnections(graph.Connections, node.NodeId, originalPortName, uniquePortName);
                }
            }
        }

        private static void RewriteOutgoingConnections(List<GenConnectionData> connections, string nodeId, string originalPortName, string replacementPortName)
        {
            if (connections == null)
            {
                return;
            }

            int connectionIndex;
            for (connectionIndex = 0; connectionIndex < connections.Count; connectionIndex++)
            {
                GenConnectionData connection = connections[connectionIndex];
                if (connection == null)
                {
                    continue;
                }

                if (string.Equals(connection.FromNodeId, nodeId, StringComparison.Ordinal) &&
                    string.Equals(connection.FromPortName, originalPortName, StringComparison.Ordinal))
                {
                    connection.FromPortName = replacementPortName;
                }
            }
        }

        private static string EnsureUniquePortName(string candidatePortName, HashSet<string> reservedPortNames)
        {
            string safeCandidatePortName = string.IsNullOrWhiteSpace(candidatePortName)
                ? GraphPortNameUtility.LegacyGenericOutputDisplayName
                : candidatePortName;

            if (!reservedPortNames.Contains(safeCandidatePortName))
            {
                return safeCandidatePortName;
            }

            int suffix = 2;
            string suffixedCandidatePortName = safeCandidatePortName + "__" + suffix.ToString();
            while (reservedPortNames.Contains(suffixedCandidatePortName))
            {
                suffix++;
                suffixedCandidatePortName = safeCandidatePortName + "__" + suffix.ToString();
            }

            return suffixedCandidatePortName;
        }
    }
}
