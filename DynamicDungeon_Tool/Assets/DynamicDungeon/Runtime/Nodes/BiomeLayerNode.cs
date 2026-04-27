using System;
using System.Collections.Generic;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using System.Globalization;
using DynamicDungeon.Runtime.Biome;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace DynamicDungeon.Runtime.Nodes
{
    [NodeCategory("Biome")]
    [NodeDisplayName("Biome Layer")]
    [Description("Assigns one biome to all tiles within a spatial or data-driven range, leaving unmatched tiles unchanged.")]
    public sealed class BiomeLayerNode : IGenNode, IInputConnectionReceiver, IParameterReceiver, IBiomeChannelNode
    {
        private const int DefaultBatchSize = 64;
        private const string DefaultNodeName = "Biome Layer";
        private const string DataPortName = "Data";

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private readonly NodePortDefinition[] _ports;

        private string _inputDataChannelName;

        [DescriptionAttribute("Selects how the biome range is evaluated across the map.")]
        private GradientDirection _axis;

        [DescriptionAttribute("Minimum inclusive value of the biome range.")]
        private float _rangeMin;

        [DescriptionAttribute("Maximum inclusive value of the biome range.")]
        private float _rangeMax;

        [AssetGuidReference(typeof(BiomeAsset))]
        [DescriptionAttribute("Biome asset assigned to tiles that fall within the range. Stored as an asset GUID in the graph.")]
        private string _biome;

        private int _resolvedBiomeIndex;
        private ChannelDeclaration[] _channelDeclarations;

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

        public BiomeLayerNode(
            string nodeId,
            string nodeName,
            string inputDataChannelName = "",
            GradientDirection axis = GradientDirection.Y,
            float rangeMin = 0.0f,
            float rangeMax = 1.0f,
            string biome = "")
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _inputDataChannelName = inputDataChannelName ?? string.Empty;
            _axis = axis;
            _rangeMin = rangeMin;
            _rangeMax = rangeMax;
            _biome = biome ?? string.Empty;
            _resolvedBiomeIndex = BiomeChannelUtility.UnassignedBiomeIndex;
            _ports = new[]
            {
                new NodePortDefinition(DataPortName, PortDirection.Input, ChannelType.Float, PortCapacity.Single, false),
                new NodePortDefinition(BiomeChannelUtility.ChannelName, PortDirection.Output, ChannelType.Int)
            };

            RefreshChannelDeclarations();
        }

        public void ReceiveInputConnections(IReadOnlyDictionary<string, string> inputConnections)
        {
            _inputDataChannelName = ResolveInputChannel(inputConnections, DataPortName);
            RefreshChannelDeclarations();
        }

        public void ReceiveParameter(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

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

            if (string.Equals(name, "rangeMin", StringComparison.OrdinalIgnoreCase))
            {
                _rangeMin = ParseFloat(value, _rangeMin);
                return;
            }

            if (string.Equals(name, "rangeMax", StringComparison.OrdinalIgnoreCase))
            {
                _rangeMax = ParseFloat(value, _rangeMax);
                return;
            }

            if (string.Equals(name, "biome", StringComparison.OrdinalIgnoreCase))
            {
                _biome = value ?? string.Empty;
            }
        }

        public bool ResolveBiomePalette(BiomeChannelPalette palette, out string errorMessage)
        {
            if (palette == null)
            {
                throw new ArgumentNullException(nameof(palette));
            }

            if (!palette.TryResolveIndex(_biome, out _resolvedBiomeIndex, out errorMessage))
            {
                errorMessage = "Biome Layer node '" + _nodeName + "' could not resolve its biome asset: " + errorMessage;
                return false;
            }

            return true;
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            NativeArray<int> biomeChannel = context.GetIntChannel(BiomeChannelUtility.ChannelName);
            float resolvedRangeMin = math.min(_rangeMin, _rangeMax);
            float resolvedRangeMax = math.max(_rangeMin, _rangeMax);

            if (_axis == GradientDirection.Data)
            {
                if (string.IsNullOrWhiteSpace(_inputDataChannelName))
                {
                    throw new InvalidOperationException("Biome Layer node in Data mode requires a connected float input.");
                }

                NativeArray<float> data = context.GetFloatChannel(_inputDataChannelName);
                BiomeLayerDataJob dataJob = new BiomeLayerDataJob
                {
                    Data = data,
                    BiomeChannel = biomeChannel,
                    RangeMin = resolvedRangeMin,
                    RangeMax = resolvedRangeMax,
                    BiomeIndex = _resolvedBiomeIndex
                };

                return dataJob.Schedule(biomeChannel.Length, DefaultBatchSize, context.InputDependency);
            }

            BiomeLayerSpatialJob spatialJob = new BiomeLayerSpatialJob
            {
                Width = context.Width,
                Height = context.Height,
                Axis = _axis,
                RangeMin = resolvedRangeMin,
                RangeMax = resolvedRangeMax,
                BiomeIndex = _resolvedBiomeIndex,
                BiomeChannel = biomeChannel
            };

            return spatialJob.Schedule(biomeChannel.Length, DefaultBatchSize, context.InputDependency);
        }

        private void RefreshChannelDeclarations()
        {
            if (!string.IsNullOrWhiteSpace(_inputDataChannelName))
            {
                _channelDeclarations = new[]
                {
                    new ChannelDeclaration(_inputDataChannelName, ChannelType.Float, false),
                    new ChannelDeclaration(BiomeChannelUtility.ChannelName, ChannelType.Int, true)
                };
                return;
            }

            _channelDeclarations = new[]
            {
                new ChannelDeclaration(BiomeChannelUtility.ChannelName, ChannelType.Int, true)
            };
        }

        private static string ResolveInputChannel(IReadOnlyDictionary<string, string> inputConnections, string portName)
        {
            string inputChannelName;
            if (inputConnections != null && inputConnections.TryGetValue(portName, out inputChannelName))
            {
                return inputChannelName ?? string.Empty;
            }

            return string.Empty;
        }

        private static float ParseFloat(string value, float fallbackValue)
        {
            float parsedValue;
            if (float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsedValue))
            {
                return parsedValue;
            }

            return fallbackValue;
        }

        [BurstCompile]
        private struct BiomeLayerDataJob : IJobParallelFor
        {
            [Unity.Collections.ReadOnly]
            public NativeArray<float> Data;

            public NativeArray<int> BiomeChannel;
            public float RangeMin;
            public float RangeMax;
            public int BiomeIndex;

            public void Execute(int index)
            {
                float value = Data[index];
                if (value >= RangeMin && value <= RangeMax)
                {
                    BiomeChannel[index] = BiomeIndex;
                }
            }
        }

        [BurstCompile]
        private struct BiomeLayerSpatialJob : IJobParallelFor
        {
            public int Width;
            public int Height;
            public GradientDirection Axis;
            public float RangeMin;
            public float RangeMax;
            public int BiomeIndex;

            public NativeArray<int> BiomeChannel;

            public void Execute(int index)
            {
                int x = index % Width;
                int y = index / Width;
                float value = EvaluateSpatialValue(x, y, Width, Height, Axis);
                if (value >= RangeMin && value <= RangeMax)
                {
                    BiomeChannel[index] = BiomeIndex;
                }
            }

            private static float EvaluateSpatialValue(int x, int y, int width, int height, GradientDirection axis)
            {
                float normalisedX = width > 1 ? (float)x / (width - 1) : 0.0f;
                float normalisedY = height > 1 ? (float)y / (height - 1) : 0.0f;

                if (axis == GradientDirection.X)
                {
                    return normalisedX;
                }

                if (axis == GradientDirection.Y)
                {
                    return normalisedY;
                }

                if (axis == GradientDirection.Diagonal)
                {
                    return math.saturate((normalisedX + normalisedY) * 0.5f);
                }

                float2 position = new float2(normalisedX, normalisedY);
                float2 centre = new float2(0.5f, 0.5f);
                float distance = math.distance(position, centre);
                float maxDistance = math.distance(new float2(0.0f, 0.0f), centre);
                return maxDistance > 0.0f ? math.saturate(distance / maxDistance) : 0.0f;
            }
        }
    }
}
