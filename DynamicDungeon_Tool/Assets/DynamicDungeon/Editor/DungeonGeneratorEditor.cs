using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(TilemapGenerator))]
public class DungeonGeneratorEditor : Editor
{
    private Texture2D _previewTexture;
    private bool _showRegionPreview = true;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        TilemapGenerator generator = (TilemapGenerator)target;

        GUILayout.Space(15);
        GUILayout.Label("Development Tools", EditorStyles.boldLabel);

        _showRegionPreview = EditorGUILayout.Foldout(_showRegionPreview, "Region Map Preview");
        if (_showRegionPreview && generator.CurrentRegionMap != null && generator.regionSettings != null)
        {
            if (_previewTexture == null || _previewTexture.width != generator.width || _previewTexture.height != generator.height)
            {
                UpdatePreview(generator);
            }

            Rect rect = GUILayoutUtility.GetRect(200, 200, GUILayout.ExpandWidth(true));
            EditorGUI.DrawPreviewTexture(rect, _previewTexture, null, ScaleMode.ScaleToFit);
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