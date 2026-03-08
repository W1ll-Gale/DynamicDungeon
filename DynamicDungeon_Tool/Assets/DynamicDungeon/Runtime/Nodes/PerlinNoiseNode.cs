using System;
using System.Collections.Generic;
using DynamicDungeon.Runtime.Core;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace DynamicDungeon.Runtime.Nodes
{
    public sealed class PerlinNoiseNode : IGenNode
    {
        private const string DefaultNodeName = "Perlin Noise";
        private const int DefaultBatchSize = 64;

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly NodePortDefinition[] _ports;
        private readonly ChannelDeclaration[] _channelDeclarations;
        private readonly string _nodeId;
        private readonly string _nodeName;
        private readonly string _outputChannelName;
        private readonly float _frequency;
        private readonly float _amplitude;
        private readonly Vector2 _offset;
        private readonly int _octaves;

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

        public PerlinNoiseNode(string nodeId, string outputChannelName, float frequency, float amplitude, Vector2 offset, int octaves = 1) : this(nodeId, DefaultNodeName, outputChannelName, frequency, amplitude, offset, octaves)
        {
        }

        public PerlinNoiseNode(string nodeId, string nodeName, string outputChannelName, float frequency, float amplitude, Vector2 offset, int octaves = 1)
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

            _ports = new[]
            {
                new NodePortDefinition(outputChannelName, PortDirection.Output, ChannelType.Float)
            };

            _channelDeclarations = new[]
            {
                new ChannelDeclaration(outputChannelName, ChannelType.Float, true)
            };

            _nodeId = nodeId;
            _nodeName = nodeName;
            _outputChannelName = outputChannelName;
            _frequency = frequency;
            _amplitude = amplitude;
            _offset = offset;
            _octaves = octaves;
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
                SeedOffset = CreateSeedOffset(context.LocalSeed),
                Octaves = _octaves
            };

            return job.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
        }

        private static float2 CreateSeedOffset(long localSeed)
        {
            unchecked
            {
                uint seedLow = (uint)localSeed;
                uint seedHigh = (uint)(localSeed >> 32);

                float seedX = ((seedLow & 65535u) / 65535.0f) * 10000.0f;
                float seedY = ((seedHigh & 65535u) / 65535.0f) * 10000.0f;
                return new float2(seedX, seedY);
            }
        }

        [BurstCompile]
        private struct PerlinNoiseJob : IJobParallelFor
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
