using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GenGraph))]
public sealed class GenGraphEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(serializedObject.FindProperty("_defaultWidth"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("_defaultHeight"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("_defaultSeed"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("_randomizeSeedByDefault"));

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Graph Layers", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("_layers"), true);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("+ Float")) AddLayer(PortDataKind.FloatLayer);
            if (GUILayout.Button("+ Int")) AddLayer(PortDataKind.IntLayer);
            if (GUILayout.Button("+ Mask")) AddLayer(PortDataKind.BoolMask);
        }

        EditorGUILayout.Space(8f);
        if (GUILayout.Button("Ensure Starter Nodes"))
            GraphAuthoringUtility.EnsureBootstrapGraph((GenGraph)target);

        serializedObject.ApplyModifiedProperties();
    }

    private void AddLayer(PortDataKind kind)
    {
        GenGraph graph = (GenGraph)target;
        graph.AddLayer($"New {kind}", kind);
        EditorUtility.SetDirty(graph);
        AssetDatabase.SaveAssets();
        serializedObject.Update();
    }
}
