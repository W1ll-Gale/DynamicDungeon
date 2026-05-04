using UnityEngine;
using UnityEditor;
using UnityEngine.Tilemaps;
using DynamicDungeon.ConstraintDungeon;
using System.Collections.Generic;
using System.IO;

namespace DynamicDungeon.ConstraintDungeon.Editor
{
    public static class RoomTemplateUtility
    {
        [MenuItem(ConstraintDungeonMenuPaths.ValidateRoomPrefabs)]
        public static void ValidateAllRoomPrefabs()
        {
            int validCount = 0;
            int invalidCount = 0;

            foreach (RoomTemplateComponent room in LoadAllRoomTemplates())
            {
                RoomValidator.ValidationResult result = RoomValidator.Validate(room);
                if (result.isValid)
                {
                    validCount++;
                    continue;
                }

                invalidCount++;
                Debug.LogError($"[RoomTemplateUtility] '{room.name}' invalid: {result.message}", room);
            }

            Debug.Log($"[RoomTemplateUtility] Room validation complete. Valid: {validCount}, Invalid: {invalidCount}.");
        }

        [MenuItem(ConstraintDungeonMenuPaths.BakeRoomPrefabs)]
        public static void BakeAllRoomPrefabs()
        {
            int bakedCount = 0;
            foreach (RoomTemplateComponent room in LoadAllRoomTemplates())
            {
                room.Bake();
                EditorUtility.SetDirty(room);
                bakedCount++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[RoomTemplateUtility] Baked {bakedCount} room template prefab(s).");
        }

        private static RoomTemplateComponent[] LoadAllRoomTemplates()
        {
            List<RoomTemplateComponent> rooms = new List<RoomTemplateComponent>();
            string[] guids = AssetDatabase.FindAssets("t:Prefab");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    continue;
                }

                RoomTemplateComponent room = prefab.GetComponent<RoomTemplateComponent>();
                if (room != null)
                {
                    rooms.Add(room);
                }
            }

            return rooms.ToArray();
        }

        [MenuItem(ConstraintDungeonMenuPaths.NewRoomPrefab, false, 10)]
        public static void CreateNewRoomPrefab()
        {
            string path = EditorUtility.SaveFilePanelInProject("Create Room Prefab", "NewRoomPrefab", "prefab", "Select where to save the room prefab");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            GameObject root = new GameObject("RoomRoot");
            Grid grid = root.AddComponent<Grid>();
            grid.cellSize = Vector3.one;

            RoomTemplateComponent comp = root.AddComponent<RoomTemplateComponent>();
            comp.roomName = Path.GetFileNameWithoutExtension(path);

            string[] mapNames = { "Floor", "Walls", "Collidable", "Other 1", "Other 2", "Other 3" };
            int sortingOrder = 0;

            foreach (string mapName in mapNames)
            {
                GameObject mapObj = new GameObject(mapName);
                mapObj.transform.SetParent(root.transform);
                
                Tilemap tilemap = mapObj.AddComponent<Tilemap>();
                TilemapRenderer renderer = mapObj.AddComponent<TilemapRenderer>();
                
                renderer.sortingOrder = sortingOrder++;
                
                if (mapName == "Walls")
                {
                    mapObj.AddComponent<TilemapCollider2D>();
                    comp.wallMap = tilemap;
                }
                else if (mapName == "Floor")
                {
                    comp.floorMap = tilemap;
                }
            }

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);

            EditorGUIUtility.PingObject(prefab);
            Debug.Log($"[RoomTemplateUtility] Created new room prefab at {path} with RoomTemplateComponent linked.");
        }

        [MenuItem(ConstraintDungeonMenuPaths.DungeonGeneratorSetup, false, 10)]
        public static void CreateConstraintDungeonGenerator(MenuCommand menuCommand)
        {
            GameObject go = new GameObject("Constraint Dungeon Generator");
            DungeonGenerator gen = go.AddComponent<DungeonGenerator>();

            string[] guids = AssetDatabase.FindAssets("t:DungeonFlow");
            if (guids.Length > 0)
            {
                string flowPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                DungeonFlow flow = AssetDatabase.LoadAssetAtPath<DungeonFlow>(flowPath);
                gen.dungeonFlow = flow;
                Debug.Log($"[RoomTemplateUtility] Automatically linked '{flow.name}' to the new Constraint Dungeon Generator.");
            }

            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeObject = go;
        }
    }
}
