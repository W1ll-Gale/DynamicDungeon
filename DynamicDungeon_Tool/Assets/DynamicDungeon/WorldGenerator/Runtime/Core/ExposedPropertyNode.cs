using System;
using System.Collections.Generic;
using DynamicDungeon.Runtime.Graph;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace DynamicDungeon.Runtime.Core
{
    [NodeCategory("Blackboard")]
    [NodeDisplayName("Exposed Property")]
    [HideInNodeSearch]
    public sealed class ExposedPropertyNode : IGenNode
    {
        private const int DefaultBatchSize = 64;

        private readonly NodePortDefinition[] _ports;
        private readonly ChannelDeclaration[] _channelDeclarations;
        private readonly BlackboardKey[] _blackboardDeclarations;
        private readonly string _nodeId;
        private readonly string _nodeName;
        private readonly string _outputChannelName;
        private readonly ChannelType _propertyType;
        private readonly FixedString64Bytes _propertyId;

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

        public ExposedPropertyNode(
            string nodeId,
            string nodeName,
            [HideInNodeInspector] string propertyId,
            [HideInNodeInspector] string propertyName,
            [HideInNodeInspector] ChannelType propertyType)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            if (string.IsNullOrWhiteSpace(nodeName))
            {
                throw new ArgumentException("Node name must be non-empty.", nameof(nodeName));
            }

            if (string.IsNullOrWhiteSpace(propertyId))
            {
                throw new ArgumentException("Property ID must be non-empty.", nameof(propertyId));
            }

            if (propertyType != ChannelType.Float && propertyType != ChannelType.Int)
            {
                throw new ArgumentException("Exposed property nodes only support Float or Int outputs.", nameof(propertyType));
            }

            string displayName = string.IsNullOrWhiteSpace(propertyName) ? nodeName : propertyName;
            string outputChannelName = ExposedPropertyNodeUtility.CreateOutputChannelName(nodeId);

            _ports = new[]
            {
                new NodePortDefinition(
                    ExposedPropertyNodeUtility.OutputPortName,
                    PortDirection.Output,
                    propertyType,
                    displayName: displayName)
            };

            _channelDeclarations = new[]
            {
                new ChannelDeclaration(outputChannelName, propertyType, true)
            };

            _blackboardDeclarations = new[]
            {
                new BlackboardKey(propertyId, false)
            };

            _nodeId = nodeId;
            _nodeName = nodeName;
            _outputChannelName = outputChannelName;
            _propertyType = propertyType;
            _propertyId = new FixedString64Bytes(propertyId);
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            if (_propertyType == ChannelType.Int)
            {
                NativeArray<int> output = context.GetIntChannel(_outputChannelName);
                BlackboardReadIntJob intJob = new BlackboardReadIntJob
                {
                    NumericBlackboard = context.NumericBlackboard.NativeMap,
                    PropertyId = _propertyId,
                    Output = output
                };

                return intJob.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
            }

            NativeArray<float> floatOutput = context.GetFloatChannel(_outputChannelName);
            BlackboardReadFloatJob floatJob = new BlackboardReadFloatJob
            {
                NumericBlackboard = context.NumericBlackboard.NativeMap,
                PropertyId = _propertyId,
                Output = floatOutput
            };

            return floatJob.Schedule(floatOutput.Length, DefaultBatchSize, context.InputDependency);
        }

        [BurstCompile]
        private struct BlackboardReadFloatJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeHashMap<FixedString64Bytes, float> NumericBlackboard;

            [ReadOnly]
            public FixedString64Bytes PropertyId;

            public NativeArray<float> Output;

            public void Execute(int index)
            {
                float value;
                Output[index] = NumericBlackboard.TryGetValue(PropertyId, out value) ? value : 0.0f;
            }
        }

        [BurstCompile]
        private struct BlackboardReadIntJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeHashMap<FixedString64Bytes, float> NumericBlackboard;

            [ReadOnly]
            public FixedString64Bytes PropertyId;

            public NativeArray<int> Output;

            public void Execute(int index)
            {
                float value;
                if (NumericBlackboard.TryGetValue(PropertyId, out value))
                {
                    Output[index] = (int)math.round(value);
                }
                else
                {
                    Output[index] = 0;
                }
            }
        }
    }
}
