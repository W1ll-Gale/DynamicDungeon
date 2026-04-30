using System;
using System.Collections.Generic;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using System.Globalization;
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
    [NodeDisplayName("Fractal Noise")]
    [Description("Stacks multiple octaves of any float input channel (FBM) to add detail at increasing frequencies. Output is normalised to 0–1.")]
    public sealed class FractalNoiseNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        private const string DefaultNodeName = "Fractal Noise";
        private const int DefaultBatchSize = 64;
        private const string InputPortName = "Input";
        private const string PreferredOutputDisplayName = GraphPortNameUtility.LegacyGenericOutputDisplayName;
        private const int MaxOctaves = 8;

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private NodePortDefinition[] _ports;
        private readonly string _nodeId;
        private readonly string _nodeName;
        private string _outputChannelName;

        private string _inputChannelName;
        private ChannelDeclaration[] _channelDeclarations;

        [Range(1, MaxOctaves)]
        [Description("Number of noise layers stacked together.")]
        private int _octaves;

        [MinValue(1.0f)]
        [Description("Frequency multiplier applied to each successive octave.")]
        private float _lacunarity;

        [Range(0.0f, 1.0f)]
        [Description("Amplitude multiplier applied to each successive octave.")]
        private float _persistence;

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

        public int Octaves
        {
            get
            {
                return _octaves;
            }
        }

        public float Lacunarity
        {
            get
            {
                return _lacunarity;
            }
        }

        public float Persistence
        {
            get
            {
                return _persistence;
            }
        }

        public FractalNoiseNode(string nodeId, string nodeName, string outputChannelName) : this(nodeId, nodeName, string.Empty, outputChannelName, 4, 2.0f, 0.5f)
        {
        }

        public FractalNoiseNode(string nodeId, string nodeName, string inputChannelName, string outputChannelName, int octaves = 4, float lacunarity = 2.0f, float persistence = 0.5f)
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

            _nodeId = nodeId;
            _nodeName = nodeName;
            _inputChannelName = inputChannelName ?? string.Empty;
            _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(nodeId, outputChannelName, PreferredOutputDisplayName);
            _octaves = math.clamp(octaves, 1, MaxOctaves);
            _lacunarity = math.max(1.0f, lacunarity);
            _persistence = math.clamp(persistence, 0.0f, 1.0f);

            RefreshPorts();
            RefreshChannelDeclarations();
        }

        private void RefreshPorts()
        {
            string outputPortDisplayName = GraphPortNameUtility.ResolveOutputDisplayName(_nodeId, _outputChannelName, PreferredOutputDisplayName);
            _ports = new[]
            {
                new NodePortDefinition(InputPortName, PortDirection.Input, ChannelType.Float, PortCapacity.Single, true),
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.Float, displayName: outputPortDisplayName)
            };
        }

        public void ReceiveInputConnections(IReadOnlyDictionary<string, string> inputConnections)
        {
            string channelName;
            if (inputConnections != null && inputConnections.TryGetValue(InputPortName, out channelName))
            {
                _inputChannelName = channelName ?? string.Empty;
            }
            else
            {
                _inputChannelName = string.Empty;
            }

            RefreshChannelDeclarations();
        }

        public void ReceiveParameter(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (string.Equals(name, "octaves", StringComparison.OrdinalIgnoreCase))
            {
                int parsedOctaves;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedOctaves))
                {
                    _octaves = math.clamp(parsedOctaves, 1, MaxOctaves);
                }

                return;
            }

            if (string.Equals(name, "lacunarity", StringComparison.OrdinalIgnoreCase))
            {
                float parsedLacunarity;
                if (float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsedLacunarity))
                {
                    _lacunarity = math.max(1.0f, parsedLacunarity);
                }

                return;
            }

            if (string.Equals(name, "persistence", StringComparison.OrdinalIgnoreCase))
            {
                float parsedPersistence;
                if (float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsedPersistence))
                {
                    _persistence = math.clamp(parsedPersistence, 0.0f, 1.0f);
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
            NativeArray<float> input = context.GetFloatChannel(_inputChannelName);
            NativeArray<float> output = context.GetFloatChannel(_outputChannelName);

            FractalNoiseJob job = new FractalNoiseJob
            {
                Input = input,
                Output = output,
                Width = context.Width,
                Height = context.Height,
                Octaves = _octaves,
                Lacunarity = _lacunarity,
                Persistence = _persistence
            };

            return job.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
        }

        private void RefreshChannelDeclarations()
        {
            if (!string.IsNullOrWhiteSpace(_inputChannelName))
            {
                _channelDeclarations = new[]
                {
                    new ChannelDeclaration(_inputChannelName, ChannelType.Float, false),
                    new ChannelDeclaration(_outputChannelName, ChannelType.Float, true)
                };
                return;
            }

            _channelDeclarations = new[]
            {
                new ChannelDeclaration(_outputChannelName, ChannelType.Float, true)
            };
        }

        [BurstCompile]
        internal struct FractalNoiseJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<float> Input;

            public NativeArray<float> Output;
            public int Width;
            public int Height;
            public int Octaves;
            public float Lacunarity;
            public float Persistence;

            public void Execute(int index)
            {
                int x = index % Width;
                int y = index / Width;

                float accumulatedValue = 0.0f;
                float accumulatedWeight = 0.0f;
                float octaveFreq = 1.0f;
                float octaveAmp = 1.0f;

                int octaveIndex;
                for (octaveIndex = 0; octaveIndex < Octaves; octaveIndex++)
                {
                    float sampleX = (float)x * octaveFreq;
                    float sampleY = (float)y * octaveFreq;
                    float sample = BilinearSample(sampleX, sampleY);
                    accumulatedValue += sample * octaveAmp;
                    accumulatedWeight += octaveAmp;
                    octaveFreq *= Lacunarity;
                    octaveAmp *= Persistence;
                }

                Output[index] = accumulatedWeight > 0.0f
                    ? math.saturate(accumulatedValue / accumulatedWeight)
                    : 0.0f;
            }

            // Bilinear interpolation of the input NativeArray at a continuous (sx, sy) position.
            // Clamps coordinates to the array bounds.
            private float BilinearSample(float sx, float sy)
            {
                float clampedX = math.clamp(sx, 0.0f, (float)(Width - 1));
                float clampedY = math.clamp(sy, 0.0f, (float)(Height - 1));

                int x0 = (int)math.floor(clampedX);
                int y0 = (int)math.floor(clampedY);
                int x1 = math.min(x0 + 1, Width - 1);
                int y1 = math.min(y0 + 1, Height - 1);

                float tx = clampedX - (float)x0;
                float ty = clampedY - (float)y0;

                float v00 = Input[y0 * Width + x0];
                float v10 = Input[y0 * Width + x1];
                float v01 = Input[y1 * Width + x0];
                float v11 = Input[y1 * Width + x1];

                float top = math.lerp(v00, v10, tx);
                float bottom = math.lerp(v01, v11, tx);
                return math.lerp(top, bottom, ty);
            }
        }
    }
}
