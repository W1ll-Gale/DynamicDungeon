using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(TilemapGenerator))]
public class DungeonGeneratorEditor : Editor
{
    private Texture2D _previewTexture;
    private bool _showRegionPreview = true;

    private Editor _regionSettingsEditor;
    private Editor _generationProfileEditor;
    private bool _showRegionSettings = true;
    private bool _showProfileSettings = false;

    private void OnEnable()
    {
        TilemapGenerator generator = (TilemapGenerator)target;
        UpdateEditors(generator);
    }

    private void OnDisable()
    {
        if (_regionSettingsEditor != null) DestroyImmediate(_regionSettingsEditor);
        if (_generationProfileEditor != null) DestroyImmediate(_generationProfileEditor);
    }

    public override void OnInspectorGUI()
    {
        TilemapGenerator generator = (TilemapGenerator)target;

        serializedObject.Update();

        EditorGUILayout.PropertyField(serializedObject.FindProperty("tilemap"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("width"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("height"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("useBorderWalls"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("seed"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("useRandomSeed"));

        SerializedProperty regionProp = serializedObject.FindProperty("regionSettings");

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(regionProp);
        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();
            UpdateEditors(generator);
        }

        if (generator.regionSettings != null)
        {
            _showRegionSettings = EditorGUILayout.Foldout(_showRegionSettings, "Region Settings (Quick Edit)", true, EditorStyles.foldoutHeader);
            if (_showRegionSettings)
            {
                EditorGUI.indentLevel++;

                if (_regionSettingsEditor == null) UpdateEditors(generator);

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                _regionSettingsEditor.OnInspectorGUI();
                EditorGUILayout.EndVertical();

                EditorGUI.indentLevel--;
            }
        }

        SerializedProperty profileProp = serializedObject.FindProperty("generationProfile");

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(profileProp);
        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();
            UpdateEditors(generator);
        }

        if (generator.generationProfile != null)
        {
            _showProfileSettings = EditorGUILayout.Foldout(_showProfileSettings, "Generation Profile (Quick Edit)", true, EditorStyles.foldoutHeader);
            if (_showProfileSettings)
            {
                EditorGUI.indentLevel++;
                if (_generationProfileEditor == null) UpdateEditors(generator);

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                _generationProfileEditor.OnInspectorGUI();
                EditorGUILayout.EndVertical();
                EditorGUI.indentLevel--;
            }
        }

        serializedObject.ApplyModifiedProperties();

        GUILayout.Space(15);
        GUILayout.Label("Development Tools", EditorStyles.boldLabel);

        _showRegionPreview = EditorGUILayout.Foldout(_showRegionPreview, "Region Map Preview", true);
        if (_showRegionPreview && generator.CurrentRegionMap != null && generator.regionSettings != null)
        {
            if (_previewTexture == null || _previewTexture.width != generator.width || _previewTexture.height != generator.height)
            {
                UpdatePreview(generator);
            }

            Rect rect = GUILayoutUtility.GetRect(200, 200, GUILayout.ExpandWidth(true));
            EditorGUI.DrawPreviewTexture(rect, _previewTexture, null, ScaleMode.ScaleToFit);

            if (GUILayout.Button("Refresh Preview Only"))
            {
                generator.GenerateTilemap();
                UpdatePreview(generator);
            }
        }

        GUILayout.Space(10);

        bool hasGenerated = generator.CurrentMapData != null;
        string generateLabel = hasGenerated ? "Regenerate Map" : "Generate Map";

        if (GUILayout.Button(generateLabel, GUILayout.Height(40)))
        {
            if (generator.tilemap != null) Undo.RecordObject(generator.tilemap, generateLabel);
            generator.GenerateTilemap();
            UpdatePreview(generator);
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
                _previewTexture = null;
                Repaint();
            }
        }
    }

    private void UpdateEditors(TilemapGenerator generator)
    {
        if (generator.regionSettings != null)
        {
            if (_regionSettingsEditor != null && _regionSettingsEditor.target != generator.regionSettings)
            {
                DestroyImmediate(_regionSettingsEditor);
                _regionSettingsEditor = null;
            }

            if (_regionSettingsEditor == null)
            {
                _regionSettingsEditor = CreateEditor(generator.regionSettings);
            }
        }

        if (generator.generationProfile != null)
        {
            if (_generationProfileEditor != null && _generationProfileEditor.target != generator.generationProfile)
            {
                DestroyImmediate(_generationProfileEditor);
                _generationProfileEditor = null;
            }

            if (_generationProfileEditor == null)
            {
                _generationProfileEditor = CreateEditor(generator.generationProfile);
            }
        }
    }

    private void UpdatePreview(TilemapGenerator generator)
    {
        if (generator.CurrentRegionMap == null) return;

        int w = generator.width;
        int h = generator.height;

        if (_previewTexture == null || _previewTexture.width != w || _previewTexture.height != h)
        {
            _previewTexture = new Texture2D(w, h);
            _previewTexture.filterMode = FilterMode.Point;
            _previewTexture.wrapMode = TextureWrapMode.Clamp;
        }

        Color[] pixels = new Color[w * h];

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                int biomeIdx = generator.CurrentRegionMap[x, y];
                if (generator.regionSettings != null && biomeIdx < generator.regionSettings.biomes.Count)
                {
                    pixels[y * w + x] = generator.regionSettings.biomes[biomeIdx].biome.debugColor;
                }
                else
                {
                    pixels[y * w + x] = Color.black;
                }
            }
        }

        _previewTexture.SetPixels(pixels);
        _previewTexture.Apply();
    }
}