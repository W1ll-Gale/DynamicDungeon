using System;
using System.Collections.Generic;
using System.Globalization;
using DynamicDungeon.Runtime.Core;

namespace DynamicDungeon.Runtime.Graph
{
    public sealed class MigrateBlackboardNodesToExposedPropertiesGraphMigration : IGraphMigration
    {
        private const string LegacyBlackboardReaderTypeName = "DynamicDungeon.Runtime.Core.BlackboardReaderNode";
        private const string LegacyBlackboardWriterTypeName = "DynamicDungeon.Runtime.Core.BlackboardWriterNode";

        public int FromVersion
        {
            get
            {
                return 3;
            }
        }

        public void Migrate(GenGraph graph)
        {
            if (graph == null)
            {
                return;
            }

            if (graph.Nodes == null)
            {
                graph.Nodes = new List<GenNodeData>();
            }

            if (graph.Connections == null)
            {
                graph.Connections = new List<GenConnectionData>();
            }

            if (graph.ExposedProperties == null)
            {
                graph.ExposedProperties = new List<ExposedProperty>();
            }

            EnsureExposedPropertyIds(graph.ExposedProperties);

            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < graph.Nodes.Count; nodeIndex++)
            {
                GenNodeData nodeData = graph.Nodes[nodeIndex];
                if (nodeData == null)
                {
                    continue;
                }

                if (string.Equals(nodeData.NodeTypeName, LegacyBlackboardWriterTypeName, StringComparison.Ordinal))
                {
                    MigrateWriterNode(graph, nodeData);
                    continue;
                }

                if (string.Equals(nodeData.NodeTypeName, LegacyBlackboardReaderTypeName, StringComparison.Ordinal))
                {
                    MigrateReaderNode(graph, nodeData);
                }
            }

            for (nodeIndex = graph.Nodes.Count - 1; nodeIndex >= 0; nodeIndex--)
            {
                GenNodeData nodeData = graph.Nodes[nodeIndex];
                if (nodeData != null &&
                    string.Equals(nodeData.NodeTypeName, LegacyBlackboardWriterTypeName, StringComparison.Ordinal))
                {
                    graph.RemoveNode(nodeData.NodeId);
                }
            }
        }

        private static void MigrateReaderNode(GenGraph graph, GenNodeData nodeData)
        {
            string propertyName = GetParameterValue(nodeData, "key");
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                propertyName = nodeData.NodeName ?? "Property";
            }

            ExposedProperty property = GetOrCreateProperty(graph, propertyName, ChannelType.Float, "0");
            string legacyOutputPortName = FindLegacyOutputPortName(nodeData);

            ExposedPropertyNodeUtility.ConfigureNodeData(nodeData, property);
            RewriteOutgoingConnections(graph.Connections, nodeData.NodeId, legacyOutputPortName, ExposedPropertyNodeUtility.OutputPortName);
        }

        private static void MigrateWriterNode(GenGraph graph, GenNodeData nodeData)
        {
            string propertyName = GetParameterValue(nodeData, "key");
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                propertyName = nodeData.NodeName ?? "Property";
            }

            string defaultValue = GetWriterDefaultValue(nodeData);
            ExposedProperty property = GetOrCreateProperty(graph, propertyName, ChannelType.Float, defaultValue);
            if (property != null && string.IsNullOrWhiteSpace(property.DefaultValue))
            {
                property.DefaultValue = defaultValue;
            }
        }

        private static void EnsureExposedPropertyIds(IList<ExposedProperty> properties)
        {
            int index;
            for (index = 0; index < properties.Count; index++)
            {
                ExposedProperty property = properties[index];
                if (property != null && string.IsNullOrWhiteSpace(property.PropertyId))
                {
                    property.PropertyId = Guid.NewGuid().ToString();
                }
            }
        }

        private static ExposedProperty GetOrCreateProperty(GenGraph graph, string propertyName, ChannelType type, string defaultValue)
        {
            ExposedProperty property = graph.GetExposedPropertyByName(propertyName);
            if (property != null)
            {
                if (string.IsNullOrWhiteSpace(property.PropertyId))
                {
                    property.PropertyId = Guid.NewGuid().ToString();
                }

                return property;
            }

            return graph.AddExposedProperty(propertyName, type, defaultValue);
        }

        private static string GetWriterDefaultValue(GenNodeData nodeData)
        {
            string rawValue = GetParameterValue(nodeData, "value");
            float parsedFloat;
            if (float.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsedFloat))
            {
                return parsedFloat.ToString("G", CultureInfo.InvariantCulture);
            }

            return "0";
        }

        private static string FindLegacyOutputPortName(GenNodeData nodeData)
        {
            if (nodeData == null || nodeData.Ports == null)
            {
                return ExposedPropertyNodeUtility.OutputPortName;
            }

            int index;
            for (index = 0; index < nodeData.Ports.Count; index++)
            {
                GenPortData port = nodeData.Ports[index];
                if (port != null && port.Direction == PortDirection.Output && !string.IsNullOrWhiteSpace(port.PortName))
                {
                    return port.PortName;
                }
            }

            return ExposedPropertyNodeUtility.OutputPortName;
        }

        private static void RewriteOutgoingConnections(
            IList<GenConnectionData> connections,
            string nodeId,
            string originalPortName,
            string replacementPortName)
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

        private static string GetParameterValue(GenNodeData nodeData, string parameterName)
        {
            if (nodeData == null || nodeData.Parameters == null)
            {
                return string.Empty;
            }

            int parameterIndex;
            for (parameterIndex = 0; parameterIndex < nodeData.Parameters.Count; parameterIndex++)
            {
                SerializedParameter parameter = nodeData.Parameters[parameterIndex];
                if (parameter != null &&
                    string.Equals(parameter.Name, parameterName, StringComparison.OrdinalIgnoreCase))
                {
                    return parameter.Value ?? string.Empty;
                }
            }

            return string.Empty;
        }
    }
}
