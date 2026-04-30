using DynamicDungeon.Runtime.Biome;
using DynamicDungeon.Runtime.Graph;
using DynamicDungeon.Runtime.Placement;
using DynamicDungeon.Runtime.Semantic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DynamicDungeon.Editor
{
    public static class DynamicDungeonAssetMenus
    {
        private const string MenuRoot = "Assets/Create/DynamicDungeon/";
        private const string RegistryAssetPath = "Assets/DynamicDungeon/TileSemanticRegistry.asset";

        [MenuItem(MenuRoot + "Generation Graph")]
        public static void CreateGenerationGraph()
        {
            DynamicDungeonEditorAssetUtility.CreateAssetInSelectedFolder<GenGraph>("GenerationGraph.asset");
        }

        [MenuItem(MenuRoot + "Biome")]
        public static void CreateBiome()
        {
            DynamicDungeonEditorAssetUtility.CreateAssetInSelectedFolder<BiomeAsset>("Biome.asset");
        }

        [MenuItem(MenuRoot + "Tile Semantic Registry")]
        public static void CreateTileSemanticRegistry()
        {
            DynamicDungeonEditorAssetUtility.EnsureFolderPath("Assets/DynamicDungeon");

            UnityEngine.Object existingAsset = AssetDatabase.LoadMainAssetAtPath(RegistryAssetPath);
            if (existingAsset != null)
            {
                TileSemanticRegistry existingRegistry = existingAsset as TileSemanticRegistry;
                if (existingRegistry == null)
                {
                    EditorUtility.DisplayDialog("Tile Semantic Registry", "Cannot create the registry because a different asset already exists at '" + RegistryAssetPath + "'.", "OK");
                    return;
                }

                Selection.activeObject = existingRegistry;
                EditorGUIUtility.PingObject(existingRegistry);
                return;
            }

            TileSemanticRegistry registry = ScriptableObject.CreateInstance<TileSemanticRegistry>();
            AssetDatabase.CreateAsset(registry, RegistryAssetPath);
            AssetDatabase.SaveAssets();
            ProjectWindowUtil.ShowCreatedAsset(registry);
        }

        [MenuItem(MenuRoot + "Tilemap Layer Definition")]
        public static void CreateTilemapLayerDefinition()
        {
            DynamicDungeonEditorAssetUtility.CreateAssetInSelectedFolder<TilemapLayerDefinition>("TilemapLayerDefinition.asset");
        }

        [MenuItem(MenuRoot + "Stampable Prefab")]
        public static void CreateStampablePrefab()
        {
            string folderPath = DynamicDungeonEditorAssetUtility.GetSelectedFolderPath();
            string prefabPath = AssetDatabase.GenerateUniqueAssetPath((folderPath + "/StampablePrefab.prefab").Replace("\\", "/"));

            GameObject root = new GameObject("StampablePrefab");
            try
            {
                PrefabStampAuthoring authoring = root.AddComponent<PrefabStampAuthoring>();

                GameObject gridObject = new GameObject("FootprintGrid");
                gridObject.transform.SetParent(root.transform, false);
                gridObject.AddComponent<Grid>();

                GameObject tilemapObject = new GameObject("FootprintTilemap");
                tilemapObject.transform.SetParent(gridObject.transform, false);
                Tilemap tilemap = tilemapObject.AddComponent<Tilemap>();
                tilemapObject.AddComponent<TilemapRenderer>();

                SerializedObject authoringObject = new SerializedObject(authoring);
                SerializedProperty footprintTilemapProperty = authoringObject.FindProperty("_footprintTilemap");
                if (footprintTilemapProperty != null)
                {
                    footprintTilemapProperty.objectReferenceValue = tilemap;
                    authoringObject.ApplyModifiedPropertiesWithoutUndo();
                }

                GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                AssetDatabase.SaveAssets();

                if (prefabAsset != null)
                {
                    ProjectWindowUtil.ShowCreatedAsset(prefabAsset);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
