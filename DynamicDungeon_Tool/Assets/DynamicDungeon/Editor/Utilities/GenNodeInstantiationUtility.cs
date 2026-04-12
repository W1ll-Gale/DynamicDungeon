using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using UnityEngine;

namespace DynamicDungeon.Editor.Utilities
{
    public static class GenNodeInstantiationUtility
    {
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

            if (!TryInstantiateNode(nodeType, nodeData, out nodeInstance, out errorMessage))
            {
                return false;
            }

            if (!ApplyParameters(nodeInstance, nodeData, out errorMessage))
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

            return TryInstantiatePrototypeNode(nodeType, nodeId, nodeName, out nodeInstance, out errorMessage);
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

            ConstructorInfo[] constructors = nodeType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            ConstructorMatch bestMatch = null;

            int constructorIndex;
            for (constructorIndex = 0; constructorIndex < constructors.Length; constructorIndex++)
            {
                ConstructorMatch constructorMatch;
                if (TryBindPrototypeConstructor(constructors[constructorIndex], nodeData.NodeId ?? string.Empty, nodeData.NodeName ?? string.Empty, out constructorMatch))
                {
                    if (bestMatch == null || constructorMatch.Score > bestMatch.Score)
                    {
                        bestMatch = constructorMatch;
                    }
                }
            }

            if (bestMatch == null)
            {
                return defaultParameters;
            }

            ParameterInfo[] parameters = bestMatch.Constructor.GetParameters();
            int parameterIndex;
            for (parameterIndex = 0; parameterIndex < parameters.Length; parameterIndex++)
            {
                ParameterInfo parameter = parameters[parameterIndex];
                if (!IsEditableSerialisedParameter(parameter))
                {
                    continue;
                }

                string serialisedValue = SerialiseParameterValue(bestMatch.Arguments[parameterIndex]);
                defaultParameters.Add(new SerializedParameter(parameter.Name, serialisedValue));
            }

            return defaultParameters;
        }

        private static bool ApplyParameters(IGenNode nodeInstance, GenNodeData nodeData, out string errorMessage)
        {
            IParameterReceiver parameterReceiver = nodeInstance as IParameterReceiver;
            if (parameterReceiver == null)
            {
                errorMessage = null;
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
                    errorMessage = "Failed to apply parameter '" + parameter.Name + "' to node '" + nodeData.NodeName + "': " + exception.Message;
                    return false;
                }
            }

            errorMessage = null;
            return true;
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
                errorMessage = "Node type '" + nodeType.FullName + "' could not be instantiated from graph data.";
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

