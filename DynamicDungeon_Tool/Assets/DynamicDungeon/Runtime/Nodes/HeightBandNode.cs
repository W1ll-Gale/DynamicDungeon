using System;
using System.Collections.Generic;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace DynamicDungeon.Runtime.Nodes
{
    [NodeCategory("Generator")]
    [NodeDisplayName("Height Band")]
    [Description("Generates a vertical band mask. Outputs 1.0 inside the band, and 0.0 outside.")]
    public sealed class HeightBandNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        private const int DefaultBatchSize = 64;
        private const string DefaultNodeName = "Height Band";
        private const string FallbackOutputPortName = "Output";

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private string _outputChannelName;
        private NodePortDefinition[] _ports;

        [Range(0f, 1f)]
        [Description("The lower normalized bound of the band (0 is bottom).")]
        private float _minHeight = 0.0f;

        [Range(0f, 1f)]
        [Description("The upper normalized bound of the band (1 is top).")]
        private float _maxHeight = 1.0f;

        private ChannelDeclaration[] _channelDeclarations;

        public IReadOnlyList<NodePortDefinition> Ports => _ports;
        public IReadOnlyList<ChannelDeclaration> ChannelDeclarations => _channelDeclarations;
        public IReadOnlyList<BlackboardKey> BlackboardDeclarations => _blackboardDeclarations;
        public string NodeId => _nodeId;
        public string NodeName => _nodeName;

        public HeightBandNode(string nodeId, string nodeName, string outputChannelName = "", float minHeight = 0f, float maxHeight = 1f)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(nodeId, outputChannelName, FallbackOutputPortName);
            _minHeight = minHeight;
            _maxHeight = maxHeight;

            RefreshPorts();
            RefreshChannelDeclarations();
        }

        private void RefreshPorts()
        {
            string outputPortDisplayName = GraphPortNameUtility.ResolveOutputDisplayName(_nodeId, _outputChannelName, FallbackOutputPortName);
            _ports = new[]
            {
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.Float, displayName: outputPortDisplayName)
            };
        }

        public void ReceiveInputConnections(IReadOnlyDictionary<string, string> inputConnections)
        {
            // No inputs
        }

        public void ReceiveParameter(string name, string value)
        {
            if (string.Equals(name, "minHeight", StringComparison.OrdinalIgnoreCase))
            {
                float.TryParse(value, out _minHeight);
            }
            else if (string.Equals(name, "maxHeight", StringComparison.OrdinalIgnoreCase))
            {
                float.TryParse(value, out _maxHeight);
            }
            else if (string.Equals(name, "outputChannelName", StringComparison.OrdinalIgnoreCase))
            {
                _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(_nodeId, value, FallbackOutputPortName);
                RefreshPorts();
                RefreshChannelDeclarations();
            }
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            NativeArray<float> output = context.GetFloatChannel(_outputChannelName);

            HeightBandJob job = new HeightBandJob
            {
                Output = output,
                Width = context.Width,
                Height = context.Height,
                MinHeight = _minHeight,
                MaxHeight = _maxHeight
            };

            return job.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
        }

        private void RefreshChannelDeclarations()
        {
            _channelDeclarations = new[]
            {
                new ChannelDeclaration(_outputChannelName, ChannelType.Float, true)
            };
        }

        [BurstCompile]
        private struct HeightBandJob : IJobParallelFor
        {
            public NativeArray<float> Output;
            public int Width;
            public int Height;
            public float MinHeight;
            public float MaxHeight;

            public void Execute(int index)
            {
                int y = index / Width;
                float normalizedY = (float)y / Height;
                Output[index] = (normalizedY >= MinHeight && normalizedY <= MaxHeight) ? 1.0f : 0.0f;
            }
        }
    }
}
