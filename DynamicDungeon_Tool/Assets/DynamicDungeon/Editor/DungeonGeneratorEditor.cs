using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.IO;

[CustomEditor(typeof(TilemapGenerator))]
public class DungeonGeneratorEditor : Editor
{
    private Texture2D _previewTexture;
    private bool _showRegionPreview = true;
    private bool _showPipeline = true;
    private bool _showRegionSettings = true;
    private bool _liveUpdate = true;

    private Editor _cachedRegionSettingsEditor;
    private RegionSettings _currentRegionSettings;

    public override void OnInspectorGUI()
    {
        TilemapGenerator generator = (TilemapGenerator)target;

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Dynamic Dungeon Generator", EditorStyles.boldLabel);
        _liveUpdate = EditorGUILayout.Toggle("Live Update", _liveUpdate);
        EditorGUILayout.EndHorizontal();

        EditorGUI.BeginChangeCheck();

        serializedObject.Update();

        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("Map Configuration", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("tilemap"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("width"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("height"));

        EditorGUILayout.Space(5);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("seed"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("useRandomSeed"));

        EditorGUILayout.Space(10);

        DrawPipelineGUI(generator);

        RegionPass regionPass = generator.generationPipeline.OfType<RegionPass>().FirstOrDefault();
        bool settingsChanged = false;

        if (regionPass != null && regionPass.regionSettings != null)
        {
            settingsChanged = DrawRegionSettingsQuickEdit(regionPass.regionSettings);
        }

        serializedObject.ApplyModifiedProperties();

        if ((EditorGUI.EndChangeCheck() || settingsChanged) && _liveUpdate)
        {
            if (generator.tilemap != null)
            {
                generator.GenerateTilemap(preserveSeed: true);
                _previewTexture = null;
            }
        }

        GUILayout.Space(15);
        DrawPreviewSection(generator, regionPass);

        GUILayout.Space(10);
        DrawActionButtons(generator);
    }

    private void DrawPipelineGUI(TilemapGenerator generator)
    {
        _showPipeline = EditorGUILayout.Foldout(_showPipeline, "Generation Pipeline", true, EditorStyles.foldoutHeader);
        if (_showPipeline)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            SerializedProperty listProp = serializedObject.FindProperty("generationPipeline");

            bool isOrderInvalid = false;
            if (generator.generationPipeline.Count > 0)
            {
                var rPass = generator.generationPipeline.OfType<RegionPass>().FirstOrDefault();
                if (rPass != null && generator.generationPipeline[0] != rPass) isOrderInvalid = true;
            }

            if (isOrderInvalid)
            {
                EditorGUILayout.HelpBox("Order Error: RegionPass must be first.", MessageType.Error);
                if (GUILayout.Button("Auto-Fix Order")) FixPipelineOrder(generator);
                GUILayout.Space(5);
            }
            else if (listProp.arraySize == 0)
            {
                EditorGUILayout.HelpBox("Pipeline is empty!", MessageType.Warning);
                if (GUILayout.Button("Create Default Pipeline")) CreateDefaultPipeline(generator);
            }

            for (int i = 0; i < listProp.arraySize; i++)
            {
                SerializedProperty element = listProp.GetArrayElementAtIndex(i);
                GenerationPass pass = element.objectReferenceValue as GenerationPass;

                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                if (pass != null)
                {
                    SerializedObject passSO = new SerializedObject(pass);
                    passSO.Update();
                    SerializedProperty enabledProp = passSO.FindProperty("enabled");

                    bool newVal = EditorGUILayout.Toggle(enabledProp.boolValue, GUILayout.Width(20));
                    if (newVal != enabledProp.boolValue)
                    {
                        enabledProp.boolValue = newVal;
                        passSO.ApplyModifiedProperties();
                    }
                }
                else
                {
                    GUILayout.Space(24);
                }

                GUILayout.Label($"{i + 1}.", GUILayout.Width(20));

                EditorGUILayout.PropertyField(element, GUIContent.none);

                GUIContent trashIcon = EditorGUIUtility.IconContent("TreeEditor.Trash");
                if (trashIcon == null) trashIcon = new GUIContent("X");

                if (GUILayout.Button(trashIcon, GUILayout.Width(30), GUILayout.Height(18)))
                {
                    listProp.DeleteArrayElementAtIndex(i);
                    break;
                }

                EditorGUILayout.EndHorizontal();
            }

            GUILayout.Space(5);
            if (GUILayout.Button("+ Add Pass Step", GUILayout.Height(25)))
            {
                listProp.InsertArrayElementAtIndex(listProp.arraySize);
            }

            EditorGUILayout.EndVertical();
        }
    }

    private void FixPipelineOrder(TilemapGenerator generator)
    {
        var sorted = generator.generationPipeline.OrderBy(x =>
        {
            if (x is RegionPass) return 0;
            if (x is NoiseFillPass) return 1;
            if (x is SmoothingPass) return 2;
            if (x is ResourcePass) return 3;
            return 4;
        }).ToList();

        generator.generationPipeline = sorted;
        EditorUtility.SetDirty(generator);
        Debug.Log("Pipeline reordered.");
    }

    private bool DrawRegionSettingsQuickEdit(RegionSettings settings)
    {
        if (_currentRegionSettings != settings)
        {
            _currentRegionSettings = settings;
            if (_cachedRegionSettingsEditor != null) DestroyImmediate(_cachedRegionSettingsEditor);
            _cachedRegionSettingsEditor = null;
        }

        if (_cachedRegionSettingsEditor == null)
            _cachedRegionSettingsEditor = CreateEditor(settings);

        _showRegionSettings = EditorGUILayout.Foldout(_showRegionSettings, "Region Settings (Quick Edit)", true, EditorStyles.foldoutHeader);
        if (_showRegionSettings)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUI.BeginChangeCheck();
            _cachedRegionSettingsEditor.OnInspectorGUI();
            bool changed = EditorGUI.EndChangeCheck();
            EditorGUILayout.EndVertical();
            EditorGUI.indentLevel--;
            return changed;
        }
        return false;
    }

