using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

namespace DynamicDungeon.Tests.Runtime
{
    public sealed class PrefabStamperNodeTests
    {
        private const string LogicalIdsChannelName = GraphOutputUtility.OutputInputPortName;
        private const string PointsChannelName = "Points";
        private const string TempAssetFolder = "Assets/DynamicDungeon/Tests/TempGenerated";
        private static readonly int VoidLogicalId = LogicalTileId.Void;

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (AssetDatabase.IsValidFolder(TempAssetFolder))
            {
                AssetDatabase.DeleteAsset(TempAssetFolder);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        [Test]
        public void PrefabStampAuthoring_BuildsTemplateFromChildTilemap()
        {
            GameObject root = null;
            Tile floorTile = null;
            Tile wallTile = null;

            try
            {
                root = new GameObject("AuthoringRoot");
                root.AddComponent<Grid>();
                PrefabStampAuthoring authoring = root.AddComponent<PrefabStampAuthoring>();

                GameObject tilemapObject = new GameObject("Footprint");
                tilemapObject.transform.SetParent(root.transform, false);
                Tilemap tilemap = tilemapObject.AddComponent<Tilemap>();
                tilemapObject.AddComponent<TilemapRenderer>();

                floorTile = ScriptableObject.CreateInstance<Tile>();
                wallTile = ScriptableObject.CreateInstance<Tile>();
                tilemap.SetTile(new Vector3Int(0, 0, 0), floorTile);
                tilemap.SetTile(new Vector3Int(2, 1, 0), wallTile);

                bool built = authoring.TryBuildTemplate("prefab-guid", out PrefabStampTemplate template, out string errorMessage);

                Assert.That(built, Is.True, errorMessage);
                Assert.That(template.PrefabGuid, Is.EqualTo("prefab-guid"));
                Assert.That(template.UsesTilemapFootprint, Is.True);
                Assert.That(template.OccupiedCells, Is.EqualTo(new[]
                {
                    new Vector2Int(0, 0),
                    new Vector2Int(2, 1)
                }));
            }
            finally
            {
                DestroyImmediateIfNotNull(floorTile);
                DestroyImmediateIfNotNull(wallTile);
                DestroyImmediateIfNotNull(root);
            }
        }

        [Test]
        public void PrefabStampAuthoring_CombinesTilemapsAndFillsEnclosedInterior()
        {
            GameObject root = null;
            Tile tile = null;

            try
            {
                root = new GameObject("AuthoringRoot");
                root.AddComponent<Grid>();
                PrefabStampAuthoring authoring = root.AddComponent<PrefabStampAuthoring>();
                tile = ScriptableObject.CreateInstance<Tile>();

                Tilemap lower = CreateTilemapChild(root.transform, "Lower");
                Tilemap upper = CreateTilemapChild(root.transform, "Upper");

                lower.SetTile(new Vector3Int(0, 0, 0), tile);
                lower.SetTile(new Vector3Int(1, 0, 0), tile);
                lower.SetTile(new Vector3Int(2, 0, 0), tile);
                lower.SetTile(new Vector3Int(0, 1, 0), tile);
                lower.SetTile(new Vector3Int(2, 1, 0), tile);
                upper.SetTile(new Vector3Int(0, 2, 0), tile);
                upper.SetTile(new Vector3Int(1, 2, 0), tile);
                upper.SetTile(new Vector3Int(2, 2, 0), tile);

                bool built = authoring.TryBuildTemplate("prefab-guid", out PrefabStampTemplate template, out string errorMessage);

                Assert.That(built, Is.True, errorMessage);
                Assert.That(template.UsesTilemapFootprint, Is.True);
                Assert.That(template.OccupiedCells, Is.EqualTo(new[]
                {
                    new Vector2Int(0, 0),
                    new Vector2Int(1, 0),
                    new Vector2Int(2, 0),
                    new Vector2Int(0, 1),
                    new Vector2Int(1, 1),
                    new Vector2Int(2, 1),
                    new Vector2Int(0, 2),
                    new Vector2Int(1, 2),
                    new Vector2Int(2, 2)
                }));
            }
            finally
            {
                DestroyImmediateIfNotNull(tile);
                DestroyImmediateIfNotNull(root);
            }
        }

        [Test]
        public void PrefabStampAuthoring_BuildsTemplateFromSnappedChildren()
        {
            GameObject root = null;

            try
            {
                root = new GameObject("AuthoringRoot");
                PrefabStampAuthoring authoring = root.AddComponent<PrefabStampAuthoring>();

                CreateChild(root.transform, "A", new Vector3(0.0f, 0.0f, 0.0f));
                CreateChild(root.transform, "B", new Vector3(1.0f, 0.0f, 0.0f));
                CreateChild(root.transform, "C", new Vector3(1.0f, 2.0f, 0.0f));

                bool built = authoring.TryBuildTemplate("prefab-guid", out PrefabStampTemplate template, out string errorMessage);

                Assert.That(built, Is.True, errorMessage);
                Assert.That(template.UsesTilemapFootprint, Is.False);
                Assert.That(template.OccupiedCells, Is.EqualTo(new[]
                {
                    new Vector2Int(0, 0),
                    new Vector2Int(1, 0),
                    new Vector2Int(1, 2)
                }));
            }
            finally
            {
                DestroyImmediateIfNotNull(root);
            }
        }

        [Test]
        public void PrefabStampAuthoring_FailsWithoutDerivedCells()
        {
            GameObject root = null;

            try
            {
                root = new GameObject("AuthoringRoot");
                PrefabStampAuthoring authoring = root.AddComponent<PrefabStampAuthoring>();

                bool built = authoring.TryBuildTemplate("prefab-guid", out PrefabStampTemplate template, out string errorMessage);

                Assert.That(built, Is.False);
                Assert.That(template.IsValid, Is.False);
                Assert.That(errorMessage, Is.EqualTo("No snapped child cells were found for prefab stamp authoring."));
            }
            finally
            {
                DestroyImmediateIfNotNull(root);
            }
        }

        [Test]
        public async Task NoneMode_RecordsPlacementWithoutChangingLogicalIds()
        {
            PrefabStampTemplate template = CreateTemplate("prefab-guid", new[] { new Vector2Int(0, 0), new Vector2Int(1, 0) });
            int[] initial =
            {
                4, 4, 4,
                4, 4, 4
            };

            WorldSnapshot snapshot = await ExecutePrefabPlanAsync(
                width: 3,
                height: 2,
                initialLogicalIds: initial,
                placements: new[] { new int2(1, 0) },
                template: template,
                footprintMode: PrefabFootprintMode.None,
                interiorLogicalId: 7,
                outlineLogicalId: 8,
                blendMode: StampBlendMode.Overwrite,
                mirrorX: false,
                mirrorY: false,
                allowRotation: false,
                maxOverlapTiles: 0);

            Assert.That(GetIntChannelData(snapshot, LogicalIdsChannelName), Is.EqualTo(initial));

            PrefabPlacementRecord[] placementsRecorded = GetPlacementData(snapshot);
            Assert.That(placementsRecorded.Length, Is.EqualTo(1));
            Assert.That(placementsRecorded[0].OriginX, Is.EqualTo(1));
            Assert.That(placementsRecorded[0].OriginY, Is.EqualTo(0));
        }

        [Test]
        public async Task FillInterior_WritesLogicalIdsAndRecordsPlacement()
        {
            PrefabStampTemplate template = CreateTemplate("prefab-guid", new[]
            {
                new Vector2Int(0, 0),
                new Vector2Int(1, 0),
                new Vector2Int(0, 1)
            });

            WorldSnapshot snapshot = await ExecutePrefabPlanAsync(
                width: 4,
                height: 4,
                initialLogicalIds: new int[16],
                placements: new[] { new int2(1, 1) },
                template: template,
                footprintMode: PrefabFootprintMode.FillInterior,
                interiorLogicalId: 7,
                outlineLogicalId: 0,
                blendMode: StampBlendMode.Overwrite,
                mirrorX: false,
                mirrorY: false,
                allowRotation: false,
                maxOverlapTiles: 3);

            int[] expected =
            {
                0, 0, 0, 0,
                0, 7, 7, 0,
                0, 7, 0, 0,
                0, 0, 0, 0
            };

            Assert.That(GetIntChannelData(snapshot, LogicalIdsChannelName), Is.EqualTo(expected));

            PrefabPlacementRecord[] placementsRecorded = GetPlacementData(snapshot);
            Assert.That(placementsRecorded.Length, Is.EqualTo(1));
            Assert.That(placementsRecorded[0].TemplateIndex, Is.EqualTo(0));
        }

        [Test]
        public async Task WeightedVariants_IgnoreZeroWeightPrefabAndUseChosenFootprint()
        {
            PrefabStampTemplate firstTemplate = CreateTemplate("prefab-a-guid", new[] { new Vector2Int(0, 0) });
            PrefabStampTemplate secondTemplate = CreateTemplate("prefab-b-guid", new[]
            {
                new Vector2Int(0, 0),
                new Vector2Int(1, 0)
            });

            WorldSnapshot snapshot = await ExecutePrefabVariantsPlanAsync(
                width: 4,
                height: 1,
                initialLogicalIds: new int[4],
                placements: new[] { new int2(0, 0) },
                templates: new[] { firstTemplate, secondTemplate },
                weights: new[] { 0.0f, 1.0f },
                footprintMode: PrefabFootprintMode.FillInterior,
                interiorLogicalId: 7,
                outlineLogicalId: 0,
                blendMode: StampBlendMode.Overwrite,
                mirrorX: false,
                mirrorY: false,
                allowRotation: false,
                maxOverlapTiles: 2);

            int[] expected =
            {
                7, 7, 0, 0
            };

            Assert.That(GetIntChannelData(snapshot, LogicalIdsChannelName), Is.EqualTo(expected));

            PrefabPlacementRecord[] placementsRecorded = GetPlacementData(snapshot);
            Assert.That(placementsRecorded.Length, Is.EqualTo(1));
            Assert.That(placementsRecorded[0].TemplateIndex, Is.EqualTo(1));
        }

        [Test]
        public async Task OutlineAndCarve_ClearsInteriorAndWritesPerimeter()
        {
            PrefabStampTemplate template = CreateTemplate("prefab-guid", new[] { new Vector2Int(0, 0) });
            int[] initial = new int[25];

            int index;
            for (index = 0; index < initial.Length; index++)
            {
                initial[index] = 9;
            }

            WorldSnapshot snapshot = await ExecutePrefabPlanAsync(
                width: 5,
                height: 5,
                initialLogicalIds: initial,
                placements: new[] { new int2(2, 2) },
                template: template,
                footprintMode: PrefabFootprintMode.OutlineAndCarve,
                interiorLogicalId: 0,
                outlineLogicalId: 3,
                blendMode: StampBlendMode.Overwrite,
                mirrorX: false,
                mirrorY: false,
                allowRotation: false,
                maxOverlapTiles: 1);

            int[] output = GetIntChannelData(snapshot, LogicalIdsChannelName);

            Assert.That(output[ToIndex(2, 2, 5)], Is.EqualTo(VoidLogicalId));
            Assert.That(output[ToIndex(3, 2, 5)], Is.EqualTo(3));
            Assert.That(output[ToIndex(1, 2, 5)], Is.EqualTo(3));
            Assert.That(output[ToIndex(2, 3, 5)], Is.EqualTo(3));
            Assert.That(output[ToIndex(2, 1, 5)], Is.EqualTo(3));
        }

        [Test]
        public async Task OverlapBudget_SkipsLogicalWritesAndPlacement()
        {
            PrefabStampTemplate template = CreateTemplate("prefab-guid", new[]
            {
                new Vector2Int(0, 0),
                new Vector2Int(1, 0)
            });

            int[] initial =
            {
                5, 5, 5,
                5, 5, 5
            };

            WorldSnapshot snapshot = await ExecutePrefabPlanAsync(
                width: 3,
                height: 2,
                initialLogicalIds: initial,
                placements: new[] { new int2(0, 0) },
                template: template,
                footprintMode: PrefabFootprintMode.FillInterior,
                interiorLogicalId: 7,
                outlineLogicalId: 0,
                blendMode: StampBlendMode.Overwrite,
                mirrorX: false,
                mirrorY: false,
                allowRotation: false,
                maxOverlapTiles: 0);

            Assert.That(GetIntChannelData(snapshot, LogicalIdsChannelName), Is.EqualTo(initial));
            Assert.That(GetPlacementData(snapshot), Is.Empty);
        }

        [Test]
        public async Task OverlapBudget_IgnoresFloorLikeCells()
        {
            PrefabStampTemplate template = CreateTemplate("prefab-guid", new[]
            {
                new Vector2Int(0, 0),
                new Vector2Int(1, 0)
            });

            int[] initial =
            {
                LogicalTileId.Floor, LogicalTileId.Floor, LogicalTileId.Wall,
                LogicalTileId.Access, LogicalTileId.Floor, LogicalTileId.Wall
            };

            WorldSnapshot snapshot = await ExecutePrefabPlanAsync(
                width: 3,
                height: 2,
                initialLogicalIds: initial,
                placements: new[] { new int2(0, 0) },
                template: template,
                footprintMode: PrefabFootprintMode.FillInterior,
                interiorLogicalId: 7,
                outlineLogicalId: 0,
                blendMode: StampBlendMode.Overwrite,
                mirrorX: false,
                mirrorY: false,
                allowRotation: false,
                maxOverlapTiles: 0);

            int[] output = GetIntChannelData(snapshot, LogicalIdsChannelName);
            Assert.That(output[ToIndex(0, 0, 3)], Is.EqualTo(7));
            Assert.That(output[ToIndex(1, 0, 3)], Is.EqualTo(7));
            Assert.That(GetPlacementData(snapshot).Length, Is.EqualTo(1));
        }

        [Test]
        public async Task CarveModes_IgnoreOverlapBudget()
        {
            PrefabStampTemplate template = CreateTemplate("prefab-guid", new[]
            {
                new Vector2Int(0, 0),
                new Vector2Int(1, 0)
            });

            int[] initial =
            {
                LogicalTileId.Wall, LogicalTileId.Wall, LogicalTileId.Wall,
                LogicalTileId.Wall, LogicalTileId.Wall, LogicalTileId.Wall
            };

            WorldSnapshot snapshot = await ExecutePrefabPlanAsync(
                width: 3,
                height: 2,
                initialLogicalIds: initial,
                placements: new[] { new int2(0, 0) },
                template: template,
                footprintMode: PrefabFootprintMode.CarveInterior,
                interiorLogicalId: 7,
                outlineLogicalId: 0,
                blendMode: StampBlendMode.Overwrite,
                mirrorX: false,
                mirrorY: false,
                allowRotation: false,
                maxOverlapTiles: 0);

            int[] output = GetIntChannelData(snapshot, LogicalIdsChannelName);
            Assert.That(output[ToIndex(0, 0, 3)], Is.EqualTo(VoidLogicalId));
            Assert.That(output[ToIndex(1, 0, 3)], Is.EqualTo(VoidLogicalId));
            Assert.That(GetPlacementData(snapshot).Length, Is.EqualTo(1));
        }

        [Test]
        public async Task CarveModes_WriteReservedMask()
        {
            PrefabStampTemplate template = CreateTemplate("prefab-guid", new[]
            {
                new Vector2Int(0, 0),
                new Vector2Int(1, 0),
                new Vector2Int(0, 1)
            });

            WorldSnapshot snapshot = await ExecutePrefabPlanAsync(
                width: 4,
                height: 4,
                initialLogicalIds: CreateFilledArray(16, LogicalTileId.Wall),
                placements: new[] { new int2(1, 1) },
                template: template,
                footprintMode: PrefabFootprintMode.CarveInterior,
                interiorLogicalId: 7,
                outlineLogicalId: 0,
                blendMode: StampBlendMode.Overwrite,
                mirrorX: false,
                mirrorY: false,
                allowRotation: false,
                maxOverlapTiles: 0);

            byte[] reservedMask = GetBoolMaskChannelData(snapshot, "ReservedMask__stamp");
            Assert.That(reservedMask[ToIndex(1, 1, 4)], Is.EqualTo(1));
            Assert.That(reservedMask[ToIndex(2, 1, 4)], Is.EqualTo(1));
            Assert.That(reservedMask[ToIndex(1, 2, 4)], Is.EqualTo(1));
            Assert.That(reservedMask[ToIndex(2, 2, 4)], Is.EqualTo(0));
        }

        [Test]
        public async Task RecordedTransformMatchesLogicalEdits()
        {
            PrefabStampTemplate template = CreateTemplate("prefab-guid", new[]
            {
                new Vector2Int(0, 0),
                new Vector2Int(1, 0),
                new Vector2Int(0, 1)
            });

            WorldSnapshot snapshot = await ExecutePrefabPlanAsync(
                width: 6,
                height: 6,
                initialLogicalIds: new int[36],
                placements: new[] { new int2(3, 3) },
                template: template,
                footprintMode: PrefabFootprintMode.FillInterior,
                interiorLogicalId: 11,
                outlineLogicalId: 0,
                blendMode: StampBlendMode.Overwrite,
                mirrorX: true,
                mirrorY: true,
                allowRotation: true,
                maxOverlapTiles: 3);

            PrefabPlacementRecord[] placementsRecorded = GetPlacementData(snapshot);
            Assert.That(placementsRecorded.Length, Is.EqualTo(1));

            PrefabPlacementRecord record = placementsRecorded[0];
            int[] logicalIds = GetIntChannelData(snapshot, LogicalIdsChannelName);

            int filledCount = 0;
            int cellIndex;
            for (cellIndex = 0; cellIndex < template.OccupiedCells.Length; cellIndex++)
            {
                Vector2Int sourceCell = template.OccupiedCells[cellIndex];
                Vector2Int transformed = TransformTemplateCell(template.OccupiedCells, sourceCell, record.MirrorX, record.MirrorY, record.RotationQuarterTurns);
                int worldX = record.OriginX + transformed.x;
                int worldY = record.OriginY + transformed.y;
                Assert.That(logicalIds[ToIndex(worldX, worldY, snapshot.Width)], Is.EqualTo(11));
                filledCount++;
            }

            Assert.That(CountCellsWithValue(logicalIds, 11), Is.EqualTo(filledCount));
        }

        [Test]
        public void PrefabPlacementOutputPass_InstantiatesPrefabUnderGeneratedRoot()
        {
            GameObject root = null;
            GameObject prefabAssetRoot = null;
            string prefabPath = null;

            try
            {
                EnsureTempFolderExists();

                root = new GameObject("GeneratorRoot");
                Grid grid = root.AddComponent<Grid>();
                GeneratedPrefabWriter writer = new GeneratedPrefabWriter();
                writer.EnsureRoot(root.transform);

                prefabAssetRoot = new GameObject("PlacedPrefab");
                prefabAssetRoot.transform.localScale = new Vector3(2.0f, 3.0f, 1.0f);
                prefabPath = AssetDatabase.GenerateUniqueAssetPath(TempAssetFolder + "/PlacedPrefab.prefab");
                GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(prefabAssetRoot, prefabPath);

                WorldSnapshot snapshot = new WorldSnapshot();
                snapshot.Width = 4;
                snapshot.Height = 4;
                snapshot.PrefabPlacementPrefabs = new[] { prefabAsset };
                snapshot.PrefabPlacementTemplates = new[]
                {
                    new PrefabStampTemplate
                    {
                        PrefabGuid = "prefab-guid",
                        AnchorOffset = new Vector3(0.25f, 0.5f, 0.0f),
                        UsesTilemapFootprint = false,
                        OccupiedCells = new[] { Vector2Int.zero }
                    }
                };
                snapshot.PrefabPlacementChannels = new[]
                {
                    new WorldSnapshot.PrefabPlacementListChannelSnapshot
                    {
                        Name = PrefabPlacementChannelUtility.ChannelName,
                        Data = new[] { new PrefabPlacementRecord(0, 1, 2, 1, false, true) }
                    }
                };

                PrefabPlacementOutputPass pass = new PrefabPlacementOutputPass();
                pass.Execute(snapshot, grid, writer, Vector3Int.zero);

                Transform generatedRoot = root.transform.Find("GeneratedPrefabs");
                Assert.That(generatedRoot, Is.Not.Null);
                Assert.That(generatedRoot.childCount, Is.EqualTo(1));

                Transform instance = generatedRoot.GetChild(0);
                Vector3 expectedPosition = grid.CellToWorld(new Vector3Int(1, 2, 0)) + Quaternion.Euler(0.0f, 0.0f, 90.0f) * new Vector3(0.5f, -0.25f, 0.0f);
                Assert.That(Vector3.Distance(instance.position, expectedPosition), Is.LessThan(0.001f));
                Assert.That(instance.rotation.eulerAngles.z, Is.EqualTo(90.0f).Within(0.1f));
                Assert.That(Vector3.Distance(instance.localScale, new Vector3(2.0f, -3.0f, 1.0f)), Is.LessThan(0.001f));
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(prefabPath))
                {
                    DeleteAssetIfExists(prefabPath);
                }

                DestroyImmediateIfNotNull(prefabAssetRoot);
                DestroyImmediateIfNotNull(root);
            }
        }

