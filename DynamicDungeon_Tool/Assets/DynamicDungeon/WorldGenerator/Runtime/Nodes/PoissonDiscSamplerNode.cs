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
    [NodeCategory("Point Generation")]
    [NodeDisplayName("Poisson Disc Sampler")]
    [Description("Generates a point list from eligible tiles while enforcing a minimum distance between accepted points.")]
    [HelpURL("https://dynamicdungeon.mrbytesized.com/docs/nodes/sampling/poisson-disc-sampler")]
    public sealed class PoissonDiscSamplerNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        private const string DefaultNodeName = "Poisson Disc Sampler";
        private const string InputPortName = "Mask";
        private const string FallbackOutputPortName = "Points";
        private const string PreferredOutputDisplayName = "Points";
        private const float MinimumSupportedDistance = 0.01f;

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;

        private NodePortDefinition[] _ports;
        private ChannelDeclaration[] _channelDeclarations;
        private string _inputChannelName;
        private string _outputChannelName;

        [MinValue(MinimumSupportedDistance)]
        [DescriptionAttribute("Minimum spacing, in tiles, between accepted points.")]
        private float _minDistance;

        [MinValue(1.0f)]
        [DescriptionAttribute("Maximum annulus samples attempted from each active point before it is retired.")]
        private int _maxAttempts;

        [MinValue(0.0f)]
        [DescriptionAttribute("Maximum number of points to emit. Zero keeps every accepted point.")]
        private int _pointCount;

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

        public PoissonDiscSamplerNode(
            string nodeId,
            string nodeName,
            string inputChannelName = "",
            string outputChannelName = "",
            float minDistance = 1.0f,
            int maxAttempts = 30,
            int pointCount = 0)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _inputChannelName = inputChannelName ?? string.Empty;
            _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(nodeId, outputChannelName, FallbackOutputPortName);
            _minDistance = math.max(MinimumSupportedDistance, minDistance);
            _maxAttempts = math.max(1, maxAttempts);
            _pointCount = math.max(0, pointCount);

            RefreshPorts();
            RefreshChannelDeclarations();
        }

        public void ReceiveInputConnections(InputConnectionMap inputConnections)
        {
            string inputChannelName;
            if (inputConnections != null && inputConnections.TryGetValue(InputPortName, out inputChannelName))
            {
                _inputChannelName = inputChannelName ?? string.Empty;
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

            if (string.Equals(name, "minDistance", StringComparison.OrdinalIgnoreCase))
            {
                float parsedValue;
                if (float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsedValue))
                {
                    _minDistance = math.max(MinimumSupportedDistance, parsedValue);
                }

                return;
            }

            if (string.Equals(name, "maxAttempts", StringComparison.OrdinalIgnoreCase))
            {
                int parsedValue;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedValue))
                {
                    _maxAttempts = math.max(1, parsedValue);
                }

                return;
            }

            if (string.Equals(name, "pointCount", StringComparison.OrdinalIgnoreCase))
            {
                int parsedValue;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedValue))
                {
                    _pointCount = math.max(0, parsedValue);
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
            NativeArray<byte> candidateMask = context.GetBoolMaskChannel(_inputChannelName);
            NativeList<int2> output = context.GetPointListChannel(_outputChannelName);
            output.Clear();

            NativeList<int2> eligiblePoints = BuildEligiblePoints(candidateMask, context.Width, context.Height);
            if (eligiblePoints.Length == 0)
            {
                eligiblePoints.Dispose();
                return context.InputDependency;
            }

            if (output.Capacity < eligiblePoints.Length)
            {
                output.Capacity = eligiblePoints.Length;
            }

            NativeList<int> activePointIndices = new NativeList<int>(eligiblePoints.Length, Allocator.TempJob);
            int gridCellSize = CalculateGridCellSize(_minDistance);
            int gridWidth = (context.Width + gridCellSize - 1) / gridCellSize;
            int gridHeight = (context.Height + gridCellSize - 1) / gridCellSize;
            NativeArray<int> occupancyGrid = new NativeArray<int>(math.max(1, gridWidth * gridHeight), Allocator.TempJob, NativeArrayOptions.ClearMemory);

            PoissonDiscSamplerJob job = new PoissonDiscSamplerJob
            {
                CandidateMask = candidateMask,
                EligiblePoints = eligiblePoints,
                Output = output,
                ActivePointIndices = activePointIndices,
                OccupancyGrid = occupancyGrid,
                Width = context.Width,
                Height = context.Height,
                GridWidth = gridWidth,
                GridHeight = gridHeight,
                GridCellSize = gridCellSize,
                MinDistance = _minDistance,
                MaxAttempts = _maxAttempts,
                LocalSeed = context.LocalSeed
            };

            JobHandle jobHandle = job.Schedule(context.InputDependency);
            jobHandle = PointListSamplingUtility.ScheduleShuffleAndLimit(output, _pointCount, context.LocalSeed, 0xC11F3D5Bu, jobHandle);
            JobHandle disposeEligibleHandle = eligiblePoints.Dispose(jobHandle);
            JobHandle disposeActiveHandle = activePointIndices.Dispose(disposeEligibleHandle);
            return occupancyGrid.Dispose(disposeActiveHandle);
        }

        private void RefreshPorts()
        {
            string outputPortDisplayName = GraphPortNameUtility.ResolveOutputDisplayName(_nodeId, _outputChannelName, PreferredOutputDisplayName);
            _ports = new[]
            {
                new NodePortDefinition(InputPortName, PortDirection.Input, ChannelType.BoolMask, PortCapacity.Single, true),
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.PointList, displayName: outputPortDisplayName)
            };
        }

        private void RefreshChannelDeclarations()
        {
            if (!string.IsNullOrWhiteSpace(_inputChannelName))
            {
                _channelDeclarations = new[]
                {
                    new ChannelDeclaration(_inputChannelName, ChannelType.BoolMask, false),
                    new ChannelDeclaration(_outputChannelName, ChannelType.PointList, true)
                };
                return;
            }

            _channelDeclarations = new[]
            {
                new ChannelDeclaration(_outputChannelName, ChannelType.PointList, true)
            };
        }

        private static NativeList<int2> BuildEligiblePoints(NativeArray<byte> candidateMask, int width, int height)
        {
            NativeList<int2> eligiblePoints = new NativeList<int2>(math.max(1, candidateMask.Length), Allocator.TempJob);

            int index;
            for (index = 0; index < candidateMask.Length; index++)
            {
                if (candidateMask[index] == 0)
                {
                    continue;
                }

                int x = index % width;
                int y = index / width;
                eligiblePoints.Add(new int2(x, y));
            }

            return eligiblePoints;
        }

        private static int CalculateGridCellSize(float minDistance)
        {
            float cellSize = minDistance / math.sqrt(2.0f);
            return math.max(1, (int)math.floor(cellSize));
        }

        [BurstCompile]
        private struct PoissonDiscSamplerJob : IJob
        {
            [ReadOnly]
            public NativeArray<byte> CandidateMask;

            [ReadOnly]
            public NativeList<int2> EligiblePoints;

            public NativeList<int2> Output;
            public NativeList<int> ActivePointIndices;
            public NativeArray<int> OccupancyGrid;
            public int Width;
            public int Height;
            public int GridWidth;
            public int GridHeight;
            public int GridCellSize;
            public float MinDistance;
            public int MaxAttempts;
            public long LocalSeed;

            public void Execute()
            {
                if (EligiblePoints.Length == 0)
                {
                    return;
                }

                int stepIndex = 0;
                while (TryAddSeedFromRemainingEligible(stepIndex))
                {
                    while (ActivePointIndices.Length > 0)
                    {
                        int activeListIndex = HashToIndex(ActivePointIndices.Length, stepIndex, 0, 2u);
                        int activeOutputIndex = ActivePointIndices[activeListIndex];
                        int2 activePoint = Output[activeOutputIndex];
                        bool placed = false;

                        int attemptIndex;
                        for (attemptIndex = 0; attemptIndex < MaxAttempts; attemptIndex++)
                        {
                            int2 candidatePoint;
                            if (!TryGenerateCandidate(activePoint, stepIndex, attemptIndex, out candidatePoint))
                            {
                                continue;
                            }

                            if (!IsCandidateValid(candidatePoint))
                            {
                                continue;
                            }

                            AddAcceptedPoint(candidatePoint);
                            placed = true;
                            break;
                        }

                        if (!placed)
                        {
                            ActivePointIndices.RemoveAtSwapBack(activeListIndex);
                        }

                        stepIndex++;
                    }
                }
            }

            private void AddAcceptedPoint(int2 point)
            {
                int outputIndex = Output.Length;
                Output.Add(point);
                ActivePointIndices.Add(outputIndex);

                int gridIndex = GetGridIndex(point.x, point.y);
                OccupancyGrid[gridIndex] = outputIndex + 1;
            }

            private bool TryGenerateCandidate(int2 activePoint, int stepIndex, int attemptIndex, out int2 candidatePoint)
            {
                float angle = HashToUnitFloat(stepIndex, attemptIndex, activePoint.x, activePoint.y, 3u) * (math.PI * 2.0f);
                float radius = MinDistance * (1.0f + HashToUnitFloat(stepIndex, attemptIndex, activePoint.x, activePoint.y, 4u));
                float2 direction = new float2(math.cos(angle), math.sin(angle));
                float2 candidatePosition = new float2(activePoint.x, activePoint.y) + (direction * radius);

                candidatePoint = new int2(
                    (int)math.round(candidatePosition.x),
                    (int)math.round(candidatePosition.y));

                return true;
            }

            private bool IsCandidateValid(int2 candidatePoint)
            {
                if (candidatePoint.x < 0 || candidatePoint.x >= Width || candidatePoint.y < 0 || candidatePoint.y >= Height)
                {
                    return false;
                }

                int candidateIndex = (candidatePoint.y * Width) + candidatePoint.x;
                if (CandidateMask[candidateIndex] == 0)
                {
                    return false;
                }

                int candidateCellX = candidatePoint.x / GridCellSize;
                int candidateCellY = candidatePoint.y / GridCellSize;
                int searchRadius = math.max(1, (int)math.ceil(MinDistance / GridCellSize));
                float minimumDistanceSquared = MinDistance * MinDistance;

                int minCellX = math.max(0, candidateCellX - searchRadius);
                int maxCellX = math.min(GridWidth - 1, candidateCellX + searchRadius);
                int minCellY = math.max(0, candidateCellY - searchRadius);
                int maxCellY = math.min(GridHeight - 1, candidateCellY + searchRadius);

                int gridY;
                for (gridY = minCellY; gridY <= maxCellY; gridY++)
                {
                    int gridX;
                    for (gridX = minCellX; gridX <= maxCellX; gridX++)
                    {
                        int storedPointSlot = OccupancyGrid[(gridY * GridWidth) + gridX];
                        if (storedPointSlot == 0)
                        {
                            continue;
                        }

                        int2 existingPoint = Output[storedPointSlot - 1];
                        float2 delta = new float2(candidatePoint.x - existingPoint.x, candidatePoint.y - existingPoint.y);
                        if (math.lengthsq(delta) < minimumDistanceSquared)
                        {
                            return false;
                        }
                    }
                }

                return true;
            }

            private int GetGridIndex(int x, int y)
            {
                int cellX = x / GridCellSize;
                int cellY = y / GridCellSize;
                return (cellY * GridWidth) + cellX;
            }

            private bool TryAddSeedFromRemainingEligible(int stepIndex)
            {
                if (ActivePointIndices.Length > 0)
                {
                    return true;
                }

                int startIndex = HashToIndex(EligiblePoints.Length, stepIndex, Output.Length, 1u);

                int offset;
                for (offset = 0; offset < EligiblePoints.Length; offset++)
                {
                    int eligibleIndex = (startIndex + offset) % EligiblePoints.Length;
                    int2 candidateSeed = EligiblePoints[eligibleIndex];
                    if (!IsCandidateValid(candidateSeed))
                    {
                        continue;
                    }

                    AddAcceptedPoint(candidateSeed);
                    return true;
                }

                return false;
            }

            private int HashToIndex(int length, int valueA, int valueB, uint salt)
            {
                uint hash = Hash(valueA, valueB, 0, 0, salt);
                return (int)(hash % (uint)length);
            }

            private float HashToUnitFloat(int valueA, int valueB, int valueC, int valueD, uint salt)
            {
                uint hash = Hash(valueA, valueB, valueC, valueD, salt);
                return (float)(hash / 4294967296.0d);
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
