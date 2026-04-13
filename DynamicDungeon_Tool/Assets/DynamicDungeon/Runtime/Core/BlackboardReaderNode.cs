using System;
using System.Collections.Generic;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace DynamicDungeon.Runtime.Core
{
    [NodeCategory("Blackboard")]
    [NodeDisplayName("Blackboard Reader")]
    [Description("Reads a numeric blackboard value and writes it into every cell of a float output channel.")]
    public sealed class BlackboardReaderNode : IGenNode
    {
        private const string DefaultNodeName = "Blackboard Reader";
        private const int DefaultBatchSize = 64;

        private readonly NodePortDefinition[] _ports;
        private readonly ChannelDeclaration[] _channelDeclarations;
        private readonly BlackboardKey[] _blackboardDeclarations;
        private readonly string _nodeId;
        private readonly string _nodeName;
        private readonly string _outputChannelName;
        [Description("Blackboard key to read from before writing the value into the output channel.")]
        private readonly FixedString64Bytes _key;

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

        public BlackboardReaderNode(string nodeId, string key, string outputChannelName) : this(nodeId, DefaultNodeName, key, outputChannelName)
        {
        }

        public BlackboardReaderNode(string nodeId, string nodeName, string key, string outputChannelName)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            if (string.IsNullOrWhiteSpace(nodeName))
            {
                throw new ArgumentException("Node name must be non-empty.", nameof(nodeName));
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Blackboard key must be non-empty.", nameof(key));
            }

            if (string.IsNullOrWhiteSpace(outputChannelName))
            {
                throw new ArgumentException("Output channel name must be non-empty.", nameof(outputChannelName));
            }

            _ports = new[]
            {
                new NodePortDefinition(outputChannelName, PortDirection.Output, ChannelType.Float)
            };

            _channelDeclarations = new[]
            {
                new ChannelDeclaration(outputChannelName, ChannelType.Float, true)
            };

            _blackboardDeclarations = new[]
            {
                new BlackboardKey(key, false)
            };

            _nodeId = nodeId;
            _nodeName = nodeName;
            _outputChannelName = outputChannelName;
            _key = new FixedString64Bytes(key);
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            NativeArray<float> output = context.GetFloatChannel(_outputChannelName);
            BlackboardReadJob job = new BlackboardReadJob
            {
                NumericBlackboard = context.NumericBlackboard.NativeMap,
                Key = _key,
                Output = output
            };

            return job.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
        }

        [BurstCompile]
        private struct BlackboardReadJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeHashMap<FixedString64Bytes, float> NumericBlackboard;

            [ReadOnly]
            public FixedString64Bytes Key;

            public NativeArray<float> Output;

            public void Execute(int index)
            {
                float value;
                if (NumericBlackboard.TryGetValue(Key, out value))
                {
                    Output[index] = value;
                }
                else
                {
                    Output[index] = 0.0f;
                }
            }
        }
    }
}
