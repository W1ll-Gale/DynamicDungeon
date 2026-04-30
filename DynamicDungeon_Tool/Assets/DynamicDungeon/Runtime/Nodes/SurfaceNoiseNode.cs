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
    [NodeDisplayName("Surface 1D Noise")]
    [Description("Generates a vertical surface mask using 1D perlin noise applied horizontally. Outputs 1.0 below the surface and 0.0 above.")]
    public sealed class SurfaceNoiseNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        private const int DefaultBatchSize = 64;
        private const string DefaultNodeName = "Surface 1D Noise";
        private const string FallbackOutputPortName = "Output";

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private string _outputChannelName;
        private NodePortDefinition[] _ports;

        [Range(0f, 1f)]
        [Description("Base normalized Y height of the surface.")]
        private float _baseHeight = 0.5f;

        [Range(0f, 1f)]
        [Description("Amplitude of the noise wave (how far up/down it deviates from the base height).")]
        private float _amplitude = 0.1f;

        [MinValue(0.01f)]
        [Description("Frequency of the noise wave.")]
        private float _frequency = 0.05f;

        [Description("Seed offset to apply to the noise generation.")]
        private float _seedOffset = 0.0f;

        private ChannelDeclaration[] _channelDeclarations;

        public IReadOnlyList<NodePortDefinition> Ports => _ports;
        public IReadOnlyList<ChannelDeclaration> ChannelDeclarations => _channelDeclarations;
        public IReadOnlyList<BlackboardKey> BlackboardDeclarations => _blackboardDeclarations;
        public string NodeId => _nodeId;
        public string NodeName => _nodeName;

        public SurfaceNoiseNode(string nodeId, string nodeName, string outputChannelName = "", float baseHeight = 0.5f, float amplitude = 0.1f, float frequency = 0.05f, float seedOffset = 0.0f)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(nodeId, outputChannelName, FallbackOutputPortName);
            
            _baseHeight = baseHeight;
            _amplitude = amplitude;
            _frequency = frequency;
            _seedOffset = seedOffset;

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
            // No inputs required
        }

        public void ReceiveParameter(string name, string value)
        {
            if (string.Equals(name, "baseHeight", StringComparison.OrdinalIgnoreCase))
                float.TryParse(value, out _baseHeight);
            else if (string.Equals(name, "amplitude", StringComparison.OrdinalIgnoreCase))
                float.TryParse(value, out _amplitude);
            else if (string.Equals(name, "frequency", StringComparison.OrdinalIgnoreCase))
                float.TryParse(value, out _frequency);
            else if (string.Equals(name, "seedOffset", StringComparison.OrdinalIgnoreCase))
                float.TryParse(value, out _seedOffset);
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

            // Mix the context seed with our custom offset to ensure stability per-node
            float nodeSeed = context.LocalSeed + _seedOffset;

            SurfaceNoiseJob job = new SurfaceNoiseJob
            {
                Output = output,
                Width = context.Width,
                Height = context.Height,
                BaseHeight = _baseHeight,
                Amplitude = _amplitude,
                Frequency = _frequency,
                NodeSeed = nodeSeed
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
        private struct SurfaceNoiseJob : IJobParallelFor
        {
            public NativeArray<float> Output;
            public int Width;
            public int Height;
            public float BaseHeight;
            public float Amplitude;
            public float Frequency;
            public float NodeSeed;

            public void Execute(int index)
            {
                int x = index % Width;
                int y = index / Width;
                
                float normalizedY = (float)y / Height;
                
                // Generate 1D noise for the X column
                // Use snoise (simplex) or cnoise (classic). Using raw cnoise scaled.
                float noiseVal = noise.snoise(new float2(x * Frequency, NodeSeed));
                
                float surfaceY = BaseHeight + (noiseVal * Amplitude);

                // For underground generation, everything below or equal to the surface gets a 1.0f mask
                Output[index] = normalizedY <= surfaceY ? 1.0f : 0.0f;
            }
        }
    }
}
