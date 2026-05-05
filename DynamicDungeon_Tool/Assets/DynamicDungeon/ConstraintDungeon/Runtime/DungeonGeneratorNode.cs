using System;
using System.Collections.Generic;
using System.Globalization;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using DynamicDungeon.ConstraintDungeon;
using DynamicDungeon.ConstraintDungeon.Solver;
using DynamicDungeon.ConstraintDungeon.Utils;
using DynamicDungeon.Runtime.Component;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using DynamicDungeon.Runtime.Placement;
using DynamicDungeon.Runtime.Semantic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DynamicDungeon.Runtime.Nodes
{
    [NodeCategory("Placement")]
    [NodeDisplayName("Dungeon Generator")]
    [Description("Generates a constraint dungeon from point inputs, aligning the start room to each point and emitting prefab placement records.")]
    public sealed class DungeonGeneratorNode : IGenNode, IInputConnectionReceiver, IParameterReceiver, IParameterVisibilityProvider, IPrefabPlacementNode, IMainThreadExecutionNode
    {
        private const string DefaultNodeName = "Dungeon Generator";
        private const string StartPointsPortName = "StartPoints";
        private const string LogicalIdsChannelName = GraphOutputUtility.OutputInputPortName;
        private const string ReservedMaskFallbackOutputName = "DungeonReservedMask";
        private const int DefaultLayoutAttempts = 1000;
        private const int DefaultMaxSearchSteps = 50000;
        private const long DefaultStableSeed = 12345L;

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private string _inputPointsChannelName;
        private string _reservedMaskChannelName;
        private NodePortDefinition[] _ports;
        private ChannelDeclaration[] _channelDeclarations;

        [DescriptionAttribute("Selects whether this node solves a hand-authored flow graph or grows an organic dungeon.")]
        private DungeonGenerationMode _generationMode;

        [AssetGuidReference(typeof(DungeonFlow))]
        [DescriptionAttribute("Dungeon Flow asset used when Generation Mode is Flow Graph. Stored as an asset GUID in the graph.")]
        private string _dungeonFlow;

        [AssetGuidReference(typeof(OrganicGenerationSettings))]
        [DescriptionAttribute("Organic generation profile used when Generation Mode is Organic Growth. Stored as an asset GUID in the graph.")]
        private string _organicSettings;

        [MinValue(1.0f)]
        [DescriptionAttribute("Number of solver attempts before generation gives up for each input point.")]
        private int _layoutAttempts;

        [MinValue(1.0f)]
        [DescriptionAttribute("Maximum number of search steps allowed for a single solver attempt.")]
        private int _maxSearchSteps;

        [DescriptionAttribute("Stable uses the configured seed. Random derives a deterministic seed from the graph run seed.")]
        private SeedMode _seedMode;

        [MinValue(0.0f)]
        [DescriptionAttribute("Seed used when Seed Mode is Stable.")]
        private long _stableSeed;

        [DescriptionAttribute("When enabled, failed attempts emit additional solver diagnostics to the Unity Console.")]
        private bool _enableDiagnostics;

        private DungeonFlow _resolvedFlow;
        private OrganicGenerationSettings _resolvedOrganicSettings;
        private TemplateCatalog _preparedTemplates;
        private Dictionary<GameObject, int> _templateIndexByPrefab = new Dictionary<GameObject, int>(ReferenceEqualityComparer<GameObject>.Instance);
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

        public DungeonGeneratorNode(
            string nodeId,
            string nodeName,
            string inputPointsChannelName = "",
            DungeonGenerationMode generationMode = DungeonGenerationMode.FlowGraph,
            string dungeonFlow = "",
            string organicSettings = "",
            int layoutAttempts = DefaultLayoutAttempts,
            int maxSearchSteps = DefaultMaxSearchSteps,
            SeedMode seedMode = SeedMode.Stable,
            long stableSeed = DefaultStableSeed,
            bool enableDiagnostics = false,
            string reservedMaskChannelName = "")
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _inputPointsChannelName = inputPointsChannelName ?? string.Empty;
            _generationMode = generationMode;
            _dungeonFlow = dungeonFlow ?? string.Empty;
            _organicSettings = organicSettings ?? string.Empty;
            _layoutAttempts = math.max(1, layoutAttempts);
            _maxSearchSteps = math.max(1, maxSearchSteps);
            _seedMode = seedMode;
            _stableSeed = Math.Max(0L, stableSeed);
            _enableDiagnostics = enableDiagnostics;
            _reservedMaskChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(nodeId, reservedMaskChannelName, ReservedMaskFallbackOutputName);

            RefreshPorts();
            RefreshChannelDeclarations();
        }

        public void ReceiveInputConnections(InputConnectionMap inputConnections)
        {
            if (inputConnections != null && inputConnections.TryGetValue(StartPointsPortName, out string inputChannelName))
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

            if (string.Equals(name, "generationMode", StringComparison.OrdinalIgnoreCase))
            {
                if (TryParseEnum(value, out DungeonGenerationMode parsedMode))
                {
                    _generationMode = parsedMode;
                    ClearResolvedState();
                }

                return;
            }

            if (string.Equals(name, "dungeonFlow", StringComparison.OrdinalIgnoreCase))
            {
                _dungeonFlow = value ?? string.Empty;
                ClearResolvedState();
                return;
            }

            if (string.Equals(name, "organicSettings", StringComparison.OrdinalIgnoreCase))
            {
                _organicSettings = value ?? string.Empty;
                ClearResolvedState();
                return;
            }

            if (string.Equals(name, "layoutAttempts", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedValue))
                {
                    _layoutAttempts = math.max(1, parsedValue);
                }

                return;
            }

            if (string.Equals(name, "maxSearchSteps", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedValue))
                {
                    _maxSearchSteps = math.max(1, parsedValue);
                }

                return;
            }

            if (string.Equals(name, "seedMode", StringComparison.OrdinalIgnoreCase))
            {
                if (TryParseEnum(value, out SeedMode parsedMode))
                {
                    _seedMode = parsedMode;
                }

                return;
            }

            if (string.Equals(name, "stableSeed", StringComparison.OrdinalIgnoreCase))
            {
                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsedValue))
                {
                    _stableSeed = Math.Max(0L, parsedValue);
                }

                return;
            }

            if (string.Equals(name, "enableDiagnostics", StringComparison.OrdinalIgnoreCase))
            {
                if (bool.TryParse(value, out bool parsedValue))
                {
                    _enableDiagnostics = parsedValue;
                }

                return;
            }

            if (string.Equals(name, "reservedMaskChannelName", StringComparison.OrdinalIgnoreCase))
            {
                _reservedMaskChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(_nodeId, value, ReservedMaskFallbackOutputName);
                RefreshPorts();
                RefreshChannelDeclarations();
            }
        }

        public bool IsParameterVisible(string parameterName)
        {
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                return true;
            }

            if (string.Equals(parameterName, "dungeonFlow", StringComparison.OrdinalIgnoreCase))
            {
                return _generationMode == DungeonGenerationMode.FlowGraph;
            }

            if (string.Equals(parameterName, "organicSettings", StringComparison.OrdinalIgnoreCase))
            {
                return _generationMode == DungeonGenerationMode.OrganicGrowth;
            }

            if (string.Equals(parameterName, "stableSeed", StringComparison.OrdinalIgnoreCase))
            {
                return _seedMode == SeedMode.Stable;
            }

            return true;
        }

        public bool ResolvePrefabPalette(PrefabStampPalette palette, out string errorMessage)
        {
            errorMessage = null;
            ClearResolvedState();

            if (palette == null)
            {
                errorMessage = "Prefab stamp palette is missing.";
                _prefabResolutionFailed = true;
                return false;
            }

            if (!TryResolveConfiguredAssets(out _resolvedFlow, out _resolvedOrganicSettings, out errorMessage))
            {
                _prefabResolutionFailed = true;
                return false;
            }

            DungeonGenerationRequest request = CreateRequest(0L);
            if (!DungeonGenerationService.TryValidateRequest(request, out string failureStatus))
            {
                errorMessage = "Dungeon Generator node configuration is invalid: " + failureStatus;
                _prefabResolutionFailed = true;
                return false;
            }

            _preparedTemplates = DungeonGenerationService.PrepareTemplates(request);
            if (_preparedTemplates.HasErrors)
            {
                errorMessage = "Dungeon Generator node template validation failed: " + string.Join(" ", _preparedTemplates.Errors);
                _prefabResolutionFailed = true;
                return false;
            }

            if (!ResolveRoomTemplates(palette, _preparedTemplates, out errorMessage))
            {
                _prefabResolutionFailed = true;
                return false;
            }

            return true;
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            context.InputDependency.Complete();

            NativeArray<byte> reservedMask = context.GetBoolMaskChannel(_reservedMaskChannelName);
            ClearReservedMask(reservedMask);

            if (_preparedTemplates == null || _templateIndexByPrefab == null || _templateIndexByPrefab.Count == 0)
            {
                string warningMessage = _prefabResolutionFailed
                    ? "Dungeon Generator could not resolve its room templates. The node will not place anything."
                    : "Dungeon Generator has no resolved room templates. The node will not place anything.";
                ManagedBlackboardDiagnosticUtility.AppendWarning(context.ManagedBlackboard, warningMessage, _nodeId, StartPointsPortName);
                return default;
            }

            if (string.IsNullOrWhiteSpace(_inputPointsChannelName))
            {
                ManagedBlackboardDiagnosticUtility.AppendWarning(context.ManagedBlackboard, "Dungeon Generator has no start point input channel.", _nodeId, StartPointsPortName);
                return default;
            }

            NativeList<int2> startPoints = context.GetPointListChannel(_inputPointsChannelName);
            if (startPoints.Length == 0)
            {
                ManagedBlackboardDiagnosticUtility.AppendWarning(context.ManagedBlackboard, "Dungeon Generator start point input is empty.", _nodeId, StartPointsPortName);
                return default;
            }

            NativeArray<int> logicalIds = context.GetIntChannel(LogicalIdsChannelName);
            NativeList<PrefabPlacementRecord> placementChannel = context.GetPrefabPlacementListChannel(PrefabPlacementChannelUtility.ChannelName);
            HashSet<Vector2Int> reservedCells = new HashSet<Vector2Int>();
            int successCount = 0;

            int pointIndex;
            for (pointIndex = 0; pointIndex < startPoints.Length; pointIndex++)
            {
                int2 startPoint = startPoints[pointIndex];
                long seed = ResolveSeed(context.LocalSeed, pointIndex);
                DungeonGenerationRequest request = CreateRequest(seed);
                DungeonGenerationResult result = DungeonGenerationService.GenerateLayoutImmediate(request, _preparedTemplates);

                if (result == null || !result.Success || result.Layout == null || result.Layout.Rooms.Count == 0)
                {
                    string failureReason = result != null && !string.IsNullOrWhiteSpace(result.FailureReason)
                        ? result.FailureReason
                        : "Generation failed.";
                    ManagedBlackboardDiagnosticUtility.AppendWarning(
                        context.ManagedBlackboard,
                        "Dungeon Generator failed for start point " + pointIndex.ToString(CultureInfo.InvariantCulture) + ": " + failureReason,
                        _nodeId,
                        StartPointsPortName);
                    continue;
                }

                WriteLayout(result.Layout, new Vector2Int(startPoint.x, startPoint.y), context.Width, context.Height, logicalIds, reservedMask, placementChannel, reservedCells, context.ManagedBlackboard);
                successCount++;
            }

            if (successCount == 0)
            {
                ManagedBlackboardDiagnosticUtility.AppendWarning(context.ManagedBlackboard, "Dungeon Generator did not produce any dungeon layouts.", _nodeId, StartPointsPortName);
            }

            return default;
        }

        private void RefreshPorts()
        {
            _ports = new[]
            {
                new NodePortDefinition(StartPointsPortName, PortDirection.Input, ChannelType.PointList, PortCapacity.Single, true, displayName: "Start Points"),
                new NodePortDefinition(LogicalIdsChannelName, PortDirection.Output, ChannelType.Int),
                new NodePortDefinition(PrefabPlacementChannelUtility.ChannelName, PortDirection.Output, ChannelType.PrefabPlacementList),
                new NodePortDefinition(
                    _reservedMaskChannelName,
                    PortDirection.Output,
                    ChannelType.BoolMask,
                    displayName: GraphPortNameUtility.ResolveOutputDisplayName(_nodeId, _reservedMaskChannelName, ReservedMaskFallbackOutputName))
            };
        }

        private void RefreshChannelDeclarations()
        {
            List<ChannelDeclaration> declarations = new List<ChannelDeclaration>(4);

            if (!string.IsNullOrWhiteSpace(_inputPointsChannelName))
            {
                declarations.Add(new ChannelDeclaration(_inputPointsChannelName, ChannelType.PointList, false));
            }

            declarations.Add(new ChannelDeclaration(LogicalIdsChannelName, ChannelType.Int, false));
            declarations.Add(new ChannelDeclaration(LogicalIdsChannelName, ChannelType.Int, true));
            declarations.Add(new ChannelDeclaration(PrefabPlacementChannelUtility.ChannelName, ChannelType.PrefabPlacementList, true));
            declarations.Add(new ChannelDeclaration(_reservedMaskChannelName, ChannelType.BoolMask, true));

            _channelDeclarations = declarations.ToArray();
        }

        private DungeonGenerationRequest CreateRequest(long seed)
        {
            return new DungeonGenerationRequest
            {
                Mode = _generationMode,
                Flow = _generationMode == DungeonGenerationMode.FlowGraph ? _resolvedFlow : null,
                OrganicSettings = _generationMode == DungeonGenerationMode.OrganicGrowth ? _resolvedOrganicSettings : null,
                LayoutAttempts = _layoutAttempts,
                MaxSearchSteps = _maxSearchSteps,
                Seed = seed,
                EnableDiagnostics = _enableDiagnostics
            };
        }

        private long ResolveSeed(long localSeed, int pointIndex)
        {
            long baseSeed = _seedMode == SeedMode.Stable ? _stableSeed : localSeed;
            if (pointIndex == 0)
            {
                return baseSeed;
            }

            unchecked
            {
                const long Prime = 1099511628211L;
                return (baseSeed * Prime) ^ pointIndex;
            }
        }

        private void WriteLayout(
            DungeonLayout layout,
            Vector2Int requestedStartPosition,
            int width,
            int height,
            NativeArray<int> logicalIds,
            NativeArray<byte> reservedMask,
            NativeList<PrefabPlacementRecord> placementChannel,
            HashSet<Vector2Int> reservedCells,
            ManagedBlackboard managedBlackboard)
        {
            PlacedRoom startRoom = layout.Rooms[0];
            Vector2Int offset = requestedStartPosition - GetAlignmentPoint(startRoom);

            int roomIndex;
            for (roomIndex = 0; roomIndex < layout.Rooms.Count; roomIndex++)
            {
                PlacedRoom room = layout.Rooms[roomIndex];
                if (room == null || ReferenceEquals(room.sourcePrefab, null))
                {
                    continue;
                }

                if (!_templateIndexByPrefab.TryGetValue(room.sourcePrefab, out int templateIndex))
                {
                    continue;
                }

                Vector2Int origin = room.position + offset;
                byte rotationQuarterTurns = (byte)((4 - (room.variant.rotation & 3)) & 3);
                int metadataId = CreateRoomPlacementMetadata(room, managedBlackboard);
                placementChannel.Add(new PrefabPlacementRecord(templateIndex, origin.x, origin.y, rotationQuarterTurns, room.variant.mirrored, false, metadataId));

                MarkReservedCells(room, offset, width, height, logicalIds, reservedMask, reservedCells);
            }
        }

        private static Vector2Int GetAlignmentPoint(PlacedRoom room)
        {
            if (room != null && room.variant != null && room.variant.hasSpawnPoint)
            {
                return room.position + room.variant.spawnPoint;
            }

            return room != null ? room.position : Vector2Int.zero;
        }

        private static int CreateRoomPlacementMetadata(PlacedRoom room, ManagedBlackboard managedBlackboard)
        {
            RoomTemplatePlacementMutation mutation = RoomTemplateRuntimeInitializer.CreateMutation(room);
            string payload = JsonUtility.ToJson(mutation);
            return PrefabPlacementMetadataUtility.AddMetadata(managedBlackboard, RoomTemplatePlacementMutation.MetadataType, payload);
        }

        private static void MarkReservedCells(
            PlacedRoom room,
            Vector2Int offset,
            int width,
            int height,
            NativeArray<int> logicalIds,
            NativeArray<byte> reservedMask,
            HashSet<Vector2Int> reservedCells)
        {
            reservedCells.Clear();
            room.FillReservedCells(reservedCells);

            foreach (Vector2Int reservedCell in reservedCells)
            {
                Vector2Int worldCell = reservedCell + offset;
                if (worldCell.x < 0 || worldCell.x >= width || worldCell.y < 0 || worldCell.y >= height)
                {
                    continue;
                }

                int index = (worldCell.y * width) + worldCell.x;
                logicalIds[index] = LogicalTileId.Void;
                reservedMask[index] = 1;
            }
        }

        private bool ResolveRoomTemplates(PrefabStampPalette palette, TemplateCatalog catalog, out string errorMessage)
        {
            errorMessage = null;
            _templateIndexByPrefab = new Dictionary<GameObject, int>(ReferenceEqualityComparer<GameObject>.Instance);

            IReadOnlyList<GameObject> templates = catalog.AllTemplates;
            int templateIndex;
            for (templateIndex = 0; templateIndex < templates.Count; templateIndex++)
            {
                GameObject prefab = templates[templateIndex];
                if (!TryGetAssetGuid(prefab, out string prefabGuid, out errorMessage))
                {
                    return false;
                }

                if (!TryBuildRoomStampTemplate(prefabGuid, prefab, out PrefabStampTemplate template, out errorMessage))
                {
                    return false;
                }

                if (!palette.TryAddResolvedTemplate(prefabGuid, prefab, template, out int resolvedTemplateIndex, out errorMessage))
                {
                    return false;
                }

                _templateIndexByPrefab[prefab] = resolvedTemplateIndex;
            }

            return true;
        }

        private static bool TryBuildRoomStampTemplate(string prefabGuid, GameObject prefab, out PrefabStampTemplate template, out string errorMessage)
        {
            template = default;
            errorMessage = null;

            RoomTemplateComponent component = prefab != null ? prefab.GetComponent<RoomTemplateComponent>() : null;
            if (component == null)
            {
                errorMessage = "Dungeon room prefab requires a RoomTemplateComponent.";
                return false;
            }

#if UNITY_EDITOR
            RoomTemplateBaker.Bake(component);
#endif

            if (component.bakedData == null || component.bakedData.cells == null || component.bakedData.cells.Count == 0)
            {
                errorMessage = "Dungeon room prefab '" + prefab.name + "' has no baked occupied cells.";
                return false;
            }

            HashSet<Vector2Int> uniqueCells = new HashSet<Vector2Int>();
            int cellIndex;
            for (cellIndex = 0; cellIndex < component.bakedData.cells.Count; cellIndex++)
            {
                CellData cell = component.bakedData.cells[cellIndex];
                if (cell != null)
                {
                    uniqueCells.Add(cell.position);
                }
            }

            Vector2Int[] occupiedCells = new Vector2Int[uniqueCells.Count];
            uniqueCells.CopyTo(occupiedCells);
            Array.Sort(occupiedCells, CompareCells);

            template = new PrefabStampTemplate
            {
                PrefabGuid = prefabGuid,
                AnchorOffset = Vector3.zero,
                SupportsRandomRotation = component.bakedData.allowRotation,
                UsesTilemapFootprint = true,
                OccupiedCells = occupiedCells
            };

            if (!template.IsValid)
            {
                errorMessage = "Dungeon room prefab '" + prefab.name + "' produced an invalid prefab stamp template.";
                return false;
            }

            return true;
        }

        private bool TryResolveConfiguredAssets(out DungeonFlow flow, out OrganicGenerationSettings organicSettings, out string errorMessage)
        {
            flow = null;
            organicSettings = null;
            errorMessage = null;

            if (_generationMode == DungeonGenerationMode.FlowGraph)
            {
                return TryLoadAsset(_dungeonFlow, "Dungeon Flow", out flow, out errorMessage);
            }

            return TryLoadAsset(_organicSettings, "Organic generation settings", out organicSettings, out errorMessage);
        }

        private void ClearResolvedState()
        {
            _resolvedFlow = null;
            _resolvedOrganicSettings = null;
            _preparedTemplates = null;
            _templateIndexByPrefab = new Dictionary<GameObject, int>(ReferenceEqualityComparer<GameObject>.Instance);
            _prefabResolutionFailed = false;
        }

        private static void ClearReservedMask(NativeArray<byte> reservedMask)
        {
            int index;
            for (index = 0; index < reservedMask.Length; index++)
            {
                reservedMask[index] = 0;
            }
        }

        private static int CompareCells(Vector2Int left, Vector2Int right)
        {
            int yComparison = left.y.CompareTo(right.y);
            return yComparison != 0 ? yComparison : left.x.CompareTo(right.x);
        }

        private static bool TryParseEnum<TEnum>(string value, out TEnum parsedValue)
            where TEnum : struct
        {
            try
            {
                parsedValue = (TEnum)Enum.Parse(typeof(TEnum), value ?? string.Empty, true);
                return true;
            }
            catch
            {
                parsedValue = default;
                return false;
            }
        }

        private static bool TryLoadAsset<TAsset>(string guid, string label, out TAsset asset, out string errorMessage)
            where TAsset : UnityEngine.Object
        {
            asset = null;
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(guid))
            {
                errorMessage = label + " asset GUID is empty.";
                return false;
            }

