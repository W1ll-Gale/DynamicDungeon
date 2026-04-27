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
    [NodeCategory("Noise")]
    [NodeDisplayName("Perlin Noise")]
    [Description("Generates layered Perlin noise as a float map for terrain, masks, or other procedural inputs.")]
    public sealed class PerlinNoiseNode : IGenNode, IParameterReceiver
    {
        private const string DefaultNodeName = "Perlin Noise";
        private const int DefaultBatchSize = 64;
        private const string PreferredOutputDisplayName = GraphPortNameUtility.LegacyGenericOutputDisplayName;

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private NodePortDefinition[] _ports;
        private ChannelDeclaration[] _channelDeclarations;
        private readonly string _nodeId;
        private readonly string _nodeName;
        private string _outputChannelName;
        [MinValue(0.0f)]
        [Description("Controls how quickly the noise pattern changes across the grid.")]
        private float _frequency;
        [MinValue(0.0f)]
        [Description("Scales the strength of the generated noise values.")]
        private float _amplitude;
        [Description("Offsets the sampled noise position in X and Y.")]
        private Vector2 _offset;
        [MinValue(1.0f)]
        [Description("Number of layered noise passes combined into the final result.")]
        private int _octaves;
        [Description("Deterministically varies this node's result relative to the graph seed without changing the graph-wide seed.")]
        private int _seedOffset;

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

        public string OutputChannelName
        {
            get
            {
                return _outputChannelName;
            }
        }

        public float Frequency
        {
            get
            {
                return _frequency;
            }
        }

        public float Amplitude
        {
            get
            {
                return _amplitude;
            }
        }

        public Vector2 Offset
        {
            get
            {
                return _offset;
            }
        }

        public int Octaves
        {
            get
            {
                return _octaves;
            }
        }

        public int SeedOffset
        {
            get
            {
                return _seedOffset;
            }
        }

        public PerlinNoiseNode(string nodeId, string outputChannelName, float frequency, float amplitude, Vector2 offset, int octaves = 1, int seedOffset = 0) : this(nodeId, DefaultNodeName, outputChannelName, frequency, amplitude, offset, octaves, seedOffset)
        {
        }

        public PerlinNoiseNode(string nodeId, string nodeName, string outputChannelName) : this(nodeId, nodeName, outputChannelName, 0.05f, 1.0f, Vector2.zero, 1, 0)
        {
        }

        public PerlinNoiseNode(string nodeId, string nodeName, string outputChannelName, float frequency, float amplitude, Vector2 offset, int octaves = 1, int seedOffset = 0)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            if (string.IsNullOrWhiteSpace(nodeName))
            {
                throw new ArgumentException("Node name must be non-empty.", nameof(nodeName));
            }

            if (string.IsNullOrWhiteSpace(outputChannelName))
            {
                throw new ArgumentException("Output channel name must be non-empty.", nameof(outputChannelName));
            }

            if (frequency < 0.0f)
            {
                throw new ArgumentOutOfRangeException(nameof(frequency), "Frequency cannot be negative.");
            }

            if (amplitude < 0.0f)
            {
                throw new ArgumentOutOfRangeException(nameof(amplitude), "Amplitude cannot be negative.");
            }

            if (octaves <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(octaves), "Octaves must be greater than zero.");
            }

            _nodeId = nodeId;
            _nodeName = nodeName;
            _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(nodeId, outputChannelName, PreferredOutputDisplayName);
            _frequency = frequency;
            _amplitude = amplitude;
            _offset = offset;
            _octaves = octaves;
            _seedOffset = seedOffset;

            RefreshPorts();
            RefreshChannelDeclarations();
        }

        private void RefreshPorts()
        {
            string outputPortDisplayName = GraphPortNameUtility.ResolveOutputDisplayName(_nodeId, _outputChannelName, PreferredOutputDisplayName);
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

        public void ReceiveParameter(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (string.Equals(name, "frequency", StringComparison.OrdinalIgnoreCase))
            {
                float parsedFrequency;
                if (float.TryParse(value, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out parsedFrequency))
                {
                    _frequency = math.max(0.0f, parsedFrequency);
                }

                return;
            }

            if (string.Equals(name, "amplitude", StringComparison.OrdinalIgnoreCase))
            {
                float parsedAmplitude;
                if (float.TryParse(value, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out parsedAmplitude))
                {
                    _amplitude = math.max(0.0f, parsedAmplitude);
                }

                return;
            }

            if (string.Equals(name, "offset", StringComparison.OrdinalIgnoreCase))
            {
                Vector2 parsedOffset;
                if (TryParseOffset(value, out parsedOffset))
                {
                    _offset = parsedOffset;
                }

                return;
            }

            if (string.Equals(name, "octaves", StringComparison.OrdinalIgnoreCase))
            {
                int parsedOctaves;
                if (int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out parsedOctaves))
                {
                    _octaves = math.max(1, parsedOctaves);
                }

                return;
            }

            if (string.Equals(name, "seedOffset", StringComparison.OrdinalIgnoreCase))
            {
                int parsedSeedOffset;
                if (int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out parsedSeedOffset))
                {
                    _seedOffset = parsedSeedOffset;
                }

                return;
            }

            if (string.Equals(name, "outputChannelName", StringComparison.OrdinalIgnoreCase))
            {
                _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(_nodeId, value, PreferredOutputDisplayName);
                RefreshPorts();
                RefreshChannelDeclarations();
            }
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            NativeArray<float> output = context.GetFloatChannel(_outputChannelName);
            PerlinNoiseJob job = new PerlinNoiseJob
            {
                Output = output,
                Width = context.Width,
                Frequency = _frequency,
                Amplitude = _amplitude,
                Offset = new float2(_offset.x, _offset.y),
                SeedOffset = CreateSeedOffset(CombineSeed(context.GlobalSeed, _seedOffset)),
                Octaves = _octaves
            };

            return job.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
        }

        private static float2 CreateSeedOffset(long globalSeed)
        {
            unchecked
            {
                uint seedLow = (uint)globalSeed;
                uint seedHigh = (uint)(globalSeed >> 32);

                float seedX = ((seedLow & 65535u) / 65535.0f) * 10000.0f;
                float seedY = ((seedHigh & 65535u) / 65535.0f) * 10000.0f;
                return new float2(seedX, seedY);
            }
        }

        private static long CombineSeed(long globalSeed, int seedOffset)
        {
            unchecked
            {
                const long Prime = 1099511628211L;
                long combinedSeed = (globalSeed * Prime) ^ seedOffset;
                combinedSeed = (combinedSeed * Prime) ^ (seedOffset >> 16);
                return combinedSeed;
            }
        }

        private static bool TryParseOffset(string rawValue, out Vector2 offset)
        {
            string safeValue = rawValue ?? string.Empty;
            string trimmedValue = safeValue.Trim();

            if (trimmedValue.Length == 0)
            {
                offset = Vector2.zero;
                return true;
            }

            string normalisedValue = trimmedValue.Replace("(", string.Empty).Replace(")", string.Empty);
            string[] parts = normalisedValue.Split(',');
            if (parts.Length == 2)
            {
                float xValue;
                float yValue;
                if (float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out xValue) &&
                    float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out yValue))
                {
                    offset = new Vector2(xValue, yValue);
                    return true;
                }
            }

            float scalarValue;
            if (float.TryParse(trimmedValue, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out scalarValue))
            {
                offset = new Vector2(scalarValue, scalarValue);
                return true;
            }

            try
            {
                Vector2 jsonVector = JsonUtility.FromJson<Vector2>(trimmedValue);
                if (!float.IsNaN(jsonVector.x) && !float.IsNaN(jsonVector.y))
                {
                    offset = jsonVector;
                    return true;
                }
            }
            catch
            {
            }

            offset = Vector2.zero;
            return false;
        }

        [BurstCompile]
        internal struct PerlinNoiseJob : IJobParallelFor
        {
            public NativeArray<float> Output;
            public int Width;
            public float Frequency;
            public float Amplitude;
            public float2 Offset;
            public float2 SeedOffset;
            public int Octaves;

            public void Execute(int index)
            {
                int x = index % Width;
                int y = index / Width;

                float octaveFrequency = 1.0f;
                float octaveWeight = 1.0f;
                float accumulatedValue = 0.0f;
                float accumulatedWeight = 0.0f;

                int octaveIndex;
                for (octaveIndex = 0; octaveIndex < Octaves; octaveIndex++)
                {
                    float2 samplePosition = new float2(
                        (((float)x + Offset.x) * Frequency * octaveFrequency) + SeedOffset.x,
                        (((float)y + Offset.y) * Frequency * octaveFrequency) + SeedOffset.y);

                    float octaveNoise = noise.cnoise(samplePosition);
                    float normalisedNoise = math.saturate((octaveNoise * 0.5f) + 0.5f);

                    accumulatedValue += normalisedNoise * octaveWeight;
                    accumulatedWeight += octaveWeight;
                    octaveFrequency *= 2.0f;
                    octaveWeight *= 0.5f;
                }

                float averagedNoise = accumulatedWeight > 0.0f ? accumulatedValue / accumulatedWeight : 0.0f;
                Output[index] = math.saturate(averagedNoise * Amplitude);
            }
        }
    }
}
