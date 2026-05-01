using System;
using System.Collections.Generic;
using System.Globalization;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace DynamicDungeon.Runtime.Nodes
{
    [NodeCategory("Filter")]
    [NodeDisplayName("Column Surface Band")]
    [Description("For each column in a bool mask, marks tiles within an inclusive depth range beneath that column's highest true cell.")]
    public sealed class ColumnSurfaceBandNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        private const string DefaultNodeName = "Column Surface Band";
        private const string InputPortName = "Input";
        private const string FallbackOutputPortName = "Output";
        private const string PreferredOutputDisplayName = "Mask";

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private string _inputChannelName;
        private string _outputChannelName;
        private NodePortDefinition[] _ports;
        private ChannelDeclaration[] _channelDeclarations;

        [MinValue(0.0f)]
        [DescriptionAttribute("Minimum inclusive depth beneath the highest solid tile in each column.")]
        private int _minDepth;

        [MinValue(0.0f)]
        [DescriptionAttribute("Maximum inclusive depth beneath the highest solid tile in each column.")]
        private int _maxDepth = 3;

        public IReadOnlyList<NodePortDefinition> Ports => _ports;
        public IReadOnlyList<ChannelDeclaration> ChannelDeclarations => _channelDeclarations;
        public IReadOnlyList<BlackboardKey> BlackboardDeclarations => _blackboardDeclarations;
        public string NodeId => _nodeId;
        public string NodeName => _nodeName;

        public ColumnSurfaceBandNode(string nodeId, string nodeName, string inputChannelName = "", string outputChannelName = "", int minDepth = 0, int maxDepth = 3)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _inputChannelName = inputChannelName ?? string.Empty;
            _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(nodeId, outputChannelName, FallbackOutputPortName);
            _minDepth = math.max(0, minDepth);
            _maxDepth = math.max(_minDepth, maxDepth);

            RefreshPorts();
            RefreshChannelDeclarations();
        }

        public void ReceiveInputConnections(IReadOnlyDictionary<string, string> inputConnections)
        {
            string inputChannelName;
            if (inputConnections != null && inputConnections.TryGetValue(InputPortName, out inputChannelName))
            {
                _inputChannelName = inputChannelName ?? string.Empty;
            }
            else
            {
                _inputChannelName = string.Empty;
            }

            RefreshChannelDeclarations();
        }

        public void ReceiveParameter(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            int parsedInt;
            if (string.Equals(name, "minDepth", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedInt))
                {
                    _minDepth = math.max(0, parsedInt);
                    _maxDepth = math.max(_minDepth, _maxDepth);
                }

                return;
            }

            if (string.Equals(name, "maxDepth", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedInt))
                {
                    _maxDepth = math.max(_minDepth, parsedInt);
                }

                return;
            }

            if (string.Equals(name, "outputChannelName", StringComparison.OrdinalIgnoreCase))
            {
                _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(_nodeId, value, FallbackOutputPortName);
                RefreshPorts();
                RefreshChannelDeclarations();
            }
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            NativeArray<byte> input = context.GetBoolMaskChannel(_inputChannelName);
            NativeArray<byte> output = context.GetBoolMaskChannel(_outputChannelName);

            ColumnSurfaceBandJob job = new ColumnSurfaceBandJob
            {
                Input = input,
                Output = output,
                Width = context.Width,
                Height = context.Height,
                MinDepth = _minDepth,
                MaxDepth = _maxDepth
            };

            return job.Schedule(context.InputDependency);
        }

        private void RefreshPorts()
        {
            string outputPortDisplayName = GraphPortNameUtility.ResolveOutputDisplayName(_nodeId, _outputChannelName, PreferredOutputDisplayName);
            _ports = new[]
            {
                new NodePortDefinition(InputPortName, PortDirection.Input, ChannelType.BoolMask, PortCapacity.Single, true),
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.BoolMask, displayName: outputPortDisplayName)
            };
        }

        private void RefreshChannelDeclarations()
        {
            if (!string.IsNullOrWhiteSpace(_inputChannelName))
            {
                _channelDeclarations = new[]
                {
                    new ChannelDeclaration(_inputChannelName, ChannelType.BoolMask, false),
                    new ChannelDeclaration(_outputChannelName, ChannelType.BoolMask, true)
                };
                return;
            }

            _channelDeclarations = new[]
            {
                new ChannelDeclaration(_outputChannelName, ChannelType.BoolMask, true)
            };
        }

        [BurstCompile]
        private struct ColumnSurfaceBandJob : IJob
        {
            [ReadOnly]
            public NativeArray<byte> Input;

            public NativeArray<byte> Output;
            public int Width;
            public int Height;
            public int MinDepth;
            public int MaxDepth;

            public void Execute()
            {
                int x;
                for (x = 0; x < Width; x++)
                {
                    int topSolidY = -1;
                    int y;
                    for (y = Height - 1; y >= 0; y--)
                    {
                        int index = (y * Width) + x;
                        if (Input[index] != 0)
                        {
                            topSolidY = y;
                            break;
                        }
                    }

                    for (y = 0; y < Height; y++)
                    {
                        int index = (y * Width) + x;
                        if (topSolidY < 0 || Input[index] == 0)
                        {
                            Output[index] = 0;
                            continue;
                        }

                        int depth = topSolidY - y;
                        Output[index] = depth >= MinDepth && depth <= MaxDepth ? (byte)1 : (byte)0;
                    }
                }
            }
        }
    }
}
