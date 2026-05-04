using NUnit.Framework;
using DynamicDungeon.ConstraintDungeon;
using DynamicDungeon.ConstraintDungeon.Solver;
using UnityEngine;

namespace DynamicDungeon.ConstraintDungeon.Tests.EditMode
{
    public class DungeonLayoutTests
    {
        [Test]
        public void AddRoomRejectsOverlappingReservedCells()
        {
            DungeonLayout layout = new DungeonLayout();
            PlacedRoom first = CreatePlacedRoom("A", Vector2Int.zero, FacingDirection.East);
            PlacedRoom second = CreatePlacedRoom("B", Vector2Int.zero, FacingDirection.West);

            layout.AddRoom(first);

            Assert.Throws<System.InvalidOperationException>(() => layout.AddRoom(second));
            Assert.IsTrue(layout.HasOverlap(second));
        }

        [Test]
        public void ValidateConnectivityAcceptsMatchingUsedDoors()
        {
            DungeonLayout layout = new DungeonLayout();
            PlacedRoom first = CreatePlacedRoom("A", Vector2Int.zero, FacingDirection.East);
            PlacedRoom second = CreatePlacedRoom("B", Vector2Int.right, FacingDirection.West);

            first.usedAnchors.Add(first.variant.anchors[0]);
            second.usedAnchors.Add(second.variant.anchors[0]);

            layout.AddRoom(first);
            layout.AddRoom(second);

            Assert.IsTrue(layout.ValidateConnectivity(out string message), message);
        }

        [Test]
        public void ValidateConnectivityRejectsDisconnectedRooms()
        {
            DungeonLayout layout = new DungeonLayout();
            layout.AddRoom(CreatePlacedRoom("A", Vector2Int.zero, FacingDirection.East));
            layout.AddRoom(CreatePlacedRoom("B", new Vector2Int(3, 0), FacingDirection.West));

            Assert.IsFalse(layout.ValidateConnectivity(out string message));
            StringAssert.Contains("connected", message);
        }

        [Test]
        public void CanConnectAcceptsMatchingOppositeDoors()
        {
            DoorAnchor east = new DoorAnchor(Vector2Int.zero, FacingDirection.East, 2, "Boss");
            DoorAnchor west = new DoorAnchor(Vector2Int.zero, FacingDirection.West, 2, "Boss");

            Assert.IsTrue(SolverPlacementUtility.CanConnect(east, west));
        }

        [Test]
        public void CanConnectRejectsIncompatibleDoors()
        {
            DoorAnchor east = new DoorAnchor(Vector2Int.zero, FacingDirection.East, 2, "Boss");

            Assert.IsFalse(SolverPlacementUtility.CanConnect(east, new DoorAnchor(Vector2Int.zero, FacingDirection.West, 2, "Standard")));
            Assert.IsFalse(SolverPlacementUtility.CanConnect(east, new DoorAnchor(Vector2Int.zero, FacingDirection.West, 1, "Boss")));
            Assert.IsFalse(SolverPlacementUtility.CanConnect(east, new DoorAnchor(Vector2Int.zero, FacingDirection.East, 2, "Boss")));
        }

        [Test]
        public void PlacementMutationRollbackRestoresDoorState()
        {
            PlacedRoom room = CreatePlacedRoom("A", Vector2Int.zero, FacingDirection.East);
            DoorAnchor anchor = room.variant.anchors[0];
            Vector2Int originalBasePosition = anchor.locallyOccupiedCell + Vector2Int.up;
            room.doorSelection[anchor] = originalBasePosition;

            PlacementMutation mutation = new PlacementMutation();
            mutation.AddUsedAnchor(room, anchor);
            mutation.SetDoorSelection(room, anchor, anchor.locallyOccupiedCell);

            mutation.Rollback();

            Assert.IsFalse(room.usedAnchors.Contains(anchor));
            Assert.AreEqual(originalBasePosition, room.doorSelection[anchor]);
        }