        [Test]
        public void PrefabPlacementOutputPass_NormalisesRotatedFootprintOrigin()
        {
            GameObject root = null;
            GameObject prefabAssetRoot = null;
            string prefabPath = null;

            try
            {
                EnsureTempFolderExists();

                root = new GameObject("GeneratorRoot");
                Grid grid = root.AddComponent<Grid>();
                GeneratedPrefabWriter writer = new GeneratedPrefabWriter();
                writer.EnsureRoot(root.transform);

                prefabAssetRoot = new GameObject("PlacedPrefab");
                prefabPath = AssetDatabase.GenerateUniqueAssetPath(TempAssetFolder + "/NormalisedPlacedPrefab.prefab");
                GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(prefabAssetRoot, prefabPath);

                WorldSnapshot snapshot = new WorldSnapshot();
                snapshot.Width = 8;
                snapshot.Height = 8;
                snapshot.PrefabPlacementPrefabs = new[] { prefabAsset };
                snapshot.PrefabPlacementTemplates = new[]
                {
                    new PrefabStampTemplate
                    {
                        PrefabGuid = "prefab-guid",
                        AnchorOffset = Vector3.zero,
                        UsesTilemapFootprint = false,
                        OccupiedCells = new[]
                        {
                            new Vector2Int(0, 0),
                            new Vector2Int(1, 0),
                            new Vector2Int(0, 1),
                            new Vector2Int(2, 0)
                        }
                    }
                };
                snapshot.PrefabPlacementChannels = new[]
                {
                    new WorldSnapshot.PrefabPlacementListChannelSnapshot
                    {
                        Name = PrefabPlacementChannelUtility.ChannelName,
                        Data = new[] { new PrefabPlacementRecord(0, 3, 4, 1, false, false) }
                    }
                };

                PrefabPlacementOutputPass pass = new PrefabPlacementOutputPass();
                pass.Execute(snapshot, grid, writer, Vector3Int.zero);

                Transform generatedRoot = root.transform.Find("GeneratedPrefabs");
                Assert.That(generatedRoot, Is.Not.Null);
                Assert.That(generatedRoot.childCount, Is.EqualTo(1));

                Transform instance = generatedRoot.GetChild(0);
                Vector3 expectedPosition = grid.CellToWorld(new Vector3Int(4, 4, 0));
                Assert.That(Vector3.Distance(instance.position, expectedPosition), Is.LessThan(0.001f));
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(prefabPath))
                {
                    DeleteAssetIfExists(prefabPath);
                }

                DestroyImmediateIfNotNull(prefabAssetRoot);
                DestroyImmediateIfNotNull(root);
            }
        }

