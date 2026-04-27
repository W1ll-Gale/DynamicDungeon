using System;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Nodes;

namespace DynamicDungeon.Runtime.Graph
{
    public static class GraphPortNameUtility
    {
        public const string LegacyGenericOutputDisplayName = "Output";

        public static string CreateGeneratedOutputPortName(string nodeId, string displayName)
        {
            string safeDisplayName = NormaliseDisplayName(displayName, LegacyGenericOutputDisplayName);
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                return safeDisplayName;
            }

            return safeDisplayName + "__" + nodeId;
        }

        public static string ResolveOwnedOutputChannelName(string nodeId, string requestedChannelName, string defaultDisplayName)
        {
            if (string.IsNullOrWhiteSpace(requestedChannelName))
            {
                return CreateGeneratedOutputPortName(nodeId, defaultDisplayName);
            }

            return requestedChannelName.Trim();
        }

        public static string GetPreferredOutputDisplayName(Type nodeType, string parameterName)
        {
            if (nodeType == typeof(BoolMaskToLogicalIdNode))
            {
                return "LogicalIds";
            }

            string desiredName = ResolveDesiredName(parameterName);
            return NormaliseDisplayName(desiredName, LegacyGenericOutputDisplayName);
        }

        public static string ResolveOutputDisplayName(string nodeId, string outputChannelName, string preferredDisplayName)
        {
            string normalPreferredDisplayName = NormaliseDisplayName(preferredDisplayName, LegacyGenericOutputDisplayName);
            string safeOutputChannelName = outputChannelName ?? string.Empty;

            if (IsNamedOutput(safeOutputChannelName, nodeId, normalPreferredDisplayName))
            {
                return normalPreferredDisplayName;
            }

            if (IsNamedOutput(safeOutputChannelName, nodeId, LegacyGenericOutputDisplayName))
            {
                return LegacyGenericOutputDisplayName;
            }

            return string.IsNullOrWhiteSpace(safeOutputChannelName) ? normalPreferredDisplayName : safeOutputChannelName;
        }

        public static bool PortMatchesName(string nodeId, NodePortDefinition portDefinition, string candidatePortName)
        {
            string safeCandidatePortName = candidatePortName ?? string.Empty;
            if (safeCandidatePortName.Length == 0)
            {
                return false;
            }

            if (string.Equals(portDefinition.Name, safeCandidatePortName, StringComparison.Ordinal) ||
                string.Equals(portDefinition.DisplayName, safeCandidatePortName, StringComparison.Ordinal))
            {
                return true;
            }

            if (portDefinition.Direction != PortDirection.Output)
            {
                return false;
            }

            string generatedNameFromDisplayName = CreateGeneratedOutputPortName(nodeId, portDefinition.DisplayName);
            if (string.Equals(generatedNameFromDisplayName, safeCandidatePortName, StringComparison.Ordinal))
            {
                return true;
            }

            string generatedNameFromPortName = CreateGeneratedOutputPortName(nodeId, portDefinition.Name);
            return string.Equals(generatedNameFromPortName, safeCandidatePortName, StringComparison.Ordinal);
        }

        private static bool IsNamedOutput(string outputChannelName, string nodeId, string displayName)
        {
            string safeDisplayName = NormaliseDisplayName(displayName, LegacyGenericOutputDisplayName);
            return string.Equals(outputChannelName, safeDisplayName, StringComparison.Ordinal) ||
                   string.Equals(outputChannelName, CreateGeneratedOutputPortName(nodeId, safeDisplayName), StringComparison.Ordinal);
        }

        private static string ResolveDesiredName(string parameterName)
        {
            string safeName = parameterName ?? string.Empty;
            if (safeName.EndsWith("ChannelName", StringComparison.OrdinalIgnoreCase))
            {
                safeName = safeName.Substring(0, safeName.Length - "ChannelName".Length);
            }
            else if (safeName.EndsWith("PortName", StringComparison.OrdinalIgnoreCase))
            {
                safeName = safeName.Substring(0, safeName.Length - "PortName".Length);
            }

            if (safeName.StartsWith("output", StringComparison.OrdinalIgnoreCase))
            {
                safeName = safeName.Length == "output".Length ? string.Empty : safeName.Substring("output".Length);
            }
            else if (safeName.StartsWith("from", StringComparison.OrdinalIgnoreCase))
            {
                safeName = safeName.Length == "from".Length ? string.Empty : safeName.Substring("from".Length);
            }

            if (string.IsNullOrWhiteSpace(safeName))
            {
                return LegacyGenericOutputDisplayName;
            }

            if (safeName.Length == 1)
            {
                return safeName.ToUpperInvariant();
            }

            return char.ToUpperInvariant(safeName[0]) + safeName.Substring(1);
        }

        private static string NormaliseDisplayName(string displayName, string fallbackDisplayName)
        {
            return string.IsNullOrWhiteSpace(displayName) ? fallbackDisplayName : displayName.Trim();
        }
    }
}
