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
using UnityEngine;

namespace DynamicDungeon.Runtime.Nodes
{
    [NodeCategory("Biome")]
    [NodeDisplayName("Biome Override")]
    [Description("Overrides the biome channel inside a mask, with optional boundary blending and probabilistic scatter.")]
    public sealed class BiomeOverrideNode : IGenNode, IInputConnectionReceiver, IParameterReceiver, IBiomeChannelNode
    {
        private enum BlendMode
        {
            AnyEdge = 0,
            VerticalOnly = 1,
            HorizontalOnly = 2
        }

        private const int DefaultBatchSize = 64;
        private const float DiagonalCost = 1.41421356237f;
        private const string DefaultNodeName = "Biome Override";
        private const string BiomeInputPortName = "Biome Input";
        private const string MaskPortName = "Mask";

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private readonly NodePortDefinition[] _ports;

        private string _inputBiomeChannelName;
        private string _inputMaskChannelName;

        [AssetGuidReference(typeof(BiomeAsset))]
        [DescriptionAttribute("Biome asset applied where the override succeeds. Stored as an asset GUID in the graph.")]
        private string _overrideBiome;

        [MinValue(0.0f)]
        [DescriptionAttribute("Boundary blend width in tiles. Tiles near the mask edge receive a lower override probability.")]
        private float _blendEdgeWidth;

        [RangeAttribute(0.0f, 1.0f)]
        [DescriptionAttribute("Base chance that a masked tile is overridden before blend-edge falloff is applied.")]
        private float _probability;

        [DescriptionAttribute("How boundary blending measures distance from the mask edge.")]
        private BlendMode _blendMode;

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

        public BiomeOverrideNode(
            string nodeId,
            string nodeName,
            string inputBiomeChannelName = "",
            string inputMaskChannelName = "",
            string overrideBiome = "",
            float blendEdgeWidth = 0.0f,
            float probability = 1.0f)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _inputBiomeChannelName = inputBiomeChannelName ?? string.Empty;
            _inputMaskChannelName = inputMaskChannelName ?? string.Empty;
            _overrideBiome = overrideBiome ?? string.Empty;
            _blendEdgeWidth = math.max(0.0f, blendEdgeWidth);
            _probability = math.saturate(probability);
            _blendMode = BlendMode.AnyEdge;
            _resolvedBiomeIndex = BiomeChannelUtility.UnassignedBiomeIndex;
            _ports = new[]
            {
                new NodePortDefinition(BiomeInputPortName, PortDirection.Input, ChannelType.Int, PortCapacity.Single, false),
                new NodePortDefinition(MaskPortName, PortDirection.Input, ChannelType.BoolMask, PortCapacity.Single, true),
                new NodePortDefinition(BiomeChannelUtility.ChannelName, PortDirection.Output, ChannelType.Int, displayName: "Biomes")
            };

            RefreshChannelDeclarations();
        }

        public void ReceiveInputConnections(InputConnectionMap inputConnections)
        {
            _inputBiomeChannelName = ResolveInputChannel(inputConnections, BiomeInputPortName);
            _inputMaskChannelName = ResolveInputChannel(inputConnections, MaskPortName);
            RefreshChannelDeclarations();
        }

        public void ReceiveParameter(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (string.Equals(name, "overrideBiome", StringComparison.OrdinalIgnoreCase))
            {
                _overrideBiome = value ?? string.Empty;
                return;
            }

            if (string.Equals(name, "blendEdgeWidth", StringComparison.OrdinalIgnoreCase))
            {
                _blendEdgeWidth = math.max(0.0f, ParseFloat(value, _blendEdgeWidth));
                return;
            }

            if (string.Equals(name, "probability", StringComparison.OrdinalIgnoreCase))
            {
                _probability = math.saturate(ParseFloat(value, _probability));
                return;
            }

            if (string.Equals(name, "blendMode", StringComparison.OrdinalIgnoreCase))
            {
                _blendMode = ParseBlendMode(value, _blendMode);
            }
        }

