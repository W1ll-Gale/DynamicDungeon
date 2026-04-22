using System;
using System.Collections.Generic;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using System.Globalization;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using DynamicDungeon.Runtime.Semantic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace DynamicDungeon.Runtime.Nodes
{
    [BurstCompile]
    internal struct BoolMaskToLogicalIdJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<byte> Input;

        public NativeArray<int> Output;
        public int TrueLogicalId;
        public int FalseLogicalId;

        public void Execute(int index)
        {
            Output[index] = Input[index] != 0 ? TrueLogicalId : FalseLogicalId;
        }
    }

    [NodeCategory("Output")]
    [NodeDisplayName("Bool Mask to Logical ID")]
    [Description("Converts a bool mask into logical tile IDs for true and false cells.")]
    public sealed class BoolMaskToLogicalIdNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        private const int DefaultBatchSize = 64;
        private const string DefaultNodeName = "Bool Mask To Logical ID";
        private const string InputPortName = "Input";
        private const string DefaultOutputChannelName = "LogicalIds";
        private const string PreferredOutputDisplayName = DefaultOutputChannelName;

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private readonly string _outputChannelName;
        private readonly NodePortDefinition[] _ports;

        private string _inputChannelName;
        [MinValue(0.0f)]
        [Description("Logical tile ID written anywhere the input mask is true.")]
        private int _trueLogicalId;
        [MinValue(0.0f)]
        [Description("Logical tile ID written anywhere the input mask is false.")]
        private int _falseLogicalId;
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

        public BoolMaskToLogicalIdNode(string nodeId, string nodeName, string inputChannelName, string outputChannelName, int trueLogicalId = 2, int falseLogicalId = 1)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _inputChannelName = inputChannelName ?? string.Empty;
            _outputChannelName = string.IsNullOrWhiteSpace(outputChannelName) ? DefaultOutputChannelName : outputChannelName;
            _trueLogicalId = trueLogicalId;
            _falseLogicalId = falseLogicalId;
            string outputPortDisplayName = GraphPortNameUtility.ResolveOutputDisplayName(nodeId, _outputChannelName, PreferredOutputDisplayName);
            _ports = new[]
            {
                new NodePortDefinition(InputPortName, PortDirection.Input, ChannelType.BoolMask, PortCapacity.Single, true),
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.Int, displayName: outputPortDisplayName)
            };

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

            int parsedValue;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedValue))
            {
                return;
            }

            if (string.Equals(name, "trueLogicalId", StringComparison.OrdinalIgnoreCase))
            {
                _trueLogicalId = parsedValue;
                return;
            }

            if (string.Equals(name, "falseLogicalId", StringComparison.OrdinalIgnoreCase))
            {
                _falseLogicalId = parsedValue;
            }
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            NativeArray<byte> input = context.GetBoolMaskChannel(_inputChannelName);
            NativeArray<int> output = context.GetIntChannel(_outputChannelName);

            BoolMaskToLogicalIdJob job = new BoolMaskToLogicalIdJob
            {
                Input = input,
                Output = output,
                TrueLogicalId = _trueLogicalId,
                FalseLogicalId = _falseLogicalId
            };

            return job.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
        }

        private void RefreshChannelDeclarations()
        {
            if (!string.IsNullOrWhiteSpace(_inputChannelName))
            {
                _channelDeclarations = new[]
                {
                    new ChannelDeclaration(_inputChannelName, ChannelType.BoolMask, false),
                    new ChannelDeclaration(_outputChannelName, ChannelType.Int, true)
                };
                return;
            }

            _channelDeclarations = new[]
            {
                new ChannelDeclaration(_outputChannelName, ChannelType.Int, true)
            };
        }
    }
}
