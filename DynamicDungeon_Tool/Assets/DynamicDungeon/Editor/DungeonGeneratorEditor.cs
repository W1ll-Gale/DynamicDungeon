using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(TilemapGenerator))]
public class DungeonGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector(); 

        TilemapGenerator generator = (TilemapGenerator)target;

        GUILayout.Space(10);

        if (GUILayout.Button("Generate Map", GUILayout.Height(40)))
        {
            if (generator.tilemap != null)
                Undo.RecordObject(generator.tilemap, "Generate Map");
            generator.GenerateTilemap();
        }

        if (GUILayout.Button("Clear Map") && generator.tilemap != null)
        {
            Undo.RecordObject(generator.tilemap, "Clear Map");
            generator.tilemap.ClearAllTiles();
        }
    }
}