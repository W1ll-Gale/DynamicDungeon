using System.Collections.Generic;
using DynamicDungeon.Runtime.Component;
using DynamicDungeon.Runtime.Output;
using DynamicDungeon.Runtime.Semantic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;
using DynamicDungeon.Editor.Windows;

namespace DynamicDungeon.Editor
{
    public static class WorldGeneratorSetup
    {
        private const string UndoOperationName = "New World Generator Setup";
        private const string ApplyLayerStructureUndoOperationName = "Apply Layer Structure";

        [MenuItem("DynamicDungeon/New World Generator Setup")]
        public static void CreateWorldGenerator()
        {
            CreateWorldGeneratorInternal(null);
        }

        [MenuItem("GameObject/DynamicDungeon/World Generator Setup", false, 10)]
        public static void CreateWorldGeneratorFromHierarchy(MenuCommand menuCommand)
        {
            CreateWorldGeneratorInternal(menuCommand);
        }

        [MenuItem("GameObject/DynamicDungeon/Apply Layer Structure", false, 11)]
        public static void ApplyLayerStructureFromHierarchy(MenuCommand menuCommand)
        {
            DungeonGeneratorComponent component = GetSelectedGenerator(menuCommand);
            if (component == null)
            {
                return;
            }

            ApplyLayerStructure(component);
        }

        [MenuItem("GameObject/DynamicDungeon/Apply Layer Structure", true)]
        public static bool ValidateApplyLayerStructureFromHierarchy(MenuCommand menuCommand)
        {
            return GetSelectedGenerator(menuCommand) != null;
        }

        [MenuItem("GameObject/DynamicDungeon/Open Generator Graph", false, 12)]
        public static void OpenGeneratorGraphFromHierarchy(MenuCommand menuCommand)
        {
            DungeonGeneratorComponent component = GetSelectedGenerator(menuCommand);
            if (component == null || component.Graph == null)
            {
                return;
            }

            DynamicDungeonEditorWindow.OpenGraph(component.Graph);
        }

        [MenuItem("GameObject/DynamicDungeon/Open Generator Graph", true)]
        public static bool ValidateOpenGeneratorGraphFromHierarchy(MenuCommand menuCommand)
        {
            DungeonGeneratorComponent component = GetSelectedGenerator(menuCommand);
            return component != null && component.Graph != null;
        }

        public static void ApplyLayerStructure(DungeonGeneratorComponent component)
        {
            if (component == null)
            {
                return;
            }

            SerializedObject componentObject = new SerializedObject(component);
            SerializedProperty gridProperty = componentObject.FindProperty("_grid");
            SerializedProperty layerDefinitionsProperty = componentObject.FindProperty("_layerDefinitions");
            Grid grid = ResolveGrid(component, componentObject, gridProperty);
            if (grid == null)
            {
                Debug.LogError("Apply Layer Structure failed: DungeonGeneratorComponent requires a Grid reference.", component);
                return;
            }

            List<TilemapLayerDefinition> layerDefinitions = GetAssignedLayerDefinitions(layerDefinitionsProperty);
            if (layerDefinitions.Count == 0)
            {
                Debug.LogError("Apply Layer Structure failed: DungeonGeneratorComponent requires at least one TilemapLayerDefinition.", component);
                return;
            }

            Undo.RecordObject(component, ApplyLayerStructureUndoOperationName);

            TilemapLayerWriter tilemapLayerWriter = new TilemapLayerWriter();
            tilemapLayerWriter.EnsureTimelapsCreated(grid, layerDefinitions);

            componentObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(component);
            EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
        }

        private static void CreateWorldGeneratorInternal(MenuCommand menuCommand)
        {
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoOperationName);

            List<TilemapLayerDefinition> layerDefinitions = DynamicDungeonBuiltInLayerDefaults.CreateOrLoadDefaultLayerAssets();
            IReadOnlyList<DynamicDungeonBuiltInLayerDefaults.LayerPreset> presets = DynamicDungeonBuiltInLayerDefaults.Presets;

            GameObject worldGeneratorObject = new GameObject("World Generator");
            GameObjectUtility.SetParentAndAlign(worldGeneratorObject, menuCommand != null ? menuCommand.context as GameObject : null);
            DungeonGeneratorComponent component = worldGeneratorObject.AddComponent<DungeonGeneratorComponent>();

            GameObject gridObject = new GameObject("Grid");
            gridObject.transform.SetParent(worldGeneratorObject.transform, false);
            Grid grid = gridObject.AddComponent<Grid>();

            List<GameObject> createdObjects = new List<GameObject>(presets.Count + 2);
            createdObjects.Add(worldGeneratorObject);
            createdObjects.Add(gridObject);

            int presetIndex;
            for (presetIndex = 0; presetIndex < presets.Count; presetIndex++)
            {
                DynamicDungeonBuiltInLayerDefaults.LayerPreset preset = presets[presetIndex];
                GameObject tilemapObject = CreateTilemapChild(gridObject.transform, preset);
                createdObjects.Add(tilemapObject);
            }

            AssignComponentReferences(component, grid, layerDefinitions);

            int objectIndex;
            for (objectIndex = 0; objectIndex < createdObjects.Count; objectIndex++)
            {
                Undo.RegisterCreatedObjectUndo(createdObjects[objectIndex], UndoOperationName);
            }