        public bool ResolveBiomePalette(BiomeChannelPalette palette, out string errorMessage)
        {
            if (palette == null)
            {
                throw new ArgumentNullException(nameof(palette));
            }

            if (!palette.TryResolveIndex(_overrideBiome, out _resolvedBiomeIndex, out errorMessage))
            {
                errorMessage = "Biome Override node '" + _nodeName + "' could not resolve its override biome: " + errorMessage;
                return false;
            }

            return true;
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            if (string.IsNullOrWhiteSpace(_inputMaskChannelName))
            {
                throw new InvalidOperationException("Biome Override node requires a connected mask input.");
            }

            NativeArray<int> biomeChannel = context.GetIntChannel(BiomeChannelUtility.ChannelName);
            NativeArray<byte> mask = context.GetBoolMaskChannel(_inputMaskChannelName);

            if (_blendEdgeWidth <= 0.0f)
            {
                BiomeOverrideSimpleApplyJob applyJob = new BiomeOverrideSimpleApplyJob
                {
                    Width = context.Width,
                    Height = context.Height,
                    LocalSeed = context.LocalSeed,
                    BiomeIndex = _resolvedBiomeIndex,
                    Probability = _probability,
                    BiomeChannel = biomeChannel,
                    Mask = mask
                };

                return applyJob.Schedule(biomeChannel.Length, DefaultBatchSize, context.InputDependency);
            }

            NativeArray<float> distances = new NativeArray<float>(biomeChannel.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            JobHandle distanceHandle;

            if (_blendMode == BlendMode.VerticalOnly)
            {
                VerticalOverrideDistanceJob verticalDistanceJob = new VerticalOverrideDistanceJob
                {
                    Mask = mask,
                    Distances = distances,
                    Width = context.Width,
                    Height = context.Height,
                    UnreachableDistance = context.Height + 1.0f
                };

                distanceHandle = verticalDistanceJob.Schedule(context.InputDependency);
            }
            else if (_blendMode == BlendMode.HorizontalOnly)
            {
                HorizontalOverrideDistanceJob horizontalDistanceJob = new HorizontalOverrideDistanceJob
                {
                    Mask = mask,
                    Distances = distances,
                    Width = context.Width,
                    Height = context.Height,
                    UnreachableDistance = context.Width + 1.0f
                };

                distanceHandle = horizontalDistanceJob.Schedule(context.InputDependency);
            }
            else
            {
                float unreachableDistance = math.sqrt((float)(context.Width * context.Width + context.Height * context.Height)) + (float)(context.Width + context.Height + 1);

                OverrideDistanceInitialiseJob initialiseJob = new OverrideDistanceInitialiseJob
                {
                    Mask = mask,
                    Distances = distances,
                    UnreachableDistance = unreachableDistance
                };

                JobHandle initialiseHandle = initialiseJob.Schedule(distances.Length, DefaultBatchSize, context.InputDependency);

                OverrideDistanceForwardSweepJob forwardSweepJob = new OverrideDistanceForwardSweepJob
                {
                    Distances = distances,
                    Width = context.Width,
                    Height = context.Height
                };

                JobHandle forwardHandle = forwardSweepJob.Schedule(initialiseHandle);

                OverrideDistanceBackwardSweepJob backwardSweepJob = new OverrideDistanceBackwardSweepJob
                {
                    Distances = distances,
                    Width = context.Width,
                    Height = context.Height
                };

                distanceHandle = backwardSweepJob.Schedule(forwardHandle);
            }

            BiomeOverrideApplyJob blendedApplyJob = new BiomeOverrideApplyJob
            {
                Width = context.Width,
                Height = context.Height,
                LocalSeed = context.LocalSeed,
                BiomeIndex = _resolvedBiomeIndex,
                Probability = _probability,
                BlendEdgeWidth = _blendEdgeWidth,
                BiomeChannel = biomeChannel,
                Mask = mask,
                Distances = distances
            };

            JobHandle applyHandle = blendedApplyJob.Schedule(biomeChannel.Length, DefaultBatchSize, distanceHandle);
            return distances.Dispose(applyHandle);
        }

        private void RefreshChannelDeclarations()
        {
            List<ChannelDeclaration> declarations = new List<ChannelDeclaration>(3);

            if (!string.IsNullOrWhiteSpace(_inputBiomeChannelName))
            {
                declarations.Add(new ChannelDeclaration(_inputBiomeChannelName, ChannelType.Int, false));
            }

            if (!string.IsNullOrWhiteSpace(_inputMaskChannelName))
            {
                declarations.Add(new ChannelDeclaration(_inputMaskChannelName, ChannelType.BoolMask, false));
            }

            declarations.Add(new ChannelDeclaration(BiomeChannelUtility.ChannelName, ChannelType.Int, true));
            _channelDeclarations = declarations.ToArray();
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

        private static BlendMode ParseBlendMode(string value, BlendMode fallbackValue)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallbackValue;
            }

            BlendMode parsedValue;
            if (Enum.TryParse(value, true, out parsedValue))
            {
                return parsedValue;
            }

            return fallbackValue;
        }

