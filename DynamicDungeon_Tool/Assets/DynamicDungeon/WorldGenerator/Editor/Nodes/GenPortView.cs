using DynamicDungeon.Editor.Utilities;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

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

        public GenPortView(NodePortDefinition portDefinition, IEdgeConnectorListener edgeConnectorListener) : base(
            Orientation.Horizontal,
            ToGraphViewDirection(portDefinition.Direction),
            ToGraphViewCapacity(portDefinition.Direction, portDefinition.Capacity),
            typeof(float))
        {
            GenPortView currentPort = this;

            _portDefinition = portDefinition;

            portName = portDefinition.Name;
            portColor = PortColourRegistry.GetColour(portDefinition.Type);
            style.marginTop = 2.0f;
            style.marginBottom = 2.0f;
            currentPort.AddManipulator(new EdgeConnector<Edge>(edgeConnectorListener));
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

        public CastMode GetDefaultCastMode(Port otherPort)
        {
            GenPortView otherGenPort = otherPort as GenPortView;
            if (otherGenPort == null)
            {
                return CastMode.None;
            }

            ChannelType fromType;
            ChannelType toType;
            ResolveConnectionTypes(otherGenPort, out fromType, out toType);

            CastMode defaultMode;
            if (fromType != toType && CastRegistry.CanCast(fromType, toType, out defaultMode))
            {
                return defaultMode;
            }

            return CastMode.None;
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

        private static Port.Capacity ToGraphViewCapacity(PortDirection direction, PortCapacity capacity)
        {
            if (direction == PortDirection.Output)
            {
                return Port.Capacity.Multi;
            }

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