        [Test]
        public void PlacementMutationRollbackRemovesNewDoorSelection()
        {
            PlacedRoom room = CreatePlacedRoom("A", Vector2Int.zero, FacingDirection.East);
            DoorAnchor anchor = room.variant.anchors[0];

            PlacementMutation mutation = new PlacementMutation();
            mutation.SetDoorSelection(room, anchor, anchor.locallyOccupiedCell);

            mutation.Rollback();

            Assert.IsFalse(room.doorSelection.ContainsKey(anchor));
        }

        [Test]
        public void PlacementMutationCommitKeepsDoorState()
        {
            PlacedRoom room = CreatePlacedRoom("A", Vector2Int.zero, FacingDirection.East);
            DoorAnchor anchor = room.variant.anchors[0];

            PlacementMutation mutation = new PlacementMutation();
            mutation.AddUsedAnchor(room, anchor);
            mutation.SetDoorSelection(room, anchor, anchor.locallyOccupiedCell);
            mutation.Commit();

            mutation.Rollback();

            Assert.IsTrue(room.usedAnchors.Contains(anchor));
            Assert.AreEqual(anchor.locallyOccupiedCell, room.doorSelection[anchor]);
        }

        [Test]
        public void RoomsExposesReadOnlyLayoutContents()
        {
            DungeonLayout layout = new DungeonLayout();
            PlacedRoom room = CreatePlacedRoom("A", Vector2Int.zero, FacingDirection.East);

            Assert.IsTrue(layout.TryAddRoom(room, out string failureReason), failureReason);

            Assert.AreEqual(1, layout.Rooms.Count);
            Assert.AreSame(room, layout.Rooms[0]);
        }

        [Test]
        public void TryAddRoomReportsOverlapWithoutThrowing()
        {
            DungeonLayout layout = new DungeonLayout();
            PlacedRoom first = CreatePlacedRoom("A", Vector2Int.zero, FacingDirection.East);
            PlacedRoom second = CreatePlacedRoom("B", Vector2Int.zero, FacingDirection.West);

            Assert.IsTrue(layout.TryAddRoom(first, out string firstFailure), firstFailure);
            Assert.IsFalse(layout.TryAddRoom(second, out string secondFailure));
            StringAssert.Contains("overlaps", secondFailure);
            Assert.AreEqual(1, layout.Rooms.Count);
        }

        [Test]
        public void DoorAnchorHybridPositionsFollowDoorOrientation()
        {
            DoorAnchor horizontal = new DoorAnchor
            {
                mode = DoorMode.Hybrid,
                direction = FacingDirection.North,
                area = new RectInt(2, 5, 4, 1),
                size = 2
            };

            DoorAnchor vertical = new DoorAnchor
            {
                mode = DoorMode.Hybrid,
                direction = FacingDirection.East,
                area = new RectInt(8, 1, 1, 4),
                size = 2
            };

            CollectionAssert.AreEqual(
                new[] { new Vector2Int(2, 5), new Vector2Int(3, 5), new Vector2Int(4, 5) },
                horizontal.GetPossibleBasePositions());
            CollectionAssert.AreEqual(
                new[] { new Vector2Int(8, 1), new Vector2Int(8, 2), new Vector2Int(8, 3) },
                vertical.GetPossibleBasePositions());
        }

        [Test]
        public void VariantGenerationRemovesDuplicateUntransformedVariants()
        {
            RoomTemplateData template = new RoomTemplateData
            {
                templateName = "SingleCell",
                allowRotation = true,
                allowMirroring = true
            };
            template.cells.Add(new CellData(Vector2Int.zero, TileType.Floor));
            template.anchors.Add(new DoorAnchor(Vector2Int.zero, FacingDirection.East));

            Assert.AreEqual(4, template.GenerateVariants().Count);
        }