        [BurstCompile]
        private struct OverrideDistanceInitialiseJob : IJobParallelFor
        {
            [Unity.Collections.ReadOnly]
            public NativeArray<byte> Mask;

            public NativeArray<float> Distances;
            public float UnreachableDistance;

            public void Execute(int index)
            {
                Distances[index] = Mask[index] == 0 ? 0.0f : UnreachableDistance;
            }
        }

        [BurstCompile]
        private struct OverrideDistanceForwardSweepJob : IJob
        {
            public NativeArray<float> Distances;
            public int Width;
            public int Height;

            public void Execute()
            {
                int y;
                for (y = 0; y < Height; y++)
                {
                    int x;
                    for (x = 0; x < Width; x++)
                    {
                        int index = y * Width + x;
                        float best = Distances[index];

                        if (x > 0)
                        {
                            best = math.min(best, Distances[index - 1] + 1.0f);
                        }

                        if (y > 0)
                        {
                            best = math.min(best, Distances[index - Width] + 1.0f);
                        }

                        if (x > 0 && y > 0)
                        {
                            best = math.min(best, Distances[index - Width - 1] + DiagonalCost);
                        }

                        if (x < Width - 1 && y > 0)
                        {
                            best = math.min(best, Distances[index - Width + 1] + DiagonalCost);
                        }

                        Distances[index] = best;
                    }
                }
            }
        }

        [BurstCompile]
        private struct OverrideDistanceBackwardSweepJob : IJob
        {
            public NativeArray<float> Distances;
            public int Width;
            public int Height;

            public void Execute()
            {
                int y;
                for (y = Height - 1; y >= 0; y--)
                {
                    int x;
                    for (x = Width - 1; x >= 0; x--)
                    {
                        int index = y * Width + x;
                        float best = Distances[index];

                        if (x < Width - 1)
                        {
                            best = math.min(best, Distances[index + 1] + 1.0f);
                        }

                        if (y < Height - 1)
                        {
                            best = math.min(best, Distances[index + Width] + 1.0f);
                        }

                        if (x < Width - 1 && y < Height - 1)
                        {
                            best = math.min(best, Distances[index + Width + 1] + DiagonalCost);
                        }

                        if (x > 0 && y < Height - 1)
                        {
                            best = math.min(best, Distances[index + Width - 1] + DiagonalCost);
                        }

                        Distances[index] = best;
                    }
                }
            }
        }

        [BurstCompile]
        private struct VerticalOverrideDistanceJob : IJob
        {
            [Unity.Collections.ReadOnly]
            public NativeArray<byte> Mask;

            public NativeArray<float> Distances;
            public int Width;
            public int Height;
            public float UnreachableDistance;

            public void Execute()
            {
                int x;
                for (x = 0; x < Width; x++)
                {
                    float distance = UnreachableDistance;
                    int y;
                    for (y = 0; y < Height; y++)
                    {
                        int index = (y * Width) + x;
                        if (Mask[index] == 0)
                        {
                            distance = 0.0f;
                            Distances[index] = 0.0f;
                            continue;
                        }

                        distance += 1.0f;
                        Distances[index] = distance;
                    }

                    distance = UnreachableDistance;
                    for (y = Height - 1; y >= 0; y--)
                    {
                        int index = (y * Width) + x;
                        if (Mask[index] == 0)
                        {
                            distance = 0.0f;
                            continue;
                        }

                        distance += 1.0f;
                        Distances[index] = math.min(Distances[index], distance);
                    }
                }
            }
        }

