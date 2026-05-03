using System;
using System.Collections.Generic;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace DynamicDungeon.Runtime.Nodes
{
    [NodeCategory("Point Generation")]
    [NodeDisplayName("Point List Offset")]
    [Description("Copies a point list while applying a tile offset and dropping points that move out of bounds.")]
    public sealed class PointListOffsetNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        private const string DefaultNodeName = "Point List Offset";
        private const string PointsPortName = "Points";
        private const string FallbackOutputPortName = "OffsetPoints";
        private const string PreferredOutputDisplayName = FallbackOutputPortName;

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private string _inputPointsChannelName;
        private string _outputChannelName;
        private int _offsetX;
        private int _offsetY;
        private NodePortDefinition[] _ports;
        private ChannelDeclaration[] _channelDeclarations;

        public IReadOnlyList<NodePortDefinition> Ports => _ports;
        public IReadOnlyList<ChannelDeclaration> ChannelDeclarations => _channelDeclarations;
        public IReadOnlyList<BlackboardKey> BlackboardDeclarations => _blackboardDeclarations;
        public string NodeId => _nodeId;
        public string NodeName => _nodeName;

        public PointListOffsetNode(string nodeId, string nodeName, string inputPointsChannelName = "", string outputChannelName = "", int offsetX = 0, int offsetY = 0)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _inputPointsChannelName = inputPointsChannelName ?? string.Empty;
            _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(nodeId, outputChannelName, FallbackOutputPortName);
            _offsetX = offsetX;
            _offsetY = offsetY;

            RefreshPorts();
            RefreshChannelDeclarations();
        }

        public void ReceiveInputConnections(InputConnectionMap inputConnections)
        {
            if (inputConnections != null && inputConnections.TryGetValue(PointsPortName, out string inputChannelName))
            {
                _inputPointsChannelName = inputChannelName ?? string.Empty;
            }
            else
            {
                _inputPointsChannelName = string.Empty;
            }

            RefreshChannelDeclarations();
        }

        public void ReceiveParameter(string name, string value)
        {
            if (string.Equals(name, "offsetX", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(value, out _offsetX);
            }
            else if (string.Equals(name, "offsetY", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(value, out _offsetY);
            }
            else if (string.Equals(name, "outputChannelName", StringComparison.OrdinalIgnoreCase))
            {
                _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(_nodeId, value, FallbackOutputPortName);
                RefreshPorts();
                RefreshChannelDeclarations();
            }
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            context.InputDependency.Complete();

            NativeList<int2> input = context.GetPointListChannel(_inputPointsChannelName);
            NativeList<int2> output = context.GetPointListChannel(_outputChannelName);
            output.Clear();

            for (int index = 0; index < input.Length; index++)
            {
                int2 point = input[index];
                int2 shifted = new int2(point.x + _offsetX, point.y + _offsetY);
                if (shifted.x < 0 || shifted.x >= context.Width || shifted.y < 0 || shifted.y >= context.Height)
                {
                    continue;
                }

                output.Add(shifted);
            }

            return default;
        }

        private void RefreshPorts()
        {
            string outputPortDisplayName = GraphPortNameUtility.ResolveOutputDisplayName(_nodeId, _outputChannelName, PreferredOutputDisplayName);
            _ports = new[]
            {
                new NodePortDefinition(PointsPortName, PortDirection.Input, ChannelType.PointList, PortCapacity.Single, true),
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.PointList, displayName: outputPortDisplayName)
            };
        }

        private void RefreshChannelDeclarations()
        {
            if (!string.IsNullOrWhiteSpace(_inputPointsChannelName))
            {
                _channelDeclarations = new[]
                {
                    new ChannelDeclaration(_inputPointsChannelName, ChannelType.PointList, false),
                    new ChannelDeclaration(_outputChannelName, ChannelType.PointList, true)
                };
                return;
            }

            _channelDeclarations = new[]
            {
                new ChannelDeclaration(_outputChannelName, ChannelType.PointList, true)
            };
        }
    }
}
