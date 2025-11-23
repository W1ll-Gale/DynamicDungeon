using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(DungeonGenerator))]
public class DungeonGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector(); // Draws the normal Width/Height/Tile fields

        DungeonGenerator generator = (DungeonGenerator)target;

        GUILayout.Space(10);

        // The Big "Generate" Button
        if (GUILayout.Button("Generate Map (P1 Test)", GUILayout.Height(40)))
        {
            generator.GenerateEmptyMap(generator.width, generator.height);
        }

        if (GUILayout.Button("Clear Map"))
        {
            if (generator.tilemap != null) generator.tilemap.ClearAllTiles();
        }
    }
}