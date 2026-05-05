using System;
using System.Collections.Generic;
using DynamicDungeon.Runtime.Core;

namespace DynamicDungeon.Runtime.Graph
{
    public static class ExposedPropertyNodeUtility
    {
        public const string OutputPortName = "Value";
        public const string PropertyIdParameterName = "propertyId";
        public const string PropertyNameParameterName = "propertyName";
        public const string PropertyTypeParameterName = "propertyType";
        private const string OutputChannelPrefix = "ExposedProperty";

        public static string NodeTypeName
        {
            get
            {
                return typeof(ExposedPropertyNode).FullName;
            }
        }

        public static bool IsExposedPropertyNode(GenNodeData nodeData)
        {
            return nodeData != null &&
                   string.Equals(nodeData.NodeTypeName, NodeTypeName, StringComparison.Ordinal);
        }

        public static string CreateOutputChannelName(string nodeId)
        {
            return OutputChannelPrefix + "::" + (nodeId ?? string.Empty) + "::" + OutputPortName;
        }

        public static void ConfigureNodeData(GenNodeData nodeData, ExposedProperty property)
        {
            if (nodeData == null || property == null)
            {
                return;
            }

            nodeData.NodeTypeName = NodeTypeName;
            nodeData.NodeName = property.PropertyName ?? string.Empty;

            if (nodeData.Parameters == null)
            {
                nodeData.Parameters = new List<SerializedParameter>();
            }

            SetParameter(nodeData.Parameters, PropertyIdParameterName, property.PropertyId ?? string.Empty);
            SetParameter(nodeData.Parameters, PropertyNameParameterName, property.PropertyName ?? string.Empty);
            SetParameter(nodeData.Parameters, PropertyTypeParameterName, property.Type.ToString());

            if (nodeData.Ports == null)
            {
                nodeData.Ports = new List<GenPortData>();
            }
            else
            {
                nodeData.Ports.Clear();
            }

            nodeData.Ports.Add(
                new GenPortData(
                    OutputPortName,
                    PortDirection.Output,
                    property.Type,
                    property.PropertyName ?? string.Empty));
        }

        public static string GetPropertyId(GenNodeData nodeData)
        {
            return GetParameterValue(nodeData, PropertyIdParameterName);
        }

        public static string GetPropertyName(GenNodeData nodeData)
        {
            return GetParameterValue(nodeData, PropertyNameParameterName);
        }

        public static ChannelType GetPropertyType(GenNodeData nodeData, ChannelType fallbackType)
        {
            string rawValue = GetParameterValue(nodeData, PropertyTypeParameterName);
            ChannelType parsedType;
            if (Enum.TryParse(rawValue, true, out parsedType))
            {
                return parsedType;
            }

            if (nodeData != null && nodeData.Ports != null)
            {
                int index;
                for (index = 0; index < nodeData.Ports.Count; index++)
                {
                    GenPortData port = nodeData.Ports[index];
                    if (port != null &&
                        string.Equals(port.PortName, OutputPortName, StringComparison.Ordinal))
                    {
                        return port.Type;
                    }
                }
            }

            return fallbackType;
        }

        private static void SetParameter(IList<SerializedParameter> parameters, string parameterName, string value)
        {
            int index;
            for (index = 0; index < parameters.Count; index++)
            {
                SerializedParameter parameter = parameters[index];
                if (parameter != null &&
                    string.Equals(parameter.Name, parameterName, StringComparison.OrdinalIgnoreCase))
                {
                    parameter.Value = value ?? string.Empty;
                    return;
                }
            }

            parameters.Add(new SerializedParameter(parameterName, value ?? string.Empty));
        }

        private static string GetParameterValue(GenNodeData nodeData, string parameterName)
        {
            if (nodeData == null || nodeData.Parameters == null)
            {
                return string.Empty;
            }

            int index;
            for (index = 0; index < nodeData.Parameters.Count; index++)
            {
                SerializedParameter parameter = nodeData.Parameters[index];
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
