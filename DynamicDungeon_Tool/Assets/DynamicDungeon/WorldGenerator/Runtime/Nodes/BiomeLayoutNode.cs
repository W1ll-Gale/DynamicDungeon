using System;
using System.Collections.Generic;
using System.Globalization;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
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
    [NodeDisplayName("Biome Layout")]
    [Description("Builds a deterministic random biome channel from weighted entries and layout constraints.")]
    [HelpURL("https://dynamicdungeon.mrbytesized.com/docs/nodes/biome/biome-layout")]
    public sealed class BiomeLayoutNode : IGenNode, IParameterReceiver, IParameterVisibilityProvider, IBiomeChannelNode
    {
        private struct ResolvedEntry
        {
            public string BiomeGuid;
            public int BiomeIndex;
            public float Weight;
            public int MinSize;
            public int MaxSize;
        }

        private struct ResolvedConstraint
        {
            public BiomeLayoutConstraintType Type;
            public int BiomeIndex;
            public int Size;
        }

        private struct RandomState
        {
            private ulong _state;

            public RandomState(long seed)
            {
                _state = unchecked((ulong)seed);
                if (_state == 0UL)
                {
                    _state = 0x9E3779B97F4A7C15UL;
                }
            }

            public uint NextUInt()
            {
                _state ^= _state << 7;
                _state ^= _state >> 9;
                _state *= 0x9E3779B97F4A7C15UL;
                return unchecked((uint)(_state >> 32));
            }

            public float NextFloat()
            {
                return (float)(NextUInt() / 4294967296.0d);
            }

            public int RangeInclusive(int min, int max)
            {
                if (max <= min)
                {
                    return min;
                }

                uint span = unchecked((uint)(max - min + 1));
                return min + (int)(NextUInt() % span);
            }
        }

        private const int DefaultBatchSize = 64;
        private const string DefaultNodeName = "Biome Layout";
        private const string OutputPortName = BiomeChannelUtility.ChannelName;
        private const int DefaultMaxRegionSize = 0;

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private readonly NodePortDefinition[] _ports;

        [DescriptionAttribute("Chooses how the biome entries are spatially arranged.")]
        private BiomeLayoutMode _layoutMode;

        [DescriptionAttribute("Axis used by Strip mode.")]
        private GradientDirection _axis;

        [MinValue(1.0f)]
        [DescriptionAttribute("Minimum generated region width or height in tiles.")]
        private int _minRegionSize;

        [MinValue(0.0f)]
        [DescriptionAttribute("Maximum generated region width or height in tiles. Zero means unlimited.")]
        private int _maxRegionSize;

        [MinValue(1.0f)]
        [DescriptionAttribute("Cell size used by Cells mode.")]
        private int _cellSize;

        [MinValue(0.0f)]
        [DescriptionAttribute("Width in tiles where neighbouring biomes can blend into each other.")]
        private int _blendWidth;

        [DescriptionAttribute("Biome entries and constraints encoded as JSON. Use the custom editor table to author this value.")]
        private string _rules;

        private BiomeLayoutRules _parsedRules;
        private ResolvedEntry[] _resolvedEntries;
        private ResolvedConstraint[] _resolvedConstraints;
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

        public BiomeLayoutNode(
            string nodeId,
            string nodeName,
            BiomeLayoutMode layoutMode = BiomeLayoutMode.Strips,
            GradientDirection axis = GradientDirection.X,
            int minRegionSize = 24,
            int maxRegionSize = DefaultMaxRegionSize,
            int cellSize = 24,
            int blendWidth = 6,
            string rules = "")
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _layoutMode = layoutMode;
            _axis = axis;
            _minRegionSize = math.max(1, minRegionSize);
            _maxRegionSize = math.max(0, maxRegionSize);
            _cellSize = math.max(1, cellSize);
            _blendWidth = math.max(0, blendWidth);
            _rules = rules ?? string.Empty;
            _parsedRules = ParseRules(_rules);
            _resolvedEntries = Array.Empty<ResolvedEntry>();
            _resolvedConstraints = Array.Empty<ResolvedConstraint>();
            _ports = new[]
            {
                                new NodePortDefinition(OutputPortName, PortDirection.Output, ChannelType.Int, displayName: "Biomes")
            };;
            _channelDeclarations = new[]
            {
                new ChannelDeclaration(BiomeChannelUtility.ChannelName, ChannelType.Int, true)
            };
        }

        public void ReceiveParameter(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (string.Equals(name, "layoutMode", StringComparison.OrdinalIgnoreCase))
            {
                BiomeLayoutMode parsedMode;
                if (Enum.TryParse(value, true, out parsedMode))
                {
                    _layoutMode = parsedMode;
                }

                return;
            }

            if (string.Equals(name, "axis", StringComparison.OrdinalIgnoreCase))
            {
                GradientDirection parsedAxis;
                if (Enum.TryParse(value, true, out parsedAxis))
                {
                    _axis = parsedAxis;
                }

                return;
            }

            if (string.Equals(name, "minRegionSize", StringComparison.OrdinalIgnoreCase))
            {
                _minRegionSize = math.max(1, ParseInt(value, _minRegionSize));
                return;
            }

            if (string.Equals(name, "maxRegionSize", StringComparison.OrdinalIgnoreCase))
            {
                _maxRegionSize = math.max(0, ParseInt(value, _maxRegionSize));
                return;
            }

            if (string.Equals(name, "cellSize", StringComparison.OrdinalIgnoreCase))
            {
                _cellSize = math.max(1, ParseInt(value, _cellSize));
                return;
            }

            if (string.Equals(name, "blendWidth", StringComparison.OrdinalIgnoreCase))
            {
                _blendWidth = math.max(0, ParseInt(value, _blendWidth));
                return;
            }

            if (string.Equals(name, "rules", StringComparison.OrdinalIgnoreCase))
            {
                _rules = value ?? string.Empty;
                _parsedRules = ParseRules(_rules);
            }
        }

        public bool IsParameterVisible(string parameterName)
        {
            if (string.Equals(parameterName, "axis", StringComparison.OrdinalIgnoreCase))
            {
                return _layoutMode == BiomeLayoutMode.Strips;
            }

            if (string.Equals(parameterName, "cellSize", StringComparison.OrdinalIgnoreCase))
            {
                return _layoutMode == BiomeLayoutMode.Cells;
            }

            return true;
        }

        public bool ResolveBiomePalette(BiomeChannelPalette palette, out string errorMessage)
        {
            if (palette == null)
            {
                throw new ArgumentNullException(nameof(palette));
            }

            List<ResolvedEntry> entries = new List<ResolvedEntry>();
            BiomeLayoutEntry[] rawEntries = _parsedRules != null && _parsedRules.Entries != null ? _parsedRules.Entries : Array.Empty<BiomeLayoutEntry>();

            int entryIndex;
            for (entryIndex = 0; entryIndex < rawEntries.Length; entryIndex++)
            {
                BiomeLayoutEntry entry = rawEntries[entryIndex];
                if (entry == null || !entry.Enabled)
                {
                    continue;
                }

                if (entry.Weight <= 0.0f)
                {
                    continue;
                }

                int biomeIndex;
                string paletteError;
                if (!palette.TryResolveIndex(entry.Biome, out biomeIndex, out paletteError))
                {
                    errorMessage = "Biome Layout node '" + _nodeName + "' has an invalid entry at index " + entryIndex.ToString(CultureInfo.InvariantCulture) + ": " + paletteError;
                    return false;
                }

                entries.Add(new ResolvedEntry
                {
                    BiomeGuid = entry.Biome ?? string.Empty,
                    BiomeIndex = biomeIndex,
                    Weight = math.max(0.0f, entry.Weight),
                    MinSize = math.max(0, entry.MinSize),
                    MaxSize = math.max(0, entry.MaxSize)
                });
            }

            if (entries.Count == 0)
            {
                errorMessage = "Biome Layout node '" + _nodeName + "' requires at least one enabled weighted biome entry.";
                return false;
            }

            List<ResolvedConstraint> constraints = new List<ResolvedConstraint>();
            BiomeLayoutConstraint[] rawConstraints = _parsedRules != null && _parsedRules.Constraints != null ? _parsedRules.Constraints : Array.Empty<BiomeLayoutConstraint>();
            int constraintIndex;
            for (constraintIndex = 0; constraintIndex < rawConstraints.Length; constraintIndex++)
            {
                BiomeLayoutConstraint constraint = rawConstraints[constraintIndex];
                if (constraint == null || !constraint.Enabled)
                {
                    continue;
                }

                int biomeIndex;
                string paletteError;
                if (!palette.TryResolveIndex(constraint.Biome, out biomeIndex, out paletteError))
                {
                    errorMessage = "Biome Layout node '" + _nodeName + "' has an invalid constraint at index " + constraintIndex.ToString(CultureInfo.InvariantCulture) + ": " + paletteError;
                    return false;
                }

                constraints.Add(new ResolvedConstraint
                {
                    Type = constraint.Type,
                    BiomeIndex = biomeIndex,
                    Size = math.max(0, constraint.Size)
                });
            }

            _resolvedEntries = entries.ToArray();
            _resolvedConstraints = constraints.ToArray();
            errorMessage = null;
            return true;
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            NativeArray<int> biomeChannel = context.GetIntChannel(BiomeChannelUtility.ChannelName);
            int tileCount = context.Width * context.Height;
            int[] assignments = new int[tileCount];
            bool[] lockedTiles = _blendWidth > 0 || (_layoutMode == BiomeLayoutMode.Cells && HasLockedTileConstraints())
                ? new bool[tileCount]
                : null;

            if (_layoutMode == BiomeLayoutMode.Cells)
            {
                BuildCellAssignments(assignments, lockedTiles, context.Width, context.Height, context.LocalSeed);
            }
            else
            {
                BuildStripAssignments(assignments, lockedTiles, context.Width, context.Height, context.LocalSeed);
            }

            if (_blendWidth > 0)
            {
                ApplyBoundaryBlend(assignments, lockedTiles, context.Width, context.Height, _blendWidth, context.LocalSeed);
            }

            NativeArray<int> nativeAssignments = new NativeArray<int>(assignments.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            int index;
            for (index = 0; index < assignments.Length; index++)
            {
                nativeAssignments[index] = assignments[index];
            }

            BiomeLayoutCopyJob job = new BiomeLayoutCopyJob
            {
                Assignments = nativeAssignments,
                Output = biomeChannel
            };

            JobHandle jobHandle = job.Schedule(biomeChannel.Length, DefaultBatchSize, context.InputDependency);
            return nativeAssignments.Dispose(jobHandle);
        }

        private void BuildStripAssignments(int[] assignments, bool[] lockedTiles, int width, int height, long localSeed)
        {
            bool useY = _axis == GradientDirection.Y;
            int axisLength = useY ? height : width;
            int[] coordinates = new int[axisLength];
            bool[] lockedCoordinates = lockedTiles != null ? new bool[axisLength] : null;

            int coordinateIndex;
            for (coordinateIndex = 0; coordinateIndex < coordinates.Length; coordinateIndex++)
            {
                coordinates[coordinateIndex] = BiomeChannelUtility.UnassignedBiomeIndex;
            }

            ApplyStripConstraints(coordinates, lockedCoordinates, axisLength, localSeed);
            FillUnassignedStripCoordinates(coordinates, axisLength, localSeed);

            int y;
            for (y = 0; y < height; y++)
            {
                int x;
                for (x = 0; x < width; x++)
                {
                    int tileIndex = x + (y * width);
                    int axisCoordinate = useY ? y : x;
                    assignments[tileIndex] = coordinates[axisCoordinate];
                    if (lockedTiles != null)
                    {
                        lockedTiles[tileIndex] = lockedCoordinates != null && lockedCoordinates[axisCoordinate];
                    }
                }
            }
        }

        private void ApplyStripConstraints(int[] coordinates, bool[] lockedCoordinates, int axisLength, long localSeed)
        {
            int constraintIndex;
            for (constraintIndex = 0; constraintIndex < _resolvedConstraints.Length; constraintIndex++)
            {
                ResolvedConstraint constraint = _resolvedConstraints[constraintIndex];
                if (constraint.Type == BiomeLayoutConstraintType.StartEdge)
                {
                    int size = math.clamp(constraint.Size > 0 ? constraint.Size : _minRegionSize, 0, axisLength);
                    ApplyCoordinateRange(coordinates, lockedCoordinates, 0, size, constraint.BiomeIndex, false);
                }
                else if (constraint.Type == BiomeLayoutConstraintType.EndEdge)
                {
                    int size = math.clamp(constraint.Size > 0 ? constraint.Size : _minRegionSize, 0, axisLength);
                    ApplyCoordinateRange(coordinates, lockedCoordinates, axisLength - size, axisLength, constraint.BiomeIndex, false);
                }
            }

            for (constraintIndex = 0; constraintIndex < _resolvedConstraints.Length; constraintIndex++)
            {
                ResolvedConstraint constraint = _resolvedConstraints[constraintIndex];
                if (constraint.Type != BiomeLayoutConstraintType.ProtectedCenter)
                {
                    continue;
                }

                int size = math.clamp(constraint.Size > 0 ? constraint.Size : _minRegionSize, 0, axisLength);
                int start = math.clamp((axisLength - size) / 2, 0, axisLength);
                ApplyCoordinateRange(coordinates, lockedCoordinates, start, start + size, constraint.BiomeIndex, true);
            }

            for (constraintIndex = 0; constraintIndex < _resolvedConstraints.Length; constraintIndex++)
            {
                ResolvedConstraint constraint = _resolvedConstraints[constraintIndex];
                if (constraint.Type != BiomeLayoutConstraintType.Required || ContainsBiome(coordinates, constraint.BiomeIndex))
                {
                    continue;
                }

                ApplyRequiredStripConstraint(coordinates, lockedCoordinates, axisLength, constraint, localSeed + constraintIndex);
            }
        }

        private void ApplyRequiredStripConstraint(int[] coordinates, bool[] lockedCoordinates, int axisLength, ResolvedConstraint constraint, long seed)
        {
            List<int2> gaps = BuildGaps(coordinates);
            int biomeIndex = constraint.BiomeIndex;
            int minSize = math.max(1, constraint.Size > 0 ? constraint.Size : ResolveEntryMinSize(biomeIndex));
            List<int2> validGaps = new List<int2>();

            int gapIndex;
            for (gapIndex = 0; gapIndex < gaps.Count; gapIndex++)
            {
                int2 gap = gaps[gapIndex];
                if (gap.y - gap.x >= minSize)
                {
                    validGaps.Add(gap);
                }
            }

            if (validGaps.Count == 0)
            {
                return;
            }

            RandomState random = new RandomState(seed ^ 0x71276D9B4F41A2D3L);
            int2 selectedGap = validGaps[random.RangeInclusive(0, validGaps.Count - 1)];
            int maxStart = selectedGap.y - minSize;
            int start = random.RangeInclusive(selectedGap.x, maxStart);
            ApplyCoordinateRange(coordinates, lockedCoordinates, start, start + minSize, biomeIndex, false);
        }

        private void FillUnassignedStripCoordinates(int[] coordinates, int axisLength, long localSeed)
        {
            RandomState random = new RandomState(localSeed ^ 0x4D2B30A1A0F3C5D7L);
            int coordinate = 0;
            while (coordinate < axisLength)
            {
                if (coordinates[coordinate] != BiomeChannelUtility.UnassignedBiomeIndex)
                {
                    coordinate++;
                    continue;
                }

                int gapStart = coordinate;
                while (coordinate < axisLength && coordinates[coordinate] == BiomeChannelUtility.UnassignedBiomeIndex)
                {
                    coordinate++;
                }

                int gapEnd = coordinate;
                FillStripGap(coordinates, gapStart, gapEnd, ref random);
            }
        }

        private void FillStripGap(int[] coordinates, int gapStart, int gapEnd, ref RandomState random)
        {
            int coordinate = gapStart;
            int previousBiome = gapStart > 0 ? coordinates[gapStart - 1] : BiomeChannelUtility.UnassignedBiomeIndex;

            while (coordinate < gapEnd)
            {
                int remaining = gapEnd - coordinate;
                int biomeIndex = SelectWeightedBiome(ref random, previousBiome);
                int segmentSize = SelectRegionSize(ref random, biomeIndex, remaining);

                int fillIndex;
                for (fillIndex = 0; fillIndex < segmentSize; fillIndex++)
                {
                    coordinates[coordinate + fillIndex] = biomeIndex;
                }

                previousBiome = biomeIndex;
                coordinate += segmentSize;
            }
        }

        private void BuildCellAssignments(int[] assignments, bool[] lockedTiles, int width, int height, long localSeed)
        {
            int cellsX = (width + _cellSize - 1) / _cellSize;
            int cellsY = (height + _cellSize - 1) / _cellSize;
            int[] cellBiomes = new int[cellsX * cellsY];

            int cellY;
            for (cellY = 0; cellY < cellsY; cellY++)
            {
                int cellX;
                for (cellX = 0; cellX < cellsX; cellX++)
                {
                    RandomState random = new RandomState(DeriveCellSeed(localSeed, cellX, cellY));
                    int leftBiome = cellX > 0 ? cellBiomes[(cellX - 1) + (cellY * cellsX)] : BiomeChannelUtility.UnassignedBiomeIndex;
                    int topBiome = cellY > 0 ? cellBiomes[cellX + ((cellY - 1) * cellsX)] : BiomeChannelUtility.UnassignedBiomeIndex;
                    int avoidBiome = leftBiome == topBiome ? leftBiome : BiomeChannelUtility.UnassignedBiomeIndex;
                    cellBiomes[cellX + (cellY * cellsX)] = SelectWeightedBiome(ref random, avoidBiome);
                }
            }

            int y;
            for (y = 0; y < height; y++)
            {
                int cellRow = math.min(cellsY - 1, y / _cellSize);
                int x;
                for (x = 0; x < width; x++)
                {
                    int cellColumn = math.min(cellsX - 1, x / _cellSize);
                    int tileIndex = x + (y * width);
                    assignments[tileIndex] = cellBiomes[cellColumn + (cellRow * cellsX)];
                }
            }

            ApplyTileConstraints(assignments, lockedTiles, width, height);
            ApplyRequiredTileConstraints(assignments, lockedTiles, width, height, localSeed);
        }

        private void ApplyTileConstraints(int[] assignments, bool[] lockedTiles, int width, int height)
        {
            int constraintIndex;
            for (constraintIndex = 0; constraintIndex < _resolvedConstraints.Length; constraintIndex++)
            {
                ResolvedConstraint constraint = _resolvedConstraints[constraintIndex];
                if (constraint.Type == BiomeLayoutConstraintType.StartEdge)
                {
                    ApplyTileEdgeConstraint(assignments, lockedTiles, width, height, constraint, true);
                }
                else if (constraint.Type == BiomeLayoutConstraintType.EndEdge)
                {
                    ApplyTileEdgeConstraint(assignments, lockedTiles, width, height, constraint, false);
                }
            }

            for (constraintIndex = 0; constraintIndex < _resolvedConstraints.Length; constraintIndex++)
            {
                ResolvedConstraint constraint = _resolvedConstraints[constraintIndex];
                if (constraint.Type != BiomeLayoutConstraintType.ProtectedCenter)
                {
                    continue;
                }

                int size = constraint.Size > 0 ? constraint.Size : _minRegionSize;
                int startX = math.clamp((width - size) / 2, 0, width);
                int endX = math.clamp(startX + size, 0, width);
                int startY = math.clamp((height - size) / 2, 0, height);
                int endY = math.clamp(startY + size, 0, height);

                int y;
                for (y = startY; y < endY; y++)
                {
                    int x;
                    for (x = startX; x < endX; x++)
                    {
                        int index = x + (y * width);
                        assignments[index] = constraint.BiomeIndex;
                        if (lockedTiles != null)
                        {
                            lockedTiles[index] = true;
                        }
                    }
                }
            }
        }

        private void ApplyTileEdgeConstraint(int[] assignments, bool[] lockedTiles, int width, int height, ResolvedConstraint constraint, bool startEdge)
        {
            int size = math.max(0, constraint.Size > 0 ? constraint.Size : _minRegionSize);
            bool useY = _axis == GradientDirection.Y;

            int y;
            for (y = 0; y < height; y++)
            {
                int x;
                for (x = 0; x < width; x++)
                {
                    int coordinate = useY ? y : x;
                    int axisLength = useY ? height : width;
                    bool insideEdge = startEdge ? coordinate < size : coordinate >= axisLength - size;
                    if (!insideEdge)
                    {
                        continue;
                    }

                    int index = x + (y * width);
                    assignments[index] = constraint.BiomeIndex;
                    if (lockedTiles != null)
                    {
                        lockedTiles[index] = true;
                    }
                }
            }
        }

        private void ApplyRequiredTileConstraints(int[] assignments, bool[] lockedTiles, int width, int height, long localSeed)
        {
            int constraintIndex;
            for (constraintIndex = 0; constraintIndex < _resolvedConstraints.Length; constraintIndex++)
            {
                ResolvedConstraint constraint = _resolvedConstraints[constraintIndex];
                if (constraint.Type != BiomeLayoutConstraintType.Required || ContainsBiome(assignments, constraint.BiomeIndex))
                {
                    continue;
                }

                int size = math.max(1, constraint.Size > 0 ? constraint.Size : _cellSize);
                ApplyRequiredTileRegion(assignments, lockedTiles, width, height, constraint.BiomeIndex, size, localSeed + constraintIndex);
            }
        }

        private void ApplyRequiredTileRegion(int[] assignments, bool[] lockedTiles, int width, int height, int biomeIndex, int size, long seed)
        {
            int regionWidth = math.clamp(size, 1, width);
            int regionHeight = math.clamp(size, 1, height);
            List<int2> candidates = new List<int2>();

            int y;
            for (y = 0; y <= height - regionHeight; y++)
            {
                int x;
                for (x = 0; x <= width - regionWidth; x++)
                {
                    if (RegionContainsLockedTile(lockedTiles, width, x, y, regionWidth, regionHeight))
                    {
                        continue;
                    }

                    candidates.Add(new int2(x, y));
                }
            }

            RandomState random = new RandomState(seed ^ 0x31726F8E45D12C9BL);
            int2 origin = candidates.Count > 0
                ? candidates[random.RangeInclusive(0, candidates.Count - 1)]
                : new int2(random.RangeInclusive(0, math.max(0, width - regionWidth)), random.RangeInclusive(0, math.max(0, height - regionHeight)));

            FillTileRange(assignments, lockedTiles, width, origin.x, origin.y, regionWidth, regionHeight, biomeIndex);
        }

        private static bool RegionContainsLockedTile(bool[] lockedTiles, int width, int startX, int startY, int regionWidth, int regionHeight)
        {
            if (lockedTiles == null)
            {
                return false;
            }

            int y;
            for (y = startY; y < startY + regionHeight; y++)
            {
                int x;
                for (x = startX; x < startX + regionWidth; x++)
                {
                    if (lockedTiles[x + (y * width)])
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void FillTileRange(int[] assignments, bool[] lockedTiles, int width, int startX, int startY, int regionWidth, int regionHeight, int biomeIndex)
        {
            int y;
            for (y = startY; y < startY + regionHeight; y++)
            {
                int x;
                for (x = startX; x < startX + regionWidth; x++)
                {
                    int index = x + (y * width);
                    assignments[index] = biomeIndex;
                    if (lockedTiles != null)
                    {
                        lockedTiles[index] = true;
                    }
                }
            }
        }

        private int SelectRegionSize(ref RandomState random, int biomeIndex, int remaining)
        {
            if (remaining <= 0)
            {
                return 0;
            }

            int minSize = ResolveEntryMinSize(biomeIndex);
            int maxSize = ResolveEntryMaxSize(biomeIndex, remaining);

            if (remaining <= minSize)
            {
                return remaining;
            }

            maxSize = math.clamp(maxSize, minSize, remaining);
            int size = random.RangeInclusive(minSize, maxSize);
            int leftover = remaining - size;
            if (leftover > 0 && leftover < _minRegionSize)
            {
                size = remaining;
            }

            return math.clamp(size, 1, remaining);
        }

        private int SelectWeightedBiome(ref RandomState random, int avoidedBiome)
        {
            float totalWeight = 0.0f;
            int validCount = 0;
            int entryIndex;
            for (entryIndex = 0; entryIndex < _resolvedEntries.Length; entryIndex++)
            {
                ResolvedEntry entry = _resolvedEntries[entryIndex];
                if (entry.Weight <= 0.0f || entry.BiomeIndex == avoidedBiome)
                {
                    continue;
                }

                totalWeight += entry.Weight;
                validCount++;
            }

            bool excludeAvoidedBiome = validCount > 0;
            if (validCount == 0)
            {
                for (entryIndex = 0; entryIndex < _resolvedEntries.Length; entryIndex++)
                {
                    ResolvedEntry entry = _resolvedEntries[entryIndex];
                    if (entry.Weight <= 0.0f)
                    {
                        continue;
                    }

                    totalWeight += entry.Weight;
                    validCount++;
                }
            }

            if (validCount == 0)
            {
                return _resolvedEntries.Length > 0 ? _resolvedEntries[0].BiomeIndex : BiomeChannelUtility.UnassignedBiomeIndex;
            }

            float roll = random.NextFloat() * totalWeight;
            float cumulative = 0.0f;
            int lastValidBiome = BiomeChannelUtility.UnassignedBiomeIndex;
            for (entryIndex = 0; entryIndex < _resolvedEntries.Length; entryIndex++)
            {
                ResolvedEntry entry = _resolvedEntries[entryIndex];
                if (entry.Weight <= 0.0f || (excludeAvoidedBiome && entry.BiomeIndex == avoidedBiome))
                {
                    continue;
                }

                cumulative += entry.Weight;
                lastValidBiome = entry.BiomeIndex;
                if (roll <= cumulative)
                {
                    return entry.BiomeIndex;
                }
            }

            return lastValidBiome;
        }

        private int ResolveEntryMinSize(int biomeIndex)
        {
            int entryIndex;
            for (entryIndex = 0; entryIndex < _resolvedEntries.Length; entryIndex++)
            {
                if (_resolvedEntries[entryIndex].BiomeIndex == biomeIndex && _resolvedEntries[entryIndex].MinSize > 0)
                {
                    return _resolvedEntries[entryIndex].MinSize;
                }
            }

            return _minRegionSize;
        }

        private int ResolveEntryMaxSize(int biomeIndex, int remaining)
        {
            int entryIndex;
            for (entryIndex = 0; entryIndex < _resolvedEntries.Length; entryIndex++)
            {
                if (_resolvedEntries[entryIndex].BiomeIndex != biomeIndex)
                {
                    continue;
                }

                if (_resolvedEntries[entryIndex].MaxSize > 0)
                {
                    return _resolvedEntries[entryIndex].MaxSize;
                }

                break;
            }

            return _maxRegionSize > 0 ? _maxRegionSize : remaining;
        }

        private static void ApplyCoordinateRange(int[] coordinates, bool[] lockedCoordinates, int start, int end, int biomeIndex, bool locked)
        {
            int safeStart = math.clamp(start, 0, coordinates.Length);
            int safeEnd = math.clamp(end, safeStart, coordinates.Length);

            int coordinate;
            for (coordinate = safeStart; coordinate < safeEnd; coordinate++)
            {
                coordinates[coordinate] = biomeIndex;
                if (lockedCoordinates != null)
                {
                    lockedCoordinates[coordinate] = locked;
                }
            }
        }

        private static List<int2> BuildGaps(int[] coordinates)
        {
            List<int2> gaps = new List<int2>();
            int coordinate = 0;
            while (coordinate < coordinates.Length)
            {
                if (coordinates[coordinate] != BiomeChannelUtility.UnassignedBiomeIndex)
                {
                    coordinate++;
                    continue;
                }

                int start = coordinate;
                while (coordinate < coordinates.Length && coordinates[coordinate] == BiomeChannelUtility.UnassignedBiomeIndex)
                {
                    coordinate++;
                }

                gaps.Add(new int2(start, coordinate));
            }

            return gaps;
        }

        private static bool ContainsBiome(int[] values, int biomeIndex)
        {
            int index;
            for (index = 0; index < values.Length; index++)
            {
                if (values[index] == biomeIndex)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasLockedTileConstraints()
        {
            int constraintIndex;
            for (constraintIndex = 0; constraintIndex < _resolvedConstraints.Length; constraintIndex++)
            {
                BiomeLayoutConstraintType type = _resolvedConstraints[constraintIndex].Type;
                if (type == BiomeLayoutConstraintType.ProtectedCenter ||
                    type == BiomeLayoutConstraintType.StartEdge ||
                    type == BiomeLayoutConstraintType.EndEdge)
                {
                    return true;
                }
            }

            return false;
        }

        private static void ApplyBoundaryBlend(int[] assignments, bool[] lockedTiles, int width, int height, int blendWidth, long seed)
        {
            int[] source = new int[assignments.Length];
            Array.Copy(assignments, source, assignments.Length);

            int index;
            for (index = 0; index < source.Length; index++)
            {
                if (lockedTiles != null && lockedTiles[index])
                {
                    continue;
                }

                int x = index % width;
                int y = index / width;
                int currentBiome = source[index];
                int candidateBiome = currentBiome;
                float candidateChance = 0.0f;

                TrySelectBlendCandidate(source, width, height, x, y, -1, 0, blendWidth, currentBiome, ref candidateBiome, ref candidateChance);
                TrySelectBlendCandidate(source, width, height, x, y, 1, 0, blendWidth, currentBiome, ref candidateBiome, ref candidateChance);
                TrySelectBlendCandidate(source, width, height, x, y, 0, -1, blendWidth, currentBiome, ref candidateBiome, ref candidateChance);
                TrySelectBlendCandidate(source, width, height, x, y, 0, 1, blendWidth, currentBiome, ref candidateBiome, ref candidateChance);

                if (candidateBiome == currentBiome || candidateChance <= 0.0f)
                {
                    continue;
                }

                float roll = HashToUnitFloat(seed, x, y, candidateBiome);
                if (roll < candidateChance)
                {
                    assignments[index] = candidateBiome;
                }
            }
        }

        private static void TrySelectBlendCandidate(int[] source, int width, int height, int x, int y, int stepX, int stepY, int blendWidth, int currentBiome, ref int candidateBiome, ref float candidateChance)
        {
            int step;
            for (step = 1; step <= blendWidth; step++)
            {
                int sampleX = x + (stepX * step);
                int sampleY = y + (stepY * step);
                if (sampleX < 0 || sampleX >= width || sampleY < 0 || sampleY >= height)
                {
                    return;
                }

                int sampleBiome = source[sampleX + (sampleY * width)];
                if (sampleBiome == currentBiome)
                {
                    continue;
                }

                float falloff = (float)(blendWidth - step + 1) / (float)math.max(1, blendWidth);
                float chance = 0.45f * falloff;
                if (chance > candidateChance)
                {
                    candidateBiome = sampleBiome;
                    candidateChance = chance;
                }

                return;
            }
        }

        private static BiomeLayoutRules ParseRules(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return new BiomeLayoutRules();
            }

            try
            {
                BiomeLayoutRules rules = JsonUtility.FromJson<BiomeLayoutRules>(rawJson);
                return rules ?? new BiomeLayoutRules();
            }
            catch
            {
                return new BiomeLayoutRules();
            }
        }

        private static int ParseInt(string value, int fallbackValue)
        {
            int parsedValue;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedValue))
            {
                return parsedValue;
            }

            return fallbackValue;
        }

        private static long DeriveCellSeed(long localSeed, int cellX, int cellY)
        {
            unchecked
            {
                ulong seed = (ulong)localSeed;
                seed ^= (uint)cellX * 0x85EBCA6Bu;
                seed *= 0x9E3779B97F4A7C15UL;
                seed ^= (uint)cellY * 0xC2B2AE35u;
                seed *= 0xD6E8FEB86659FD93UL;
                return (long)seed;
            }
        }

        private static float HashToUnitFloat(long seed, int x, int y, int salt)
        {
            unchecked
            {
                uint hash = (uint)seed;
                hash ^= (uint)(seed >> 32);
                hash ^= (uint)x * 374761393u;
                hash = (hash << 13) | (hash >> 19);
                hash ^= (uint)y * 668265263u;
                hash = (hash << 11) | (hash >> 21);
                hash ^= (uint)salt * 2246822519u;
                hash *= 3266489917u;
                hash ^= hash >> 16;
                return (float)(hash / 4294967296.0d);
            }
        }

        [BurstCompile]
        private struct BiomeLayoutCopyJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<int> Assignments;

            public NativeArray<int> Output;

            public void Execute(int index)
            {
                Output[index] = Assignments[index];
            }
        }
    }
}
