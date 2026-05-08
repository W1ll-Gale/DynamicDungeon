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
    [NodeDisplayName("Vein")]
    [HelpURL("https://dynamicdungeon.mrbytesized.com/docs/nodes/filter-transform/vein")]
    [Description("Grows chain-like filaments from explicit seed points or from sampled mask tiles.")]
    public sealed class VeinNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        private const string DefaultNodeName = "Vein";
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
        [DescriptionAttribute("Minimum number of walk steps for each sampled vein.")]
        private int _veinLengthMin;

        [MinValue(0.0f)]
        [DescriptionAttribute("Maximum number of walk steps for each sampled vein.")]
        private int _veinLengthMax;

        [RangeAttribute(0.0f, 1.0f)]
        [DescriptionAttribute("Chance that a step turns to a perpendicular direction instead of continuing forward.")]
        private float _wanderProbability;

        [RangeAttribute(0.0f, 1.0f)]
        [DescriptionAttribute("Chance that a walked step spawns one additional branch.")]
        private float _forkProbability;

        [MinValue(0.0f)]
        [DescriptionAttribute("Maximum angular deviation, in degrees, used when converting a fork into the nearest cardinal or diagonal direction.")]
        private float _forkAngleRange;

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

        public VeinNode(
            string nodeId,
            string nodeName,
            string seedChannelName = "",
            string maskChannelName = "",
            string outputChannelName = "",
            int seedCount = 1,
            int veinLengthMin = 4,
            int veinLengthMax = 8,
            float wanderProbability = 0.25f,
            float forkProbability = 0.15f,
            float forkAngleRange = 45.0f)
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
            _veinLengthMin = math.max(0, veinLengthMin);
            _veinLengthMax = math.max(_veinLengthMin, veinLengthMax);
            _wanderProbability = math.saturate(wanderProbability);
            _forkProbability = math.saturate(forkProbability);
            _forkAngleRange = math.max(0.0f, forkAngleRange);

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

            if (string.Equals(name, "veinLengthMin", StringComparison.OrdinalIgnoreCase))
            {
                int parsedValue;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedValue))
                {
                    _veinLengthMin = math.max(0, parsedValue);
                    _veinLengthMax = math.max(_veinLengthMin, _veinLengthMax);
                }

                return;
            }

            if (string.Equals(name, "veinLengthMax", StringComparison.OrdinalIgnoreCase))
            {
                int parsedValue;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedValue))
                {
                    _veinLengthMax = math.max(_veinLengthMin, parsedValue);
                }

                return;
            }

            if (string.Equals(name, "wanderProbability", StringComparison.OrdinalIgnoreCase))
            {
                float parsedValue;
                if (float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsedValue))
                {
                    _wanderProbability = math.saturate(parsedValue);
                }

                return;
            }

            if (string.Equals(name, "forkProbability", StringComparison.OrdinalIgnoreCase))
            {
                float parsedValue;
                if (float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsedValue))
                {
                    _forkProbability = math.saturate(parsedValue);
                }

                return;
            }

            if (string.Equals(name, "forkAngleRange", StringComparison.OrdinalIgnoreCase))
            {
                float parsedValue;
                if (float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsedValue))
                {
                    _forkAngleRange = math.max(0.0f, parsedValue);
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
            NativeList<BranchState> branchStack = new NativeList<BranchState>(math.max(1, ResolveBranchCapacity(pointSeeds, _seedCount, _veinLengthMax)), Allocator.TempJob);

            VeinGrowthJob job = new VeinGrowthJob
            {
                PointSeeds = pointSeeds,
                CandidateTiles = candidateTiles,
                SelectedSeeds = selectedSeeds,
                BranchStack = branchStack,
                Output = output,
                Width = context.Width,
                Height = context.Height,
                SeedCount = _seedCount,
                VeinLengthMin = _veinLengthMin,
                VeinLengthMax = _veinLengthMax,
                WanderProbability = _wanderProbability,
                ForkProbability = _forkProbability,
                ForkAngleRange = _forkAngleRange,
                LocalSeed = context.LocalSeed
            };

            JobHandle jobHandle = job.Schedule(context.InputDependency);
            JobHandle disposeSelectedSeedsHandle = selectedSeeds.Dispose(jobHandle);
            JobHandle disposeBranchStackHandle = branchStack.Dispose(jobHandle);
            JobHandle combinedHandle = JobHandle.CombineDependencies(disposeSelectedSeedsHandle, disposeBranchStackHandle);

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

        private static int ResolveBranchCapacity(NativeList<int2> pointSeeds, int seedCount, int veinLengthMax)
        {
            int resolvedSeedCount = pointSeeds.IsCreated ? pointSeeds.Length : math.max(1, seedCount);
            return math.max(1, resolvedSeedCount * math.max(1, veinLengthMax + 1));
        }

        [BurstCompile]
        private struct VeinGrowthJob : IJob
        {
            [ReadOnly]
            public NativeList<int2> PointSeeds;

            public NativeList<int> CandidateTiles;
            public NativeList<int> SelectedSeeds;
            public NativeList<BranchState> BranchStack;
            public NativeArray<byte> Output;
            public int Width;
            public int Height;
            public int SeedCount;
            public int VeinLengthMin;
            public int VeinLengthMax;
            public float WanderProbability;
            public float ForkProbability;
            public float ForkAngleRange;
            public long LocalSeed;

            public void Execute()
            {
                ClearOutput();
                SelectedSeeds.Clear();
                BranchStack.Clear();

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

                int nextBranchId = 1;

                int seedIndex;
                for (seedIndex = 0; seedIndex < SelectedSeeds.Length; seedIndex++)
                {
                    int seedTileIndex = SelectedSeeds[seedIndex];
                    int2 seedPosition = ToPosition(seedTileIndex);
                    Mark(seedPosition);

                    BranchState branchState = new BranchState
                    {
                        Position = seedPosition,
                        Direction = GetDirectionFromOrdinal((int)(Hash(seedTileIndex, seedIndex, 0, 0, 0x8B1Du) % 8u)),
                        RemainingSteps = ResolveLength(seedTileIndex, seedIndex),
                        BranchId = nextBranchId,
                        CanFork = true
                    };

                    nextBranchId++;
                    BranchStack.Add(branchState);
                }

                while (BranchStack.Length > 0)
                {
                    BranchState branchState = BranchStack[BranchStack.Length - 1];
                    BranchStack.Length--;
                    WalkBranch(branchState, ref nextBranchId);
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
                    if (IsWithinBounds(point))
                    {
                        SelectedSeeds.Add(ToIndex(point));
                    }
                }
            }

            private void GatherSampledSeeds()
            {
                if (!CandidateTiles.IsCreated || CandidateTiles.Length == 0 || SeedCount <= 0)
                {
                    return;
                }

                ShuffleCandidateTiles();

                int candidateIndex;
                for (candidateIndex = 0; candidateIndex < CandidateTiles.Length && SelectedSeeds.Length < SeedCount; candidateIndex++)
                {
                    SelectedSeeds.Add(CandidateTiles[candidateIndex]);
                }
            }

            private void ShuffleCandidateTiles()
            {
                int index;
                for (index = CandidateTiles.Length - 1; index > 0; index--)
                {
                    int swapIndex = (int)(Hash(index, CandidateTiles.Length, 0, 0, 0xB4275319u) % (uint)(index + 1));
                    if (swapIndex == index)
                    {
                        continue;
                    }

                    int swappedValue = CandidateTiles[swapIndex];
                    CandidateTiles[swapIndex] = CandidateTiles[index];
                    CandidateTiles[index] = swappedValue;
                }
            }

            private void WalkBranch(BranchState branchState, ref int nextBranchId)
            {
                int2 position = branchState.Position;
                int2 direction = branchState.Direction;

                int stepIndex;
                for (stepIndex = 0; stepIndex < branchState.RemainingSteps; stepIndex++)
                {
                    direction = ResolveStepDirection(direction, branchState.BranchId, stepIndex);
                    int2 nextPosition = position + direction;
                    if (!IsWithinBounds(nextPosition))
                    {
                        break;
                    }

                    position = nextPosition;
                    Mark(position);

                    int remainingAfterStep = branchState.RemainingSteps - stepIndex - 1;
                    if (branchState.CanFork && remainingAfterStep > 0 && ShouldFork(branchState.BranchId, stepIndex, position))
                    {
                        BranchState forkState = new BranchState
                        {
                            Position = position,
                            Direction = ResolveForkDirection(direction, branchState.BranchId, stepIndex),
                            RemainingSteps = remainingAfterStep,
                            BranchId = nextBranchId,
                            CanFork = false
                        };

                        nextBranchId++;
                        BranchStack.Add(forkState);
                    }
                }
            }

            private int ResolveLength(int seedTileIndex, int seedOrderIndex)
            {
                if (VeinLengthMax <= VeinLengthMin)
                {
                    return VeinLengthMin;
                }

                int range = VeinLengthMax - VeinLengthMin + 1;
                return VeinLengthMin + (int)(Hash(seedTileIndex, seedOrderIndex, VeinLengthMin, VeinLengthMax, 0x35A79B61u) % (uint)range);
            }

            private int2 ResolveStepDirection(int2 currentDirection, int branchId, int stepIndex)
            {
                if (HashToUnitFloat(branchId, stepIndex, currentDirection.x, currentDirection.y, 0xCC5AB24Fu) >= WanderProbability)
                {
                    return currentDirection;
                }

                int turnSign = HashToUnitFloat(branchId, stepIndex, currentDirection.y, currentDirection.x, 0xA91F2037u) < 0.5f ? -1 : 1;
                return RotatePerpendicular(currentDirection, turnSign);
            }

            private bool ShouldFork(int branchId, int stepIndex, int2 position)
            {
                return HashToUnitFloat(branchId, stepIndex, position.x, position.y, 0xF1732C85u) < ForkProbability;
            }

            private int2 ResolveForkDirection(int2 currentDirection, int branchId, int stepIndex)
            {
                float currentAngle = math.degrees(math.atan2(currentDirection.y, currentDirection.x));
                float deviation = (HashToUnitFloat(branchId, stepIndex, currentDirection.x, currentDirection.y, 0x76E49A11u) * 2.0f) - 1.0f;
                float targetAngle = currentAngle + (deviation * ForkAngleRange);
                return AngleToNearestDirection(targetAngle);
            }

            private int2 AngleToNearestDirection(float degrees)
            {
                float wrappedDegrees = degrees;
                while (wrappedDegrees <= -180.0f)
                {
                    wrappedDegrees += 360.0f;
                }

                while (wrappedDegrees > 180.0f)
                {
                    wrappedDegrees -= 360.0f;
                }

                float ordinalFloat = wrappedDegrees / 45.0f;
                int ordinal = (int)math.round(ordinalFloat);
                ordinal = ((ordinal % 8) + 8) % 8;
                return GetDirectionFromOrdinal(ordinal);
            }

            private int2 RotatePerpendicular(int2 direction, int turnSign)
            {
                if (turnSign < 0)
                {
                    return new int2(-direction.y, direction.x);
                }

                return new int2(direction.y, -direction.x);
            }

            private int2 GetDirectionFromOrdinal(int ordinal)
            {
                int wrappedOrdinal = ((ordinal % 8) + 8) % 8;

                if (wrappedOrdinal == 0)
                {
                    return new int2(1, 0);
                }

                if (wrappedOrdinal == 1)
                {
                    return new int2(1, 1);
                }

                if (wrappedOrdinal == 2)
                {
                    return new int2(0, 1);
                }

                if (wrappedOrdinal == 3)
                {
                    return new int2(-1, 1);
                }

                if (wrappedOrdinal == 4)
                {
                    return new int2(-1, 0);
                }

                if (wrappedOrdinal == 5)
                {
                    return new int2(-1, -1);
                }

                if (wrappedOrdinal == 6)
                {
                    return new int2(0, -1);
                }

                return new int2(1, -1);
            }

            private void Mark(int2 point)
            {
                Output[ToIndex(point)] = 1;
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

        private struct BranchState
        {
            public int2 Position;
            public int2 Direction;
            public int RemainingSteps;
            public int BranchId;
            public bool CanFork;
        }
    }
}
