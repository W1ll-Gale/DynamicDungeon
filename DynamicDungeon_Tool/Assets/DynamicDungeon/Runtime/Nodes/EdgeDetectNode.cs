using System;
using System.Collections.Generic;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using System.Globalization;
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
    [NodeDisplayName("Edge Detect")]
    [Description("Marks tiles whose four cardinal neighbours differ from the input value.")]
    public sealed class EdgeDetectNode : IGenNode, IInputConnectionReceiver, IParameterReceiver, IParameterVisibilityProvider
    {
        private const int DefaultBatchSize = 64;
        private const float DefaultTolerancePercent = 1.0f;
        private const string DefaultNodeName = "Edge Detect";
        private const string InputPortName = "Input";
        private const string FallbackOutputPortName = "Output";
        private const string PreferredOutputDisplayName = FallbackOutputPortName;

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private string _outputChannelName;
        private readonly ChannelType _inputType;
        private NodePortDefinition[] _ports;

        private string _inputChannelName;
        [Range(0.0f, 100.0f)]
        [InspectorName("Tolerance %")]
        [Description("Minimum float difference percentage required for neighbouring tiles to count as different.")]
        private float _tolerancePercent;
        private ChannelDeclaration[] _channelDeclarations;

        public IReadOnlyList<NodePortDefinition> Ports
        {
            get
            {
                return _ports;
            }
        }

        public IReadOnlyList<ChannelDeclaration> ChannelDeclarations
        {
            get
            {
                return _channelDeclarations;
            }
        }

        public IReadOnlyList<BlackboardKey> BlackboardDeclarations
        {
            get
            {
                return _blackboardDeclarations;
            }
        }

        public string NodeId
        {
            get
            {
                return _nodeId;
            }
        }

        public string NodeName
        {
            get
            {
                return _nodeName;
            }
        }

        public EdgeDetectNode(string nodeId, string nodeName, string inputChannelName, string outputChannelName, ChannelType inputType = ChannelType.Float, float tolerancePercent = DefaultTolerancePercent)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _inputChannelName = inputChannelName ?? string.Empty;
            _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(nodeId, outputChannelName, FallbackOutputPortName);
            _inputType = inputType == ChannelType.Int ? ChannelType.Int : ChannelType.Float;
            _tolerancePercent = math.clamp(tolerancePercent, 0.0f, 100.0f);

            RefreshPorts();
            RefreshChannelDeclarations();
        }

        private void RefreshPorts()
        {
            string outputPortDisplayName = GraphPortNameUtility.ResolveOutputDisplayName(_nodeId, _outputChannelName, PreferredOutputDisplayName);
            _ports = new[]
            {
                new NodePortDefinition(InputPortName, PortDirection.Input, _inputType, PortCapacity.Single, true),
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.BoolMask, displayName: outputPortDisplayName)
            };
        }

        public void ReceiveInputConnections(InputConnectionMap inputConnections)
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

            if (string.Equals(name, "tolerancePercent", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "tolerance", StringComparison.OrdinalIgnoreCase))
            {
                float parsedTolerancePercent;
                if (float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsedTolerancePercent))
                {
                    _tolerancePercent = math.clamp(parsedTolerancePercent, 0.0f, 100.0f);
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

        public bool IsParameterVisible(string parameterName)
        {
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                return false;
            }

            return !string.Equals(parameterName, "tolerance", StringComparison.OrdinalIgnoreCase);
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            NativeArray<byte> output = context.GetBoolMaskChannel(_outputChannelName);
            if (_inputType == ChannelType.Int)
            {
                NativeArray<int> input = context.GetIntChannel(_inputChannelName);
                EdgeDetectIntJob intJob = new EdgeDetectIntJob
                {
                    Input = input,
                    Output = output,
                    Width = context.Width,
                    Height = context.Height
                };

                return intJob.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
            }

            NativeArray<float> floatInput = context.GetFloatChannel(_inputChannelName);
            EdgeDetectFloatJob floatJob = new EdgeDetectFloatJob
            {
                Input = floatInput,
                Output = output,
                Width = context.Width,
                Height = context.Height,
                Tolerance = _tolerancePercent * 0.01f
            };

            return floatJob.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
        }

        private void RefreshChannelDeclarations()
        {
            if (!string.IsNullOrWhiteSpace(_inputChannelName))
            {
                _channelDeclarations = new[]
                {
                    new ChannelDeclaration(_inputChannelName, _inputType, false),
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
        private struct EdgeDetectFloatJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<float> Input;

            public NativeArray<byte> Output;
            public int Width;
            public int Height;
            public float Tolerance;

            public void Execute(int index)
            {
                int x = index % Width;
                int y = index / Width;
                float centre = Input[index];
                bool differs = IsDifferent(GetValue(x - 1, y), centre) ||
                    IsDifferent(GetValue(x + 1, y), centre) ||
                    IsDifferent(GetValue(x, y - 1), centre) ||
                    IsDifferent(GetValue(x, y + 1), centre);
                Output[index] = differs ? (byte)1 : (byte)0;
            }

            private bool IsDifferent(float neighbour, float centre)
            {
                return math.abs(neighbour - centre) > Tolerance;
            }

            private float GetValue(int x, int y)
            {
                if (x < 0 || x >= Width || y < 0 || y >= Height)
                {
                    return 0.0f;
                }

                return Input[y * Width + x];
            }
        }

        [BurstCompile]
        private struct EdgeDetectIntJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<int> Input;

            public NativeArray<byte> Output;
            public int Width;
            public int Height;

            public void Execute(int index)
            {
                int x = index % Width;
                int y = index / Width;
                int centre = Input[index];
                bool differs = GetValue(x - 1, y) != centre ||
                    GetValue(x + 1, y) != centre ||
                    GetValue(x, y - 1) != centre ||
                    GetValue(x, y + 1) != centre;
                Output[index] = differs ? (byte)1 : (byte)0;
            }

            private int GetValue(int x, int y)
            {
                if (x < 0 || x >= Width || y < 0 || y >= Height)
                {
                    return 0;
                }

                return Input[y * Width + x];
            }
        }
    }
}
