using System;
using System.Collections.Generic;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace DynamicDungeon.Runtime.Nodes
{
    [NodeCategory("Blend")]
    [NodeDisplayName("Combine Masks")]
    [Description("Combines bool mask inputs using AND, OR, XOR, or NOT operations.")]
    public sealed class CombineMasksNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        private const int DefaultBatchSize = 64;
        private const string DefaultNodeName = "Combine Masks";
        private const string InputAPortName = "A";
        private const string InputBPortName = "B";
        private const string FallbackOutputPortName = "Output";
        private const string PreferredOutputDisplayName = FallbackOutputPortName;

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private string _outputChannelName;
        private NodePortDefinition[] _ports;

        private string _inputAChannelName;
        private string _inputBChannelName;
        [Description("Boolean operation used to combine the input masks.")]
        private MaskOperation _operation;
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

        public CombineMasksNode(string nodeId, string nodeName, string inputAChannelName, string inputBChannelName, string outputChannelName, MaskOperation operation = MaskOperation.AND)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _inputAChannelName = inputAChannelName ?? string.Empty;
            _inputBChannelName = inputBChannelName ?? string.Empty;
            _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(nodeId, outputChannelName, FallbackOutputPortName);
            _operation = operation;

            RefreshPorts();
            RefreshChannelDeclarations();
        }

        private void RefreshPorts()
        {
            string outputPortDisplayName = GraphPortNameUtility.ResolveOutputDisplayName(_nodeId, _outputChannelName, PreferredOutputDisplayName);
            _ports = new[]
            {
                new NodePortDefinition(InputAPortName, PortDirection.Input, ChannelType.BoolMask, PortCapacity.Single, true),
                new NodePortDefinition(InputBPortName, PortDirection.Input, ChannelType.BoolMask, PortCapacity.Single, false),
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.BoolMask, displayName: outputPortDisplayName)
            };
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

            if (string.Equals(name, "operation", StringComparison.OrdinalIgnoreCase))
            {
                MaskOperation parsedOperation;
                if (Enum.TryParse(value, true, out parsedOperation))
                {
                    _operation = parsedOperation;
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
            NativeArray<byte> inputA = context.GetBoolMaskChannel(_inputAChannelName);
            NativeArray<byte> inputB = !string.IsNullOrWhiteSpace(_inputBChannelName)
                ? context.GetBoolMaskChannel(_inputBChannelName)
                : inputA;
            NativeArray<byte> output = context.GetBoolMaskChannel(_outputChannelName);

            switch (_operation)
            {
                case MaskOperation.OR:
                    OrMaskJob orJob = new OrMaskJob
                    {
                        InputA = inputA,
                        InputB = inputB,
                        Output = output
                    };
                    return orJob.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
                case MaskOperation.XOR:
                    XorMaskJob xorJob = new XorMaskJob
                    {
                        InputA = inputA,
                        InputB = inputB,
                        Output = output
                    };
                    return xorJob.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
                case MaskOperation.NOT:
                    NotMaskJob notJob = new NotMaskJob
                    {
                        InputA = inputA,
                        Output = output
                    };
                    return notJob.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
                default:
                    AndMaskJob andJob = new AndMaskJob
                    {
                        InputA = inputA,
                        InputB = inputB,
                        Output = output
                    };
                    return andJob.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
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
                channelDeclarations.Add(new ChannelDeclaration(_inputAChannelName, ChannelType.BoolMask, false));
            }

            if (!string.IsNullOrWhiteSpace(_inputBChannelName))
            {
                channelDeclarations.Add(new ChannelDeclaration(_inputBChannelName, ChannelType.BoolMask, false));
            }

            channelDeclarations.Add(new ChannelDeclaration(_outputChannelName, ChannelType.BoolMask, true));
            _channelDeclarations = channelDeclarations.ToArray();
        }

        [BurstCompile]
        private struct AndMaskJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<byte> InputA;

            [ReadOnly]
            public NativeArray<byte> InputB;

            public NativeArray<byte> Output;

            public void Execute(int index)
            {
                bool result = InputA[index] != 0 && InputB[index] != 0;
                Output[index] = result ? (byte)1 : (byte)0;
            }
        }

        [BurstCompile]
        private struct OrMaskJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<byte> InputA;

            [ReadOnly]
            public NativeArray<byte> InputB;

            public NativeArray<byte> Output;

            public void Execute(int index)
            {
                bool result = InputA[index] != 0 || InputB[index] != 0;
                Output[index] = result ? (byte)1 : (byte)0;
            }
        }

        [BurstCompile]
        private struct XorMaskJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<byte> InputA;

            [ReadOnly]
            public NativeArray<byte> InputB;

            public NativeArray<byte> Output;

            public void Execute(int index)
            {
                bool aValue = InputA[index] != 0;
                bool bValue = InputB[index] != 0;
                bool result = aValue != bValue;
                Output[index] = result ? (byte)1 : (byte)0;
            }
        }

        [BurstCompile]
        private struct NotMaskJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<byte> InputA;

            public NativeArray<byte> Output;

            public void Execute(int index)
            {
                bool result = InputA[index] == 0;
                Output[index] = result ? (byte)1 : (byte)0;
            }
        }
    }
}
