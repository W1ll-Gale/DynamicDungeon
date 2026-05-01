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
    [NodeDisplayName("Axis Band")]
    [Description("Generates a normalized X or Y band mask. Outputs 1.0 inside the band, and 0.0 outside.")]
    public sealed class AxisBandNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        private const int DefaultBatchSize = 64;
        private const string DefaultNodeName = "Axis Band";
        private const string FallbackOutputPortName = "Output";

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private string _outputChannelName;
        private NodePortDefinition[] _ports;

        [DescriptionAttribute("The axis used to evaluate the band.")]
        private GradientDirection _axis = GradientDirection.Y;

        [Range(0f, 1f)]
        [Description("The lower normalized bound of the band.")]
        private float _min = 0.0f;

        [Range(0f, 1f)]
        [Description("The upper normalized bound of the band.")]
        private float _max = 1.0f;

        private ChannelDeclaration[] _channelDeclarations;

        public IReadOnlyList<NodePortDefinition> Ports => _ports;
        public IReadOnlyList<ChannelDeclaration> ChannelDeclarations => _channelDeclarations;
        public IReadOnlyList<BlackboardKey> BlackboardDeclarations => _blackboardDeclarations;
        public string NodeId => _nodeId;
        public string NodeName => _nodeName;

        public AxisBandNode(
            string nodeId,
            string nodeName,
            string outputChannelName = "",
            GradientDirection axis = GradientDirection.Y,
            float min = 0f,
            float max = 1f)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(nodeId, outputChannelName, FallbackOutputPortName);
            _axis = axis;
            _min = min;
            _max = max;

            RefreshPorts();
            RefreshChannelDeclarations();
        }

        public void ReceiveInputConnections(IReadOnlyDictionary<string, string> inputConnections)
        {
        }

        public void ReceiveParameter(string name, string value)
        {
            if (string.Equals(name, "axis", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    _axis = (GradientDirection)Enum.Parse(typeof(GradientDirection), value ?? string.Empty, true);
                }
                catch
                {
                }

                return;
            }

            if (string.Equals(name, "min", StringComparison.OrdinalIgnoreCase))
            {
                float.TryParse(value, out _min);
                return;
            }

            if (string.Equals(name, "max", StringComparison.OrdinalIgnoreCase))
            {
                float.TryParse(value, out _max);
                return;
            }

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

            AxisBandJob job = new AxisBandJob
            {
                Output = output,
                Width = context.Width,
                Height = context.Height,
                Axis = _axis,
                Min = _min,
                Max = _max
            };

            return job.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
        }

        private void RefreshPorts()
        {
            string outputPortDisplayName = GraphPortNameUtility.ResolveOutputDisplayName(_nodeId, _outputChannelName, FallbackOutputPortName);
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

        [BurstCompile]
        private struct AxisBandJob : IJobParallelFor
        {
            public NativeArray<float> Output;
            public int Width;
            public int Height;
            public GradientDirection Axis;
            public float Min;
            public float Max;

            public void Execute(int index)
            {
                int x = index % Width;
                int y = index / Width;

                float normalizedPosition = Axis == GradientDirection.X
                    ? (float)x / Width
                    : (float)y / Height;

                float resolvedMin = math.min(Min, Max);
                float resolvedMax = math.max(Min, Max);
                Output[index] = normalizedPosition >= resolvedMin && normalizedPosition <= resolvedMax ? 1.0f : 0.0f;
            }
        }
    }
}