#if UNITY_EDITOR
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                errorMessage = label + " asset GUID '" + guid + "' could not be resolved.";
                return false;
            }

            asset = AssetDatabase.LoadAssetAtPath<TAsset>(assetPath);
            if (asset == null)
            {
                errorMessage = label + " asset GUID '" + guid + "' does not resolve to " + typeof(TAsset).Name + ".";
                return false;
            }

            return true;
#else
            errorMessage = label + " asset GUID resolution requires the Unity Editor asset database.";
            return false;
#endif
        }

        private static bool TryGetAssetGuid(GameObject prefab, out string guid, out string errorMessage)
        {
            guid = string.Empty;
            errorMessage = null;

            if (prefab == null)
            {
                errorMessage = "Dungeon room prefab is missing.";
                return false;
            }

#if UNITY_EDITOR
            string assetPath = AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                errorMessage = "Dungeon room prefab '" + prefab.name + "' must be saved as an asset before it can be used by the graph.";
                return false;
            }

            guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrWhiteSpace(guid))
            {
                errorMessage = "Dungeon room prefab '" + prefab.name + "' does not have an asset GUID.";
                return false;
            }

            return true;
#else
            errorMessage = "Dungeon room prefab GUID resolution requires the Unity Editor asset database.";
            return false;
#endif
        }
    }
}
