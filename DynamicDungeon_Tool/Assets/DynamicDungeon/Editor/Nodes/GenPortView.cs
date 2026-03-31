using DynamicDungeon.Editor.Utilities;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace DynamicDungeon.Editor.Nodes
{
    public sealed class GenPortView : Port
    {
        private readonly NodePortDefinition _portDefinition;

        public NodePortDefinition PortDefinition
        {
            get
            {
                return _portDefinition;
            }
        }

        public GenPortView(NodePortDefinition portDefinition) : base(
            Orientation.Horizontal,
            ToGraphViewDirection(portDefinition.Direction),
            ToGraphViewCapacity(portDefinition.Capacity),
            typeof(float))
        {
            _portDefinition = portDefinition;

            portName = portDefinition.Name;
            portColor = PortColourRegistry.GetColour(portDefinition.Type);
            style.marginTop = 2.0f;
            style.marginBottom = 2.0f;
        }

        public bool CanConnectTo(Port otherPort)
        {
            GenPortView otherGenPort = otherPort as GenPortView;
            if (otherGenPort == null)
            {
                return false;
            }

            if (ReferenceEquals(this, otherGenPort))
            {
                return false;
            }

            if (direction == otherGenPort.direction)
            {
                return false;
            }

            ChannelType fromType;
            ChannelType toType;
            ResolveConnectionTypes(otherGenPort, out fromType, out toType);

            return fromType == toType || CastRegistry.CanCast(fromType, toType);
        }

        public bool RequiresCast(Port otherPort)
        {
            GenPortView otherGenPort = otherPort as GenPortView;
            if (otherGenPort == null)
            {
                return false;
            }

            ChannelType fromType;
            ChannelType toType;
            ResolveConnectionTypes(otherGenPort, out fromType, out toType);

            return fromType != toType && CastRegistry.CanCast(fromType, toType);
        }

        public Color GetPortColour()
        {
            return PortColourRegistry.GetColour(_portDefinition.Type);
        }

        private static UnityEditor.Experimental.GraphView.Direction ToGraphViewDirection(PortDirection direction)
        {
            return direction == PortDirection.Input
                ? UnityEditor.Experimental.GraphView.Direction.Input
                : UnityEditor.Experimental.GraphView.Direction.Output;
        }

        private static Port.Capacity ToGraphViewCapacity(PortCapacity capacity)
        {
            return capacity == PortCapacity.Multi ? Port.Capacity.Multi : Port.Capacity.Single;
        }

        private void ResolveConnectionTypes(GenPortView otherPort, out ChannelType fromType, out ChannelType toType)
        {
            if (_portDefinition.Direction == PortDirection.Output)
            {
                fromType = _portDefinition.Type;
                toType = otherPort.PortDefinition.Type;
                return;
            }

            fromType = otherPort.PortDefinition.Type;
            toType = _portDefinition.Type;
        }
    }
}