        [Test]
        public void PrefabPlacementOutputPass_TilemapFootprintMirrorYAddsExpectedCellOffset()
        {
            GameObject root = null;
            GameObject prefabAssetRoot = null;
            string prefabPath = null;

            try
            {
                EnsureTempFolderExists();

                root = new GameObject("GeneratorRoot");
                Grid grid = root.AddComponent<Grid>();
                GeneratedPrefabWriter writer = new GeneratedPrefabWriter();
                writer.EnsureRoot(root.transform);

                prefabAssetRoot = new GameObject("PlacedPrefab");
                prefabPath = AssetDatabase.GenerateUniqueAssetPath(TempAssetFolder + "/TilemapMirrorYOffsetPrefab.prefab");
                GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(prefabAssetRoot, prefabPath);

                WorldSnapshot snapshot = new WorldSnapshot();
                snapshot.Width = 8;
                snapshot.Height = 8;
                snapshot.PrefabPlacementPrefabs = new[] { prefabAsset };
                snapshot.PrefabPlacementTemplates = new[]
                {
                    new PrefabStampTemplate
                    {
                        PrefabGuid = "prefab-guid",
                        AnchorOffset = Vector3.zero,
                        UsesTilemapFootprint = true,
                        OccupiedCells = new[]
                        {
                            new Vector2Int(0, 0),
                            new Vector2Int(0, 1)
                        }
                    }
                };
                snapshot.PrefabPlacementChannels = new[]
                {
                    new WorldSnapshot.PrefabPlacementListChannelSnapshot
                    {
                        Name = PrefabPlacementChannelUtility.ChannelName,
                        Data = new[] { new PrefabPlacementRecord(0, 2, 3, 0, false, true) }
                    }
                };

                PrefabPlacementOutputPass pass = new PrefabPlacementOutputPass();
                pass.Execute(snapshot, grid, writer, Vector3Int.zero);

                Transform generatedRoot = root.transform.Find("GeneratedPrefabs");
                Assert.That(generatedRoot, Is.Not.Null);
                Assert.That(generatedRoot.childCount, Is.EqualTo(1));

                Transform instance = generatedRoot.GetChild(0);
                Vector3 expectedPosition = grid.CellToWorld(new Vector3Int(2, 5, 0));
                Assert.That(Vector3.Distance(instance.position, expectedPosition), Is.LessThan(0.001f));
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(prefabPath))
                {
                    DeleteAssetIfExists(prefabPath);
                }

                DestroyImmediateIfNotNull(prefabAssetRoot);
                DestroyImmediateIfNotNull(root);
            }
        }

        private static async Task<WorldSnapshot> ExecutePrefabPlanAsync(
            int width,
            int height,
            int[] initialLogicalIds,
            int2[] placements,
            PrefabStampTemplate template,
            PrefabFootprintMode footprintMode,
            int interiorLogicalId,
            int outlineLogicalId,
            StampBlendMode blendMode,
            bool mirrorX,
            bool mirrorY,
            bool allowRotation,
            int maxOverlapTiles)
        {
            return await ExecutePrefabVariantsPlanAsync(
                width,
                height,
                initialLogicalIds,
                placements,
                new[] { template },
                new[] { 1.0f },
                footprintMode,
                interiorLogicalId,
                outlineLogicalId,
                blendMode,
                mirrorX,
                mirrorY,
                allowRotation,
                maxOverlapTiles);
        }

        private static async Task<WorldSnapshot> ExecutePrefabVariantsPlanAsync(
            int width,
            int height,
            int[] initialLogicalIds,
            int2[] placements,
            PrefabStampTemplate[] templates,
            float[] weights,
            PrefabFootprintMode footprintMode,
            int interiorLogicalId,
            int outlineLogicalId,
            StampBlendMode blendMode,
            bool mirrorX,
            bool mirrorY,
            bool allowRotation,
            int maxOverlapTiles)
        {
            GraphCompilerPointListNode pointsNode = new GraphCompilerPointListNode("points", "Points Node", PointsChannelName);
            pointsNode.ReceiveParameter("points", SerialisePoints(placements));

            StampTestIntPatternNode logicalIdsNode = new StampTestIntPatternNode("logical-base", "Logical Base", LogicalIdsChannelName, initialLogicalIds);
            PrefabStamperNode stampNode = CreateResolvedPrefabNode(
                templates,
                weights,
                footprintMode,
                interiorLogicalId,
                outlineLogicalId,
                blendMode,
                mirrorX,
                mirrorY,
                allowRotation,
                maxOverlapTiles);

            List<IGenNode> nodes = new List<IGenNode>
            {
                pointsNode,
                logicalIdsNode,
                stampNode
            };

            ExecutionPlan plan = ExecutionPlan.Build(nodes, width, height, 12345L);
            GameObject[] palettePrefabs = new GameObject[templates.Length];

            try
            {
                int prefabIndex;
                for (prefabIndex = 0; prefabIndex < palettePrefabs.Length; prefabIndex++)
                {
                    palettePrefabs[prefabIndex] = new GameObject("PalettePrefab" + prefabIndex.ToString(CultureInfo.InvariantCulture));
                }

                plan.SetPrefabPlacementPalette(palettePrefabs, templates);

                Executor executor = new Executor();
                ExecutionResult result = await executor.ExecuteAsync(plan, CancellationToken.None);

                Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
                Assert.That(result.Snapshot, Is.Not.Null);
                return result.Snapshot;
            }
            finally
            {
                int prefabIndex;
                for (prefabIndex = 0; prefabIndex < palettePrefabs.Length; prefabIndex++)
                {
                    DestroyImmediateIfNotNull(palettePrefabs[prefabIndex]);
                }
            }
        }

        private static PrefabStamperNode CreateResolvedPrefabNode(
            PrefabStampTemplate template,
            PrefabFootprintMode footprintMode,
            int interiorLogicalId,
            int outlineLogicalId,
            StampBlendMode blendMode,
            bool mirrorX,
            bool mirrorY,
            bool allowRotation,
            int maxOverlapTiles)
        {
            return CreateResolvedPrefabNode(
                new[] { template },
                new[] { 1.0f },
                footprintMode,
                interiorLogicalId,
                outlineLogicalId,
                blendMode,
                mirrorX,
                mirrorY,
                allowRotation,
                maxOverlapTiles);
        }

        private static PrefabStamperNode CreateResolvedPrefabNode(
            PrefabStampTemplate[] templates,
            float[] weights,
            PrefabFootprintMode footprintMode,
            int interiorLogicalId,
            int outlineLogicalId,
            StampBlendMode blendMode,
            bool mirrorX,
            bool mirrorY,
            bool allowRotation,
            int maxOverlapTiles)
        {
            PrefabStamperNode node = new PrefabStamperNode("stamp", "Stamp");
            node.ReceiveInputConnections(new Dictionary<string, string>
            {
                { PointsChannelName, PointsChannelName }
            });

            int variantCount = templates.Length;
            PrefabStampVariantSet variantSet = new PrefabStampVariantSet();
            variantSet.Variants = new PrefabStampVariant[variantCount];
            int variantIndex;
            for (variantIndex = 0; variantIndex < variantCount; variantIndex++)
            {
                variantSet.Variants[variantIndex] = new PrefabStampVariant
                {
                    Prefab = templates[variantIndex].PrefabGuid,
                    Weight = weights[variantIndex]
                };
            }

            node.ReceiveParameter("prefabVariants", JsonUtility.ToJson(variantSet));
            node.ReceiveParameter("footprintMode", footprintMode.ToString());
            node.ReceiveParameter("interiorLogicalId", interiorLogicalId.ToString(CultureInfo.InvariantCulture));
            node.ReceiveParameter("outlineLogicalId", outlineLogicalId.ToString(CultureInfo.InvariantCulture));
            node.ReceiveParameter("blendMode", blendMode.ToString());
            node.ReceiveParameter("mirrorX", mirrorX.ToString(CultureInfo.InvariantCulture));
            node.ReceiveParameter("mirrorY", mirrorY.ToString(CultureInfo.InvariantCulture));
            node.ReceiveParameter("allowRotation", allowRotation.ToString(CultureInfo.InvariantCulture));
            node.ReceiveParameter("maxOverlapTiles", maxOverlapTiles.ToString(CultureInfo.InvariantCulture));

            PrefabStampTemplate[] resolvedTemplates = new PrefabStampTemplate[variantCount];
            int[] resolvedTemplateIndices = new int[variantCount];
            float[] resolvedWeights = new float[variantCount];

            for (variantIndex = 0; variantIndex < variantCount; variantIndex++)
            {
                resolvedTemplates[variantIndex] = templates[variantIndex];
                resolvedTemplateIndices[variantIndex] = variantIndex;
                resolvedWeights[variantIndex] = weights[variantIndex];
            }

            SetPrivateField(node, "_resolvedTemplates", resolvedTemplates);
            SetPrivateField(node, "_resolvedTemplateIndices", resolvedTemplateIndices);
            SetPrivateField(node, "_resolvedTemplateWeights", resolvedWeights);
            SetPrivateField(node, "_prefabResolutionFailed", false);

            return node;
        }

        private static PrefabStampTemplate CreateTemplate(string prefabGuid, Vector2Int[] occupiedCells)
        {
            PrefabStampTemplate template = new PrefabStampTemplate();
            template.PrefabGuid = prefabGuid;
            template.AnchorOffset = Vector3.zero;
            template.SupportsRandomRotation = true;
            template.UsesTilemapFootprint = false;
            template.OccupiedCells = occupiedCells;
            return template;
        }

        private static PrefabPlacementRecord[] GetPlacementData(WorldSnapshot snapshot)
        {
            if (snapshot.PrefabPlacementChannels == null)
            {
                return Array.Empty<PrefabPlacementRecord>();
            }

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

        private static int[] GetIntChannelData(WorldSnapshot snapshot, string channelName)
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

        private static byte[] GetBoolMaskChannelData(WorldSnapshot snapshot, string channelName)
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

        private static int[] CreateFilledArray(int length, int value)
        {
            int[] values = new int[length];
            int index;
            for (index = 0; index < values.Length; index++)
            {
                values[index] = value;
            }

            return values;
        }

        private static int CountCellsWithValue(int[] data, int value)
        {
            int count = 0;

            int index;
            for (index = 0; index < data.Length; index++)
            {
                if (data[index] == value)
                {
                    count++;
                }
            }

            return count;
        }

        private static int ToIndex(int x, int y, int width)
        {
            return (y * width) + x;
        }

        private static Vector2Int TransformTemplateCell(IReadOnlyList<Vector2Int> occupiedCells, Vector2Int sourceCell, bool mirrorX, bool mirrorY, int quarterTurns)
        {
            Vector2Int transformed = TransformTemplateCellRaw(sourceCell, mirrorX, mirrorY, quarterTurns);
            if (occupiedCells == null || occupiedCells.Count == 0)
            {
                return transformed;
            }

            Vector2Int min = TransformTemplateCellRaw(occupiedCells[0], mirrorX, mirrorY, quarterTurns);

            int index;
            for (index = 1; index < occupiedCells.Count; index++)
            {
                Vector2Int candidate = TransformTemplateCellRaw(occupiedCells[index], mirrorX, mirrorY, quarterTurns);
                min = new Vector2Int(Mathf.Min(min.x, candidate.x), Mathf.Min(min.y, candidate.y));
            }

            return transformed - min;
        }

        private static Vector2Int TransformTemplateCellRaw(Vector2Int sourceCell, bool mirrorX, bool mirrorY, int quarterTurns)
        {
            int transformedX = mirrorX ? -sourceCell.x : sourceCell.x;
            int transformedY = mirrorY ? -sourceCell.y : sourceCell.y;

            if (quarterTurns == 1)
            {
                return new Vector2Int(-transformedY, transformedX);
            }

            if (quarterTurns == 2)
            {
                return new Vector2Int(-transformedX, -transformedY);
            }

            if (quarterTurns == 3)
            {
                return new Vector2Int(transformedY, -transformedX);
            }

            return new Vector2Int(transformedX, transformedY);
        }

        private static string SerialisePoints(IReadOnlyList<int2> points)
        {
            if (points == null || points.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();

            int index;
            for (index = 0; index < points.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(';');
                }

                builder.Append(points[index].x.ToString(CultureInfo.InvariantCulture));
                builder.Append(',');
                builder.Append(points[index].y.ToString(CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        private static void EnsureTempFolderExists()
        {
            if (!AssetDatabase.IsValidFolder("Assets/DynamicDungeon"))
            {
                AssetDatabase.CreateFolder("Assets", "DynamicDungeon");
            }

            if (!AssetDatabase.IsValidFolder("Assets/DynamicDungeon/Tests"))
            {
                AssetDatabase.CreateFolder("Assets/DynamicDungeon", "Tests");
            }

            if (!AssetDatabase.IsValidFolder(TempAssetFolder))
            {
                AssetDatabase.CreateFolder("Assets/DynamicDungeon/Tests", "TempGenerated");
            }
        }

        private static void DeleteAssetIfExists(string assetPath)
        {
            if (!string.IsNullOrWhiteSpace(assetPath) && AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null)
            {
                AssetDatabase.DeleteAsset(assetPath);
                AssetDatabase.SaveAssets();
            }
        }

        private static void CreateChild(Transform parent, string name, Vector3 localPosition)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            child.transform.localPosition = localPosition;
        }

        private static Tilemap CreateTilemapChild(Transform parent, string name)
        {
            GameObject tilemapObject = new GameObject(name);
            tilemapObject.transform.SetParent(parent, false);
            Tilemap tilemap = tilemapObject.AddComponent<Tilemap>();
            tilemapObject.AddComponent<TilemapRenderer>();
            return tilemap;
        }

        private static void SetPrivateField<TTarget, TValue>(TTarget target, string fieldName, TValue value)
        {
            FieldInfo field = typeof(TTarget).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Missing field '" + fieldName + "' on " + typeof(TTarget).Name + ".");
            field.SetValue(target, value);
        }

        private static void DestroyImmediateIfNotNull(UnityEngine.Object unityObject)
        {
            if (unityObject != null)
            {
                UnityEngine.Object.DestroyImmediate(unityObject);
            }
        }
    }

    internal sealed class GraphCompilerPointListNode : IGenNode, IParameterReceiver
    {
        private const string DefaultNodeName = "Point List Source";

        private readonly string _nodeId;
        private readonly string _nodeName;
        private readonly string _outputChannelName;

        private readonly NodePortDefinition[] _ports;
        private readonly ChannelDeclaration[] _channelDeclarations;
        private int2[] _points;

        public IReadOnlyList<NodePortDefinition> Ports => _ports;
        public IReadOnlyList<ChannelDeclaration> ChannelDeclarations => _channelDeclarations;
        public IReadOnlyList<BlackboardKey> BlackboardDeclarations => Array.Empty<BlackboardKey>();
        public string NodeId => _nodeId;
        public string NodeName => _nodeName;

        public GraphCompilerPointListNode(string nodeId, string nodeName, string outputChannelName)
        {
            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _outputChannelName = outputChannelName;
            _points = Array.Empty<int2>();
            _ports = new[]
            {
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.PointList)
            };
            _channelDeclarations = new[]
            {
                new ChannelDeclaration(_outputChannelName, ChannelType.PointList, true)
            };
        }

        public void ReceiveParameter(string name, string value)
        {
            if (!string.Equals(name, "points", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _points = ParsePoints(value);
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            NativeList<int2> output = context.GetPointListChannel(_outputChannelName);
            output.Clear();

            if (output.Capacity < _points.Length)
            {
                output.Capacity = _points.Length;
            }

            NativeArray<int2> sourcePoints = new NativeArray<int2>(_points, Allocator.TempJob);
            GraphCompilerPointListFillJob fillJob = new GraphCompilerPointListFillJob
            {
                SourcePoints = sourcePoints,
                Output = output
            };

            JobHandle jobHandle = fillJob.Schedule(context.InputDependency);
            return sourcePoints.Dispose(jobHandle);
        }

        private static int2[] ParsePoints(string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return Array.Empty<int2>();
            }

            string[] pointParts = rawValue.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            int2[] points = new int2[pointParts.Length];

            int pointIndex;
            for (pointIndex = 0; pointIndex < pointParts.Length; pointIndex++)
            {
                string[] coordinateParts = pointParts[pointIndex].Split(',');
                if (coordinateParts.Length != 2)
                {
                    continue;
                }

                int x;
                int y;
                if (!int.TryParse(coordinateParts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out x) ||
                    !int.TryParse(coordinateParts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out y))
                {
                    continue;
                }

                points[pointIndex] = new int2(x, y);
            }

            return points;
        }

        private struct GraphCompilerPointListFillJob : IJob
        {
            [ReadOnly]
            public NativeArray<int2> SourcePoints;

            public NativeList<int2> Output;

            public void Execute()
            {
                int index;
                for (index = 0; index < SourcePoints.Length; index++)
                {
                    Output.Add(SourcePoints[index]);
                }
            }
        }
    }

    internal sealed class StampTestIntPatternNode : IGenNode
    {
        private readonly string _nodeId;
        private readonly string _nodeName;
        private readonly string _outputChannelName;
        private readonly int[] _values;

        private readonly NodePortDefinition[] _ports;
        private readonly ChannelDeclaration[] _channelDeclarations;

        public IReadOnlyList<NodePortDefinition> Ports => _ports;
        public IReadOnlyList<ChannelDeclaration> ChannelDeclarations => _channelDeclarations;
        public IReadOnlyList<BlackboardKey> BlackboardDeclarations => Array.Empty<BlackboardKey>();
        public string NodeId => _nodeId;
        public string NodeName => _nodeName;

        public StampTestIntPatternNode(string nodeId, string nodeName, string outputChannelName, int[] values)
        {
            _nodeId = nodeId;
            _nodeName = nodeName;
            _outputChannelName = outputChannelName;
            _values = values ?? Array.Empty<int>();
            _ports = new[]
            {
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.Int)
            };
            _channelDeclarations = new[]
            {
                new ChannelDeclaration(_outputChannelName, ChannelType.Int, true)
            };
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            NativeArray<int> output = context.GetIntChannel(_outputChannelName);
            NativeArray<int> sourceValues = new NativeArray<int>(_values, Allocator.TempJob);
            StampTestIntPatternFillJob job = new StampTestIntPatternFillJob
            {
                SourceValues = sourceValues,
                Output = output
            };

            JobHandle jobHandle = job.Schedule(context.InputDependency);
            return sourceValues.Dispose(jobHandle);
        }

        private struct StampTestIntPatternFillJob : IJob
        {
            [ReadOnly]
            public NativeArray<int> SourceValues;

            public NativeArray<int> Output;

            public void Execute()
            {
                int index;
                for (index = 0; index < Output.Length && index < SourceValues.Length; index++)
                {
                    Output[index] = SourceValues[index];
                }
            }
        }
    }
}
