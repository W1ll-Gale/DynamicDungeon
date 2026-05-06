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

        [MinValue(1.0f)]
        [Description("Number of fractal noise octaves blended together.")]
        private int _octaves = 1;

        [Range(0f, 1f)]
        [Description("Amplitude multiplier applied after each octave.")]
        private float _persistence = 0.5f;

        [MinValue(1.0f)]
        [Description("Frequency multiplier applied after each octave.")]
        private float _lacunarity = 2.0f;

        [Description("Seed offset to apply to the noise generation.")]
        private float _seedOffset = 0.0f;

        [MinValue(0.0f)]
        [Description("Optional authoring height in tiles. When greater than zero, preserves the top margin and hill amplitude from that height so extra Y space is added below the surface.")]
        private int _referenceHeight;

        private ChannelDeclaration[] _channelDeclarations;

        public IReadOnlyList<NodePortDefinition> Ports => _ports;
        public IReadOnlyList<ChannelDeclaration> ChannelDeclarations => _channelDeclarations;
        public IReadOnlyList<BlackboardKey> BlackboardDeclarations => _blackboardDeclarations;
        public string NodeId => _nodeId;
        public string NodeName => _nodeName;

        public SurfaceNoiseNode(string nodeId, string nodeName, string outputChannelName = "", float baseHeight = 0.5f, float amplitude = 0.1f, float frequency = 0.05f, int octaves = 1, float persistence = 0.5f, float lacunarity = 2.0f, float seedOffset = 0.0f, int referenceHeight = 0)
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
            _octaves = math.max(1, octaves);
            _persistence = math.clamp(persistence, 0.0f, 1.0f);
            _lacunarity = math.max(1.0f, lacunarity);
            _seedOffset = seedOffset;
            _referenceHeight = math.max(0, referenceHeight);

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

        public void ReceiveInputConnections(InputConnectionMap inputConnections)
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
            else if (string.Equals(name, "octaves", StringComparison.OrdinalIgnoreCase))
            {
                int parsedOctaves;
                if (int.TryParse(value, out parsedOctaves))
                {
                    _octaves = math.max(1, parsedOctaves);
                }
            }
            else if (string.Equals(name, "persistence", StringComparison.OrdinalIgnoreCase))
            {
                float parsedPersistence;
                if (float.TryParse(value, out parsedPersistence))
                {
                    _persistence = math.clamp(parsedPersistence, 0.0f, 1.0f);
                }
            }
            else if (string.Equals(name, "lacunarity", StringComparison.OrdinalIgnoreCase))
            {
                float parsedLacunarity;
                if (float.TryParse(value, out parsedLacunarity))
                {
                    _lacunarity = math.max(1.0f, parsedLacunarity);
                }
            }
            else if (string.Equals(name, "seedOffset", StringComparison.OrdinalIgnoreCase))
                float.TryParse(value, out _seedOffset);
            else if (string.Equals(name, "referenceHeight", StringComparison.OrdinalIgnoreCase))
            {
                int parsedReferenceHeight;
                if (int.TryParse(value, out parsedReferenceHeight))
                {
                    _referenceHeight = math.max(0, parsedReferenceHeight);
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

            // Mix the context seed with our custom offset to ensure stability per-node
            float seedPhase;
            float seedSlice;
            float resolvedBaseHeight;
            float resolvedAmplitude;
            CreateSeedOffsets(context.LocalSeed, _seedOffset, out seedPhase, out seedSlice);
            ResolveSurfaceScale(context.Height, _baseHeight, _amplitude, _referenceHeight, out resolvedBaseHeight, out resolvedAmplitude);

            SurfaceNoiseJob job = new SurfaceNoiseJob
            {
                Output = output,
                Width = context.Width,
                Height = context.Height,
                BaseHeight = resolvedBaseHeight,
                Amplitude = resolvedAmplitude,
                Frequency = _frequency,
                Octaves = _octaves,
                Persistence = _persistence,
                Lacunarity = _lacunarity,
                SeedPhase = seedPhase,
                SeedSlice = seedSlice
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

        private static void CreateSeedOffsets(long localSeed, float seedOffset, out float seedPhase, out float seedSlice)
        {
            long mixedSeed = localSeed + (long)math.round(seedOffset * 1000.0f);
            uint lowerBits = unchecked((uint)(mixedSeed & 0xFFFF));
            uint upperBits = unchecked((uint)((mixedSeed >> 16) & 0xFFFF));
            seedPhase = lowerBits * 0.0001f;
            seedSlice = upperBits * 0.0001f;
        }

        private static void ResolveSurfaceScale(int currentHeight, float baseHeight, float amplitude, int referenceHeight, out float resolvedBaseHeight, out float resolvedAmplitude)
        {
            float safeCurrentHeight = math.max(1.0f, currentHeight);
            resolvedBaseHeight = math.saturate(baseHeight);
            resolvedAmplitude = math.max(0.0f, amplitude);

            if (referenceHeight <= 0)
            {
                return;
            }

            float safeReferenceHeight = math.max(1.0f, referenceHeight);
            float topMarginTiles = safeReferenceHeight * (1.0f - resolvedBaseHeight);
            float baseTileY = math.clamp(safeCurrentHeight - topMarginTiles, 0.0f, safeCurrentHeight);
            resolvedBaseHeight = math.saturate(baseTileY / safeCurrentHeight);
            resolvedAmplitude *= safeReferenceHeight / safeCurrentHeight;
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
            public int Octaves;
            public float Persistence;
            public float Lacunarity;
            public float SeedPhase;
            public float SeedSlice;

            public void Execute(int index)
            {
                int x = index % Width;
                int y = index / Width;
                
                float normalizedY = (float)y / Height;
                
                // Generate 1D noise for the X column
                // Use snoise (simplex) or cnoise (classic). Using raw cnoise scaled.
                float frequency = math.max(0.0001f, Frequency);
                float amplitude = 1.0f;
                float totalAmplitude = 0.0f;
                float accumulatedNoise = 0.0f;
                int octaveIndex;
                for (octaveIndex = 0; octaveIndex < Octaves; octaveIndex++)
                {
                    float octaveNoise = noise.snoise(new float2(x * frequency, SeedSlice + (octaveIndex * 0.137f)));
                    accumulatedNoise += octaveNoise * amplitude;
                    totalAmplitude += amplitude;
                    amplitude *= Persistence;
                    frequency *= Lacunarity;
                }

                float noiseVal = totalAmplitude > 0.0f ? accumulatedNoise / totalAmplitude : 0.0f;
                
                float surfaceY = BaseHeight + (noiseVal * Amplitude);

                // For underground generation, everything below or equal to the surface gets a 1.0f mask
                Output[index] = normalizedY <= surfaceY ? 1.0f : 0.0f;
            }
        }
    }
}
