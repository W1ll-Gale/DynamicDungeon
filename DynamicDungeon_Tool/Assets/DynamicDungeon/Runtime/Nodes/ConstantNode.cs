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

namespace DynamicDungeon.Runtime.Nodes
{
    [NodeCategory("Noise")]
    [NodeDisplayName("Constant")]
    [Description("Emits a single constant float or int value to every tile in the channel.")]
    public sealed class ConstantNode : IGenNode, IParameterReceiver
    {
        private const string DefaultNodeName = "Constant";
        private const int DefaultBatchSize = 64;
        private const string PreferredOutputDisplayName = GraphPortNameUtility.LegacyGenericOutputDisplayName;

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private NodePortDefinition[] _ports;
        private ChannelDeclaration[] _channelDeclarations;
        private readonly string _nodeId;
        private readonly string _nodeName;
        private readonly string _outputChannelName;

        [Description("Determines whether the node writes a float or integer channel.")]
        private ChannelType _outputType;

        [Description("The constant float value written to every tile when Output Type is Float.")]
        private float _floatValue;

        [Description("The constant integer value written to every tile when Output Type is Int.")]
        private int _intValue;

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

        public string OutputChannelName
        {
            get
            {
                return _outputChannelName;
            }
        }

        public ChannelType OutputType
        {
            get
            {
                return _outputType;
            }
        }

        public float FloatValue
        {
            get
            {
                return _floatValue;
            }
        }

        public int IntValue
        {
            get
            {
                return _intValue;
            }
        }

        public ConstantNode(string nodeId, string nodeName, string outputChannelName) : this(nodeId, nodeName, outputChannelName, ChannelType.Float, 0.0f, 0)
        {
        }

        public ConstantNode(string nodeId, string nodeName, string outputChannelName, ChannelType outputType, float floatValue = 0.0f, int intValue = 0)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            if (string.IsNullOrWhiteSpace(nodeName))
            {
                throw new ArgumentException("Node name must be non-empty.", nameof(nodeName));
            }

            if (string.IsNullOrWhiteSpace(outputChannelName))
            {
                throw new ArgumentException("Output channel name must be non-empty.", nameof(outputChannelName));
            }

            if (outputType != ChannelType.Float && outputType != ChannelType.Int)
            {
                throw new ArgumentOutOfRangeException(nameof(outputType), "ConstantNode only supports Float or Int output types.");
            }

            _nodeId = nodeId;
            _nodeName = nodeName;
            _outputChannelName = outputChannelName;
            _outputType = outputType;
            _floatValue = floatValue;
            _intValue = intValue;
            RefreshOutputDeclarations();
        }

        public void ReceiveParameter(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (string.Equals(name, "floatValue", StringComparison.OrdinalIgnoreCase))
            {
                float parsedFloat;
                if (float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsedFloat))
                {
                    _floatValue = parsedFloat;
                }

                return;
            }

            if (string.Equals(name, "outputType", StringComparison.OrdinalIgnoreCase))
            {
                ChannelType parsedOutputType;
                try
                {
                    parsedOutputType = (ChannelType)Enum.Parse(typeof(ChannelType), value, true);
                }
                catch
                {
                    return;
                }

                if (parsedOutputType != ChannelType.Float && parsedOutputType != ChannelType.Int)
                {
                    return;
                }

                if (_outputType != parsedOutputType)
                {
                    _outputType = parsedOutputType;
                    RefreshOutputDeclarations();
                }

                return;
            }

            if (string.Equals(name, "intValue", StringComparison.OrdinalIgnoreCase))
            {
                int parsedInt;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedInt))
                {
                    _intValue = parsedInt;
                }
            }
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            if (_outputType == ChannelType.Float)
            {
                NativeArray<float> output = context.GetFloatChannel(_outputChannelName);
                FloatFillJob floatJob = new FloatFillJob
                {
                    Output = output,
                    Value = _floatValue
                };
                return floatJob.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
            }

            NativeArray<int> intOutput = context.GetIntChannel(_outputChannelName);
            IntFillJob intJob = new IntFillJob
            {
                Output = intOutput,
                Value = _intValue
            };
            return intJob.Schedule(intOutput.Length, DefaultBatchSize, context.InputDependency);
        }

        private void RefreshOutputDeclarations()
        {
            string outputPortDisplayName = GraphPortNameUtility.ResolveOutputDisplayName(_nodeId, _outputChannelName, PreferredOutputDisplayName);
            _ports = new[]
            {
                new NodePortDefinition(_outputChannelName, PortDirection.Output, _outputType, displayName: outputPortDisplayName)
            };

            _channelDeclarations = new[]
            {
                new ChannelDeclaration(_outputChannelName, _outputType, true)
            };
        }

        [BurstCompile]
        internal struct FloatFillJob : IJobParallelFor
        {
            public NativeArray<float> Output;
            public float Value;

            public void Execute(int index)
            {
                Output[index] = Value;
            }
        }

        [BurstCompile]
        internal struct IntFillJob : IJobParallelFor
        {
            public NativeArray<int> Output;
            public int Value;

            public void Execute(int index)
            {
                Output[index] = Value;
            }
        }
    }
}
