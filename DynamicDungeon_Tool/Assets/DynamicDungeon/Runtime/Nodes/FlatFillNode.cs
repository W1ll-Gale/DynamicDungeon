using System;
using System.Collections.Generic;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using DynamicDungeon.Runtime.Core;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace DynamicDungeon.Runtime.Nodes
{
    [Description("Outputs a flat float map where every cell uses the same fill value.")]
    public sealed class FlatFillNode : IGenNode
    {
        private const string DefaultNodeName = "Flat Fill";
        private const string OutputChannelName = "FlatOutput";
        private const int DefaultBatchSize = 64;

        private static readonly NodePortDefinition[] _ports =
        {
            new NodePortDefinition(OutputChannelName, PortDirection.Output, ChannelType.Float)
        };

        private static readonly ChannelDeclaration[] _channelDeclarations =
        {
            new ChannelDeclaration(OutputChannelName, ChannelType.Float, true)
        };

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
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

        public FlatFillNode(string nodeId, float fillValue) : this(nodeId, DefaultNodeName, fillValue)
        {
        }

        public FlatFillNode(string nodeId, string nodeName, float fillValue)
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
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            NativeArray<float> output = context.GetFloatChannel(OutputChannelName);
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
