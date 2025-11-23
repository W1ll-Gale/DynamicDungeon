using UnityEngine;
using UnityEditor;

namespace DynamicDungeon.Editor
{
    public static class DungeonMenu
    {
        [MenuItem("GameObject/Dynamic Dungeon/Create Generator", false, 10)]
        static void CreateGenerator(MenuCommand menuCommand)
        {
            GameObject gameObject = new GameObject("DungeonGenerator");

            if (gameObject.GetComponent<Grid>() == null) gameObject.AddComponent<Grid>();
            gameObject.AddComponent<DungeonGenerator>();

            GameObjectUtility.SetParentAndAlign(gameObject, menuCommand.context as GameObject);

            Undo.RegisterCreatedObjectUndo(gameObject, "Create Dungeon Generator");

            Selection.activeObject = gameObject;
        }
    }
}