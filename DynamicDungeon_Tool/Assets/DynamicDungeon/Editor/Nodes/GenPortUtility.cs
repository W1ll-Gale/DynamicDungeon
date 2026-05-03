using DynamicDungeon.Editor.Utilities;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using System;
using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace DynamicDungeon.Editor.Nodes
{
    public static class GenPortUtility
    {
        public static bool TryGetPortDefinition(Port port, out NodePortDefinition portDefinition)
        {
            if (port != null && port.userData is NodePortDefinition)
            {
                portDefinition = (NodePortDefinition)port.userData;
                return true;
            }

            portDefinition = default(NodePortDefinition);
            return false;
        }

        public static bool CanConnectTo(Port port, Port otherPort)
        {
            NodePortDefinition portDefinition;
            NodePortDefinition otherPortDefinition;
            if (!TryGetPortDefinition(port, out portDefinition) ||
                !TryGetPortDefinition(otherPort, out otherPortDefinition))
            {
                return false;
            }

            if (ReferenceEquals(port, otherPort))
            {
                return false;
            }

            if (port.direction == otherPort.direction)
            {
                return false;
            }

            if (IsSubGraphAutoPort(portDefinition) || IsSubGraphAutoPort(otherPortDefinition))
            {
                return true;
            }

            ChannelType fromType;
            ChannelType toType;
            ResolveConnectionTypes(portDefinition, otherPortDefinition, out fromType, out toType);

            return fromType == toType || CastRegistry.CanCast(fromType, toType);
        }

        public static bool RequiresCast(Port port, Port otherPort)
        {
            NodePortDefinition portDefinition;
            NodePortDefinition otherPortDefinition;
            if (!TryGetPortDefinition(port, out portDefinition) ||
                !TryGetPortDefinition(otherPort, out otherPortDefinition))
            {
                return false;
            }

            ChannelType fromType;
            ChannelType toType;
            ResolveConnectionTypes(portDefinition, otherPortDefinition, out fromType, out toType);

            if (IsSubGraphAutoPort(portDefinition) || IsSubGraphAutoPort(otherPortDefinition))
            {
                return false;
            }

            return fromType != toType && CastRegistry.CanCast(fromType, toType);
        }

        public static Color GetPortColour(Port port)
        {
            NodePortDefinition portDefinition;
            return TryGetPortDefinition(port, out portDefinition)
                ? PortColourRegistry.GetColour(portDefinition.Type)
                : Color.white;
        }

        public static bool IsSubGraphAutoPort(NodePortDefinition portDefinition)
        {
            return string.Equals(portDefinition.Name, SubGraphNodeView.AutoInputPortName, StringComparison.Ordinal) ||
                   string.Equals(portDefinition.Name, SubGraphNodeView.AutoOutputPortName, StringComparison.Ordinal) ||
                   string.Equals(portDefinition.Name, SubGraphBoundaryNodeView.AutoInputBoundaryPortName, StringComparison.Ordinal) ||
                   string.Equals(portDefinition.Name, SubGraphBoundaryNodeView.AutoOutputBoundaryPortName, StringComparison.Ordinal);
        }

        public static string BuildPortTooltip(NodePortDefinition portDefinition)
        {
            List<string> lines = new List<string>();
            lines.Add(portDefinition.DisplayName);

            if (!string.IsNullOrWhiteSpace(portDefinition.Description))
            {
                lines.Add(portDefinition.Description);
            }

            List<string> details = new List<string>();
            details.Add(FormatDirection(portDefinition.Direction));
            details.Add(FormatChannelType(portDefinition.Type));
            details.Add(portDefinition.Capacity == PortCapacity.Multi ? "Multi connection" : "Single connection");

            if (portDefinition.Direction == PortDirection.Input)
            {
                details.Add(portDefinition.Required ? "Required" : "Optional");
            }

            lines.Add(string.Join("  •  ", details));
            return string.Join("\n", lines);
        }

        private static void ResolveConnectionTypes(
            NodePortDefinition portDefinition,
            NodePortDefinition otherPortDefinition,
            out ChannelType fromType,
            out ChannelType toType)
        {
            if (portDefinition.Direction == PortDirection.Output)
            {
                fromType = portDefinition.Type;
                toType = otherPortDefinition.Type;
                return;
            }

            fromType = otherPortDefinition.Type;
            toType = portDefinition.Type;
        }

        private static string FormatDirection(PortDirection direction)
        {
            return direction == PortDirection.Input ? "Input" : "Output";
        }

        private static string FormatChannelType(ChannelType channelType)
        {
            switch (channelType)
            {
                case ChannelType.Float:
                    return "Float values";
                case ChannelType.Int:
                    return "Integer values";
                case ChannelType.BoolMask:
                    return "Boolean mask";
                case ChannelType.PointList:
                    return "Point list";
                default:
                    return Enum.GetName(typeof(ChannelType), channelType) ?? channelType.ToString();
            }
        }
    }
}