            Selection.activeGameObject = worldGeneratorObject;
            EditorSceneManager.MarkSceneDirty(worldGeneratorObject.scene);
            Undo.CollapseUndoOperations(undoGroup);
        }

        private static DungeonGeneratorComponent GetSelectedGenerator(MenuCommand menuCommand)
        {
            GameObject contextObject = menuCommand != null ? menuCommand.context as GameObject : null;
            if (contextObject == null)
            {
                contextObject = Selection.activeGameObject;
            }

            if (contextObject == null)
            {
                return null;
            }

            return contextObject.GetComponentInParent<DungeonGeneratorComponent>();
        }

        private static List<TilemapLayerDefinition> GetAssignedLayerDefinitions(SerializedProperty layerDefinitionsProperty)
        {
            List<TilemapLayerDefinition> layerDefinitions = new List<TilemapLayerDefinition>(layerDefinitionsProperty != null ? layerDefinitionsProperty.arraySize : 0);
            if (layerDefinitionsProperty == null)
            {
                return layerDefinitions;
            }

            int index;
            for (index = 0; index < layerDefinitionsProperty.arraySize; index++)
            {
                SerializedProperty elementProperty = layerDefinitionsProperty.GetArrayElementAtIndex(index);
                TilemapLayerDefinition layerDefinition = elementProperty.objectReferenceValue as TilemapLayerDefinition;
                if (layerDefinition != null)
                {
                    layerDefinitions.Add(layerDefinition);
                }
            }

            return layerDefinitions;
        }

        private static Grid ResolveGrid(DungeonGeneratorComponent component, SerializedObject componentObject, SerializedProperty gridProperty)
        {
            Grid grid = gridProperty != null ? gridProperty.objectReferenceValue as Grid : null;
            if (grid != null)
            {
                return grid;
            }

            grid = component.GetComponentInChildren<Grid>();
            if (grid == null || gridProperty == null)
            {
                return grid;
            }

            gridProperty.objectReferenceValue = grid;
            return grid;
        }

        private static void AssignComponentReferences(DungeonGeneratorComponent component, Grid grid, List<TilemapLayerDefinition> layerDefinitions)
        {
            SerializedObject componentObject = new SerializedObject(component);
            SerializedProperty gridProperty = componentObject.FindProperty("_grid");
            SerializedProperty layerDefinitionsProperty = componentObject.FindProperty("_layerDefinitions");

            gridProperty.objectReferenceValue = grid;
            layerDefinitionsProperty.arraySize = layerDefinitions.Count;

            int layerIndex;
            for (layerIndex = 0; layerIndex < layerDefinitions.Count; layerIndex++)
            {
                SerializedProperty layerElementProperty = layerDefinitionsProperty.GetArrayElementAtIndex(layerIndex);
                layerElementProperty.objectReferenceValue = layerDefinitions[layerIndex];
            }

            componentObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(component);
        }

        private static GameObject CreateTilemapChild(Transform gridTransform, DynamicDungeonBuiltInLayerDefaults.LayerPreset preset)
        {
            GameObject tilemapObject = new GameObject("Tilemap_" + preset.LayerName);
            tilemapObject.transform.SetParent(gridTransform, false);

            tilemapObject.AddComponent<Tilemap>();
            TilemapRenderer tilemapRenderer = tilemapObject.AddComponent<TilemapRenderer>();
            tilemapRenderer.sortingOrder = preset.SortOrder;

            ApplyDefaultComponents(tilemapObject, preset);

            return tilemapObject;
        }

        private static void ApplyDefaultComponents(GameObject tilemapObject, DynamicDungeonBuiltInLayerDefaults.LayerPreset preset)
        {
            TilemapCollider2D tilemapCollider2D = null;

            int componentIndex;
            for (componentIndex = 0; componentIndex < preset.ComponentTypeNames.Length; componentIndex++)
            {
                string componentTypeName = preset.ComponentTypeNames[componentIndex];
                if (componentTypeName == "UnityEngine.Tilemaps.TilemapCollider2D")
                {
                    tilemapCollider2D = GetOrAddComponent<TilemapCollider2D>(tilemapObject);
                    if (tilemapCollider2D != null)
                    {
                        tilemapCollider2D.isTrigger = preset.UseTriggerCollider;
                    }

                    continue;
                }

                if (componentTypeName == "UnityEngine.CompositeCollider2D")
                {
                    GetOrAddComponent<CompositeCollider2D>(tilemapObject);
                    if (tilemapCollider2D != null)
                    {
                        tilemapCollider2D.compositeOperation = Collider2D.CompositeOperation.Merge;
                    }

                    continue;
                }

                if (componentTypeName == "UnityEngine.Rigidbody2D")
                {
                    Rigidbody2D rigidbody2D = GetOrAddComponent<Rigidbody2D>(tilemapObject);
                    if (rigidbody2D != null)
                    {
                        rigidbody2D.bodyType = RigidbodyType2D.Static;
                    }
                }
            }
        }

        private static T GetOrAddComponent<T>(GameObject gameObject) where T : Component
        {
            T existingComponent = gameObject.GetComponent<T>();
            if (existingComponent != null)
            {
                return existingComponent;
            }

            return gameObject.AddComponent<T>();
        }
    }
}
