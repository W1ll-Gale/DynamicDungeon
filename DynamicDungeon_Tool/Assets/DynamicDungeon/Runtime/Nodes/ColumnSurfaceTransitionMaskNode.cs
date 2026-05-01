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
using UnityEngine;

namespace DynamicDungeon.Runtime.Nodes
{
    [NodeCategory("Filter")]
    [NodeDisplayName("Column Surface Transition Mask")]
    [Description("For each solid column, creates a coherent mask that gradually becomes true with depth beneath that column's highest solid tile.")]
    public sealed class ColumnSurfaceTransitionMaskNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        private const string DefaultNodeName = "Column Surface Transition Mask";
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
        [DescriptionAttribute("Depth below the highest solid tile where the transition starts.")]
        private int _startDepth = 8;

        [MinValue(0.0f)]
        [DescriptionAttribute("Depth below the highest solid tile where the transition is fully complete.")]
        private int _endDepth = 32;

        [MinValue(0.001f)]
        [DescriptionAttribute("Frequency of the coherent transition breakup. Lower values make larger, calmer patches.")]
        private float _noiseFrequency = 0.1f;

        public IReadOnlyList<NodePortDefinition> Ports => _ports;
        public IReadOnlyList<ChannelDeclaration> ChannelDeclarations => _channelDeclarations;
        public IReadOnlyList<BlackboardKey> BlackboardDeclarations => _blackboardDeclarations;
        public string NodeId => _nodeId;
        public string NodeName => _nodeName;

        public ColumnSurfaceTransitionMaskNode(
            string nodeId,
            string nodeName,
            string inputChannelName = "",
            string outputChannelName = "",
            int startDepth = 8,
            int endDepth = 32,
            float noiseFrequency = 0.1f)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _inputChannelName = inputChannelName ?? string.Empty;
            _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(nodeId, outputChannelName, FallbackOutputPortName);
            _startDepth = math.max(0, startDepth);
            _endDepth = math.max(_startDepth + 1, endDepth);
            _noiseFrequency = math.max(0.001f, noiseFrequency);

            RefreshPorts();
            RefreshChannelDeclarations();
        }

        public void ReceiveInputConnections(IReadOnlyDictionary<string, string> inputConnections)
        {
            string inputChannelName;
            _inputChannelName = inputConnections != null && inputConnections.TryGetValue(InputPortName, out inputChannelName)
                ? inputChannelName ?? string.Empty
                : string.Empty;
            RefreshChannelDeclarations();
        }

        public void ReceiveParameter(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            int parsedInt;
            if (string.Equals(name, "startDepth", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedInt))
                {
                    _startDepth = math.max(0, parsedInt);
                    _endDepth = math.max(_startDepth + 1, _endDepth);
                }

                return;
            }

            if (string.Equals(name, "endDepth", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedInt))
                {
                    _endDepth = math.max(_startDepth + 1, parsedInt);
                }

                return;
            }

            float parsedFloat;
            if (string.Equals(name, "noiseFrequency", StringComparison.OrdinalIgnoreCase))
            {
                if (float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsedFloat))
                {
                    _noiseFrequency = math.max(0.001f, parsedFloat);
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
            ulong seedBits = unchecked((ulong)context.LocalSeed);
            float seedOffsetX = (float)(seedBits & 0xffffUL) * 0.173f;
            float seedOffsetY = (float)((seedBits >> 16) & 0xffffUL) * 0.219f;

            ColumnSurfaceTransitionMaskJob job = new ColumnSurfaceTransitionMaskJob
            {
                Input = input,
                Output = output,
                Width = context.Width,
                Height = context.Height,
                StartDepth = _startDepth,
                EndDepth = _endDepth,
                NoiseFrequency = _noiseFrequency,
                SeedOffset = new float2(seedOffsetX, seedOffsetY)
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
        private struct ColumnSurfaceTransitionMaskJob : IJob
        {
            [ReadOnly]
            public NativeArray<byte> Input;

            public NativeArray<byte> Output;
            public int Width;
            public int Height;
            public int StartDepth;
            public int EndDepth;
            public float NoiseFrequency;
            public float2 SeedOffset;

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
                        if (depth < StartDepth)
                        {
                            Output[index] = 0;
                            continue;
                        }

                        if (depth >= EndDepth)
                        {
                            Output[index] = 1;
                            continue;
                        }

                        float t = math.saturate((float)(depth - StartDepth) / (float)(EndDepth - StartDepth));
                        t = t * t * (3.0f - (2.0f * t));
                        float coherentNoise = (noise.cnoise((new float2(x, y) + SeedOffset) * NoiseFrequency) * 0.5f) + 0.5f;
                        Output[index] = coherentNoise <= t ? (byte)1 : (byte)0;
                    }
                }
            }
        }
    }
}
