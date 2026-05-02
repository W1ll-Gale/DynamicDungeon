using System;
using System.Collections.Generic;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace DynamicDungeon.Runtime.Nodes
{
    [NodeCategory("Generator")]
    [NodeDisplayName("Height Band")]
    [Description("Generates a vertical band mask. Outputs 1.0 inside the band, and 0.0 outside.")]
    public sealed class HeightBandNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        private enum BoundAnchor
        {
            Normalized = 0,
            Bottom = 1,
            Top = 2
        }

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

        [MinValue(0.0f)]
        [Description("Optional authoring height in tiles for anchored bounds. Use 0 for regular normalized behavior.")]
        private int _referenceHeight;

        [Description("How the lower bound responds to world height changes when Reference Height is set.")]
        private BoundAnchor _minAnchor = BoundAnchor.Normalized;

        [Description("How the upper bound responds to world height changes when Reference Height is set.")]
        private BoundAnchor _maxAnchor = BoundAnchor.Normalized;

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
            else if (string.Equals(name, "referenceHeight", StringComparison.OrdinalIgnoreCase))
            {
                int parsedReferenceHeight;
                if (int.TryParse(value, out parsedReferenceHeight))
                {
                    _referenceHeight = math.max(0, parsedReferenceHeight);
                }
            }
            else if (string.Equals(name, "minAnchor", StringComparison.OrdinalIgnoreCase))
            {
                BoundAnchor parsedAnchor;
                if (Enum.TryParse(value ?? string.Empty, true, out parsedAnchor))
                {
                    _minAnchor = parsedAnchor;
                }
            }
            else if (string.Equals(name, "maxAnchor", StringComparison.OrdinalIgnoreCase))
            {
                BoundAnchor parsedAnchor;
                if (Enum.TryParse(value ?? string.Empty, true, out parsedAnchor))
                {
                    _maxAnchor = parsedAnchor;
                }
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
            float minHeight = ResolveBound(_minHeight, _minAnchor, context.Height, _referenceHeight);
            float maxHeight = ResolveBound(_maxHeight, _maxAnchor, context.Height, _referenceHeight);
            float resolvedMinHeight = math.min(minHeight, maxHeight);
            float resolvedMaxHeight = math.max(minHeight, maxHeight);

            HeightBandJob job = new HeightBandJob
            {
                Output = output,
                Width = context.Width,
                Height = context.Height,
                MinHeight = resolvedMinHeight,
                MaxHeight = resolvedMaxHeight
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

        private static float ResolveBound(float normalizedValue, BoundAnchor anchor, int currentHeight, int referenceHeight)
        {
            float value = math.saturate(normalizedValue);
            if (referenceHeight <= 0 || anchor == BoundAnchor.Normalized)
            {
                return value;
            }

            float safeCurrentHeight = math.max(1.0f, currentHeight);
            float safeReferenceHeight = math.max(1.0f, referenceHeight);
            if (anchor == BoundAnchor.Bottom)
            {
                return math.saturate((value * safeReferenceHeight) / safeCurrentHeight);
            }

            float topMarginTiles = (1.0f - value) * safeReferenceHeight;
            return math.saturate((safeCurrentHeight - topMarginTiles) / safeCurrentHeight);
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
