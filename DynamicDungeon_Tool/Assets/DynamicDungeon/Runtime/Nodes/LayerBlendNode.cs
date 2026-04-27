using System;
using System.Collections.Generic;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace DynamicDungeon.Runtime.Nodes
{
    [NodeCategory("Blend")]
    [NodeDisplayName("Layer Blend")]
    [Description("Combines two float inputs using a configurable layer blend mode with 0 to 1 clamped output.")]
    public sealed class LayerBlendNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        private const int DefaultBatchSize = 64;
        private const string DefaultNodeName = "Layer Blend";
        private const string InputAPortName = "A";
        private const string InputBPortName = "B";
        private const string FallbackOutputPortName = "Output";
        private const string PreferredOutputDisplayName = FallbackOutputPortName;

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private readonly string _outputChannelName;
        private readonly NodePortDefinition[] _ports;

        private string _inputAChannelName;
        private string _inputBChannelName;
        [Description("Blend mode used to combine inputs A and B.")]
        private LayerBlendMode _mode;
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

        public LayerBlendNode(string nodeId, string nodeName, string inputAChannelName, string inputBChannelName, string outputChannelName, LayerBlendMode mode = LayerBlendMode.Multiply)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _inputAChannelName = inputAChannelName ?? string.Empty;
            _inputBChannelName = inputBChannelName ?? string.Empty;
            _outputChannelName = string.IsNullOrWhiteSpace(outputChannelName) || string.Equals(outputChannelName, GraphPortNameUtility.LegacyGenericOutputDisplayName, StringComparison.Ordinal) ? GraphPortNameUtility.CreateGeneratedOutputPortName(nodeId, FallbackOutputPortName) : outputChannelName;
            _mode = mode;
            string outputPortDisplayName = GraphPortNameUtility.ResolveOutputDisplayName(nodeId, _outputChannelName, PreferredOutputDisplayName);
            _ports = new[]
            {
                new NodePortDefinition(InputAPortName, PortDirection.Input, ChannelType.Float, PortCapacity.Single, true),
                new NodePortDefinition(InputBPortName, PortDirection.Input, ChannelType.Float, PortCapacity.Single, true),
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.Float, displayName: outputPortDisplayName)
            };

            RefreshChannelDeclarations();
        }

        public void ReceiveInputConnections(IReadOnlyDictionary<string, string> inputConnections)
        {
            _inputAChannelName = ResolveInputConnection(inputConnections, InputAPortName);
            _inputBChannelName = ResolveInputConnection(inputConnections, InputBPortName);
            RefreshChannelDeclarations();
        }

        public void ReceiveParameter(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (string.Equals(name, "mode", StringComparison.OrdinalIgnoreCase))
            {
                LayerBlendMode parsedMode;
                if (Enum.TryParse(value, true, out parsedMode))
                {
                    _mode = parsedMode;
                }
            }
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            NativeArray<float> inputA = context.GetFloatChannel(_inputAChannelName);
            NativeArray<float> inputB = context.GetFloatChannel(_inputBChannelName);
            NativeArray<float> output = context.GetFloatChannel(_outputChannelName);

            switch (_mode)
            {
                case LayerBlendMode.Screen:
                    ScreenBlendJob screenJob = new ScreenBlendJob
                    {
                        InputA = inputA,
                        InputB = inputB,
                        Output = output
                    };
                    return screenJob.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
                case LayerBlendMode.Overlay:
                    OverlayBlendJob overlayJob = new OverlayBlendJob
                    {
                        InputA = inputA,
                        InputB = inputB,
                        Output = output
                    };
                    return overlayJob.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
                case LayerBlendMode.Difference:
                    DifferenceBlendJob differenceJob = new DifferenceBlendJob
                    {
                        InputA = inputA,
                        InputB = inputB,
                        Output = output
                    };
                    return differenceJob.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
                case LayerBlendMode.Add:
                    AddBlendJob addJob = new AddBlendJob
                    {
                        InputA = inputA,
                        InputB = inputB,
                        Output = output
                    };
                    return addJob.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
                case LayerBlendMode.Subtract:
                    SubtractBlendJob subtractJob = new SubtractBlendJob
                    {
                        InputA = inputA,
                        InputB = inputB,
                        Output = output
                    };
                    return subtractJob.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
                default:
                    MultiplyBlendJob multiplyJob = new MultiplyBlendJob
                    {
                        InputA = inputA,
                        InputB = inputB,
                        Output = output
                    };
                    return multiplyJob.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
            }
        }

        private static string ResolveInputConnection(IReadOnlyDictionary<string, string> inputConnections, string portName)
        {
            string inputChannelName;
            if (inputConnections != null && inputConnections.TryGetValue(portName, out inputChannelName))
            {
                return inputChannelName ?? string.Empty;
            }

            return string.Empty;
        }

        private void RefreshChannelDeclarations()
        {
            List<ChannelDeclaration> channelDeclarations = new List<ChannelDeclaration>(3);

            if (!string.IsNullOrWhiteSpace(_inputAChannelName))
            {
                channelDeclarations.Add(new ChannelDeclaration(_inputAChannelName, ChannelType.Float, false));
            }

            if (!string.IsNullOrWhiteSpace(_inputBChannelName))
            {
                channelDeclarations.Add(new ChannelDeclaration(_inputBChannelName, ChannelType.Float, false));
            }

            channelDeclarations.Add(new ChannelDeclaration(_outputChannelName, ChannelType.Float, true));
            _channelDeclarations = channelDeclarations.ToArray();
        }

        [BurstCompile]
        private struct MultiplyBlendJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<float> InputA;

            [ReadOnly]
            public NativeArray<float> InputB;

            public NativeArray<float> Output;

            public void Execute(int index)
            {
                Output[index] = math.saturate(InputA[index] * InputB[index]);
            }
        }

        [BurstCompile]
        private struct ScreenBlendJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<float> InputA;

            [ReadOnly]
            public NativeArray<float> InputB;

            public NativeArray<float> Output;

            public void Execute(int index)
            {
                float aValue = InputA[index];
                float bValue = InputB[index];
                Output[index] = math.saturate(1.0f - ((1.0f - aValue) * (1.0f - bValue)));
            }
        }

        [BurstCompile]
        private struct OverlayBlendJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<float> InputA;

            [ReadOnly]
            public NativeArray<float> InputB;

            public NativeArray<float> Output;

            public void Execute(int index)
            {
                float aValue = InputA[index];
                float bValue = InputB[index];
                float blendedValue = aValue < 0.5f
                    ? 2.0f * aValue * bValue
                    : 1.0f - (2.0f * (1.0f - aValue) * (1.0f - bValue));
                Output[index] = math.saturate(blendedValue);
            }
        }

        [BurstCompile]
        private struct DifferenceBlendJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<float> InputA;

            [ReadOnly]
            public NativeArray<float> InputB;

            public NativeArray<float> Output;

            public void Execute(int index)
            {
                Output[index] = math.saturate(math.abs(InputA[index] - InputB[index]));
            }
        }

        [BurstCompile]
        private struct AddBlendJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<float> InputA;

            [ReadOnly]
            public NativeArray<float> InputB;

            public NativeArray<float> Output;

            public void Execute(int index)
            {
                Output[index] = math.clamp(InputA[index] + InputB[index], 0.0f, 1.0f);
            }
        }

        [BurstCompile]
        private struct SubtractBlendJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<float> InputA;

            [ReadOnly]
            public NativeArray<float> InputB;

            public NativeArray<float> Output;

            public void Execute(int index)
            {
                Output[index] = math.clamp(InputA[index] - InputB[index], 0.0f, 1.0f);
            }
        }
    }
}
