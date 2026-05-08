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
    [NodeCategory("Growth")]
    [HelpURL("https://dynamicdungeon.mrbytesized.com/docs/nodes/utility/cluster")]
    [NodeDisplayName("Cluster")]
    [Description("Grows blob-like clusters from explicit seed points or from sampled mask tiles.")]
    public sealed class ClusterNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        private const string DefaultNodeName = "Cluster";
        private const string SeedPortName = "Seeds";
        private const string MaskPortName = "Mask";
        private const string FallbackOutputPortName = "Output";
        private const string PreferredOutputDisplayName = FallbackOutputPortName;

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;

        private NodePortDefinition[] _ports;
        private ChannelDeclaration[] _channelDeclarations;
        private string _seedChannelName;
        private string _maskChannelName;
        private string _outputChannelName;

        [MinValue(0.0f)]
        [DescriptionAttribute("Number of seeds to sample when no point-list seed input is connected.")]
        private int _seedCount;

        [MinValue(0.0f)]
        [DescriptionAttribute("Number of outward growth passes to run from each seed.")]
        private int _iterations;

        [RangeAttribute(0.0f, 1.0f)]
        [DescriptionAttribute("Base probability that a frontier tile spreads into a neighbouring tile.")]
        private float _initialSpreadProbability;

        [RangeAttribute(0.0f, 1.0f)]
        [DescriptionAttribute("Per-step falloff applied to the spread probability as the cluster grows away from its seed.")]
        private float _falloff;

        [MinValue(0.0f)]
        [DescriptionAttribute("Minimum Euclidean tile distance required between sampled seed points.")]
        private int _minClusterSeparation;

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

        public ClusterNode(
            string nodeId,
            string nodeName,
            string seedChannelName = "",
            string maskChannelName = "",
            string outputChannelName = "",
            int seedCount = 1,
            int iterations = 1,
            float initialSpreadProbability = 0.7f,
            float falloff = 0.8f,
            int minClusterSeparation = 0)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _seedChannelName = seedChannelName ?? string.Empty;
            _maskChannelName = maskChannelName ?? string.Empty;
            _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(nodeId, outputChannelName, FallbackOutputPortName);
            _seedCount = math.max(0, seedCount);
            _iterations = math.max(0, iterations);
            _initialSpreadProbability = math.saturate(initialSpreadProbability);
            _falloff = math.saturate(falloff);
            _minClusterSeparation = math.max(0, minClusterSeparation);

            RefreshPorts();
            RefreshChannelDeclarations();
        }

        public void ReceiveInputConnections(InputConnectionMap inputConnections)
        {
            _seedChannelName = GrowthSeedUtility.ResolveInputConnection(inputConnections, SeedPortName);
            _maskChannelName = GrowthSeedUtility.ResolveInputConnection(inputConnections, MaskPortName);
            RefreshChannelDeclarations();
        }

        public void ReceiveParameter(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (string.Equals(name, "seedCount", StringComparison.OrdinalIgnoreCase))
            {
                int parsedValue;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedValue))
                {
                    _seedCount = math.max(0, parsedValue);
                }

                return;
            }

            if (string.Equals(name, "iterations", StringComparison.OrdinalIgnoreCase))
            {
                int parsedValue;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedValue))
                {
                    _iterations = math.max(0, parsedValue);
                }

                return;
            }

            if (string.Equals(name, "initialSpreadProbability", StringComparison.OrdinalIgnoreCase))
            {
                float parsedValue;
                if (float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsedValue))
                {
                    _initialSpreadProbability = math.saturate(parsedValue);
                }

                return;
            }

            if (string.Equals(name, "falloff", StringComparison.OrdinalIgnoreCase))
            {
                float parsedValue;
                if (float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsedValue))
                {
                    _falloff = math.saturate(parsedValue);
                }

                return;
            }

            if (string.Equals(name, "minClusterSeparation", StringComparison.OrdinalIgnoreCase))
            {
                int parsedValue;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedValue))
                {
                    _minClusterSeparation = math.max(0, parsedValue);
                }

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
            NativeArray<byte> output = context.GetBoolMaskChannel(_outputChannelName);
            NativeList<int2> pointSeeds = new NativeList<int2>(1, Allocator.TempJob);
            NativeList<int> candidateTiles = new NativeList<int>(1, Allocator.TempJob);
            bool disposePointSeeds = true;
            bool disposeCandidateTiles = true;

            if (!string.IsNullOrWhiteSpace(_seedChannelName))
            {
                pointSeeds.Dispose();
                pointSeeds = context.GetPointListChannel(_seedChannelName);
                disposePointSeeds = false;
            }
            else if (!string.IsNullOrWhiteSpace(_maskChannelName))
            {
                candidateTiles.Dispose();
                candidateTiles = GrowthSeedUtility.BuildCandidateTileIndices(context.GetBoolMaskChannel(_maskChannelName));
            }
            else
            {
                candidateTiles.Dispose();
                candidateTiles = GrowthSeedUtility.BuildAllTileIndices(context.Width, context.Height);
            }

            NativeList<int> selectedSeeds = new NativeList<int>(math.max(1, ResolveSeedCapacity(pointSeeds, candidateTiles, _seedCount)), Allocator.TempJob);
            NativeList<int> frontier = new NativeList<int>(math.max(1, output.Length), Allocator.TempJob);
            NativeList<int> nextFrontier = new NativeList<int>(math.max(1, output.Length), Allocator.TempJob);
            NativeArray<int> visitMarkers = new NativeArray<int>(math.max(1, output.Length), Allocator.TempJob, NativeArrayOptions.ClearMemory);

            ClusterGrowthJob job = new ClusterGrowthJob
            {
                PointSeeds = pointSeeds,
                CandidateTiles = candidateTiles,
                SelectedSeeds = selectedSeeds,
                Frontier = frontier,
                NextFrontier = nextFrontier,
                VisitMarkers = visitMarkers,
                Output = output,
                Width = context.Width,
                Height = context.Height,
                SeedCount = _seedCount,
                Iterations = _iterations,
                InitialSpreadProbability = _initialSpreadProbability,
                Falloff = _falloff,
                MinClusterSeparation = _minClusterSeparation,
                LocalSeed = context.LocalSeed
            };

            JobHandle jobHandle = job.Schedule(context.InputDependency);
            JobHandle disposeSelectedSeedsHandle = selectedSeeds.Dispose(jobHandle);
            JobHandle disposeFrontierHandle = frontier.Dispose(jobHandle);
            JobHandle disposeNextFrontierHandle = nextFrontier.Dispose(jobHandle);
            JobHandle disposeVisitMarkersHandle = visitMarkers.Dispose(jobHandle);
            JobHandle combinedHandle = JobHandle.CombineDependencies(disposeSelectedSeedsHandle, disposeFrontierHandle);
            combinedHandle = JobHandle.CombineDependencies(combinedHandle, disposeNextFrontierHandle);
            combinedHandle = JobHandle.CombineDependencies(combinedHandle, disposeVisitMarkersHandle);

            if (disposePointSeeds)
            {
                JobHandle disposePointSeedsHandle = pointSeeds.Dispose(jobHandle);
                combinedHandle = JobHandle.CombineDependencies(combinedHandle, disposePointSeedsHandle);
            }

            if (disposeCandidateTiles)
            {
                JobHandle disposeCandidateTilesHandle = candidateTiles.Dispose(jobHandle);
                combinedHandle = JobHandle.CombineDependencies(combinedHandle, disposeCandidateTilesHandle);
            }

            return combinedHandle;
        }

        private void RefreshPorts()
        {
            string outputPortDisplayName = GraphPortNameUtility.ResolveOutputDisplayName(_nodeId, _outputChannelName, PreferredOutputDisplayName);
            _ports = new[]
            {
                new NodePortDefinition(SeedPortName, PortDirection.Input, ChannelType.PointList, PortCapacity.Single, false),
                new NodePortDefinition(MaskPortName, PortDirection.Input, ChannelType.BoolMask, PortCapacity.Single, false),
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.BoolMask, displayName: outputPortDisplayName)
            };
        }

        private void RefreshChannelDeclarations()
        {
            List<ChannelDeclaration> channelDeclarations = new List<ChannelDeclaration>(3);
            GrowthSeedUtility.AppendReadDeclarationIfConnected(channelDeclarations, _seedChannelName, ChannelType.PointList);
            GrowthSeedUtility.AppendReadDeclarationIfConnected(channelDeclarations, _maskChannelName, ChannelType.BoolMask);
            channelDeclarations.Add(new ChannelDeclaration(_outputChannelName, ChannelType.BoolMask, true));
            _channelDeclarations = channelDeclarations.ToArray();
        }

        private static int ResolveSeedCapacity(NativeList<int2> pointSeeds, NativeList<int> candidateTiles, int seedCount)
        {
            if (pointSeeds.IsCreated)
            {
                return pointSeeds.Length;
            }

            if (candidateTiles.IsCreated)
            {
                return math.min(candidateTiles.Length, math.max(0, seedCount));
            }

            return math.max(1, seedCount);
        }

        [BurstCompile]
        private struct ClusterGrowthJob : IJob
        {
            [ReadOnly]
            public NativeList<int2> PointSeeds;

            public NativeList<int> CandidateTiles;
            public NativeList<int> SelectedSeeds;
            public NativeList<int> Frontier;
            public NativeList<int> NextFrontier;
            public NativeArray<int> VisitMarkers;
            public NativeArray<byte> Output;
            public int Width;
            public int Height;
            public int SeedCount;
            public int Iterations;
            public float InitialSpreadProbability;
            public float Falloff;
            public int MinClusterSeparation;
            public long LocalSeed;

            public void Execute()
            {
                ClearOutput();
                SelectedSeeds.Clear();
                Frontier.Clear();
                NextFrontier.Clear();

                if (PointSeeds.IsCreated && PointSeeds.Length > 0)
                {
                    GatherPointSeeds();
                }
                else
                {
                    GatherSampledSeeds();
                }

                if (SelectedSeeds.Length == 0)
                {
                    return;
                }

                int seedOrderIndex;
                for (seedOrderIndex = 0; seedOrderIndex < SelectedSeeds.Length; seedOrderIndex++)
                {
                    GrowClusterFromSeed(seedOrderIndex, SelectedSeeds[seedOrderIndex]);
                }
            }

            private void ClearOutput()
            {
                int index;
                for (index = 0; index < Output.Length; index++)
                {
                    Output[index] = 0;
                }
            }

            private void GatherPointSeeds()
            {
                int seedIndex;
                for (seedIndex = 0; seedIndex < PointSeeds.Length; seedIndex++)
                {
                    int2 point = PointSeeds[seedIndex];
                    if (!IsWithinBounds(point))
                    {
                        continue;
                    }

                    SelectedSeeds.Add(ToIndex(point));
                }
            }

            private void GatherSampledSeeds()
            {
                if (!CandidateTiles.IsCreated || CandidateTiles.Length == 0 || SeedCount <= 0)
                {
                    return;
                }

                ShuffleCandidateTiles();

                int minimumDistanceSquared = MinClusterSeparation * MinClusterSeparation;
                int candidateIndex;
                for (candidateIndex = 0; candidateIndex < CandidateTiles.Length && SelectedSeeds.Length < SeedCount; candidateIndex++)
                {
                    int candidateTileIndex = CandidateTiles[candidateIndex];
                    if (minimumDistanceSquared > 0 && !IsCandidateSeparated(candidateTileIndex, minimumDistanceSquared))
                    {
                        continue;
                    }

                    SelectedSeeds.Add(candidateTileIndex);
                }
            }

            private void ShuffleCandidateTiles()
            {
                int index;
                for (index = CandidateTiles.Length - 1; index > 0; index--)
                {
                    int swapIndex = (int)(Hash(index, CandidateTiles.Length, 0, 0, 0x41B8C27Du) % (uint)(index + 1));
                    if (swapIndex == index)
                    {
                        continue;
                    }

                    int swappedValue = CandidateTiles[swapIndex];
                    CandidateTiles[swapIndex] = CandidateTiles[index];
                    CandidateTiles[index] = swappedValue;
                }
            }

            private bool IsCandidateSeparated(int candidateTileIndex, int minimumDistanceSquared)
            {
                int2 candidatePosition = ToPosition(candidateTileIndex);

                int seedIndex;
                for (seedIndex = 0; seedIndex < SelectedSeeds.Length; seedIndex++)
                {
                    int2 acceptedPosition = ToPosition(SelectedSeeds[seedIndex]);
                    int2 delta = candidatePosition - acceptedPosition;
                    if (math.lengthsq(new int2(delta.x, delta.y)) < minimumDistanceSquared)
                    {
                        return false;
                    }
                }

                return true;
            }

            private void GrowClusterFromSeed(int seedOrderIndex, int seedTileIndex)
            {
                int visitMarker = seedOrderIndex + 1;
                visitMarker = math.max(visitMarker, 1);

                Frontier.Clear();
                NextFrontier.Clear();
                VisitMarkers[seedTileIndex] = visitMarker;
                Output[seedTileIndex] = 1;
                Frontier.Add(seedTileIndex);

                int stepDistance;
                for (stepDistance = 1; stepDistance <= Iterations && Frontier.Length > 0; stepDistance++)
                {
                    float spreadProbability = InitialSpreadProbability * math.pow(Falloff, stepDistance);
                    if (spreadProbability <= 0.0f)
                    {
                        break;
                    }

                    NextFrontier.Clear();

                    int frontierIndex;
                    for (frontierIndex = 0; frontierIndex < Frontier.Length; frontierIndex++)
                    {
                        int frontierTileIndex = Frontier[frontierIndex];
                        int2 frontierPosition = ToPosition(frontierTileIndex);
                        TrySpreadToNeighbour(seedTileIndex, frontierPosition.x - 1, frontierPosition.y, visitMarker, stepDistance, spreadProbability);
                        TrySpreadToNeighbour(seedTileIndex, frontierPosition.x + 1, frontierPosition.y, visitMarker, stepDistance, spreadProbability);
                        TrySpreadToNeighbour(seedTileIndex, frontierPosition.x, frontierPosition.y - 1, visitMarker, stepDistance, spreadProbability);
                        TrySpreadToNeighbour(seedTileIndex, frontierPosition.x, frontierPosition.y + 1, visitMarker, stepDistance, spreadProbability);
                    }

                    NativeList<int> swappedFrontier = Frontier;
                    Frontier = NextFrontier;
                    NextFrontier = swappedFrontier;
                }
            }

            private void TrySpreadToNeighbour(int seedTileIndex, int x, int y, int visitMarker, int stepDistance, float spreadProbability)
            {
                if (x < 0 || x >= Width || y < 0 || y >= Height)
                {
                    return;
                }

                int tileIndex = (y * Width) + x;
                if (VisitMarkers[tileIndex] == visitMarker)
                {
                    return;
                }

                VisitMarkers[tileIndex] = visitMarker;
                if (HashToUnitFloat(seedTileIndex, tileIndex, stepDistance, visitMarker, 0xD8F16A31u) > spreadProbability)
                {
                    return;
                }

                Output[tileIndex] = 1;
                NextFrontier.Add(tileIndex);
            }

            private bool IsWithinBounds(int2 point)
            {
                return point.x >= 0 && point.x < Width && point.y >= 0 && point.y < Height;
            }

            private int ToIndex(int2 point)
            {
                return (point.y * Width) + point.x;
            }

            private int2 ToPosition(int tileIndex)
            {
                return new int2(tileIndex % Width, tileIndex / Width);
            }

            private float HashToUnitFloat(int valueA, int valueB, int valueC, int valueD, uint salt)
            {
                return (float)(Hash(valueA, valueB, valueC, valueD, salt) / 4294967296.0d);
            }

            private uint Hash(int valueA, int valueB, int valueC, int valueD, uint salt)
            {
                uint seedLow = unchecked((uint)LocalSeed);
                uint seedHigh = unchecked((uint)(LocalSeed >> 32));
                return math.hash(new uint4(
                    unchecked((uint)valueA) ^ salt,
                    unchecked((uint)valueB) ^ (salt * 0x9E3779B9u),
                    unchecked((uint)valueC) ^ seedLow,
                    unchecked((uint)valueD) ^ seedHigh));
            }
        }
    }
}