        [BurstCompile]
        private struct HorizontalOverrideDistanceJob : IJob
        {
            [Unity.Collections.ReadOnly]
            public NativeArray<byte> Mask;

            public NativeArray<float> Distances;
            public int Width;
            public int Height;
            public float UnreachableDistance;

            public void Execute()
            {
                int y;
                for (y = 0; y < Height; y++)
                {
                    float distance = UnreachableDistance;
                    int x;
                    for (x = 0; x < Width; x++)
                    {
                        int index = (y * Width) + x;
                        if (Mask[index] == 0)
                        {
                            distance = 0.0f;
                            Distances[index] = 0.0f;
                            continue;
                        }

                        distance += 1.0f;
                        Distances[index] = distance;
                    }

                    distance = UnreachableDistance;
                    for (x = Width - 1; x >= 0; x--)
                    {
                        int index = (y * Width) + x;
                        if (Mask[index] == 0)
                        {
                            distance = 0.0f;
                            continue;
                        }

                        distance += 1.0f;
                        Distances[index] = math.min(Distances[index], distance);
                    }
                }
            }
        }

        [BurstCompile]
        private struct BiomeOverrideSimpleApplyJob : IJobParallelFor
        {
            public int Width;
            public int Height;
            public long LocalSeed;
            public int BiomeIndex;
            public float Probability;

            public NativeArray<int> BiomeChannel;

            [Unity.Collections.ReadOnly]
            public NativeArray<byte> Mask;

            public void Execute(int index)
            {
                if (Mask[index] == 0 || Probability <= 0.0f)
                {
                    return;
                }

                int x = index % Width;
                int y = index / Width;
                if (HashToUnitFloat(LocalSeed, x, y) <= Probability)
                {
                    BiomeChannel[index] = BiomeIndex;
                }
            }

            private static float HashToUnitFloat(long localSeed, int x, int y)
            {
                uint seedLow = unchecked((uint)localSeed);
                uint seedHigh = unchecked((uint)(localSeed >> 32));
                uint hash = math.hash(new uint4((uint)x, (uint)y, seedLow, seedHigh));
                return (float)(hash / 4294967296.0d);
            }
        }

        [BurstCompile]
        private struct BiomeOverrideApplyJob : IJobParallelFor
        {
            public int Width;
            public int Height;
            public long LocalSeed;
            public int BiomeIndex;
            public float Probability;
            public float BlendEdgeWidth;

            public NativeArray<int> BiomeChannel;

            [Unity.Collections.ReadOnly]
            public NativeArray<byte> Mask;

            [Unity.Collections.ReadOnly]
            public NativeArray<float> Distances;

            public void Execute(int index)
            {
                if (Mask[index] == 0)
                {
                    return;
                }

                float effectiveProbability = Probability;
                if (BlendEdgeWidth > 0.0f)
                {
                    float interiorDistance = math.max(0.0f, Distances[index] - 1.0f);
                    effectiveProbability *= math.saturate(interiorDistance / BlendEdgeWidth);
                }

                if (effectiveProbability <= 0.0f)
                {
                    return;
                }

                int x = index % Width;
                int y = index / Width;
                if (HashToUnitFloat(LocalSeed, x, y) <= effectiveProbability)
                {
                    BiomeChannel[index] = BiomeIndex;
                }
            }

            private static float HashToUnitFloat(long localSeed, int x, int y)
            {
                uint seedLow = unchecked((uint)localSeed);
                uint seedHigh = unchecked((uint)(localSeed >> 32));
                uint hash = math.hash(new uint4((uint)x, (uint)y, seedLow, seedHigh));
                return (float)(hash / 4294967296.0d);
            }
        }
    }
}