        private static bool TryInstantiatePrototypeNode(Type nodeType, string nodeId, string nodeName, out IGenNode nodeInstance, out string errorMessage)
        {
            ConstructorInfo[] constructors = nodeType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            ConstructorMatch bestMatch = null;

            int constructorIndex;
            for (constructorIndex = 0; constructorIndex < constructors.Length; constructorIndex++)
            {
                ConstructorMatch constructorMatch;
                if (TryBindPrototypeConstructor(constructors[constructorIndex], nodeId, nodeName, out constructorMatch))
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
                errorMessage = "Node type '" + nodeType.FullName + "' could not be instantiated with prototype arguments.";
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
                errorMessage = "Node type '" + nodeType.FullName + "' failed during prototype construction: " + innerException.Message;
                return false;
            }
            catch (Exception exception)
            {
                nodeInstance = null;
                errorMessage = "Node type '" + nodeType.FullName + "' failed during prototype construction: " + exception.Message;
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

        private static bool TryBindPrototypeConstructor(ConstructorInfo constructor, string nodeId, string nodeName, out ConstructorMatch constructorMatch)
        {
            ParameterInfo[] parameters = constructor.GetParameters();
            object[] arguments = new object[parameters.Length];
            int score = 0;

            int parameterIndex;
            for (parameterIndex = 0; parameterIndex < parameters.Length; parameterIndex++)
            {
                ParameterInfo parameter = parameters[parameterIndex];
                string parameterName = parameter.Name ?? string.Empty;

                if (parameter.ParameterType == typeof(string) && string.Equals(parameterName, "nodeId", StringComparison.OrdinalIgnoreCase))
                {
                    arguments[parameterIndex] = nodeId;
                    score += 2;
                    continue;
                }

                if (parameter.ParameterType == typeof(string) &&
                    (string.Equals(parameterName, "nodeName", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(parameterName, "displayName", StringComparison.OrdinalIgnoreCase)))
                {
                    arguments[parameterIndex] = nodeName;
                    score += 2;
                    continue;
                }

                if (parameter.HasDefaultValue)
                {
                    arguments[parameterIndex] = parameter.DefaultValue;
                    score += 1;
                    continue;
                }

                object prototypeArgument;
                if (TryGetPrototypeArgumentValue(parameter, out prototypeArgument))
                {
                    arguments[parameterIndex] = prototypeArgument;
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
                (string.Equals(parameterName, "nodeName", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(parameterName, "displayName", StringComparison.OrdinalIgnoreCase)))
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

        private static bool TryGetPrototypeArgumentValue(ParameterInfo parameter, out object argumentValue)
        {
            string parameterName = parameter.Name ?? string.Empty;
            Type parameterType = parameter.ParameterType;

            if (parameterType == typeof(string))
            {
                argumentValue = CreatePrototypeStringValue(parameterName);
                return true;
            }

            if (parameterType == typeof(ChannelType))
            {
                argumentValue = ChannelType.Float;
                return true;
            }

            if (parameterType == typeof(int))
            {
                argumentValue = CreatePrototypeIntValue(parameterName);
                return true;
            }

            if (parameterType == typeof(long))
            {
                argumentValue = 0L;
                return true;
            }

            if (parameterType == typeof(float))
            {
                argumentValue = CreatePrototypeFloatValue(parameterName);
                return true;
            }

            if (parameterType == typeof(double))
            {
                argumentValue = (double)CreatePrototypeFloatValue(parameterName);
                return true;
            }

            if (parameterType == typeof(bool))
            {
                argumentValue = false;
                return true;
            }

            if (parameterType == typeof(Vector2))
            {
                argumentValue = Vector2.zero;
                return true;
            }

            if (parameterType.IsEnum)
            {
                Array enumValues = Enum.GetValues(parameterType);
                if (enumValues.Length > 0)
                {
                    argumentValue = enumValues.GetValue(0);
                    return true;
                }
            }

            argumentValue = null;
            return false;
        }

        private static bool IsEditableSerialisedParameter(ParameterInfo parameter)
        {
            if (parameter == null)
            {
                return false;
            }

            object specialArgumentValue;
            if (TryGetSpecialArgumentValue(parameter, new GenNodeData(), out specialArgumentValue))
            {
                return false;
            }

            string parameterName = parameter.Name ?? string.Empty;
            if (parameterName.EndsWith("ChannelName", StringComparison.OrdinalIgnoreCase) ||
                parameterName.EndsWith("PortName", StringComparison.OrdinalIgnoreCase) ||
                parameterName.EndsWith("Type", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return parameter.ParameterType != typeof(Vector2);
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

        private static string SerialiseParameterValue(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            Type valueType = value.GetType();
            if (valueType == typeof(float))
            {
                return ((float)value).ToString(CultureInfo.InvariantCulture);
            }

            if (valueType == typeof(double))
            {
                return ((double)value).ToString(CultureInfo.InvariantCulture);
            }

            if (valueType == typeof(int))
            {
                return ((int)value).ToString(CultureInfo.InvariantCulture);
            }

            if (valueType == typeof(long))
            {
                return ((long)value).ToString(CultureInfo.InvariantCulture);
            }

            if (valueType == typeof(bool))
            {
                return ((bool)value) ? bool.TrueString.ToLowerInvariant() : bool.FalseString.ToLowerInvariant();
            }

            if (valueType.IsEnum)
            {
                return Enum.GetName(valueType, value) ?? value.ToString();
            }

            return value.ToString() ?? string.Empty;
        }

        private static float CreatePrototypeFloatValue(string parameterName)
        {
            if (parameterName.IndexOf("frequency", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 0.05f;
            }

            if (parameterName.IndexOf("amplitude", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 1.0f;
            }

            if (parameterName.IndexOf("threshold", StringComparison.OrdinalIgnoreCase) >= 0 ||
                parameterName.IndexOf("probability", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 0.5f;
            }

            return 0.0f;
        }

        private static int CreatePrototypeIntValue(string parameterName)
        {
            if (parameterName.IndexOf("octave", StringComparison.OrdinalIgnoreCase) >= 0 ||
                parameterName.IndexOf("iteration", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 1;
            }

            if (parameterName.IndexOf("logicalId", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 1;
            }

            return 0;
        }

        private static string CreatePrototypeStringValue(string parameterName)
        {
            string desiredName = GetDesiredPortName(parameterName);
            if (string.IsNullOrEmpty(desiredName))
            {
                desiredName = parameterName;
            }

            if (string.IsNullOrEmpty(desiredName))
            {
                return "Channel";
            }

            if (desiredName.Length == 1)
            {
                return desiredName.ToUpperInvariant();
            }

            return char.ToUpperInvariant(desiredName[0]) + desiredName.Substring(1);
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
    }
}
