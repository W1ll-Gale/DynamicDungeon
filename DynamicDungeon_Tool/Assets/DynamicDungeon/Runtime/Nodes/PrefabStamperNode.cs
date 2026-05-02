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

        [InspectorName("Prefab Variants")]
        [DescriptionAttribute("Prefab variants and relative weights encoded as JSON. Use the custom editor table to author this value.")]
        private string _prefabVariants;

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
        private PrefabStampTemplate[] _resolvedTemplates = Array.Empty<PrefabStampTemplate>();
        private int[] _resolvedTemplateIndices = Array.Empty<int>();
        private float[] _resolvedTemplateWeights = Array.Empty<float>();
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
            PrefabFootprintMode footprintMode = PrefabFootprintMode.None,
            int interiorLogicalId = DefaultInteriorLogicalId,
            int outlineLogicalId = DefaultOutlineLogicalId,
            StampBlendMode blendMode = StampBlendMode.Overwrite,
            bool mirrorX = false,
            bool mirrorY = false,
            bool allowRotation = false,
            int maxOverlapTiles = 0,
            string reservedMaskChannelName = "",
            string prefabVariants = "")
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _inputPointsChannelName = inputPointsChannelName ?? string.Empty;
            _prefabVariants = prefabVariants ?? string.Empty;
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

            if (string.Equals(name, "prefabVariants", StringComparison.OrdinalIgnoreCase))
            {
                _prefabVariants = value ?? string.Empty;
                ClearResolvedPrefabVariants();
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
            _resolvedTemplates = Array.Empty<PrefabStampTemplate>();
            _resolvedTemplateIndices = Array.Empty<int>();
            _resolvedTemplateWeights = Array.Empty<float>();
            _prefabResolutionFailed = false;

            PrefabStampVariant[] variants = GetConfiguredVariants();
            if (variants.Length == 0)
            {
                return true;
            }

            if (palette == null)
            {
                errorMessage = "Prefab stamp palette is missing.";
                _prefabResolutionFailed = true;
                return false;
            }

            List<PrefabStampTemplate> resolvedTemplates = new List<PrefabStampTemplate>(variants.Length);
            List<int> resolvedTemplateIndices = new List<int>(variants.Length);
            List<float> resolvedTemplateWeights = new List<float>(variants.Length);

            int variantIndex;
            for (variantIndex = 0; variantIndex < variants.Length; variantIndex++)
            {
                PrefabStampVariant variant = variants[variantIndex];
                if (!TryResolvePrefabVariant(palette, variant, variantIndex, resolvedTemplates, resolvedTemplateIndices, resolvedTemplateWeights, out errorMessage))
                {
                    _prefabResolutionFailed = true;
                    return false;
                }
            }

            _resolvedTemplates = resolvedTemplates.ToArray();
            _resolvedTemplateIndices = resolvedTemplateIndices.ToArray();
            _resolvedTemplateWeights = resolvedTemplateWeights.ToArray();
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

            if (_resolvedTemplates == null || _resolvedTemplates.Length == 0)
            {
                string warningMessage = _prefabResolutionFailed
                    ? "Prefab Stamper could not resolve a prefab variant footprint. The node will not place anything."
                    : "Prefab Stamper has no prefab variants with a positive weight. The node will not place anything.";
                ManagedBlackboardDiagnosticUtility.AppendWarning(context.ManagedBlackboard, warningMessage, _nodeId, PointsPortName);
                return context.InputDependency;
            }

            NativeList<int2> placements = context.GetPointListChannel(_inputPointsChannelName);
            NativeArray<int> logicalIds = context.GetIntChannel(LogicalIdsChannelName);
            NativeList<PrefabPlacementRecord> placementChannel = context.GetPrefabPlacementListChannel(PrefabPlacementChannelUtility.ChannelName);
            NativeArray<int> templateIndices;
            NativeArray<float> templateWeights;
            NativeArray<int2> occupiedOffsetRanges;
            NativeArray<int2> occupiedOffsets;
            CreatePrefabVariantData(out templateIndices, out templateWeights, out occupiedOffsetRanges, out occupiedOffsets);
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
                TemplateIndices = templateIndices,
                TemplateWeights = templateWeights,
                OccupiedOffsetRanges = occupiedOffsetRanges,
                OccupiedOffsets = occupiedOffsets,
                WorldWidth = context.Width,
                WorldHeight = context.Height,
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
            disposeHandle = occupiedOffsetRanges.Dispose(disposeHandle);
            disposeHandle = templateWeights.Dispose(disposeHandle);
            disposeHandle = templateIndices.Dispose(disposeHandle);
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

            declarations.Add(new ChannelDeclaration(_reservedMaskChannelName, ChannelType.BoolMask, true));

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
            return HasConfiguredPrefab() &&
                   !string.IsNullOrWhiteSpace(_inputPointsChannelName);
        }

        private bool HasConfiguredPrefab()
        {
            return GetConfiguredVariants().Length > 0;
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

        private void ClearResolvedPrefabVariants()
        {
            _resolvedTemplates = Array.Empty<PrefabStampTemplate>();
            _resolvedTemplateIndices = Array.Empty<int>();
            _resolvedTemplateWeights = Array.Empty<float>();
            _prefabResolutionFailed = false;
        }

        private static float SanitizeWeight(float weight)
        {
            if (float.IsNaN(weight) || float.IsInfinity(weight))
            {
                return 0.0f;
            }

            return math.max(0.0f, weight);
        }

        private static bool TryResolvePrefabVariant(
            PrefabStampPalette palette,
            PrefabStampVariant variant,
            int variantIndex,
            List<PrefabStampTemplate> templates,
            List<int> templateIndices,
            List<float> weights,
            out string errorMessage)
        {
            errorMessage = null;

            if (variant == null ||
                string.IsNullOrWhiteSpace(variant.Prefab) ||
                SanitizeWeight(variant.Weight) <= 0.0f)
            {
                return true;
            }

            int templateIndex;
            PrefabStampTemplate template;
            if (!palette.TryResolve(variant.Prefab, out templateIndex, out template, out errorMessage))
            {
                errorMessage = "Prefab variant " + (variantIndex + 1).ToString(CultureInfo.InvariantCulture) + " could not be resolved: " + errorMessage;
                return false;
            }

            templates.Add(template);
            templateIndices.Add(templateIndex);
            weights.Add(SanitizeWeight(variant.Weight));
            return true;
        }

        private PrefabStampVariant[] GetConfiguredVariants()
        {
            PrefabStampVariantSet variantSet = ParseVariantSet(_prefabVariants);
            return variantSet != null && variantSet.Variants != null
                ? variantSet.Variants
                : Array.Empty<PrefabStampVariant>();
        }

        private static PrefabStampVariantSet ParseVariantSet(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new PrefabStampVariantSet();
            }

            try
            {
                return JsonUtility.FromJson<PrefabStampVariantSet>(value) ?? new PrefabStampVariantSet();
            }
            catch
            {
                return new PrefabStampVariantSet();
            }
        }

        private void CreatePrefabVariantData(
            out NativeArray<int> templateIndices,
            out NativeArray<float> templateWeights,
            out NativeArray<int2> occupiedOffsetRanges,
            out NativeArray<int2> occupiedOffsets)
        {
            int variantCount = _resolvedTemplates != null ? _resolvedTemplates.Length : 0;
            int totalOccupiedCellCount = 0;

            int index;
            for (index = 0; index < variantCount; index++)
            {
                Vector2Int[] occupiedCells = _resolvedTemplates[index].OccupiedCells ?? Array.Empty<Vector2Int>();
                totalOccupiedCellCount += occupiedCells.Length;
            }

            templateIndices = new NativeArray<int>(variantCount, Allocator.TempJob);
            templateWeights = new NativeArray<float>(variantCount, Allocator.TempJob);
            occupiedOffsetRanges = new NativeArray<int2>(variantCount, Allocator.TempJob);
            occupiedOffsets = new NativeArray<int2>(totalOccupiedCellCount, Allocator.TempJob);

            int offsetCursor = 0;
            for (index = 0; index < variantCount; index++)
            {
                PrefabStampTemplate template = _resolvedTemplates[index];
                Vector2Int[] occupiedCells = template.OccupiedCells ?? Array.Empty<Vector2Int>();

                templateIndices[index] = _resolvedTemplateIndices[index];
                templateWeights[index] = _resolvedTemplateWeights[index];
                occupiedOffsetRanges[index] = new int2(offsetCursor, occupiedCells.Length);

                int cellIndex;
                for (cellIndex = 0; cellIndex < occupiedCells.Length; cellIndex++)
                {
                    Vector2Int cell = occupiedCells[cellIndex];
                    occupiedOffsets[offsetCursor + cellIndex] = new int2(cell.x, cell.y);
                }

                offsetCursor += occupiedCells.Length;
            }
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
            public NativeArray<int> TemplateIndices;

            [ReadOnly]
            public NativeArray<float> TemplateWeights;

            [ReadOnly]
            public NativeArray<int2> OccupiedOffsetRanges;

            [ReadOnly]
            public NativeArray<int2> OccupiedOffsets;

            public int WorldWidth;
            public int WorldHeight;
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
                    int variantIndex = ResolveVariantIndex(placementIndex);
                    int templateIndex = TemplateIndices[variantIndex];

                    bool applyMirrorX;
                    bool applyMirrorY;
                    int quarterTurns;
                    ResolvePlacementTransform(placementIndex, out applyMirrorX, out applyMirrorY, out quarterTurns);

                    if (ExceedsOverlapBudget(placement, variantIndex, applyMirrorX, applyMirrorY, quarterTurns))
                    {
                        continue;
                    }

                    ApplyFootprint(placement, variantIndex, applyMirrorX, applyMirrorY, quarterTurns);
                    PlacementChannel.Add(new PrefabPlacementRecord(templateIndex, placement.x, placement.y, (byte)quarterTurns, applyMirrorX, applyMirrorY));
                }
            }

            private bool ExceedsOverlapBudget(int2 placement, int variantIndex, bool applyMirrorX, bool applyMirrorY, int quarterTurns)
            {
                if (FootprintMode == (int)PrefabFootprintMode.CarveInterior ||
                    FootprintMode == (int)PrefabFootprintMode.OutlineAndCarve)
                {
                    return false;
                }

                int2 occupiedRange = OccupiedOffsetRanges[variantIndex];
                int2 normalizationOffset = GetNormalizationOffset(occupiedRange, applyMirrorX, applyMirrorY, quarterTurns);
                int overlapCount = 0;

                int rangeIndex;
                for (rangeIndex = 0; rangeIndex < occupiedRange.y; rangeIndex++)
                {
                    int2 offset = TransformOffset(OccupiedOffsets[occupiedRange.x + rangeIndex], applyMirrorX, applyMirrorY, quarterTurns, normalizationOffset);
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

            private void ApplyFootprint(int2 placement, int variantIndex, bool applyMirrorX, bool applyMirrorY, int quarterTurns)
            {
                if (FootprintMode == (int)PrefabFootprintMode.None)
                {
                    return;
                }

                int2 occupiedRange = OccupiedOffsetRanges[variantIndex];
                int2 normalizationOffset = GetNormalizationOffset(occupiedRange, applyMirrorX, applyMirrorY, quarterTurns);
                int rangeIndex;
                for (rangeIndex = 0; rangeIndex < occupiedRange.y; rangeIndex++)
                {
                    int2 offset = TransformOffset(OccupiedOffsets[occupiedRange.x + rangeIndex], applyMirrorX, applyMirrorY, quarterTurns, normalizationOffset);
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
                for (cellIndex = 0; cellIndex < occupiedRange.y; cellIndex++)
                {
                    int2 baseOffset = TransformOffset(OccupiedOffsets[occupiedRange.x + cellIndex], applyMirrorX, applyMirrorY, quarterTurns, normalizationOffset);
                    ApplyOutlineNeighbour(placement, occupiedRange, baseOffset + new int2(1, 0), applyMirrorX, applyMirrorY, quarterTurns, normalizationOffset);
                    ApplyOutlineNeighbour(placement, occupiedRange, baseOffset + new int2(-1, 0), applyMirrorX, applyMirrorY, quarterTurns, normalizationOffset);
                    ApplyOutlineNeighbour(placement, occupiedRange, baseOffset + new int2(0, 1), applyMirrorX, applyMirrorY, quarterTurns, normalizationOffset);
                    ApplyOutlineNeighbour(placement, occupiedRange, baseOffset + new int2(0, -1), applyMirrorX, applyMirrorY, quarterTurns, normalizationOffset);
                }
            }

            private void ApplyOutlineNeighbour(int2 placement, int2 occupiedRange, int2 candidateOffset, bool applyMirrorX, bool applyMirrorY, int quarterTurns, int2 normalizationOffset)
            {
                if (ContainsOccupiedCell(occupiedRange, candidateOffset, applyMirrorX, applyMirrorY, quarterTurns, normalizationOffset))
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

            private bool ContainsOccupiedCell(int2 occupiedRange, int2 candidateOffset, bool applyMirrorX, bool applyMirrorY, int quarterTurns, int2 normalizationOffset)
            {
                int rangeIndex;
                for (rangeIndex = 0; rangeIndex < occupiedRange.y; rangeIndex++)
                {
                    int2 occupiedOffset = TransformOffset(OccupiedOffsets[occupiedRange.x + rangeIndex], applyMirrorX, applyMirrorY, quarterTurns, normalizationOffset);
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

            private int ResolveVariantIndex(int placementIndex)
            {
                if (TemplateWeights.Length <= 1)
                {
                    return 0;
                }

                float totalWeight = 0.0f;
                int index;
                for (index = 0; index < TemplateWeights.Length; index++)
                {
                    totalWeight += TemplateWeights[index];
                }

                if (totalWeight <= 0.0f)
                {
                    return 0;
                }

                uint hash = HashPlacement(placementIndex, 0xC2B2AE35u);
                float roll = HashToUnitFloat(hash) * totalWeight;
                float cumulativeWeight = 0.0f;

                for (index = 0; index < TemplateWeights.Length; index++)
                {
                    cumulativeWeight += TemplateWeights[index];
                    if (roll < cumulativeWeight)
                    {
                        return index;
                    }
                }

                return TemplateWeights.Length - 1;
            }

            private int2 GetNormalizationOffset(int2 occupiedRange, bool applyMirrorX, bool applyMirrorY, int quarterTurns)
            {
                if (occupiedRange.y == 0)
                {
                    return int2.zero;
                }

                int2 min = TransformOffsetRaw(OccupiedOffsets[occupiedRange.x], applyMirrorX, applyMirrorY, quarterTurns);

                int rangeIndex;
                for (rangeIndex = 1; rangeIndex < occupiedRange.y; rangeIndex++)
                {
                    int2 transformed = TransformOffsetRaw(OccupiedOffsets[occupiedRange.x + rangeIndex], applyMirrorX, applyMirrorY, quarterTurns);
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
                return HashPlacement(placementIndex, 0x9B17C4A5u);
            }

            private uint HashPlacement(int placementIndex, uint salt)
            {
                uint seedLow = unchecked((uint)LocalSeed);
                uint seedHigh = unchecked((uint)(LocalSeed >> 32));
                return math.hash(new uint4(unchecked((uint)placementIndex), seedLow, seedHigh, salt));
            }

            private static float HashToUnitFloat(uint hash)
            {
                return (hash & 0x00FFFFFFu) / 16777216.0f;
            }
        }
    }
}
