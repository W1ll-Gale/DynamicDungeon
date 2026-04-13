using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DynamicDungeon.Runtime.Biome;
using DynamicDungeon.Runtime.Component;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using DynamicDungeon.Runtime.Nodes;
using DynamicDungeon.Runtime.Semantic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace DynamicDungeon.Editor.Utilities
{
    public static class VerificationUtility
    {
        private const string VerificationRootFolder = "Assets/DynamicDungeon/Verification";
        private const string TilesFolder = VerificationRootFolder + "/Tiles";
        private const string GraphsFolder = VerificationRootFolder + "/Graphs";
        private const string LayersFolder = VerificationRootFolder + "/Layers";
        private const string BiomesFolder = VerificationRootFolder + "/Biomes";
        private const string BakesFolder = VerificationRootFolder + "/Bakes";
        private const string TexturesFolder = VerificationRootFolder + "/Textures";
        private const string ScenesFolder = VerificationRootFolder + "/Scenes";

        private const string ValidGraphAssetPath = GraphsFolder + "/Verification_ValidGraph.asset";
        private const string CompileErrorGraphAssetPath = GraphsFolder + "/Verification_CompileErrorGraph.asset";
        private const string BiomeAssetPath = BiomesFolder + "/Verification_Biome.asset";
        private const string FloorTileAssetPath = TilesFolder + "/Verification_FloorTile.asset";
        private const string WallTileAssetPath = TilesFolder + "/Verification_WallTile.asset";
        private const string FloorTextureAssetPath = TexturesFolder + "/Verification_FloorTile.png";
        private const string WallTextureAssetPath = TexturesFolder + "/Verification_WallTile.png";
        private const string DefaultLayerAssetPath = LayersFolder + "/Verification_DefaultLayer.asset";
        private const string FloorLayerAssetPath = LayersFolder + "/Verification_FloorLayer.asset";
        private const string SolidLayerAssetPath = LayersFolder + "/Verification_SolidLayer.asset";
        private const string MismatchedBakeAssetPath = BakesFolder + "/Verification_MismatchedBakedSnapshot.asset";
        private const string DemoSceneAssetPath = ScenesFolder + "/VerificationDemo.unity";
        private const string GeneratorObjectName = "Verification Generator";
        private const string CameraObjectName = "Main Camera";

        [MenuItem("DynamicDungeon/Verification/Create Verification Assets")]
        public static void CreateVerificationAssets()
        {
            EnsureVerificationFolders();

            Tile floorTile = CreateOrUpdateTile(
                FloorTileAssetPath,
                FloorTextureAssetPath,
                new Color(0.40f, 0.85f, 0.40f, 1.0f));

            Tile wallTile = CreateOrUpdateTile(
                WallTileAssetPath,
                WallTextureAssetPath,
                new Color(0.45f, 0.45f, 0.45f, 1.0f));

            EnsureTileSemanticRegistry();
            CreateOrUpdateBiome(floorTile, wallTile);
            CreateOrUpdateLayerDefinitions();
            CreateOrUpdateValidGraph();
            CreateOrUpdateCompileErrorGraph();
            CreateOrUpdateMismatchedBake();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog(
                "Verification",
                "Verification assets are ready under Assets/DynamicDungeon/Verification.",
                "OK");
        }

        [MenuItem("DynamicDungeon/Verification/Create Verification Generator In Scene")]
        public static void CreateVerificationGeneratorInScene()
        {
            CreateVerificationAssets();
            DungeonGeneratorComponent component = CreateOrConfigureGeneratorInCurrentScene();
            Selection.activeGameObject = component.gameObject;

            EditorUtility.DisplayDialog(
                "Verification",
                "The verification generator is ready in the current scene and selected in the Hierarchy.",
                "OK");
        }

        [MenuItem("DynamicDungeon/Verification/Create Verification Demo Scene")]
        public static void CreateVerificationDemoScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            CreateVerificationAssets();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ConfigureSceneCamera();

            DungeonGeneratorComponent component = CreateOrConfigureGeneratorInCurrentScene();
            Selection.activeGameObject = component.gameObject;

            EditorSceneManager.SaveScene(scene, DemoSceneAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "Verification",
                "The verification demo scene has been created at Assets/DynamicDungeon/Verification/Scenes/VerificationDemo.unity.",
                "OK");
        }

        [MenuItem("DynamicDungeon/Verification/Open Verification Demo Scene")]
        public static void OpenVerificationDemoScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            CreateVerificationAssets();

            if (!File.Exists(Path.Combine(Directory.GetCurrentDirectory(), DemoSceneAssetPath)))
            {
                CreateVerificationDemoScene();
                return;
            }

            EditorSceneManager.OpenScene(DemoSceneAssetPath, OpenSceneMode.Single);
        }

        [MenuItem("DynamicDungeon/Verification/Selected Generator/Assign Valid Graph")]
        public static void AssignValidGraphToSelectedGenerator()
        {
            CreateVerificationAssets();
            DungeonGeneratorComponent component = GetSelectedGeneratorComponent();
            if (component == null)
            {
                return;
            }

            SerializedObject serializedObject = new SerializedObject(component);
            serializedObject.FindProperty("_graph").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GenGraph>(ValidGraphAssetPath);
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            MarkGeneratorDirty(component);
        }

        [MenuItem("DynamicDungeon/Verification/Selected Generator/Assign Compile Error Graph")]
        public static void AssignCompileErrorGraphToSelectedGenerator()
        {
            CreateVerificationAssets();
            DungeonGeneratorComponent component = GetSelectedGeneratorComponent();
            if (component == null)
            {
                return;
            }

            SerializedObject serializedObject = new SerializedObject(component);
            serializedObject.FindProperty("_graph").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GenGraph>(CompileErrorGraphAssetPath);
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            MarkGeneratorDirty(component);
        }

        [MenuItem("DynamicDungeon/Verification/Selected Generator/Clear Graph Assignment")]
        public static void ClearGraphAssignmentOnSelectedGenerator()
        {
            DungeonGeneratorComponent component = GetSelectedGeneratorComponent();
            if (component == null)
            {
                return;
            }

            SerializedObject serializedObject = new SerializedObject(component);
            serializedObject.FindProperty("_graph").objectReferenceValue = null;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            MarkGeneratorDirty(component);
        }

        [MenuItem("DynamicDungeon/Verification/Selected Generator/Assign Mismatched Bake")]
        public static void AssignMismatchedBakeToSelectedGenerator()
        {
            CreateVerificationAssets();
            DungeonGeneratorComponent component = GetSelectedGeneratorComponent();
            if (component == null)
            {
                return;
            }

            SerializedObject serializedObject = new SerializedObject(component);
            serializedObject.FindProperty("_bakedWorldSnapshot").objectReferenceValue = AssetDatabase.LoadAssetAtPath<BakedWorldSnapshot>(MismatchedBakeAssetPath);
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            MarkGeneratorDirty(component);
        }

        [MenuItem("DynamicDungeon/Verification/Selected Generator/Clear Bake Assignment")]
        public static void ClearBakeAssignmentOnSelectedGenerator()
        {
            DungeonGeneratorComponent component = GetSelectedGeneratorComponent();
            if (component == null)
            {
                return;
            }

            component.ClearBake();
            MarkGeneratorDirty(component);
        }

        [MenuItem("DynamicDungeon/Verification/Selected Generator/Assign Valid Graph", true)]
        [MenuItem("DynamicDungeon/Verification/Selected Generator/Assign Compile Error Graph", true)]
        [MenuItem("DynamicDungeon/Verification/Selected Generator/Clear Graph Assignment", true)]
        [MenuItem("DynamicDungeon/Verification/Selected Generator/Assign Mismatched Bake", true)]
        [MenuItem("DynamicDungeon/Verification/Selected Generator/Clear Bake Assignment", true)]
        public static bool ValidateSelectedGeneratorMenu()
        {
            return Selection.activeGameObject != null &&
                Selection.activeGameObject.GetComponent<DungeonGeneratorComponent>() != null;
        }

        private static DungeonGeneratorComponent CreateOrConfigureGeneratorInCurrentScene()
        {
            GameObject generatorObject = GameObject.Find(GeneratorObjectName);
            if (generatorObject == null)
            {
                generatorObject = new GameObject(GeneratorObjectName);
            }

            Grid grid = generatorObject.GetComponent<Grid>();
            if (grid == null)
            {
                grid = generatorObject.AddComponent<Grid>();
            }

            DungeonGeneratorComponent component = generatorObject.GetComponent<DungeonGeneratorComponent>();
            if (component == null)
            {
                component = generatorObject.AddComponent<DungeonGeneratorComponent>();
            }

            GenGraph validGraph = AssetDatabase.LoadAssetAtPath<GenGraph>(ValidGraphAssetPath);
            BiomeAsset biome = AssetDatabase.LoadAssetAtPath<BiomeAsset>(BiomeAssetPath);
            TilemapLayerDefinition solidLayer = AssetDatabase.LoadAssetAtPath<TilemapLayerDefinition>(SolidLayerAssetPath);
            TilemapLayerDefinition floorLayer = AssetDatabase.LoadAssetAtPath<TilemapLayerDefinition>(FloorLayerAssetPath);
            TilemapLayerDefinition defaultLayer = AssetDatabase.LoadAssetAtPath<TilemapLayerDefinition>(DefaultLayerAssetPath);

            ConfigureGenerator(component, grid, validGraph, biome, solidLayer, floorLayer, defaultLayer, null);
            return component;
        }

        private static void ConfigureGenerator(
            DungeonGeneratorComponent component,
            Grid grid,
            GenGraph graph,
            BiomeAsset biome,
            TilemapLayerDefinition solidLayer,
            TilemapLayerDefinition floorLayer,
            TilemapLayerDefinition defaultLayer,
            BakedWorldSnapshot bakedSnapshot)
        {
            SerializedObject serializedObject = new SerializedObject(component);
            serializedObject.FindProperty("_generateOnStart").boolValue = false;
            serializedObject.FindProperty("_seedMode").enumValueIndex = 0;
            serializedObject.FindProperty("_stableSeed").longValue = 12345L;
            serializedObject.FindProperty("_worldWidth").intValue = 128;
            serializedObject.FindProperty("_worldHeight").intValue = 128;
            serializedObject.FindProperty("_graph").objectReferenceValue = graph;
            serializedObject.FindProperty("_grid").objectReferenceValue = grid;
            serializedObject.FindProperty("_biome").objectReferenceValue = biome;
            serializedObject.FindProperty("_tilemapOffset").vector3IntValue = Vector3Int.zero;
            serializedObject.FindProperty("_bakedWorldSnapshot").objectReferenceValue = bakedSnapshot;

            SerializedProperty layerDefinitionsProperty = serializedObject.FindProperty("_layerDefinitions");
            layerDefinitionsProperty.ClearArray();
            layerDefinitionsProperty.InsertArrayElementAtIndex(0);
            layerDefinitionsProperty.GetArrayElementAtIndex(0).objectReferenceValue = solidLayer;
            layerDefinitionsProperty.InsertArrayElementAtIndex(1);
            layerDefinitionsProperty.GetArrayElementAtIndex(1).objectReferenceValue = floorLayer;
            layerDefinitionsProperty.InsertArrayElementAtIndex(2);
            layerDefinitionsProperty.GetArrayElementAtIndex(2).objectReferenceValue = defaultLayer;

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            MarkGeneratorDirty(component);
        }

        private static void ConfigureSceneCamera()
        {
            GameObject cameraObject = GameObject.Find(CameraObjectName);
            if (cameraObject == null)
            {
                cameraObject = new GameObject(CameraObjectName);
            }

            Camera cameraComponent = cameraObject.GetComponent<Camera>();
            if (cameraComponent == null)
            {
                cameraComponent = cameraObject.AddComponent<Camera>();
            }

            AudioListener audioListener = cameraObject.GetComponent<AudioListener>();
            if (audioListener == null)
            {
                audioListener = cameraObject.AddComponent<AudioListener>();
            }

            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(64.0f, 64.0f, -10.0f);
            cameraObject.transform.rotation = Quaternion.identity;

            cameraComponent.orthographic = true;
            cameraComponent.orthographicSize = 72.0f;
            cameraComponent.clearFlags = CameraClearFlags.SolidColor;
            cameraComponent.backgroundColor = new Color(0.08f, 0.10f, 0.12f, 1.0f);
            cameraComponent.nearClipPlane = 0.01f;
            cameraComponent.farClipPlane = 100.0f;
        }

        private static void CreateOrUpdateBiome(Tile floorTile, Tile wallTile)
        {
            BiomeAsset biome = LoadOrCreateAsset<BiomeAsset>(BiomeAssetPath);
            biome.TileMappings.Clear();

            BiomeTileMapping floorMapping = new BiomeTileMapping();
            floorMapping.LogicalId = LogicalTileId.Floor;
            floorMapping.Tile = floorTile;
            biome.TileMappings.Add(floorMapping);

            BiomeTileMapping wallMapping = new BiomeTileMapping();
            wallMapping.LogicalId = LogicalTileId.Wall;
            wallMapping.Tile = wallTile;
            biome.TileMappings.Add(wallMapping);

            EditorUtility.SetDirty(biome);
        }

        private static void CreateOrUpdateCompileErrorGraph()
        {
            GenGraph graph = LoadOrCreateAsset<GenGraph>(CompileErrorGraphAssetPath);
            graph.SchemaVersion = GraphSchemaVersion.Current;
            graph.WorldWidth = 128;
            graph.WorldHeight = 128;
            graph.DefaultSeed = 12345L;
            graph.Nodes.Clear();
            graph.Connections.Clear();

            GenNodeData logicalIdNode = new GenNodeData("logical-id-node", typeof(BoolMaskToLogicalIdNode).FullName, "Mask To Logical IDs", Vector2.zero);
            logicalIdNode.Ports.Add(new GenPortData("Input", PortDirection.Input, ChannelType.BoolMask));
            logicalIdNode.Ports.Add(new GenPortData("LogicalIds", PortDirection.Output, ChannelType.Int));
            logicalIdNode.Parameters.Add(new SerializedParameter("trueLogicalId", ((ushort)LogicalTileId.Wall).ToString(CultureInfo.InvariantCulture)));
            logicalIdNode.Parameters.Add(new SerializedParameter("falseLogicalId", ((ushort)LogicalTileId.Floor).ToString(CultureInfo.InvariantCulture)));
            graph.Nodes.Add(logicalIdNode);

            GenNodeData outputNode = GraphOutputUtility.EnsureSingleOutputNode(graph, false);
            graph.Connections.Add(new GenConnectionData(logicalIdNode.NodeId, "LogicalIds", outputNode.NodeId, GraphOutputUtility.OutputInputPortName));

            EditorUtility.SetDirty(graph);
        }

        private static void CreateOrUpdateLayerDefinitions()
        {
            TilemapLayerDefinition solidLayer = LoadOrCreateAsset<TilemapLayerDefinition>(SolidLayerAssetPath);
            solidLayer.LayerName = "Solid";
            solidLayer.IsCatchAll = false;
            solidLayer.SortOrder = 0;
            solidLayer.RoutingTags.Clear();
            solidLayer.RoutingTags.Add("Solid");
            solidLayer.ComponentsToAdd.Clear();
            solidLayer.ComponentsToAdd.Add("UnityEngine.Tilemaps.TilemapCollider2D");
            EditorUtility.SetDirty(solidLayer);

            TilemapLayerDefinition floorLayer = LoadOrCreateAsset<TilemapLayerDefinition>(FloorLayerAssetPath);
            floorLayer.LayerName = "Floor";
            floorLayer.IsCatchAll = false;
            floorLayer.SortOrder = -1;
            floorLayer.RoutingTags.Clear();
            floorLayer.RoutingTags.Add("Walkable");
            floorLayer.ComponentsToAdd.Clear();
            EditorUtility.SetDirty(floorLayer);

            TilemapLayerDefinition defaultLayer = LoadOrCreateAsset<TilemapLayerDefinition>(DefaultLayerAssetPath);
            defaultLayer.LayerName = "Default";
            defaultLayer.IsCatchAll = true;
            defaultLayer.SortOrder = -2;
            defaultLayer.RoutingTags.Clear();
            defaultLayer.ComponentsToAdd.Clear();
            EditorUtility.SetDirty(defaultLayer);
        }

        private static void CreateOrUpdateMismatchedBake()
        {
            BakedWorldSnapshot bakedSnapshot = LoadOrCreateAsset<BakedWorldSnapshot>(MismatchedBakeAssetPath);
            bakedSnapshot.Width = 64;
            bakedSnapshot.Height = 64;
            bakedSnapshot.Seed = 12345L;
            bakedSnapshot.Timestamp = "2026-04-27T00:00:00.0000000Z";

            WorldSnapshot snapshot = new WorldSnapshot();
            snapshot.Width = 64;
            snapshot.Height = 64;
            snapshot.Seed = 12345;
            snapshot.IntChannels = new[]
            {
                new WorldSnapshot.IntChannelSnapshot
                {
                    Name = "LogicalIds",
                    Data = new int[64 * 64]
                }
            };

            bakedSnapshot.Snapshot = snapshot;
            bakedSnapshot.OutputChannelName = "LogicalIds";
            EditorUtility.SetDirty(bakedSnapshot);
        }

        private static void CreateOrUpdateValidGraph()
        {
            GenGraph graph = LoadOrCreateAsset<GenGraph>(ValidGraphAssetPath);
            graph.SchemaVersion = GraphSchemaVersion.Current;
            graph.WorldWidth = 128;
            graph.WorldHeight = 128;
            graph.DefaultSeed = 12345L;
            graph.Nodes.Clear();
            graph.Connections.Clear();

            GenNodeData perlinNode = new GenNodeData("perlin-node", typeof(PerlinNoiseNode).FullName, "Perlin", new Vector2(0.0f, 0.0f));
            perlinNode.Ports.Add(new GenPortData("Noise", PortDirection.Output, ChannelType.Float));
            perlinNode.Parameters.Add(new SerializedParameter("frequency", "0.08"));
            perlinNode.Parameters.Add(new SerializedParameter("amplitude", "1.0"));
            perlinNode.Parameters.Add(new SerializedParameter("offset", "0,0"));
            perlinNode.Parameters.Add(new SerializedParameter("octaves", "3"));
            graph.Nodes.Add(perlinNode);

            GenNodeData thresholdNode = new GenNodeData("threshold-node", typeof(ThresholdNode).FullName, "Threshold", new Vector2(250.0f, 0.0f));
            thresholdNode.Ports.Add(new GenPortData("Input", PortDirection.Input, ChannelType.Float));
            thresholdNode.Ports.Add(new GenPortData("Mask", PortDirection.Output, ChannelType.BoolMask));
            thresholdNode.Parameters.Add(new SerializedParameter("threshold", "0.5"));
            graph.Nodes.Add(thresholdNode);

            GenNodeData cellularNode = new GenNodeData("cellular-node", typeof(CellularAutomataNode).FullName, "Cellular", new Vector2(500.0f, 0.0f));
            cellularNode.Ports.Add(new GenPortData("Input", PortDirection.Input, ChannelType.BoolMask));
            cellularNode.Ports.Add(new GenPortData("SmoothedMask", PortDirection.Output, ChannelType.BoolMask));
            cellularNode.Parameters.Add(new SerializedParameter("birthRule", "3"));
            cellularNode.Parameters.Add(new SerializedParameter("survivalRule", "23456"));
            cellularNode.Parameters.Add(new SerializedParameter("iterations", "5"));
            cellularNode.Parameters.Add(new SerializedParameter("initialFillProbability", "0.45"));
            graph.Nodes.Add(cellularNode);

            GenNodeData logicalIdNode = new GenNodeData("logical-id-node", typeof(BoolMaskToLogicalIdNode).FullName, "Mask To Logical IDs", new Vector2(750.0f, 0.0f));
            logicalIdNode.Ports.Add(new GenPortData("Input", PortDirection.Input, ChannelType.BoolMask));
            logicalIdNode.Ports.Add(new GenPortData("LogicalIds", PortDirection.Output, ChannelType.Int));
            logicalIdNode.Parameters.Add(new SerializedParameter("trueLogicalId", ((ushort)LogicalTileId.Wall).ToString(CultureInfo.InvariantCulture)));
            logicalIdNode.Parameters.Add(new SerializedParameter("falseLogicalId", ((ushort)LogicalTileId.Floor).ToString(CultureInfo.InvariantCulture)));
            graph.Nodes.Add(logicalIdNode);

            GenNodeData outputNode = GraphOutputUtility.EnsureSingleOutputNode(graph, false);

            graph.Connections.Add(new GenConnectionData("perlin-node", "Noise", "threshold-node", "Input"));
            graph.Connections.Add(new GenConnectionData("threshold-node", "Mask", "cellular-node", "Input"));
            graph.Connections.Add(new GenConnectionData("cellular-node", "SmoothedMask", "logical-id-node", "Input"));
            graph.Connections.Add(new GenConnectionData("logical-id-node", "LogicalIds", outputNode.NodeId, GraphOutputUtility.OutputInputPortName));

            EditorUtility.SetDirty(graph);
        }

        private static Tile CreateOrUpdateTile(string tileAssetPath, string textureAssetPath, Color colour)
        {
            Sprite sprite = CreateOrUpdateSprite(textureAssetPath, colour);
            Tile tile = LoadOrCreateAsset<Tile>(tileAssetPath);
            tile.sprite = sprite;
            tile.color = Color.white;
            EditorUtility.SetDirty(tile);
            return tile;
        }

        private static Sprite CreateOrUpdateSprite(string textureAssetPath, Color colour)
        {
            Texture2D texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[16 * 16];

            int pixelIndex;
            for (pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex++)
            {
                pixels[pixelIndex] = colour;
            }

            texture.SetPixels(pixels);
            texture.Apply();

            byte[] pngBytes = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);

            string absoluteTexturePath = Path.Combine(Directory.GetCurrentDirectory(), textureAssetPath);
            File.WriteAllBytes(absoluteTexturePath, pngBytes);
            AssetDatabase.ImportAsset(textureAssetPath, ImportAssetOptions.ForceUpdate);

            TextureImporter textureImporter = AssetImporter.GetAtPath(textureAssetPath) as TextureImporter;
            if (textureImporter != null)
            {
                textureImporter.textureType = TextureImporterType.Sprite;
                textureImporter.spritePixelsPerUnit = 16.0f;
                textureImporter.filterMode = FilterMode.Point;
                textureImporter.mipmapEnabled = false;
                textureImporter.alphaIsTransparency = false;
                textureImporter.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(textureAssetPath);
        }

        private static void EnsureTileSemanticRegistry()
        {
            const string registryAssetPath = "Assets/DynamicDungeon/TileSemanticRegistry.asset";
            TileSemanticRegistry registry = LoadOrCreateAsset<TileSemanticRegistry>(registryAssetPath);
            registry.AllTags.Clear();
            registry.AllTags.Add("Walkable");
            registry.AllTags.Add("Solid");
            registry.Entries.Clear();

            TileEntry floorEntry = new TileEntry();
            floorEntry.LogicalId = LogicalTileId.Floor;
            floorEntry.DisplayName = "Floor";
            floorEntry.Tags.Add("Walkable");
            registry.Entries.Add(floorEntry);

            TileEntry wallEntry = new TileEntry();
            wallEntry.LogicalId = LogicalTileId.Wall;
            wallEntry.DisplayName = "Wall";
            wallEntry.Tags.Add("Solid");
            registry.Entries.Add(wallEntry);

            EditorUtility.SetDirty(registry);
        }

        private static void EnsureVerificationFolders()
        {
            EnsureFolder("Assets/DynamicDungeon", "Verification");
            EnsureFolder(VerificationRootFolder, "Tiles");
            EnsureFolder(VerificationRootFolder, "Graphs");
            EnsureFolder(VerificationRootFolder, "Layers");
            EnsureFolder(VerificationRootFolder, "Biomes");
            EnsureFolder(VerificationRootFolder, "Bakes");
            EnsureFolder(VerificationRootFolder, "Textures");
            EnsureFolder(VerificationRootFolder, "Scenes");
        }

        private static void EnsureFolder(string parentFolderPath, string childFolderName)
        {
            string folderPath = parentFolderPath + "/" + childFolderName;
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                AssetDatabase.CreateFolder(parentFolderPath, childFolderName);
            }
        }

        private static DungeonGeneratorComponent GetSelectedGeneratorComponent()
        {
            GameObject selectedObject = Selection.activeGameObject;
            if (selectedObject == null)
            {
                EditorUtility.DisplayDialog("Verification", "Select the verification generator in the Hierarchy first.", "OK");
                return null;
            }

            DungeonGeneratorComponent component = selectedObject.GetComponent<DungeonGeneratorComponent>();
            if (component == null)
            {
                EditorUtility.DisplayDialog("Verification", "The selected object does not have a DungeonGeneratorComponent.", "OK");
                return null;
            }

            return component;
        }

        private static T LoadOrCreateAsset<T>(string assetPath) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset != null)
            {
                return asset;
            }

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, assetPath);
            return asset;
        }

        private static void MarkGeneratorDirty(DungeonGeneratorComponent component)
        {
            EditorUtility.SetDirty(component);
            EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
        }
    }
}
