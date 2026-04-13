using System;
using System.Collections.Generic;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using System.Globalization;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace DynamicDungeon.Runtime.Nodes
{
    [NodeCategory("Generators")]
    [NodeDisplayName("Empty Grid")]
    [Description("Creates a fresh float grid and fills every cell with the same value.")]
    public sealed class EmptyGridNode : IGenNode, IParameterReceiver
    {
        private const int DefaultBatchSize = 64;
        private const string DefaultNodeName = "Empty Grid";
        private const string FallbackOutputPortName = "Output";

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private readonly string _outputChannelName;
        private readonly NodePortDefinition[] _ports;
        private readonly ChannelDeclaration[] _channelDeclarations;

        [Description("Value written into every cell of the generated grid.")]
        private float _fillValue;

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

        public EmptyGridNode(string nodeId, string nodeName, string outputChannelName, float fillValue = 0.0f)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _outputChannelName = string.IsNullOrWhiteSpace(outputChannelName) ? FallbackOutputPortName : outputChannelName;
            _fillValue = fillValue;
            _ports = new[]
            {
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.Float)
            };
            _channelDeclarations = new[]
            {
                new ChannelDeclaration(_outputChannelName, ChannelType.Float, true)
            };
        }

        public void ReceiveParameter(string name, string value)
        {
            if (!string.Equals(name, "fillValue", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            float parsedFillValue;
            if (float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsedFillValue))
            {
                _fillValue = parsedFillValue;
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
