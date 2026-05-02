using System;
using System.Collections.Generic;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using System.Globalization;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using DynamicDungeon.Runtime.Placement;
using DynamicDungeon.Runtime.Semantic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace DynamicDungeon.Runtime.Nodes
{
    [NodeCategory("Placement")]
    [NodeDisplayName("Prefab Stamper")]
    [Description("Places prefabs into the generated world using a prefab-authored footprint, with optional logical ID edits and deterministic transform variation.")]
    public sealed class PrefabStamperNode : IGenNode, IInputConnectionReceiver, IParameterReceiver, IParameterVisibilityProvider, IPrefabPlacementNode
    {
        private const string DefaultNodeName = "Prefab Stamper";
        private const string PointsPortName = "Points";
        private const string LogicalIdsChannelName = GraphOutputUtility.OutputInputPortName;
        private const string ReservedMaskFallbackOutputName = "ReservedMask";
        private const int DefaultInteriorLogicalId = 1;
        private const int DefaultOutlineLogicalId = 2;

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private NodePortDefinition[] _ports;

        private string _inputPointsChannelName;
        private string _reservedMaskChannelName;

        [AssetGuidReference(typeof(GameObject))]
        [DescriptionAttribute("Prefab asset stored as an asset GUID in the graph. The prefab must have PrefabStampAuthoring on its root.")]
        private string _prefab;

        [DescriptionAttribute("How the inferred footprint edits the logical ID channel. None only records prefab placements.")]
        private PrefabFootprintMode _footprintMode;

        [DescriptionAttribute("Logical ID written to occupied cells when FillInterior is used.")]
        private int _interiorLogicalId = DefaultInteriorLogicalId;

        [DescriptionAttribute("Logical ID written to the inferred perimeter when OutlineAndCarve is used.")]
        private int _outlineLogicalId = DefaultOutlineLogicalId;

        [DescriptionAttribute("How logical writes interact with existing logical IDs.")]
        private StampBlendMode _blendMode;

        [DescriptionAttribute("When enabled, each placement has a 50% chance to mirror horizontally.")]
        private bool _mirrorX;

        [DescriptionAttribute("When enabled, each placement has a 50% chance to mirror vertically.")]
        private bool _mirrorY;

        [DescriptionAttribute("When enabled, each placement randomly rotates by 0, 90, 180, or 270 degrees.")]
        private bool _allowRotation;

        [MinValue(0.0f)]
        [DescriptionAttribute("Skip a placement when it would overlap more than this many existing non-floor-like logical cells.")]
        private int _maxOverlapTiles;

        private ChannelDeclaration[] _channelDeclarations;
        private PrefabStampTemplate _resolvedTemplate;
        private int _resolvedTemplateIndex = -1;
        private bool _prefabResolutionFailed;

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

        public PrefabStamperNode(
            string nodeId,
            string nodeName,
            string inputPointsChannelName = "",
            string prefab = "",
            PrefabFootprintMode footprintMode = PrefabFootprintMode.None,
            int interiorLogicalId = DefaultInteriorLogicalId,
            int outlineLogicalId = DefaultOutlineLogicalId,
            StampBlendMode blendMode = StampBlendMode.Overwrite,
            bool mirrorX = false,
            bool mirrorY = false,
            bool allowRotation = false,
            int maxOverlapTiles = 0,
            string reservedMaskChannelName = "")
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _inputPointsChannelName = inputPointsChannelName ?? string.Empty;
            _prefab = prefab ?? string.Empty;
            _footprintMode = footprintMode;
            _interiorLogicalId = interiorLogicalId;
            _outlineLogicalId = outlineLogicalId;
            _blendMode = blendMode;
            _mirrorX = mirrorX;
            _mirrorY = mirrorY;
            _allowRotation = allowRotation;
            _maxOverlapTiles = math.max(0, maxOverlapTiles);
            _reservedMaskChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(nodeId, reservedMaskChannelName, ReservedMaskFallbackOutputName);

            RefreshPorts();
            RefreshChannelDeclarations();
        }

        public void ReceiveInputConnections(IReadOnlyDictionary<string, string> inputConnections)
        {
            string inputChannelName;
            if (inputConnections != null && inputConnections.TryGetValue(PointsPortName, out inputChannelName))
            {
                _inputPointsChannelName = inputChannelName ?? string.Empty;
            }
            else
            {
                _inputPointsChannelName = string.Empty;
            }

            RefreshChannelDeclarations();
        }

        public void ReceiveParameter(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (string.Equals(name, "prefab", StringComparison.OrdinalIgnoreCase))
            {
                _prefab = value ?? string.Empty;
                _resolvedTemplate = default;
                _resolvedTemplateIndex = -1;
                _prefabResolutionFailed = false;
                RefreshChannelDeclarations();
                return;
            }

            if (string.Equals(name, "reservedMaskChannelName", StringComparison.OrdinalIgnoreCase))
            {
                _reservedMaskChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(_nodeId, value, ReservedMaskFallbackOutputName);
                RefreshPorts();
                RefreshChannelDeclarations();
                return;
            }

            if (string.Equals(name, "footprintMode", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    _footprintMode = (PrefabFootprintMode)Enum.Parse(typeof(PrefabFootprintMode), value ?? string.Empty, true);
                    RefreshChannelDeclarations();
                }
                catch
                {
                }

                return;
            }

            if (string.Equals(name, "interiorLogicalId", StringComparison.OrdinalIgnoreCase))
            {
                int parsedValue;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedValue))
                {
                    _interiorLogicalId = parsedValue;
                }

                return;
            }

            if (string.Equals(name, "outlineLogicalId", StringComparison.OrdinalIgnoreCase))
            {
                int parsedValue;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedValue))
                {
                    _outlineLogicalId = parsedValue;
                }

                return;
            }

            if (string.Equals(name, "blendMode", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    _blendMode = (StampBlendMode)Enum.Parse(typeof(StampBlendMode), value ?? string.Empty, true);
                }
                catch
                {
                }

                return;
            }

            if (string.Equals(name, "mirrorX", StringComparison.OrdinalIgnoreCase))
            {
                bool parsedValue;
                if (bool.TryParse(value, out parsedValue))
                {
                    _mirrorX = parsedValue;
                }

                return;
            }

            if (string.Equals(name, "mirrorY", StringComparison.OrdinalIgnoreCase))
            {
                bool parsedValue;
                if (bool.TryParse(value, out parsedValue))
                {
                    _mirrorY = parsedValue;
                }

                return;
            }

            if (string.Equals(name, "allowRotation", StringComparison.OrdinalIgnoreCase))
            {
                bool parsedValue;
                if (bool.TryParse(value, out parsedValue))
                {
                    _allowRotation = parsedValue;
                }

                return;
            }

            if (string.Equals(name, "maxOverlapTiles", StringComparison.OrdinalIgnoreCase))
            {
                int parsedValue;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedValue))
                {
                    _maxOverlapTiles = math.max(0, parsedValue);
                }
            }
        }

        public bool ResolvePrefabPalette(PrefabStampPalette palette, out string errorMessage)
        {
            errorMessage = null;
            _resolvedTemplate = default;
            _resolvedTemplateIndex = -1;
            _prefabResolutionFailed = false;

            if (string.IsNullOrWhiteSpace(_prefab))
            {
                return true;
            }

            if (palette == null)
            {
                errorMessage = "Prefab stamp palette is missing.";
                _prefabResolutionFailed = true;
                return false;
            }

            if (!palette.TryResolve(_prefab, out _resolvedTemplateIndex, out _resolvedTemplate, out errorMessage))
            {
                _prefabResolutionFailed = true;
                return false;
            }

            return true;
        }

        public bool IsParameterVisible(string parameterName)
        {
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                return true;
            }

            if (string.Equals(parameterName, "interiorLogicalId", StringComparison.OrdinalIgnoreCase))
            {
                return _footprintMode == PrefabFootprintMode.FillInterior;
            }

            if (string.Equals(parameterName, "outlineLogicalId", StringComparison.OrdinalIgnoreCase))
            {
                return _footprintMode == PrefabFootprintMode.OutlineAndCarve;
            }

            if (string.Equals(parameterName, "blendMode", StringComparison.OrdinalIgnoreCase))
            {
                return UsesBlendMode();
            }

            if (string.Equals(parameterName, "maxOverlapTiles", StringComparison.OrdinalIgnoreCase))
            {
                return UsesOverlapBudget();
            }

            return true;
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            if (!HasActivePlacement())
            {
                return context.InputDependency;
            }

            if (_resolvedTemplateIndex < 0 || !_resolvedTemplate.IsValid)
            {
                string warningMessage = _prefabResolutionFailed
                    ? "Prefab Stamper could not resolve its prefab footprint from the stored prefab GUID. The node will not place anything."
                    : "Prefab Stamper has no valid prefab footprint. The node will not place anything.";
                ManagedBlackboardDiagnosticUtility.AppendWarning(context.ManagedBlackboard, warningMessage, _nodeId, PointsPortName);
                return context.InputDependency;
            }

            NativeList<int2> placements = context.GetPointListChannel(_inputPointsChannelName);
            NativeArray<int> logicalIds = context.GetIntChannel(LogicalIdsChannelName);
            NativeList<PrefabPlacementRecord> placementChannel = context.GetPrefabPlacementListChannel(PrefabPlacementChannelUtility.ChannelName);
            NativeArray<int2> occupiedOffsets = CreateOccupiedOffsets();
            bool writesReservedMask = UsesReservedMask();
            NativeArray<byte> reservedMask = writesReservedMask
                ? context.GetBoolMaskChannel(_reservedMaskChannelName)
                : new NativeArray<byte>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);

            PrefabPlacementStampJob job = new PrefabPlacementStampJob
            {
                Placements = placements,
                LogicalIds = logicalIds,
                PlacementChannel = placementChannel,
                ReservedMask = reservedMask,
                OccupiedOffsets = occupiedOffsets,
                WorldWidth = context.Width,
                WorldHeight = context.Height,
                TemplateIndex = _resolvedTemplateIndex,
                FootprintMode = (int)_footprintMode,
                BlendMode = (int)_blendMode,
                MirrorX = _mirrorX,
                MirrorY = _mirrorY,
                AllowRotation = _allowRotation,
                MaxOverlapTiles = _maxOverlapTiles,
                InteriorLogicalId = _interiorLogicalId,
                OutlineLogicalId = _outlineLogicalId,
                WriteReservedMask = writesReservedMask,
                LocalSeed = context.LocalSeed
            };

            JobHandle jobHandle = job.Schedule(context.InputDependency);
            JobHandle disposeHandle = occupiedOffsets.Dispose(jobHandle);
            if (!writesReservedMask)
            {
                disposeHandle = reservedMask.Dispose(disposeHandle);
            }

            return disposeHandle;
        }

        private void RefreshChannelDeclarations()
        {
            List<ChannelDeclaration> declarations = new List<ChannelDeclaration>(4);
            bool hasActivePlacement = HasActivePlacement();

            if (!string.IsNullOrWhiteSpace(_inputPointsChannelName))
            {
                declarations.Add(new ChannelDeclaration(_inputPointsChannelName, ChannelType.PointList, false));
            }

            if (hasActivePlacement)
            {
                declarations.Add(new ChannelDeclaration(LogicalIdsChannelName, ChannelType.Int, false));
                declarations.Add(new ChannelDeclaration(LogicalIdsChannelName, ChannelType.Int, true));
            }

            if (hasActivePlacement)
            {
                declarations.Add(new ChannelDeclaration(PrefabPlacementChannelUtility.ChannelName, ChannelType.PrefabPlacementList, true));
            }

            if (hasActivePlacement)
            {
                declarations.Add(new ChannelDeclaration(_reservedMaskChannelName, ChannelType.BoolMask, true));
            }

            _channelDeclarations = declarations.ToArray();
        }

        private void RefreshPorts()
        {
            _ports = new[]
            {
                new NodePortDefinition(PointsPortName, PortDirection.Input, ChannelType.PointList, PortCapacity.Single, false),
                new NodePortDefinition(LogicalIdsChannelName, PortDirection.Output, ChannelType.Int),
                new NodePortDefinition(_reservedMaskChannelName, PortDirection.Output, ChannelType.BoolMask, displayName: GraphPortNameUtility.ResolveOutputDisplayName(_nodeId, _reservedMaskChannelName, ReservedMaskFallbackOutputName))
            };
        }

        private bool HasActivePlacement()
        {
            return !string.IsNullOrWhiteSpace(_prefab) &&
                   !string.IsNullOrWhiteSpace(_inputPointsChannelName);
        }

        private bool UsesBlendMode()
        {
            return _footprintMode == PrefabFootprintMode.FillInterior ||
                   _footprintMode == PrefabFootprintMode.OutlineAndCarve;
        }

        private bool UsesOverlapBudget()
        {
            return _footprintMode == PrefabFootprintMode.None ||
                   _footprintMode == PrefabFootprintMode.FillInterior;
        }

        private bool UsesReservedMask()
        {
            return _footprintMode == PrefabFootprintMode.CarveInterior ||
                   _footprintMode == PrefabFootprintMode.OutlineAndCarve;
        }

        private NativeArray<int2> CreateOccupiedOffsets()
        {
            Vector2Int[] occupiedCells = _resolvedTemplate.OccupiedCells ?? Array.Empty<Vector2Int>();
            NativeArray<int2> occupiedOffsets = new NativeArray<int2>(occupiedCells.Length, Allocator.TempJob);

            int index;
            for (index = 0; index < occupiedCells.Length; index++)
            {
                Vector2Int cell = occupiedCells[index];
                occupiedOffsets[index] = new int2(cell.x, cell.y);
            }

            return occupiedOffsets;
        }

        [BurstCompile]
        private struct PrefabPlacementStampJob : IJob
        {
            [ReadOnly]
            public NativeList<int2> Placements;

            public NativeArray<int> LogicalIds;
            public NativeList<PrefabPlacementRecord> PlacementChannel;
            public NativeArray<byte> ReservedMask;

            [ReadOnly]
            public NativeArray<int2> OccupiedOffsets;

            public int WorldWidth;
            public int WorldHeight;
            public int TemplateIndex;
            public int FootprintMode;
            public int BlendMode;
            public bool MirrorX;
            public bool MirrorY;
            public bool AllowRotation;
            public int MaxOverlapTiles;
            public int InteriorLogicalId;
            public int OutlineLogicalId;
            public bool WriteReservedMask;
            public long LocalSeed;

            public void Execute()
            {
                int placementIndex;
                for (placementIndex = 0; placementIndex < Placements.Length; placementIndex++)
                {
                    int2 placement = Placements[placementIndex];

                    bool applyMirrorX;
                    bool applyMirrorY;
                    int quarterTurns;
                    ResolvePlacementTransform(placementIndex, out applyMirrorX, out applyMirrorY, out quarterTurns);

                    if (ExceedsOverlapBudget(placement, applyMirrorX, applyMirrorY, quarterTurns))
                    {
                        continue;
                    }

                    ApplyFootprint(placement, applyMirrorX, applyMirrorY, quarterTurns);
                    PlacementChannel.Add(new PrefabPlacementRecord(TemplateIndex, placement.x, placement.y, (byte)quarterTurns, applyMirrorX, applyMirrorY));
                }
            }

            private bool ExceedsOverlapBudget(int2 placement, bool applyMirrorX, bool applyMirrorY, int quarterTurns)
            {
                if (FootprintMode == (int)PrefabFootprintMode.CarveInterior ||
                    FootprintMode == (int)PrefabFootprintMode.OutlineAndCarve)
                {
                    return false;
                }

                int2 normalizationOffset = GetNormalizationOffset(applyMirrorX, applyMirrorY, quarterTurns);
                int overlapCount = 0;

                int index;
                for (index = 0; index < OccupiedOffsets.Length; index++)
                {
                    int2 offset = TransformOffset(OccupiedOffsets[index], applyMirrorX, applyMirrorY, quarterTurns, normalizationOffset);
                    int worldX = placement.x + offset.x;
                    int worldY = placement.y + offset.y;
                    if (worldX < 0 || worldX >= WorldWidth || worldY < 0 || worldY >= WorldHeight)
                    {
                        continue;
                    }

                    int worldIndex = (worldY * WorldWidth) + worldX;
                    if (ShouldCountOverlap(LogicalIds[worldIndex]))
                    {
                        overlapCount++;
                        if (overlapCount > MaxOverlapTiles)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            private bool ShouldCountOverlap(int existingLogicalId)
            {
                if (existingLogicalId == LogicalTileId.Void ||
                    existingLogicalId == LogicalTileId.Floor ||
                    existingLogicalId == LogicalTileId.Access)
                {
                    return false;
                }

                if (FootprintMode == (int)PrefabFootprintMode.FillInterior &&
                    existingLogicalId == InteriorLogicalId)
                {
                    return false;
                }

                return true;
            }

            private void ApplyFootprint(int2 placement, bool applyMirrorX, bool applyMirrorY, int quarterTurns)
            {
                if (FootprintMode == (int)PrefabFootprintMode.None)
                {
                    return;
                }

                int2 normalizationOffset = GetNormalizationOffset(applyMirrorX, applyMirrorY, quarterTurns);
                int index;
                for (index = 0; index < OccupiedOffsets.Length; index++)
                {
                    int2 offset = TransformOffset(OccupiedOffsets[index], applyMirrorX, applyMirrorY, quarterTurns, normalizationOffset);
                    int worldX = placement.x + offset.x;
                    int worldY = placement.y + offset.y;
                    if (worldX < 0 || worldX >= WorldWidth || worldY < 0 || worldY >= WorldHeight)
                    {
                        continue;
                    }

                    int worldIndex = (worldY * WorldWidth) + worldX;

                    if (FootprintMode == (int)PrefabFootprintMode.CarveInterior ||
                        FootprintMode == (int)PrefabFootprintMode.OutlineAndCarve)
                    {
                        LogicalIds[worldIndex] = LogicalTileId.Void;
                        if (WriteReservedMask)
                        {
                            ReservedMask[worldIndex] = 1;
                        }

                        continue;
                    }

                    if (FootprintMode == (int)PrefabFootprintMode.FillInterior)
                    {
                        ApplyBlend(worldIndex, InteriorLogicalId);
                    }
                }

                if (FootprintMode != (int)PrefabFootprintMode.OutlineAndCarve)
                {
                    return;
                }

                int cellIndex;
                for (cellIndex = 0; cellIndex < OccupiedOffsets.Length; cellIndex++)
                {
                    int2 baseOffset = TransformOffset(OccupiedOffsets[cellIndex], applyMirrorX, applyMirrorY, quarterTurns, normalizationOffset);
                    ApplyOutlineNeighbour(placement, baseOffset + new int2(1, 0), applyMirrorX, applyMirrorY, quarterTurns, normalizationOffset);
                    ApplyOutlineNeighbour(placement, baseOffset + new int2(-1, 0), applyMirrorX, applyMirrorY, quarterTurns, normalizationOffset);
                    ApplyOutlineNeighbour(placement, baseOffset + new int2(0, 1), applyMirrorX, applyMirrorY, quarterTurns, normalizationOffset);
                    ApplyOutlineNeighbour(placement, baseOffset + new int2(0, -1), applyMirrorX, applyMirrorY, quarterTurns, normalizationOffset);
                }
            }

            private void ApplyOutlineNeighbour(int2 placement, int2 candidateOffset, bool applyMirrorX, bool applyMirrorY, int quarterTurns, int2 normalizationOffset)
            {
                if (ContainsOccupiedCell(candidateOffset, applyMirrorX, applyMirrorY, quarterTurns, normalizationOffset))
                {
                    return;
                }

                int worldX = placement.x + candidateOffset.x;
                int worldY = placement.y + candidateOffset.y;
                if (worldX < 0 || worldX >= WorldWidth || worldY < 0 || worldY >= WorldHeight)
                {
                    return;
                }

                int worldIndex = (worldY * WorldWidth) + worldX;
                ApplyBlend(worldIndex, OutlineLogicalId);
            }

            private bool ContainsOccupiedCell(int2 candidateOffset, bool applyMirrorX, bool applyMirrorY, int quarterTurns, int2 normalizationOffset)
            {
                int index;
                for (index = 0; index < OccupiedOffsets.Length; index++)
                {
                    int2 occupiedOffset = TransformOffset(OccupiedOffsets[index], applyMirrorX, applyMirrorY, quarterTurns, normalizationOffset);
                    if (occupiedOffset.x == candidateOffset.x && occupiedOffset.y == candidateOffset.y)
                    {
                        return true;
                    }
                }

                return false;
            }

            private void ApplyBlend(int worldIndex, int logicalId)
            {
                if (BlendMode == (int)StampBlendMode.Additive)
                {
                    if (LogicalIds[worldIndex] == LogicalTileId.Void)
                    {
                        LogicalIds[worldIndex] = logicalId;
                    }

                    return;
                }

                if (BlendMode == (int)StampBlendMode.Subtractive)
                {
                    LogicalIds[worldIndex] = LogicalTileId.Void;
                    return;
                }

                LogicalIds[worldIndex] = logicalId;
            }

            private void ResolvePlacementTransform(int placementIndex, out bool applyMirrorX, out bool applyMirrorY, out int quarterTurns)
            {
                uint hash = HashPlacement(placementIndex);
                applyMirrorX = MirrorX && ((hash & 1u) != 0u);
                applyMirrorY = MirrorY && ((hash & 2u) != 0u);
                quarterTurns = AllowRotation ? (int)((hash >> 2) & 3u) : 0;
            }

            private int2 GetNormalizationOffset(bool applyMirrorX, bool applyMirrorY, int quarterTurns)
            {
                if (OccupiedOffsets.Length == 0)
                {
                    return int2.zero;
                }

                int2 min = TransformOffsetRaw(OccupiedOffsets[0], applyMirrorX, applyMirrorY, quarterTurns);

                int index;
                for (index = 1; index < OccupiedOffsets.Length; index++)
                {
                    int2 transformed = TransformOffsetRaw(OccupiedOffsets[index], applyMirrorX, applyMirrorY, quarterTurns);
                    min = math.min(min, transformed);
                }

                return new int2(-min.x, -min.y);
            }

            private static int2 TransformOffset(int2 source, bool applyMirrorX, bool applyMirrorY, int quarterTurns, int2 normalizationOffset)
            {
                return TransformOffsetRaw(source, applyMirrorX, applyMirrorY, quarterTurns) + normalizationOffset;
            }

            private static int2 TransformOffsetRaw(int2 source, bool applyMirrorX, bool applyMirrorY, int quarterTurns)
            {
                int transformedX = applyMirrorX ? -source.x : source.x;
                int transformedY = applyMirrorY ? -source.y : source.y;

                if (quarterTurns == 1)
                {
                    return new int2(-transformedY, transformedX);
                }

                if (quarterTurns == 2)
                {
                    return new int2(-transformedX, -transformedY);
                }

                if (quarterTurns == 3)
                {
                    return new int2(transformedY, -transformedX);
                }

                return new int2(transformedX, transformedY);
            }

            private uint HashPlacement(int placementIndex)
            {
                uint seedLow = unchecked((uint)LocalSeed);
                uint seedHigh = unchecked((uint)(LocalSeed >> 32));
                return math.hash(new uint4(unchecked((uint)placementIndex), seedLow, seedHigh, 0x9B17C4A5u));
            }
        }
    }
}
