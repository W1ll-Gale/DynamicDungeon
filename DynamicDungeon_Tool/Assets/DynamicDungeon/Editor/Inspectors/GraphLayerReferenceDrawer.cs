using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(GraphLayerReference))]
public sealed class GraphLayerReferenceDrawer : PropertyDrawer
{
    private const float AddButtonWidth = 26f;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        SerializedProperty idProperty = property.FindPropertyRelative("_layerId");
        GraphLayerReferenceAttribute layerAttribute = GetLayerAttribute();
        bool allowNone = layerAttribute == null || layerAttribute.AllowNone;
        PortDataKind expectedKind = layerAttribute != null ? layerAttribute.ExpectedKind : PortDataKind.FloatLayer;

        GenGraph graph = ResolveGraph(property);
        if (graph == null)
        {
            EditorGUI.BeginDisabledGroup(true);
            EditorGUI.TextField(position, label.text, "Assign a graph first");
            EditorGUI.EndDisabledGroup();
            return;
        }

        Rect popupRect = new Rect(position.x, position.y, position.width - AddButtonWidth - 4f, position.height);
        Rect addRect = new Rect(popupRect.xMax + 4f, position.y, AddButtonWidth, position.height);

        List<GraphLayerDefinition> layers = graph.GetLayersOfKind(expectedKind);
        List<string> optionLabels = new List<string>();
        List<string> optionIds = new List<string>();

        if (allowNone)
        {
            optionLabels.Add("<None>");
            optionIds.Add(string.Empty);
        }

        foreach (GraphLayerDefinition layer in layers)
        {
            optionLabels.Add(layer.DisplayName);
            optionIds.Add(layer.LayerId);
        }

        if (!allowNone && optionIds.Count == 0)
        {
            GraphLayerDefinition created = graph.AddLayer(GetDefaultLayerName(expectedKind), expectedKind);
            EditorUtility.SetDirty(graph);
            AssetDatabase.SaveAssets();
            optionLabels.Add(created.DisplayName);
            optionIds.Add(created.LayerId);
        }

        int currentIndex = 0;
        bool foundCurrent = false;
        for (int index = 0; index < optionIds.Count; index++)
        {
            if (optionIds[index] == idProperty.stringValue)
            {
                currentIndex = index;
                foundCurrent = true;
                break;
            }
        }

        if (!allowNone && optionIds.Count > 0 && !foundCurrent)
        {
            currentIndex = 0;
            idProperty.stringValue = optionIds[0];
        }

        EditorGUI.BeginProperty(position, label, property);
        int nextIndex = EditorGUI.Popup(popupRect, label.text, currentIndex, optionLabels.ToArray());
        if (nextIndex >= 0 && nextIndex < optionIds.Count)
            idProperty.stringValue = optionIds[nextIndex];

        if (GUI.Button(addRect, "+"))
        {
            GraphLayerDefinition created = graph.AddLayer(GetDefaultLayerName(expectedKind), expectedKind);
            idProperty.stringValue = created.LayerId;
            EditorUtility.SetDirty(graph);
            AssetDatabase.SaveAssets();
        }
        EditorGUI.EndProperty();
    }

    private static string GetDefaultLayerName(PortDataKind kind)
    {
        switch (kind)
        {
            case PortDataKind.FloatLayer: return "New Float Layer";
            case PortDataKind.IntLayer: return "New Int Layer";
            case PortDataKind.BoolMask: return "New Mask Layer";
            case PortDataKind.MarkerSet: return "New Marker Set";
            default: return $"New {kind}";
        }
    }

    private static GenGraph ResolveGraph(SerializedProperty property)
    {
        Object target = property.serializedObject.targetObject;
        if (target is GenGraph directGraph)
            return directGraph;

        if (target is DungeonGeneratorComponent)
        {
            SerializedProperty graphProperty = property.serializedObject.FindProperty("_graph");
            return graphProperty != null ? graphProperty.objectReferenceValue as GenGraph : null;
        }

        string path = AssetDatabase.GetAssetPath(target);
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return AssetDatabase.LoadMainAssetAtPath(path) as GenGraph;
    }

    private GraphLayerReferenceAttribute GetLayerAttribute()
    {
        if (fieldInfo == null)
            return null;

        return fieldInfo.GetCustomAttribute<GraphLayerReferenceAttribute>();
    }
}
