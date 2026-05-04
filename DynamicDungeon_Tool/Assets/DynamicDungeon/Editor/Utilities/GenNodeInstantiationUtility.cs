using System;
using System.Collections.Generic;
using System.Reflection;
using DynamicDungeon.Editor.Nodes;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using DynamicDungeon.Runtime.Nodes;

namespace DynamicDungeon.Editor.Utilities
{
    public static class GenNodeInstantiationUtility
    {
        public static bool TryCreateNodeInstance(GenNodeData nodeData, out IGenNode nodeInstance, out string errorMessage)
        {
            if (nodeData == null)
            {
                nodeInstance = null;
                errorMessage = "Node data cannot be null.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(nodeData.NodeTypeName))
            {
                nodeInstance = null;
                errorMessage = "Node type name is missing for node '" + nodeData.NodeName + "'.";
                return false;
            }

            Type nodeType = ResolveNodeType(nodeData.NodeTypeName);
            if (nodeType == null)
            {
                nodeInstance = null;
                errorMessage = "Node type '" + nodeData.NodeTypeName + "' could not be found.";
                return false;
            }

            if (!typeof(IGenNode).IsAssignableFrom(nodeType) || nodeType.IsAbstract)
            {
                nodeInstance = null;
                errorMessage = "Node type '" + nodeData.NodeTypeName + "' does not implement IGenNode.";
                return false;
            }

            NormaliseSerialisedParameters(nodeData, nodeType);

            if (!GraphNodeInstantiationUtility.TryInstantiateNode(nodeType, nodeData, out nodeInstance, out errorMessage, true))
            {
                return false;
            }

            if (!GraphNodeInstantiationUtility.TryApplyParameters(nodeInstance, nodeData, out errorMessage))
            {
                nodeInstance = null;
                return false;
            }

            return true;
        }

        public static bool TryCreatePrototypeNodeInstance(Type nodeType, string nodeId, string nodeName, out IGenNode nodeInstance, out string errorMessage)
        {
            if (nodeType == null)
            {
                nodeInstance = null;
                errorMessage = "Node type cannot be null.";
                return false;
            }

            if (!typeof(IGenNode).IsAssignableFrom(nodeType) || nodeType.IsAbstract)
            {
                nodeInstance = null;
                errorMessage = "Node type '" + nodeType.FullName + "' does not implement IGenNode.";
                return false;
            }

            return GraphNodeInstantiationUtility.TryInstantiatePrototypeNode(nodeType, nodeId, nodeName, out nodeInstance, out errorMessage);
        }

        public static void PopulateDefaultParameters(GenNodeData nodeData, Type nodeType)
        {
            if (nodeData == null || nodeType == null)
            {
                return;
            }

            if (nodeData.Parameters == null)
            {
                nodeData.Parameters = new List<SerializedParameter>();
            }

            List<SerializedParameter> defaultParameters = CreateDefaultParameters(nodeData, nodeType);

            int parameterIndex;
            for (parameterIndex = 0; parameterIndex < defaultParameters.Count; parameterIndex++)
            {
                SerializedParameter defaultParameter = defaultParameters[parameterIndex];
                if (defaultParameter == null || HasSerialisedParameter(nodeData.Parameters, defaultParameter.Name))
                {
                    continue;
                }

                nodeData.Parameters.Add(new SerializedParameter(defaultParameter.Name, defaultParameter.Value));
            }
        }

        public static List<SerializedParameter> CreateDefaultParameters(GenNodeData nodeData, Type nodeType)
        {
            List<SerializedParameter> defaultParameters = new List<SerializedParameter>();
            if (nodeData == null || nodeType == null)
            {
                return defaultParameters;
            }

            ConstructorInfo constructor;
            object[] arguments;
            if (!GraphNodeInstantiationUtility.TryGetBestPrototypeConstructor(nodeType, nodeData.NodeId ?? string.Empty, nodeData.NodeName ?? string.Empty, out constructor, out arguments))
            {
                return defaultParameters;
            }

            ParameterInfo[] parameters = constructor.GetParameters();
            int parameterIndex;
            for (parameterIndex = 0; parameterIndex < parameters.Length; parameterIndex++)
            {
                ParameterInfo parameter = parameters[parameterIndex];
                if (!IsEditableSerialisedParameter(nodeType, parameter))
                {
                    continue;
                }

                string serialisedValue = GraphNodeInstantiationUtility.SerialiseParameterValue(arguments[parameterIndex]);
                defaultParameters.Add(new SerializedParameter(parameter.Name, serialisedValue));
            }

            return defaultParameters;
        }

        public static void NormaliseSerialisedParameters(GenNodeData nodeData, Type nodeType)
        {
            if (nodeData == null || nodeType == null || nodeData.Parameters == null)
            {
                return;
            }

            int parameterIndex;
            for (parameterIndex = 0; parameterIndex < nodeData.Parameters.Count; parameterIndex++)
            {
                SerializedParameter parameter = nodeData.Parameters[parameterIndex];
                if (parameter == null || string.IsNullOrWhiteSpace(parameter.Name))
                {
                    continue;
                }

                string normalisedValue;
                if (InlinedControlFactory.TryNormaliseParameterValue(nodeType, parameter.Name, parameter.Value ?? string.Empty, out normalisedValue))
                {
                    parameter.Value = normalisedValue;
                }
            }
        }

        public static bool IsEditableSerialisedParameter(Type nodeType, string parameterName)
        {
            if (nodeType == null || string.IsNullOrWhiteSpace(parameterName))
            {
                return false;
            }

            ConstructorInfo[] constructors = nodeType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            int constructorIndex;
            for (constructorIndex = 0; constructorIndex < constructors.Length; constructorIndex++)
            {
                ParameterInfo[] parameters = constructors[constructorIndex].GetParameters();
                int parameterIndex;
                for (parameterIndex = 0; parameterIndex < parameters.Length; parameterIndex++)
                {
                    ParameterInfo parameter = parameters[parameterIndex];
                    if (string.Equals(parameter.Name, parameterName, StringComparison.OrdinalIgnoreCase))
                    {
                        return IsEditableSerialisedParameter(nodeType, parameter);
                    }
                }
            }

            return false;
        }

        public static bool IsKnownSerialisedParameter(Type nodeType, string parameterName)
        {
            if (nodeType == null || string.IsNullOrWhiteSpace(parameterName))
            {
                return false;
            }

            SubGraphNodeAttribute subGraphAttribute = Attribute.GetCustomAttribute(nodeType, typeof(SubGraphNodeAttribute)) as SubGraphNodeAttribute;
            if (subGraphAttribute != null &&
                string.Equals(parameterName, subGraphAttribute.NestedGraphParameterName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            ConstructorInfo[] constructors = nodeType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            int constructorIndex;
            for (constructorIndex = 0; constructorIndex < constructors.Length; constructorIndex++)
            {
                ParameterInfo[] parameters = constructors[constructorIndex].GetParameters();
                int parameterIndex;
                for (parameterIndex = 0; parameterIndex < parameters.Length; parameterIndex++)
                {
                    ParameterInfo parameter = parameters[parameterIndex];
                    if (parameter != null && string.Equals(parameter.Name, parameterName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsEditableSerialisedParameter(Type nodeType, ParameterInfo parameter)
        {
            if (parameter == null)
            {
                return false;
            }

            if (GraphNodeInstantiationUtility.IsGraphDataConstructorParameter(parameter))
            {
                return false;
            }

            string parameterName = parameter.Name ?? string.Empty;
            if (parameterName.EndsWith("ChannelName", StringComparison.OrdinalIgnoreCase) ||
                parameterName.EndsWith("PortName", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (parameterName.EndsWith("Type", StringComparison.OrdinalIgnoreCase) &&
                !(nodeType == typeof(ConstantNode) && string.Equals(parameterName, "outputType", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            if (Attribute.IsDefined(parameter, typeof(HideInNodeInspectorAttribute), false))
            {
                return false;
            }

            return true;
        }

        private static bool HasSerialisedParameter(IReadOnlyList<SerializedParameter> parameters, string parameterName)
        {
            string safeParameterName = parameterName ?? string.Empty;

            int parameterIndex;
            for (parameterIndex = 0; parameterIndex < parameters.Count; parameterIndex++)
            {
                SerializedParameter parameter = parameters[parameterIndex];
                if (parameter != null && string.Equals(parameter.Name, safeParameterName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static Type ResolveNodeType(string nodeTypeName)
        {
            return GraphNodeInstantiationUtility.ResolveNodeType(nodeTypeName);
        }
    }
}
