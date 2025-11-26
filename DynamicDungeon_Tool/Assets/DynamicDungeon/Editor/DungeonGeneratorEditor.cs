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

        bool hasGenerated = generator.CurrentMapData != null && 
                            generator.CurrentMapData.GetLength(0) > 0 && 
                            generator.CurrentMapData.GetLength(1) > 0;

        string generateLabel = hasGenerated ? "Regenerate Map" : "Generate Map";

        if (GUILayout.Button(generateLabel, GUILayout.Height(40)))
        {
            if (generator.tilemap != null)
            {
                Undo.RecordObject(generator.tilemap, generateLabel);
            }

            generator.GenerateTilemap();
            Repaint();
        }

        using (new EditorGUI.DisabledScope(generator.tilemap == null))
        {
            if (GUILayout.Button("Clear Map"))
            {
                if (generator.tilemap != null)
                {
                    Undo.RecordObject(generator.tilemap, "Clear Map");
                    generator.tilemap.ClearAllTiles();
                }

                generator.ClearGeneratedMap();
                Repaint();
            }
        }
    }
}