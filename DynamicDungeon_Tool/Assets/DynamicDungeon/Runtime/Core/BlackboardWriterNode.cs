using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace DynamicDungeon.Runtime.Core
{
    public sealed class BlackboardWriterNode : IGenNode
    {
        private const string DefaultNodeName = "Blackboard Writer";

        private static readonly NodePortDefinition[] _ports = Array.Empty<NodePortDefinition>();
        private static readonly ChannelDeclaration[] _channelDeclarations = Array.Empty<ChannelDeclaration>();

        private readonly BlackboardKey[] _blackboardDeclarations;
        private readonly string _nodeId;
        private readonly string _nodeName;
        private readonly FixedString64Bytes _key;
        private readonly float _value;

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

        public BlackboardWriterNode(string nodeId, string key, float value) : this(nodeId, DefaultNodeName, key, value)
        {
        }

        public BlackboardWriterNode(string nodeId, string nodeName, string key, float value)
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

            _blackboardDeclarations = new[]
            {
                new BlackboardKey(key, true)
            };

            _nodeId = nodeId;
            _nodeName = nodeName;
            _key = new FixedString64Bytes(key);
            _value = value;
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            BlackboardWriteJob job = new BlackboardWriteJob
            {
                NumericBlackboard = context.NumericBlackboard.NativeMap,
                Key = _key,
                Value = _value
            };

            return job.Schedule(context.InputDependency);
        }

        [BurstCompile]
        private struct BlackboardWriteJob : IJob
        {
            public NativeHashMap<FixedString64Bytes, float> NumericBlackboard;
            public FixedString64Bytes Key;
            public float Value;

            public void Execute()
            {
                NumericBlackboard[Key] = Value;
            }
        }
    }
}
