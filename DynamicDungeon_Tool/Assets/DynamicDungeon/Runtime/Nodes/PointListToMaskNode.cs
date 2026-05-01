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
    [NodeCategory("Utility")]
    [NodeDisplayName("Point List To Mask")]
    [Description("Converts a point list into a bool mask by marking every referenced tile as true.")]
    public sealed class PointListToMaskNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        private const string DefaultNodeName = "Point List To Mask";
        private const string PointsPortName = "Points";
        private const string FallbackOutputPortName = "Mask";
        private const string PreferredOutputDisplayName = FallbackOutputPortName;

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private string _outputChannelName;
        private NodePortDefinition[] _ports;

        private string _inputPointsChannelName;
        private ChannelDeclaration[] _channelDeclarations;

        public IReadOnlyList<NodePortDefinition> Ports => _ports;
        public IReadOnlyList<ChannelDeclaration> ChannelDeclarations => _channelDeclarations;
        public IReadOnlyList<BlackboardKey> BlackboardDeclarations => _blackboardDeclarations;
        public string NodeId => _nodeId;
        public string NodeName => _nodeName;

        public PointListToMaskNode(string nodeId, string nodeName, string inputPointsChannelName = "", string outputChannelName = "")
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _inputPointsChannelName = inputPointsChannelName ?? string.Empty;
            _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(nodeId, outputChannelName, FallbackOutputPortName);

            RefreshPorts();
            RefreshChannelDeclarations();
        }

        private void RefreshPorts()
        {
            string outputPortDisplayName = GraphPortNameUtility.ResolveOutputDisplayName(_nodeId, _outputChannelName, PreferredOutputDisplayName);
            _ports = new[]
            {
                new NodePortDefinition(PointsPortName, PortDirection.Input, ChannelType.PointList, PortCapacity.Single, true),
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.BoolMask, displayName: outputPortDisplayName)
            };
        }

        public void ReceiveInputConnections(IReadOnlyDictionary<string, string> inputConnections)
        {
            string inputChannelName;
            if (inputConnections != null && inputConnections.TryGetValue(PointsPortName, out inputChannelName))
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
            if (string.Equals(name, "outputChannelName", StringComparison.OrdinalIgnoreCase))
            {
                _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(_nodeId, value, FallbackOutputPortName);
                RefreshPorts();
                RefreshChannelDeclarations();
            }
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            context.InputDependency.Complete();

            NativeList<int2> points = context.GetPointListChannel(_inputPointsChannelName);
            NativeArray<byte> output = context.GetBoolMaskChannel(_outputChannelName);

            int index;
            for (index = 0; index < output.Length; index++)
            {
                output[index] = 0;
            }

            for (index = 0; index < points.Length; index++)
            {
                int2 point = points[index];
                if (point.x < 0 || point.x >= context.Width || point.y < 0 || point.y >= context.Height)
                {
                    continue;
                }

                int outputIndex = point.y * context.Width + point.x;
                output[outputIndex] = 1;
            }

            return default;
        }

        private void RefreshChannelDeclarations()
        {
            if (!string.IsNullOrWhiteSpace(_inputPointsChannelName))
            {
                _channelDeclarations = new[]
                {
                    new ChannelDeclaration(_inputPointsChannelName, ChannelType.PointList, false),
                    new ChannelDeclaration(_outputChannelName, ChannelType.BoolMask, true)
                };
                return;
            }

            _channelDeclarations = new[]
            {
                new ChannelDeclaration(_outputChannelName, ChannelType.BoolMask, true)
            };
        }
    }
}
