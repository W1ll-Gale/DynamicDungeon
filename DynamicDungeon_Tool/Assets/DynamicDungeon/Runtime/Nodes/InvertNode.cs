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
    [NodeCategory("Filters")]
    [NodeDisplayName("Invert")]
    [Description("Inverts an input channel, flipping floats, ints, or bool mask values cell by cell.")]
    public sealed class InvertNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        private const int DefaultBatchSize = 64;
        private const string DefaultNodeName = "Invert";
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

        public InvertNode(string nodeId, string nodeName, string inputChannelName, string outputChannelName, ChannelType inputType)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _inputChannelName = inputChannelName ?? string.Empty;
            _outputChannelName = string.IsNullOrWhiteSpace(outputChannelName) || string.Equals(outputChannelName, GraphPortNameUtility.LegacyGenericOutputDisplayName, StringComparison.Ordinal) ? GraphPortNameUtility.CreateGeneratedOutputPortName(nodeId, FallbackOutputPortName) : outputChannelName;
            _inputType = inputType;

            RefreshPorts();
            RefreshChannelDeclarations();
        }

        private void RefreshPorts()
        {
            string outputPortDisplayName = GraphPortNameUtility.ResolveOutputDisplayName(_nodeId, _outputChannelName, PreferredOutputDisplayName);
            _ports = new[]
            {
                new NodePortDefinition(InputPortName, PortDirection.Input, _inputType, PortCapacity.Single, true),
                new NodePortDefinition(_outputChannelName, PortDirection.Output, _inputType, displayName: outputPortDisplayName)
            };
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
            if (string.Equals(name, "outputChannelName", StringComparison.OrdinalIgnoreCase))
            {
                _outputChannelName = string.IsNullOrWhiteSpace(value) || string.Equals(value, GraphPortNameUtility.LegacyGenericOutputDisplayName, StringComparison.Ordinal) ? GraphPortNameUtility.CreateGeneratedOutputPortName(_nodeId, FallbackOutputPortName) : value;
                RefreshPorts();
                RefreshChannelDeclarations();
            }
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            if (_inputType == ChannelType.Float)
            {
                NativeArray<float> input = context.GetFloatChannel(_inputChannelName);
                NativeArray<float> output = context.GetFloatChannel(_outputChannelName);
                InvertFloatJob floatJob = new InvertFloatJob
                {
                    Input = input,
                    Output = output
                };

                return floatJob.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
            }

            if (_inputType == ChannelType.BoolMask)
            {
                NativeArray<byte> input = context.GetBoolMaskChannel(_inputChannelName);
                NativeArray<byte> output = context.GetBoolMaskChannel(_outputChannelName);
                InvertBoolMaskJob boolMaskJob = new InvertBoolMaskJob
                {
                    Input = input,
                    Output = output
                };

                return boolMaskJob.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
            }

            NativeArray<int> intInput = context.GetIntChannel(_inputChannelName);
            NativeArray<int> intOutput = context.GetIntChannel(_outputChannelName);
            InvertIntJob intJob = new InvertIntJob
            {
                Input = intInput,
                Output = intOutput
            };

            return intJob.Schedule(intOutput.Length, DefaultBatchSize, context.InputDependency);
        }

        private void RefreshChannelDeclarations()
        {
            if (!string.IsNullOrWhiteSpace(_inputChannelName))
            {
                _channelDeclarations = new[]
                {
                    new ChannelDeclaration(_inputChannelName, _inputType, false),
                    new ChannelDeclaration(_outputChannelName, _inputType, true)
                };
                return;
            }

            _channelDeclarations = new[]
            {
                new ChannelDeclaration(_outputChannelName, _inputType, true)
            };
        }

        [BurstCompile]
        private struct InvertFloatJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<float> Input;

            public NativeArray<float> Output;

            public void Execute(int index)
            {
                Output[index] = 1.0f - Input[index];
            }
        }

        [BurstCompile]
        private struct InvertBoolMaskJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<byte> Input;

            public NativeArray<byte> Output;

            public void Execute(int index)
            {
                Output[index] = Input[index] == 0 ? (byte)1 : (byte)0;
            }
        }

        [BurstCompile]
        private struct InvertIntJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<int> Input;

            public NativeArray<int> Output;

            public void Execute(int index)
            {
                Output[index] = 1 - Input[index];
            }
        }
    }
}
