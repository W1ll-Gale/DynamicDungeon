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
    [NodeCategory("Generator")]
    [NodeDisplayName("Flat Fill")]
    [Description("Outputs a flat float map where every cell uses the same fill value.")]
    public sealed class FlatFillNode : IGenNode, IParameterReceiver
    {
        private const string DefaultNodeName = "Flat Fill";
        private const string FallbackOutputPortName = "Output";
        private const int DefaultBatchSize = 64;
        private const string PreferredOutputDisplayName = FallbackOutputPortName;

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private NodePortDefinition[] _ports;
        private ChannelDeclaration[] _channelDeclarations;
        private readonly string _nodeId;
        private readonly string _nodeName;
        private string _outputChannelName;
        [Description("Value written into every cell of the output map.")]
        private readonly float _fillValue;

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

        public float FillValue
        {
            get
            {
                return _fillValue;
            }
        }

        public FlatFillNode(string nodeId, float fillValue) : this(nodeId, DefaultNodeName, string.Empty, fillValue)
        {
        }

        public FlatFillNode(string nodeId, string nodeName, float fillValue)
            : this(nodeId, nodeName, string.Empty, fillValue)
        {
        }

        private FlatFillNode(string nodeId, string nodeName, string outputChannelName, float fillValue)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            if (string.IsNullOrWhiteSpace(nodeName))
            {
                throw new ArgumentException("Node name must be non-empty.", nameof(nodeName));
            }

            _nodeId = nodeId;
            _nodeName = nodeName;
            _fillValue = fillValue;
            _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(nodeId, outputChannelName, FallbackOutputPortName);

            RefreshPorts();
            RefreshChannelDeclarations();
        }

        private void RefreshPorts()
        {
            string outputPortDisplayName = GraphPortNameUtility.ResolveOutputDisplayName(_nodeId, _outputChannelName, PreferredOutputDisplayName);
            _ports = new[]
            {
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.Float, displayName: outputPortDisplayName)
            };
        }

        private void RefreshChannelDeclarations()
        {
            _channelDeclarations = new[]
            {
                new ChannelDeclaration(_outputChannelName, ChannelType.Float, true)
            };
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
            NativeArray<float> output = context.GetFloatChannel(_outputChannelName);
            FillJob job = new FillJob
            {
                Output = output,
                FillValue = _fillValue
            };

            return job.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
        }

        [BurstCompile]
        private struct FillJob : IJobParallelFor
        {
            public NativeArray<float> Output;
            public float FillValue;

            public void Execute(int index)
            {
                Output[index] = FillValue;
            }
        }
    }
}
