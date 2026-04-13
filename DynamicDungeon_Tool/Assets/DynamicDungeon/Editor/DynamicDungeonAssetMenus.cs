using DynamicDungeon.Runtime.Biome;
using DynamicDungeon.Runtime.Graph;
using DynamicDungeon.Runtime.Semantic;
using UnityEditor;
using UnityEngine;

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
    }
}