        [Test]
        public void OrganicSettingsValidationRejectsImpossibleRequiredMinimum()
        {
            OrganicGenerationSettings settings = new OrganicGenerationSettings
            {
                targetRoomCount = 1
            };
            GameObject prefab = CreateTemplatePrefab("RequiredRoom", RoomType.Room);
            settings.templates.Add(new TemplateEntry
            {
                prefab = prefab,
                enabled = true,
                weight = 1f,
                requiredMinimumCount = 2
            });

            ValidationReport report = settings.Validate();

            Assert.IsFalse(report.IsValid);
            Assert.GreaterOrEqual(report.ErrorCount, 1);

            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void OrganicSolverProducesDeterministicValidLayout()
        {
            GameObject startPrefab = CreateTemplatePrefab("Start", RoomType.Entrance);
            GameObject roomPrefab = CreateTemplatePrefab("Room", RoomType.Room);
            TemplateCatalog catalog = CreateOrganicLineCatalog(startPrefab, roomPrefab);
            OrganicGenerationSettings settings = CreateOrganicLineSettings(startPrefab, roomPrefab, null, 8, 123);

            DungeonLayout firstLayout = GenerateOrganicLayout(settings, catalog, 123, out DungeonGenerationDiagnostics firstDiagnostics);
            DungeonLayout secondLayout = GenerateOrganicLayout(settings, catalog, 123, out DungeonGenerationDiagnostics secondDiagnostics);

            Assert.NotNull(firstLayout);
            Assert.NotNull(secondLayout);
            Assert.IsTrue(firstLayout.ValidateNoOverlaps(out string overlapMessage), overlapMessage);
            Assert.IsTrue(firstLayout.ValidateConnectivity(out string connectivityMessage), connectivityMessage);
            Assert.AreEqual(firstLayout.Rooms.Count, secondLayout.Rooms.Count);
            Assert.Greater(firstDiagnostics.precomputedCompatibilityHits, 0);
            Assert.Greater(secondDiagnostics.pooledListReuses, 0);

            for (int i = 0; i < firstLayout.Rooms.Count; i++)
            {
                Assert.AreEqual(firstLayout.Rooms[i].position, secondLayout.Rooms[i].position);
                Assert.AreSame(firstLayout.Rooms[i].sourcePrefab, secondLayout.Rooms[i].sourcePrefab);
            }

            Object.DestroyImmediate(startPrefab);
            Object.DestroyImmediate(roomPrefab);
        }

        [Test]
        public void OrganicSolverPlacesRequiredStartAndEndTemplates()
        {
            GameObject startPrefab = CreateTemplatePrefab("Start", RoomType.Entrance);
            GameObject requiredPrefab = CreateTemplatePrefab("Required", RoomType.Reward);
            GameObject fillerPrefab = CreateTemplatePrefab("Filler", RoomType.Room);
            GameObject endPrefab = CreateTemplatePrefab("End", RoomType.Boss);
            TemplateCatalog catalog = CreateOrganicLineCatalog(startPrefab, requiredPrefab, fillerPrefab, endPrefab);
            OrganicGenerationSettings settings = CreateOrganicLineSettings(startPrefab, fillerPrefab, endPrefab, 4, 321);
            settings.templates.Insert(0, new TemplateEntry
            {
                prefab = requiredPrefab,
                enabled = true,
                weight = 1f,
                requiredMinimumCount = 1,
                maximumCount = 1
            });

            DungeonLayout layout = GenerateOrganicLayout(settings, catalog, 321, out DungeonGenerationDiagnostics diagnostics);

            Assert.NotNull(layout);
            Assert.AreSame(startPrefab, layout.Rooms[0].sourcePrefab);
            Assert.AreEqual(1, CountPrefab(layout, requiredPrefab));
            Assert.AreEqual(1, CountPrefab(layout, endPrefab));
            Assert.IsTrue(layout.ValidateNoOverlaps(out string overlapMessage), overlapMessage);
            Assert.IsTrue(layout.ValidateConnectivity(out string connectivityMessage), connectivityMessage);
            Assert.Greater(diagnostics.precomputedCompatibilityHits, 0);

            Object.DestroyImmediate(startPrefab);
            Object.DestroyImmediate(requiredPrefab);
            Object.DestroyImmediate(fillerPrefab);
            Object.DestroyImmediate(endPrefab);
        }

        [Test]
        public void OrganicSolverStressBenchmarkGeneratesLargeMixedLayout()
        {
            GameObject startPrefab = CreateTemplatePrefab("Start", RoomType.Entrance);
            GameObject roomPrefab = CreateTemplatePrefab("Room", RoomType.Room);
            GameObject corridorPrefab = CreateTemplatePrefab("Corridor", RoomType.Corridor);
            TemplateCatalog catalog = CreateOrganicLineCatalog(startPrefab, roomPrefab, corridorPrefab);
            OrganicGenerationSettings settings = CreateOrganicLineSettings(startPrefab, roomPrefab, null, 50, 777);
            settings.corridorChance = 0.35f;
            settings.maxCorridorChain = 2;
            settings.templates.Add(new TemplateEntry
            {
                prefab = corridorPrefab,
                enabled = true,
                weight = 1f
            });

            DungeonLayout layout = GenerateOrganicLayout(settings, catalog, 777, out DungeonGenerationDiagnostics diagnostics);

            Assert.NotNull(layout);
            Assert.IsTrue(layout.ValidateNoOverlaps(out string overlapMessage), overlapMessage);
            Assert.IsTrue(layout.ValidateConnectivity(out string connectivityMessage), connectivityMessage);
            Assert.AreEqual(50, CountCountedRooms(layout));
            Assert.Greater(diagnostics.precomputedCompatibilityHits, 0);
            Assert.Greater(diagnostics.pooledListReuses, 0);
            TestContext.WriteLine(diagnostics.ToSummary());

            Object.DestroyImmediate(startPrefab);
            Object.DestroyImmediate(roomPrefab);
            Object.DestroyImmediate(corridorPrefab);
        }

        [Test]
        public void OrganicSolverHighStraightnessBiasStillGeneratesValidLayout()
        {
            GameObject startPrefab = CreateTemplatePrefab("StraightStart", RoomType.Entrance);
            GameObject roomPrefab = CreateTemplatePrefab("StraightRoom", RoomType.Room);
            TemplateCatalog catalog = CreateOrganicLineCatalog(startPrefab, roomPrefab);
            OrganicGenerationSettings settings = CreateOrganicLineSettings(startPrefab, roomPrefab, null, 16, 778);
            settings.branchingBias = OrganicBranchingBias.Straighter;
            settings.branchingBiasStrength = 1f;
            settings.branchingProbability = 0f;
            settings.corridorChance = 0f;

            DungeonLayout layout = GenerateOrganicLayout(settings, catalog, 778, out DungeonGenerationDiagnostics diagnostics);

            Assert.NotNull(layout);
            Assert.IsTrue(layout.ValidateNoOverlaps(out string overlapMessage), overlapMessage);
            Assert.IsTrue(layout.ValidateConnectivity(out string connectivityMessage), connectivityMessage);
            Assert.AreEqual(16, CountCountedRooms(layout));

            Object.DestroyImmediate(startPrefab);
            Object.DestroyImmediate(roomPrefab);
        }

        [Test]
        public void OrganicSolverDirectionalGrowthHeuristicBiasesFrontierDirection()
        {
            GameObject startPrefab = CreateTemplatePrefab("DirectionalStart", RoomType.Entrance);
            GameObject roomPrefab = CreateTemplatePrefab("EastRoom", RoomType.Room);
            TemplateCatalog catalog = new TemplateCatalog();
            RegisterOrganicTemplate(
                catalog,
                startPrefab,
                RoomType.Entrance,
                CreateRoomVariant(
                    "DirectionalStart",
                    new DoorAnchor(new Vector2Int(0, 1), FacingDirection.North),
                    new DoorAnchor(new Vector2Int(0, -1), FacingDirection.South),
                    new DoorAnchor(new Vector2Int(1, 0), FacingDirection.East),
                    new DoorAnchor(new Vector2Int(-1, 0), FacingDirection.West)));
            RegisterOrganicTemplate(
                catalog,
                roomPrefab,
                RoomType.Room,
                CreateRoomVariant("EastRoom", new DoorAnchor(new Vector2Int(-1, 0), FacingDirection.West)));

            OrganicGenerationSettings settings = CreateOrganicLineSettings(startPrefab, roomPrefab, null, 2, 909);
            settings.useDirectionalGrowthHeuristic = true;
            settings.preferredGrowthDirection = FacingDirection.East;
            settings.directionalGrowthBias = 1f;

            DungeonLayout layout = GenerateOrganicLayout(settings, catalog, 909, out DungeonGenerationDiagnostics _);

            Assert.NotNull(layout);
            Assert.AreEqual(2, layout.Rooms.Count);
            Assert.Greater(layout.Rooms[1].position.x, layout.Rooms[0].position.x);

            Object.DestroyImmediate(startPrefab);
            Object.DestroyImmediate(roomPrefab);
        }

        [Test]
        public void OrganicSolverRoomCountRangeResolvesTargetWithinRange()
        {
            GameObject startPrefab = CreateTemplatePrefab("Start", RoomType.Entrance);
            GameObject roomPrefab = CreateTemplatePrefab("Room", RoomType.Room);
            TemplateCatalog catalog = CreateOrganicLineCatalog(startPrefab, roomPrefab);
            OrganicGenerationSettings settings = CreateOrganicLineSettings(startPrefab, roomPrefab, null, 99, 444);
            settings.useRoomCountRange = true;
            settings.minRoomCount = 5;
            settings.maxRoomCount = 5;

            DungeonLayout layout = GenerateOrganicLayout(settings, catalog, 444, out DungeonGenerationDiagnostics _);

            Assert.NotNull(layout);
            Assert.AreEqual(5, CountCountedRooms(layout));

            Object.DestroyImmediate(startPrefab);
            Object.DestroyImmediate(roomPrefab);
        }

        [Test]
        public void OrganicSettingsTemplateDirectionBiasAdjustsDirectionalWeight()
        {
            GameObject eastPrefab = CreateTemplatePrefab("EastBiased", RoomType.Room);
            OrganicGenerationSettings settings = new OrganicGenerationSettings();
            settings.templates.Add(new TemplateEntry
            {
                prefab = eastPrefab,
                enabled = true,
                weight = 2f,
                useDirectionBias = true,
                preferredDirection = FacingDirection.East,
                directionBias = 1f
            });

            Assert.Greater(settings.GetDirectionalWeight(eastPrefab, FacingDirection.East), settings.GetWeight(eastPrefab));
            Assert.Less(settings.GetDirectionalWeight(eastPrefab, FacingDirection.West), settings.GetWeight(eastPrefab));

            Object.DestroyImmediate(eastPrefab);
        }

        [Test]
        public void OrganicSettingsValidationWarnsForSocketWithoutCompatibleOpposite()
        {
            GameObject startPrefab = CreateTemplatePrefab("SocketStart", RoomType.Entrance);
            RoomTemplateComponent component = startPrefab.GetComponent<RoomTemplateComponent>();
            component.bakedData.anchors.Add(new DoorAnchor(Vector2Int.zero, FacingDirection.East, 1, "SimpleDoor"));

            OrganicGenerationSettings settings = new OrganicGenerationSettings
            {
                startPrefab = startPrefab,
                targetRoomCount = 1
            };

            ValidationReport report = settings.Validate();

            Assert.IsTrue(new System.Collections.Generic.List<string>(report.Warnings).Exists(w => w.Contains("no compatible opposite door")));

            Object.DestroyImmediate(startPrefab);
        }

        [Test]
        public void FlowCreateSolverFlowCollapsesCorridorChains()
        {
            DungeonFlow flow = ScriptableObject.CreateInstance<DungeonFlow>();
            flow.nodes.Add(new RoomNode("A") { type = RoomType.Room });
            flow.nodes.Add(new RoomNode("C1") { type = RoomType.Corridor });
            flow.nodes.Add(new RoomNode("C2") { type = RoomType.Corridor });
            flow.nodes.Add(new RoomNode("B") { type = RoomType.Room });
            flow.edges.Add(new RoomEdge("A", "C1"));
            flow.edges.Add(new RoomEdge("C1", "C2"));
            flow.edges.Add(new RoomEdge("C2", "B"));

            DungeonFlow expanded = flow.CreateSolverFlow(true);
            DungeonFlow collapsed = flow.CreateSolverFlow(false);

            Assert.AreEqual(4, expanded.nodes.Count);
            Assert.AreEqual(3, collapsed.nodes.Count);
            Assert.IsTrue(flow.HasExpandedCorridorLinks());

            Object.DestroyImmediate(flow);
            Object.DestroyImmediate(expanded);
            Object.DestroyImmediate(collapsed);
        }

        [Test]
        public void DynamicCorridorCountAllowsZeroButNotNegative()
        {
            DungeonFlow flow = ScriptableObject.CreateInstance<DungeonFlow>();
            flow.corridorPlacementMode = CorridorPlacementMode.Dynamic;
            flow.dynamicCorridorSpacing = 10f;
            flow.maxDynamicCorridorCount = 6;

            Assert.AreEqual(0, flow.GetCorridorCountForConnection(Vector2.zero, Vector2.one));

            flow.maxDynamicCorridorCount = 0;
            Assert.AreEqual(0, flow.GetCorridorCountForConnection(Vector2.zero, new Vector2(100f, 0f)));

            Object.DestroyImmediate(flow);
        }

        [Test]
        public void ValidationReportSeparatesErrorsAndWarnings()
        {
            ValidationReport report = new ValidationReport();

            report.AddWarning("Warn");
            report.AddError("Error");

            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(1, report.ErrorCount);
            CollectionAssert.AreEqual(new[] { "Warn" }, new System.Collections.Generic.List<string>(report.Warnings));
            CollectionAssert.AreEqual(new[] { "Error" }, new System.Collections.Generic.List<string>(report.Errors));
        }

        [Test]
        public void TemplateCatalogPrecomputesDoorPositionsAndCompatibility()
        {
            GameObject eastPrefab = new GameObject("EastTemplate");
            GameObject westPrefab = new GameObject("WestTemplate");
            RoomVariant eastVariant = CreateRoomVariant("EastTemplate", FacingDirection.East);
            RoomVariant westVariant = CreateRoomVariant("WestTemplate", FacingDirection.West);
            TemplateCatalog catalog = new TemplateCatalog();

            catalog.Cache[eastPrefab] = new System.Collections.Generic.List<RoomVariant> { eastVariant };
            catalog.Cache[westPrefab] = new System.Collections.Generic.List<RoomVariant> { westVariant };
            catalog.RegisterTemplate(eastPrefab, catalog.Cache[eastPrefab]);
            catalog.RegisterTemplate(westPrefab, catalog.Cache[westPrefab]);

            DoorAnchor eastDoor = eastVariant.anchors[0];
            DoorAnchor westDoor = westVariant.anchors[0];

            CollectionAssert.AreEqual(new[] { eastDoor.locallyOccupiedCell }, catalog.GetDoorBasePositions(eastDoor));
            Assert.IsTrue(new System.Collections.Generic.List<DoorAnchor>(catalog.GetCompatibleDoors(eastDoor)).Contains(westDoor));
            Assert.Greater(catalog.CompatibleDoorIndexCount, 0);

            Object.DestroyImmediate(eastPrefab);
            Object.DestroyImmediate(westPrefab);
        }

        [Test]
        public void FlowSolverUsesPrecomputedCompatibilityAndProducesReplayableLayout()
        {
            GameObject eastPrefab = new GameObject("EastTemplate");
            GameObject westPrefab = new GameObject("WestTemplate");
            RoomVariant eastVariant = CreateRoomVariant("EastTemplate", FacingDirection.East);
            RoomVariant westVariant = CreateRoomVariant("WestTemplate", FacingDirection.West);
            TemplateCatalog catalog = new TemplateCatalog();
            catalog.Cache[eastPrefab] = new System.Collections.Generic.List<RoomVariant> { eastVariant };
            catalog.Cache[westPrefab] = new System.Collections.Generic.List<RoomVariant> { westVariant };
            catalog.RegisterTemplate(eastPrefab, catalog.Cache[eastPrefab]);
            catalog.RegisterTemplate(westPrefab, catalog.Cache[westPrefab]);

            DungeonFlow flow = ScriptableObject.CreateInstance<DungeonFlow>();
            flow.nodes.Add(new RoomNode("A") { type = RoomType.Room, allowedTemplates = new System.Collections.Generic.List<GameObject> { eastPrefab } });
            flow.nodes.Add(new RoomNode("B") { type = RoomType.Room, allowedTemplates = new System.Collections.Generic.List<GameObject> { westPrefab } });
            flow.edges.Add(new RoomEdge("A", "B"));

            DungeonGenerationDiagnostics diagnostics = new DungeonGenerationDiagnostics();
            diagnostics.Begin(1, 123);
            DungeonSolver firstSolver = new DungeonSolver(
                flow,
                catalog,
                new DungeonSolver.SolverSettings { seed = 123, useRandomisation = false },
                null,
                System.Threading.CancellationToken.None,
                diagnostics);
            DungeonLayout firstLayout = firstSolver.Generate();

            DungeonSolver secondSolver = new DungeonSolver(flow, catalog, new DungeonSolver.SolverSettings { seed = 123, useRandomisation = false });
            DungeonLayout secondLayout = secondSolver.Generate();

            Assert.NotNull(firstLayout, firstSolver.LastFailureReason);
            Assert.NotNull(secondLayout, secondSolver.LastFailureReason);
            Assert.AreEqual(firstLayout.Rooms.Count, secondLayout.Rooms.Count);
            Assert.AreEqual(firstLayout.Rooms[1].position, secondLayout.Rooms[1].position);
            Assert.GreaterOrEqual(diagnostics.precomputedCompatibilityHits, 1);

            Object.DestroyImmediate(flow);
            Object.DestroyImmediate(eastPrefab);
            Object.DestroyImmediate(westPrefab);
        }

        [Test]
        public void DungeonGenerationResultFailedCarriesFailureReason()
        {
            DungeonGenerationResult result = DungeonGenerationResult.Failed("No templates.");

            Assert.IsFalse(result.Success);
            Assert.AreEqual("No templates.", result.FailureReason);
            Assert.IsNull(result.Layout);
        }

        private static PlacedRoom CreatePlacedRoom(string id, Vector2Int position, FacingDirection direction)
        {
            DoorAnchor anchor = new DoorAnchor(Vector2Int.zero, direction);
            RoomVariant variant = CreateRoomVariant(id, anchor);
            RoomNode node = new RoomNode(id) { displayName = id };
            return new PlacedRoom(node, variant, position, null);
        }

        private static RoomVariant CreateRoomVariant(string id, FacingDirection direction)
        {
            return CreateRoomVariant(id, new DoorAnchor(Vector2Int.zero, direction));
        }

        private static RoomVariant CreateRoomVariant(string id, DoorAnchor anchor)
        {
            RoomTemplateData template = new RoomTemplateData
            {
                templateName = id,
                allowRotation = false,
                allowMirroring = false
            };
            template.cells.Add(new CellData(Vector2Int.zero, TileType.Floor));
            template.anchors.Add(anchor);

            return template.GenerateVariants()[0];
        }

        private static RoomVariant CreateRoomVariant(string id, params DoorAnchor[] anchors)
        {
            RoomTemplateData template = new RoomTemplateData
            {
                templateName = id,
                allowRotation = false,
                allowMirroring = false
            };
            template.cells.Add(new CellData(Vector2Int.zero, TileType.Floor));
            foreach (DoorAnchor anchor in anchors)
            {
                template.anchors.Add(anchor);
            }

            return template.GenerateVariants()[0];
        }

        private static GameObject CreateTemplatePrefab(string name, RoomType roomType)
        {
            GameObject prefab = new GameObject(name);
            RoomTemplateComponent component = prefab.AddComponent<RoomTemplateComponent>();
            component.roomType = roomType;
            return prefab;
        }

        private static TemplateCatalog CreateOrganicLineCatalog(GameObject startPrefab, GameObject roomPrefab)
        {
            return CreateOrganicLineCatalog(startPrefab, roomPrefab, null, null);
        }

        private static TemplateCatalog CreateOrganicLineCatalog(GameObject startPrefab, GameObject roomPrefab, GameObject thirdPrefab)
        {
            return CreateOrganicLineCatalog(startPrefab, roomPrefab, thirdPrefab, null);
        }

        private static TemplateCatalog CreateOrganicLineCatalog(GameObject startPrefab, GameObject roomPrefab, GameObject thirdPrefab, GameObject endPrefab)
        {
            TemplateCatalog catalog = new TemplateCatalog();
            RegisterOrganicTemplate(catalog, startPrefab, RoomType.Entrance, CreateRoomVariant("Start", new DoorAnchor(Vector2Int.zero, FacingDirection.East)));
            RegisterOrganicTemplate(catalog, roomPrefab, roomPrefab.GetComponent<RoomTemplateComponent>().roomType, CreateRoomVariant(roomPrefab.name, new DoorAnchor(Vector2Int.zero, FacingDirection.West), new DoorAnchor(Vector2Int.zero, FacingDirection.East)));

            if (thirdPrefab != null)
            {
                RoomType thirdType = thirdPrefab.GetComponent<RoomTemplateComponent>().roomType;
                RoomVariant thirdVariant = thirdType == RoomType.Boss
                    ? CreateRoomVariant(thirdPrefab.name, new DoorAnchor(Vector2Int.zero, FacingDirection.West))
                    : CreateRoomVariant(thirdPrefab.name, new DoorAnchor(Vector2Int.zero, FacingDirection.West), new DoorAnchor(Vector2Int.zero, FacingDirection.East));
                RegisterOrganicTemplate(catalog, thirdPrefab, thirdType, thirdVariant);
            }

            if (endPrefab != null)
            {
                RegisterOrganicTemplate(catalog, endPrefab, RoomType.Boss, CreateRoomVariant("End", new DoorAnchor(Vector2Int.zero, FacingDirection.West)));
            }

            return catalog;
        }

        private static void RegisterOrganicTemplate(TemplateCatalog catalog, GameObject prefab, RoomType roomType, RoomVariant variant)
        {
            catalog.Cache[prefab] = new System.Collections.Generic.List<RoomVariant> { variant };
            catalog.Metadata[prefab] = new TemplateMetadata { name = prefab.name, type = roomType };
            catalog.RegisterTemplate(prefab, catalog.Cache[prefab]);

            if (roomType == RoomType.Corridor)
            {
                catalog.CorridorTemplates.Add(prefab);
            }
            else if (roomType == RoomType.Entrance)
            {
                catalog.EntranceTemplates.Add(prefab);
            }
            else
            {
                catalog.RoomTemplates.Add(prefab);
            }
        }

        private static OrganicGenerationSettings CreateOrganicLineSettings(GameObject startPrefab, GameObject roomPrefab, GameObject endPrefab, int targetRoomCount, int seed)
        {
            OrganicGenerationSettings settings = new OrganicGenerationSettings
            {
                startPrefab = startPrefab,
                endPrefab = endPrefab,
                targetRoomCount = targetRoomCount,
                seed = seed,
                useRandomSeed = false,
                corridorChance = 0f,
                maxCorridorChain = 0,
                branchingProbability = 0f
            };
            settings.templates.Add(new TemplateEntry
            {
                prefab = roomPrefab,
                enabled = true,
                weight = 1f
            });
            return settings;
        }

        private static DungeonLayout GenerateOrganicLayout(OrganicGenerationSettings settings, TemplateCatalog catalog, int seed, out DungeonGenerationDiagnostics diagnostics)
        {
            diagnostics = new DungeonGenerationDiagnostics();
            diagnostics.Begin(1, seed);
            OrganicDungeonSolver solver = new OrganicDungeonSolver(
                settings,
                catalog,
                new DungeonSolver.SolverSettings { seed = seed, useRandomisation = true, maxSearchSteps = 10000 },
                null,
                System.Threading.CancellationToken.None,
                diagnostics);
            DungeonLayout layout = solver.Generate();
            diagnostics.End(solver.LastFailureReason);
            Assert.NotNull(layout, solver.LastFailureReason);
            return layout;
        }

        private static int CountPrefab(DungeonLayout layout, GameObject prefab)
        {
            int count = 0;
            foreach (PlacedRoom room in layout.Rooms)
            {
                if (ReferenceEquals(room.sourcePrefab, prefab))
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountCountedRooms(DungeonLayout layout)
        {
            int count = 0;
            foreach (PlacedRoom room in layout.Rooms)
            {
                if (room.node.type != RoomType.Corridor)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
