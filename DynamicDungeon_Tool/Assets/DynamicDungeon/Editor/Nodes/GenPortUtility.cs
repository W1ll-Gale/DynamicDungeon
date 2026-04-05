using DynamicDungeon.Editor.Utilities;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
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

            return fromType != toType && CastRegistry.CanCast(fromType, toType);
        }

        public static Color GetPortColour(Port port)
        {
            NodePortDefinition portDefinition;
            return TryGetPortDefinition(port, out portDefinition)
                ? PortColourRegistry.GetColour(portDefinition.Type)
                : Color.white;
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
    }
}
