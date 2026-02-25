using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DungeonGeneratorComponent))]
public sealed class DungeonGeneratorComponentEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8f);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Generate"))
            {
                ((DungeonGeneratorComponent)target).Generate();
            }

            if (GUILayout.Button("Ping Graph"))
            {
                SerializedProperty graphProperty = serializedObject.FindProperty("_graph");
                if (graphProperty != null && graphProperty.objectReferenceValue != null)
                    EditorGUIUtility.PingObject(graphProperty.objectReferenceValue);
            }
        }
    }
}
