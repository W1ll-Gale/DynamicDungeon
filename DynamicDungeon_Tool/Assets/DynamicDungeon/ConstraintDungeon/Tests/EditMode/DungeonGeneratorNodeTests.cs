using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using DynamicDungeon.Runtime.Component;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using DynamicDungeon.Runtime.Nodes;
using DynamicDungeon.Runtime.Output;
using DynamicDungeon.Runtime.Placement;
using DynamicDungeon.Runtime.Semantic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DynamicDungeon.ConstraintDungeon.Tests.EditMode
{
    public sealed class DungeonGeneratorNodeTests
    {
        private const string TempFolder = "Assets/DynamicDungeon/ConstraintDungeon/Tests/TempGeneratedNode";
        private const string PointChannelName = "DungeonStartPoints";
        private const string ReservedMaskChannelName = "DungeonReservedMask";
        private const string StartPointsPortName = "StartPoints";
        private const string LogicalIdsChannelName = GraphOutputUtility.OutputInputPortName;

        [SetUp]
        public void SetUp()
        {
            DeleteTempFolder();
            EnsureTempFolder();
        }

        [TearDown]
        public void TearDown()
        {
            DeleteTempFolder();
        }

        [Test]
        public void ResolvePrefabPalette_AddsRoomTemplatesWithoutPrefabStampAuthoring()
        {
            GameObject startPrefab = CreateTemplatePrefab("PaletteStart", RoomType.Entrance, FacingDirection.East);
            GameObject roomPrefab = CreateTemplatePrefab("PaletteRoom", RoomType.Room, FacingDirection.West);
            OrganicGenerationSettings settings = CreateOrganicSettingsAsset("PaletteSettings", startPrefab, roomPrefab, 2, out string settingsGuid);
            DungeonGeneratorNode node = CreateOrganicNode(settingsGuid);
            PrefabStampPalette palette = new PrefabStampPalette();

            bool resolved = node.ResolvePrefabPalette(palette, out string errorMessage);

            Assert.That(resolved, Is.True, errorMessage);
            Assert.That(palette.Prefabs, Has.Count.EqualTo(2));
            Assert.That(palette.Prefabs, Does.Contain(startPrefab));
            Assert.That(palette.Prefabs, Does.Contain(roomPrefab));
            Assert.That(startPrefab.GetComponent<PrefabStampAuthoring>(), Is.Null);
            Assert.That(settings, Is.Not.Null);
        }

        [Test]
        public void OrganicMode_GeneratesOneDungeonPerStartPointAndCarvesReservedMask()
        {
            GameObject startPrefab = CreateTemplatePrefab("OrganicStart", RoomType.Entrance, FacingDirection.East);
            GameObject roomPrefab = CreateTemplatePrefab("OrganicRoom", RoomType.Room, FacingDirection.West);
            CreateOrganicSettingsAsset("OrganicSettings", startPrefab, roomPrefab, 2, out string settingsGuid);
            DungeonGeneratorNode node = CreateOrganicNode(settingsGuid);
            WorldSnapshot snapshot = ExecuteNode(node, new[] { new int2(10, 10), new int2(24, 10) }, 40, 24);

            PrefabPlacementRecord[] placements = GetPrefabPlacements(snapshot);
            Assert.That(placements.Length, Is.EqualTo(4));
            Assert.That(ContainsPlacementAt(placements, 10, 10), Is.True);
            Assert.That(ContainsPlacementAt(placements, 24, 10), Is.True);

            byte[] reservedMask = GetBoolMask(snapshot, ReservedMaskChannelName);
            int[] logicalIds = GetIntChannel(snapshot, LogicalIdsChannelName);
            Assert.That(reservedMask[ToIndex(10, 10, snapshot.Width)], Is.EqualTo(1));
            Assert.That(reservedMask[ToIndex(24, 10, snapshot.Width)], Is.EqualTo(1));
            int voidLogicalId = LogicalTileId.Void;
            Assert.That(logicalIds[ToIndex(10, 10, snapshot.Width)], Is.EqualTo(voidLogicalId));
            Assert.That(logicalIds[ToIndex(24, 10, snapshot.Width)], Is.EqualTo(voidLogicalId));
            Assert.That(snapshot.PrefabPlacementMetadata, Is.Not.Null);
            Assert.That(snapshot.PrefabPlacementMetadata.Length, Is.EqualTo(placements.Length));
        }

        [Test]
        public void OrganicMode_AlignsPaintedSpawnPointToInputPoint()
        {
            GameObject startPrefab = CreateTemplatePrefab(
                "SpawnPointStart",
                RoomType.Entrance,
                new[] { FacingDirection.East },
                new[] { Vector2Int.zero, Vector2Int.right, new Vector2Int(2, 0) },
                new Vector2Int(2, 0));
            GameObject roomPrefab = CreateTemplatePrefab("SpawnPointRoom", RoomType.Room, FacingDirection.West);
            CreateOrganicSettingsAsset("SpawnPointSettings", startPrefab, roomPrefab, 1, out string settingsGuid);
            DungeonGeneratorNode node = CreateOrganicNode(settingsGuid);
            WorldSnapshot snapshot = ExecuteNode(node, new[] { new int2(10, 10) }, 32, 20);

            PrefabPlacementRecord[] placements = GetPrefabPlacements(snapshot);
            Assert.That(placements.Length, Is.EqualTo(1));
            Assert.That(placements[0].OriginX, Is.EqualTo(8));
            Assert.That(placements[0].OriginY, Is.EqualTo(10));

            byte[] reservedMask = GetBoolMask(snapshot, ReservedMaskChannelName);
            Assert.That(reservedMask[ToIndex(10, 10, snapshot.Width)], Is.EqualTo(1));
        }

        [Test]
        public void PrefabOutput_InitialisesDungeonDoorOpeningsFromPlacementMetadata()
        {
            GameObject startPrefab = CreateTemplatePrefab("DoorOutputStart", RoomType.Entrance, FacingDirection.East);
            GameObject roomPrefab = CreateTemplatePrefab("DoorOutputRoom", RoomType.Room, FacingDirection.West);
            CreateOrganicSettingsAsset("DoorOutputSettings", startPrefab, roomPrefab, 2, out string settingsGuid);
            DungeonGeneratorNode node = CreateOrganicNode(settingsGuid);
            WorldSnapshot snapshot = ExecuteNode(node, new[] { new int2(10, 10) }, 32, 20);

            GameObject root = new GameObject("PrefabOutputRoot");
            try
            {
                Grid grid = root.AddComponent<Grid>();
                GeneratedPrefabWriter writer = new GeneratedPrefabWriter();
                writer.EnsureRoot(root.transform);
                new PrefabPlacementOutputPass().Execute(snapshot, grid, writer, Vector3Int.zero);

                RoomTemplateComponent[] remainingTemplateComponents = root.GetComponentsInChildren<RoomTemplateComponent>(true);
                Assert.That(remainingTemplateComponents.Length, Is.EqualTo(0));

                Tilemap startFloor = null;
                Tilemap startWalls = null;
                FindTilemapsAtWorldPosition(root, new Vector3(10, 10, 0), out startFloor, out startWalls);
                Assert.That(startFloor, Is.Not.Null);
                Assert.That(startWalls, Is.Not.Null);
                Assert.That(startWalls.HasTile(new Vector3Int(1, 0, 0)), Is.False);
                Assert.That(startFloor.HasTile(new Vector3Int(1, 0, 0)), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void FlowGraphMode_ValidatesAndEmitsPrefabPlacements()
        {
            GameObject startPrefab = CreateTemplatePrefab("FlowStart", RoomType.Entrance, FacingDirection.East);
            GameObject roomPrefab = CreateTemplatePrefab("FlowRoom", RoomType.Room, FacingDirection.West);
            CreateFlowAsset("FlowSettings", startPrefab, roomPrefab, out string flowGuid);

            DungeonGeneratorNode node = new DungeonGeneratorNode(
                "flow-dungeon",
                "Flow Dungeon",
                generationMode: DungeonGenerationMode.FlowGraph,
                dungeonFlow: flowGuid,
                layoutAttempts: 20,
                maxSearchSteps: 10000,
                seedMode: SeedMode.Stable,
                stableSeed: 77,
                reservedMaskChannelName: ReservedMaskChannelName);

            BindStartPoints(node);
            WorldSnapshot snapshot = ExecuteNode(node, new[] { new int2(8, 8) }, 32, 20);

            PrefabPlacementRecord[] placements = GetPrefabPlacements(snapshot);
            Assert.That(placements.Length, Is.EqualTo(2));
            Assert.That(ContainsPlacementAt(placements, 8, 8), Is.True);
        }

        [Test]
        public void StableSeed_ReplaysIdenticalPlacements()
        {
            GameObject startPrefab = CreateTemplatePrefab("StableStart", RoomType.Entrance, FacingDirection.East);
            GameObject roomPrefab = CreateTemplatePrefab("StableRoom", RoomType.Room, FacingDirection.West);
            CreateOrganicSettingsAsset("StableSettings", startPrefab, roomPrefab, 2, out string settingsGuid);

            WorldSnapshot firstSnapshot = ExecuteNode(CreateOrganicNode(settingsGuid), new[] { new int2(12, 12) }, 32, 24);
            WorldSnapshot secondSnapshot = ExecuteNode(CreateOrganicNode(settingsGuid), new[] { new int2(12, 12) }, 32, 24);

            PrefabPlacementRecord[] firstPlacements = GetPrefabPlacements(firstSnapshot);
            PrefabPlacementRecord[] secondPlacements = GetPrefabPlacements(secondSnapshot);
            Assert.That(secondPlacements.Length, Is.EqualTo(firstPlacements.Length));

            int index;
            for (index = 0; index < firstPlacements.Length; index++)
            {
                Assert.That(secondPlacements[index].TemplateIndex, Is.EqualTo(firstPlacements[index].TemplateIndex));
                Assert.That(secondPlacements[index].OriginX, Is.EqualTo(firstPlacements[index].OriginX));
                Assert.That(secondPlacements[index].OriginY, Is.EqualTo(firstPlacements[index].OriginY));
                Assert.That(secondPlacements[index].RotationQuarterTurns, Is.EqualTo(firstPlacements[index].RotationQuarterTurns));
                Assert.That(secondPlacements[index].Flags, Is.EqualTo(firstPlacements[index].Flags));
            }
        }

        [Test]
        public void RandomSeed_UsesGraphSeedWhileStableSeedIgnoresGraphSeed()
        {
            DungeonGeneratorNode stableNode = new DungeonGeneratorNode(
                "stable-seed-dungeon",
                "Stable Seed Dungeon",
                generationMode: DungeonGenerationMode.OrganicGrowth,
                seedMode: SeedMode.Stable,
                stableSeed: 42);
            DungeonGeneratorNode randomNode = new DungeonGeneratorNode(
                "random-seed-dungeon",
                "Random Seed Dungeon",
                generationMode: DungeonGenerationMode.OrganicGrowth,
                seedMode: SeedMode.Random,
                stableSeed: 42);

            Assert.That(InvokeResolveSeed(stableNode, 100, 0), Is.EqualTo(42));
            Assert.That(InvokeResolveSeed(stableNode, 101, 0), Is.EqualTo(42));
            Assert.That(InvokeResolveSeed(randomNode, 100, 0), Is.EqualTo(100));
            Assert.That(InvokeResolveSeed(randomNode, 101, 0), Is.EqualTo(101));
            Assert.That(InvokeResolveSeed(randomNode, 100, 1), Is.Not.EqualTo(InvokeResolveSeed(randomNode, 100, 0)));
        }

        [Test]
        public void MissingFlowGraphSettings_ReturnsConfigurationError()
        {
            DungeonGeneratorNode node = new DungeonGeneratorNode(
                "missing-flow-dungeon",
                "Missing Flow Dungeon",
                generationMode: DungeonGenerationMode.FlowGraph,
                dungeonFlow: string.Empty,
                reservedMaskChannelName: ReservedMaskChannelName);
            PrefabStampPalette palette = new PrefabStampPalette();

            bool resolved = node.ResolvePrefabPalette(palette, out string errorMessage);

            Assert.That(resolved, Is.False);
            StringAssert.Contains("Dungeon Flow asset GUID is empty", errorMessage);
        }

        [Test]
        public void MissingOrganicSettings_ReturnsConfigurationError()
        {
            DungeonGeneratorNode node = CreateOrganicNode(string.Empty);
            PrefabStampPalette palette = new PrefabStampPalette();

            bool resolved = node.ResolvePrefabPalette(palette, out string errorMessage);

            Assert.That(resolved, Is.False);
            StringAssert.Contains("Organic generation settings asset GUID is empty", errorMessage);
        }

        private static DungeonGeneratorNode CreateOrganicNode(string settingsGuid)
        {
            DungeonGeneratorNode node = new DungeonGeneratorNode(
                "organic-dungeon",
                "Organic Dungeon",
                generationMode: DungeonGenerationMode.OrganicGrowth,
                organicSettings: settingsGuid,
                layoutAttempts: 20,
                maxSearchSteps: 10000,
                seedMode: SeedMode.Stable,
                stableSeed: 123,
                reservedMaskChannelName: ReservedMaskChannelName);

            BindStartPoints(node);
            return node;
        }

        private static void BindStartPoints(DungeonGeneratorNode node)
        {
            InputConnectionMap connections = new InputConnectionMap();
            connections.SetConnections(StartPointsPortName, new[] { PointChannelName });
            node.ReceiveInputConnections(connections);
        }

        private static WorldSnapshot ExecuteNode(DungeonGeneratorNode node, int2[] startPoints, int width, int height)
        {
            PrefabStampPalette palette = new PrefabStampPalette();
            Assert.That(node.ResolvePrefabPalette(palette, out string errorMessage), Is.True, errorMessage);

            List<IGenNode> nodes = new List<IGenNode>
            {
                new TestPointListNode("points", PointChannelName, startPoints),
                new TestIntFillNode("logical-fill", LogicalIdsChannelName, LogicalTileId.Wall),
                node
            };

            ExecutionPlan plan = ExecutionPlan.Build(nodes, width, height, 98765L);
            plan.SetPrefabPlacementPalette(palette.Prefabs, palette.Templates);

            Executor executor = new Executor();
            ExecutionResult result = executor.Execute(plan, CancellationToken.None);

            Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
            Assert.That(result.Snapshot, Is.Not.Null);
            return result.Snapshot;
        }

        private static OrganicGenerationSettings CreateOrganicSettingsAsset(string name, GameObject startPrefab, GameObject roomPrefab, int targetRoomCount, out string guid)
        {
            OrganicGenerationSettings settings = ScriptableObject.CreateInstance<OrganicGenerationSettings>();
            settings.startPrefab = startPrefab;
            settings.targetRoomCount = targetRoomCount;
            settings.corridorChance = 0f;
            settings.maxCorridorChain = 0;
            settings.branchingProbability = 0f;
            settings.templates.Add(new TemplateEntry
            {
                prefab = roomPrefab,
                enabled = true,
                weight = 1f
            });

            string path = TempFolder + "/" + name + ".asset";
            AssetDatabase.CreateAsset(settings, path);
            AssetDatabase.SaveAssets();
            guid = AssetDatabase.AssetPathToGUID(path);
            return AssetDatabase.LoadAssetAtPath<OrganicGenerationSettings>(path);
        }

        private static DungeonFlow CreateFlowAsset(string name, GameObject startPrefab, GameObject roomPrefab, out string guid)
        {
            DungeonFlow flow = ScriptableObject.CreateInstance<DungeonFlow>();
            flow.nodes.Add(new RoomNode("start")
            {
                displayName = "Start",
                type = RoomType.Entrance,
                allowedTemplates = new List<GameObject> { startPrefab }
            });
            flow.nodes.Add(new RoomNode("room")
            {
                displayName = "Room",
                type = RoomType.Room,
                allowedTemplates = new List<GameObject> { roomPrefab }
            });
            flow.edges.Add(new RoomEdge("start", "room"));

            string path = TempFolder + "/" + name + ".asset";
            AssetDatabase.CreateAsset(flow, path);
            AssetDatabase.SaveAssets();
            guid = AssetDatabase.AssetPathToGUID(path);
            return AssetDatabase.LoadAssetAtPath<DungeonFlow>(path);
        }

        private static GameObject CreateTemplatePrefab(string name, RoomType roomType, params FacingDirection[] doors)
        {
            return CreateTemplatePrefab(name, roomType, doors, new[] { Vector2Int.zero }, null);
        }

        private static GameObject CreateTemplatePrefab(
            string name,
            RoomType roomType,
            IReadOnlyList<FacingDirection> doors,
            IReadOnlyList<Vector2Int> floorCells,
            Vector2Int? spawnPoint)
        {
            Tile floorTile = CreateTileAsset<Tile>(name + "_FloorTile");
            Tile wallTile = CreateTileAsset<Tile>(name + "_WallTile");
            GameObject root = new GameObject(name);

            try
            {
                root.AddComponent<Grid>();
                RoomTemplateComponent component = root.AddComponent<RoomTemplateComponent>();
                component.roomName = name;
                component.roomType = roomType;
                component.allowRotation = false;
                component.allowMirroring = false;
                component.hasSpawnPoint = spawnPoint.HasValue;
                component.spawnPoint = spawnPoint.GetValueOrDefault();

                Tilemap floorMap = CreateTilemapChild(root.transform, "Floor");
                Tilemap wallMap = CreateTilemapChild(root.transform, "Walls");
                component.floorMap = floorMap;
                component.wallMap = wallMap;

                IReadOnlyList<Vector2Int> safeFloorCells = floorCells != null && floorCells.Count > 0
                    ? floorCells
                    : new[] { Vector2Int.zero };
                int floorIndex;
                for (floorIndex = 0; floorIndex < safeFloorCells.Count; floorIndex++)
                {
                    Vector2Int floorCell = safeFloorCells[floorIndex];
                    floorMap.SetTile(new Vector3Int(floorCell.x, floorCell.y, 0), floorTile);
                }

                int doorIndex;
                for (doorIndex = 0; doorIndex < doors.Count; doorIndex++)
                {
                    Vector2Int doorCell = ToDoorCell(doors[doorIndex]);
                    wallMap.SetTile(new Vector3Int(doorCell.x, doorCell.y, 0), wallTile);
                    component.manualDoorPoints.Add(new DoorTileData(doorCell, null, 1));
                }

                component.Bake();
                string path = TempFolder + "/" + name + ".prefab";
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                AssetDatabase.SaveAssets();
                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static TTile CreateTileAsset<TTile>(string name)
            where TTile : Tile
        {
            TTile tile = ScriptableObject.CreateInstance<TTile>();
            AssetDatabase.CreateAsset(tile, TempFolder + "/" + name + ".asset");
            return tile;
        }

        private static Tilemap CreateTilemapChild(Transform parent, string name)
        {
            GameObject tilemapObject = new GameObject(name);
            tilemapObject.transform.SetParent(parent, false);
            Tilemap tilemap = tilemapObject.AddComponent<Tilemap>();
            tilemapObject.AddComponent<TilemapRenderer>();
            return tilemap;
        }

        private static Vector2Int ToDoorCell(FacingDirection direction)
        {
            switch (direction)
            {
                case FacingDirection.North:
                    return new Vector2Int(0, 1);
                case FacingDirection.South:
                    return new Vector2Int(0, -1);
                case FacingDirection.East:
                    return new Vector2Int(1, 0);
                case FacingDirection.West:
                    return new Vector2Int(-1, 0);
                default:
                    return Vector2Int.right;
            }
        }

        private static PrefabPlacementRecord[] GetPrefabPlacements(WorldSnapshot snapshot)
        {
            int index;
            for (index = 0; index < snapshot.PrefabPlacementChannels.Length; index++)
            {
                WorldSnapshot.PrefabPlacementListChannelSnapshot channel = snapshot.PrefabPlacementChannels[index];
                if (channel != null && string.Equals(channel.Name, PrefabPlacementChannelUtility.ChannelName, StringComparison.Ordinal))
                {
                    return channel.Data ?? Array.Empty<PrefabPlacementRecord>();
                }
            }

            return Array.Empty<PrefabPlacementRecord>();
        }

        private static int[] GetIntChannel(WorldSnapshot snapshot, string channelName)
        {
            int index;
            for (index = 0; index < snapshot.IntChannels.Length; index++)
            {
                WorldSnapshot.IntChannelSnapshot channel = snapshot.IntChannels[index];
                if (channel != null && string.Equals(channel.Name, channelName, StringComparison.Ordinal))
                {
                    return channel.Data;
                }
            }

            return null;
        }

        private static byte[] GetBoolMask(WorldSnapshot snapshot, string channelName)
        {
            int index;
            for (index = 0; index < snapshot.BoolMaskChannels.Length; index++)
            {
                WorldSnapshot.BoolMaskChannelSnapshot channel = snapshot.BoolMaskChannels[index];
                if (channel != null && string.Equals(channel.Name, channelName, StringComparison.Ordinal))
                {
                    return channel.Data;
                }
            }

            return null;
        }

        private static void FindTilemapsAtWorldPosition(GameObject root, Vector3 worldPosition, out Tilemap floorMap, out Tilemap wallMap)
        {
            floorMap = null;
            wallMap = null;

            RoomTemplateComponent[] components = root.GetComponentsInChildren<RoomTemplateComponent>(true);
            Assert.That(components.Length, Is.EqualTo(0));

            Transform generatedRoot = root.transform.Find("GeneratedPrefabs");
            Assert.That(generatedRoot, Is.Not.Null);

            int childIndex;
            for (childIndex = 0; childIndex < generatedRoot.childCount; childIndex++)
            {
                Transform child = generatedRoot.GetChild(childIndex);
                if (Vector3.Distance(child.position, worldPosition) > 0.01f)
                {
                    continue;
                }

                Tilemap[] tilemaps = child.GetComponentsInChildren<Tilemap>(true);
                int tilemapIndex;
                for (tilemapIndex = 0; tilemapIndex < tilemaps.Length; tilemapIndex++)
                {
                    if (tilemaps[tilemapIndex].name == "Floor")
                    {
                        floorMap = tilemaps[tilemapIndex];
                    }
                    else if (tilemaps[tilemapIndex].name == "Walls")
                    {
                        wallMap = tilemaps[tilemapIndex];
                    }
                }

                return;
            }
        }

        private static bool ContainsPlacementAt(IReadOnlyList<PrefabPlacementRecord> placements, int x, int y)
        {
            int index;
            for (index = 0; index < placements.Count; index++)
            {
                if (placements[index].OriginX == x && placements[index].OriginY == y)
                {
                    return true;
                }
            }

            return false;
        }

        private static long InvokeResolveSeed(DungeonGeneratorNode node, long localSeed, int pointIndex)
        {
            MethodInfo method = typeof(DungeonGeneratorNode).GetMethod("ResolveSeed", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (long)method.Invoke(node, new object[] { localSeed, pointIndex });
        }

        private static int ToIndex(int x, int y, int width)
        {
            return (y * width) + x;
        }

        private static void EnsureTempFolder()
        {
            EnsureFolder("Assets/DynamicDungeon");
            EnsureFolder("Assets/DynamicDungeon/ConstraintDungeon");
            EnsureFolder("Assets/DynamicDungeon/ConstraintDungeon/Tests");
            EnsureFolder(TempFolder);
        }

        private static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string[] parts = folderPath.Split('/');
            string parent = parts[0];
            int index;
            for (index = 1; index < parts.Length; index++)
            {
                string current = parent + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(current))
                {
                    AssetDatabase.CreateFolder(parent, parts[index]);
                }

                parent = current;
            }
        }

        private static void DeleteTempFolder()
        {
            if (AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.DeleteAsset(TempFolder);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        private sealed class TestPointListNode : IGenNode
        {
            private readonly string _nodeId;
            private readonly string _outputChannelName;
            private readonly int2[] _points;

            public IReadOnlyList<NodePortDefinition> Ports { get; }
            public IReadOnlyList<ChannelDeclaration> ChannelDeclarations { get; }
            public IReadOnlyList<BlackboardKey> BlackboardDeclarations => Array.Empty<BlackboardKey>();
            public string NodeId => _nodeId;
            public string NodeName => "Test Point List";

            public TestPointListNode(string nodeId, string outputChannelName, int2[] points)
            {
                _nodeId = nodeId;
                _outputChannelName = outputChannelName;
                _points = points ?? Array.Empty<int2>();
                Ports = new[] { new NodePortDefinition(outputChannelName, PortDirection.Output, ChannelType.PointList) };
                ChannelDeclarations = new[] { new ChannelDeclaration(outputChannelName, ChannelType.PointList, true) };
            }

            public JobHandle Schedule(NodeExecutionContext context)
            {
                context.InputDependency.Complete();
                NativeList<int2> output = context.GetPointListChannel(_outputChannelName);
                output.Clear();

                int index;
                for (index = 0; index < _points.Length; index++)
                {
                    output.Add(_points[index]);
                }

                return default;
            }
        }

        private sealed class TestIntFillNode : IGenNode
        {
            private readonly string _nodeId;
            private readonly string _outputChannelName;
            private readonly int _value;

            public IReadOnlyList<NodePortDefinition> Ports { get; }
            public IReadOnlyList<ChannelDeclaration> ChannelDeclarations { get; }
            public IReadOnlyList<BlackboardKey> BlackboardDeclarations => Array.Empty<BlackboardKey>();
            public string NodeId => _nodeId;
            public string NodeName => "Test Int Fill";

            public TestIntFillNode(string nodeId, string outputChannelName, int value)
            {
                _nodeId = nodeId;
                _outputChannelName = outputChannelName;
                _value = value;
                Ports = new[] { new NodePortDefinition(outputChannelName, PortDirection.Output, ChannelType.Int) };
                ChannelDeclarations = new[] { new ChannelDeclaration(outputChannelName, ChannelType.Int, true) };
            }

            public JobHandle Schedule(NodeExecutionContext context)
            {
                context.InputDependency.Complete();
                NativeArray<int> output = context.GetIntChannel(_outputChannelName);

                int index;
                for (index = 0; index < output.Length; index++)
                {
                    output[index] = _value;
                }

                return default;
            }
        }
    }
}
