using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(DungeonGenerator))]
public class DungeonGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector(); 

        DungeonGenerator generator = (DungeonGenerator)target;

        GUILayout.Space(10);

        if (GUILayout.Button("Generate Map", GUILayout.Height(40)))
        {
            generator.GenerateEmptyMap(generator.width, generator.height);
        }

        if (GUILayout.Button("Clear Map") && generator.tilemap != null)
        {
            Undo.RecordObject(generator.tilemap, "Clear Map");
            generator.tilemap.ClearAllTiles();
        }
    }
}