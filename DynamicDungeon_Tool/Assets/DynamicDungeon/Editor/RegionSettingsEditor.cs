using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

[CustomEditor(typeof(RegionSettings))]
public class RegionSettingsEditor : Editor
{
    private SerializedProperty _biomesProp;
    private SerializedProperty _algorithmProp;

    private SerializedProperty _voronoiNumSites;
    private SerializedProperty _perlinScale;

    private SerializedProperty _octaves;
    private SerializedProperty _persistence;
    private SerializedProperty _lacunarity;

    private Dictionary<string, Editor> _biomeEditors = new Dictionary<string, Editor>();

    private void OnEnable()
    {
        _biomesProp = serializedObject.FindProperty("biomes");
        _algorithmProp = serializedObject.FindProperty("algorithm");

        _voronoiNumSites = serializedObject.FindProperty("voronoiNumSites");
        _perlinScale = serializedObject.FindProperty("perlinScale");

        _octaves = serializedObject.FindProperty("octaves");
        _persistence = serializedObject.FindProperty("persistence");
        _lacunarity = serializedObject.FindProperty("lacunarity");
    }

    private void OnDisable()
    {
        foreach (Editor editor in _biomeEditors.Values)
        {
            if (editor != null) DestroyImmediate(editor);
        }
        _biomeEditors.Clear();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(_algorithmProp);
        RegionAlgorithm algo = (RegionAlgorithm)_algorithmProp.enumValueIndex;

        EditorGUILayout.Space(5);
        if (algo == RegionAlgorithm.Voronoi)
        {
            EditorGUILayout.LabelField("Voronoi Configuration", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_voronoiNumSites);
        }
        else if (algo == RegionAlgorithm.PerlinNoise)
        {
            EditorGUILayout.LabelField("Perlin Configuration", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_perlinScale);

            EditorGUILayout.PropertyField(_octaves);
            EditorGUILayout.PropertyField(_persistence);
            EditorGUILayout.PropertyField(_lacunarity);
        }

        GUILayout.Space(10);
        EditorGUILayout.LabelField("Biome Palette Configuration", EditorStyles.boldLabel);

        DrawBiomesList();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawBiomesList()
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label($"Biomes Defined: {_biomesProp.arraySize}", EditorStyles.miniLabel);
        if (GUILayout.Button("+ Add Biome", GUILayout.Width(100)))
        {
            _biomesProp.InsertArrayElementAtIndex(_biomesProp.arraySize);
            SerializedProperty newItem = _biomesProp.GetArrayElementAtIndex(_biomesProp.arraySize - 1);
            newItem.FindPropertyRelative("weight").intValue = 10;
        }
        EditorGUILayout.EndHorizontal();

        for (int i = 0; i < _biomesProp.arraySize; i++)
        {
            SerializedProperty element = _biomesProp.GetArrayElementAtIndex(i);
            SerializedProperty biomeRef = element.FindPropertyRelative("biome");
            SerializedProperty weight = element.FindPropertyRelative("weight");

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Weight:", GUILayout.Width(50));
            int w = EditorGUILayout.IntField(weight.intValue, GUILayout.Width(50));
            if (w < 1) w = 1;
            weight.intValue = w;
            EditorGUILayout.PropertyField(biomeRef, GUIContent.none);
            if (GUILayout.Button("X", GUILayout.Width(25)))
            {
                _biomesProp.DeleteArrayElementAtIndex(i);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                break;
            }
            EditorGUILayout.EndHorizontal();

            if (biomeRef.objectReferenceValue != null)
            {
                BiomeData biomeData = (BiomeData)biomeRef.objectReferenceValue;
                element.isExpanded = EditorGUILayout.Foldout(element.isExpanded, $"Edit {biomeData.name} Details", true);
                if (element.isExpanded)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    Editor cachedEditor = null;
                    string key = biomeData.GetInstanceID().ToString();
                    if (!_biomeEditors.TryGetValue(key, out cachedEditor))
                    {
                        cachedEditor = CreateEditor(biomeData);
                        _biomeEditors[key] = cachedEditor;
                    }
                    if (cachedEditor != null && cachedEditor.target != null) cachedEditor.OnInspectorGUI();
                    else _biomeEditors.Remove(key);
                    EditorGUILayout.EndVertical();
                    EditorGUI.indentLevel--;
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Assign a BiomeData asset to configure it.", MessageType.Info);
            }
            EditorGUILayout.EndVertical();
            GUILayout.Space(4);
        }
    }
}