    private void DrawPreviewSection(TilemapGenerator generator, RegionPass regionPass)
    {
        GUILayout.Label("Visualization", EditorStyles.boldLabel);
        _showRegionPreview = EditorGUILayout.Foldout(_showRegionPreview, "Region Map Preview", true);
        if (_showRegionPreview)
        {
            if (generator.CurrentRegionMap != null && regionPass != null)
            {
                if (_previewTexture == null || _previewTexture.width != generator.width || _previewTexture.height != generator.height)
                    UpdatePreviewTexture(generator, regionPass.regionSettings);

                Rect rect = GUILayoutUtility.GetRect(200, 200, GUILayout.ExpandWidth(true));
                if (_previewTexture != null) EditorGUI.DrawPreviewTexture(rect, _previewTexture, null, ScaleMode.ScaleToFit);
            }
            else
            {
                EditorGUILayout.HelpBox("Generate map to see preview.", MessageType.Info);
            }
        }
    }

    private void DrawActionButtons(TilemapGenerator generator)
    {
        bool hasGenerated = generator.CurrentMapData != null;
        string generateLabel = hasGenerated ? "Regenerate Map (New Seed)" : "Generate Map";

        if (GUILayout.Button(generateLabel, GUILayout.Height(40)))
        {
            if (generator.tilemap != null) Undo.RecordObject(generator.tilemap, generateLabel);
            generator.GenerateTilemap(preserveSeed: false);
            _previewTexture = null;
            Repaint();
        }

        using (new EditorGUI.DisabledScope(generator.tilemap == null))
        {
            if (GUILayout.Button("Clear Map"))
            {
                if (generator.tilemap != null) Undo.RecordObject(generator.tilemap, "Clear Map");
                generator.ClearGeneratedMap();
                _previewTexture = null;
                Repaint();
            }
        }
    }

    private void UpdatePreviewTexture(TilemapGenerator generator, RegionSettings settings)
    {
        if (generator.CurrentRegionMap == null || settings == null) return;
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
                if (biomeIdx < settings.biomes.Count)
                    pixels[y * w + x] = settings.biomes[biomeIdx].biome.debugColor;
                else
                    pixels[y * w + x] = Color.black;
            }
        }
        _previewTexture.SetPixels(pixels);
        _previewTexture.Apply();
    }

    private void CreateDefaultPipeline(TilemapGenerator generator)
    {
        string path = "Assets/DynamicDungeon/Profiles/Default";
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);

        RegionPass regionPass = CreateInstance<RegionPass>();
        regionPass.name = "Default_RegionPass";
        RegionSettings settings = CreateInstance<RegionSettings>();
        settings.name = "Default_RegionSettings";
        settings.algorithm = RegionAlgorithm.Voronoi;
        BiomeData defaultBiome = CreateInstance<BiomeData>();
        defaultBiome.name = "StoneBiome";
        defaultBiome.debugColor = Color.gray;
        defaultBiome.randomFillPercent = 45;
        settings.biomes = new List<WeightedBiome> { new WeightedBiome { biome = defaultBiome, weight = 10 } };
        regionPass.regionSettings = settings;

        NoiseFillPass terrainPass = CreateInstance<NoiseFillPass>();
        terrainPass.name = "Default_TerrainPass";
        terrainPass.useBorderWalls = true;

        SmoothingPass smoothingPass = CreateInstance<SmoothingPass>();
        smoothingPass.name = "Default_SmoothingPass";
        smoothingPass.maxGlobalIterations = 5;

        ResourcePass resourcePass = CreateInstance<ResourcePass>();
        resourcePass.name = "Default_ResourcePass";

        generator.generationPipeline = new List<GenerationPass>
        {
            regionPass,
            terrainPass,
            smoothingPass,
            resourcePass
        };
        Debug.Log("Created Default Pipeline in memory.");
    }

    private void OnDisable()
    {
        if (_cachedRegionSettingsEditor != null) DestroyImmediate(_cachedRegionSettingsEditor);
    }
}