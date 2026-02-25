using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

[CustomPropertyDrawer(typeof(ThresholdBand))]
public sealed class ThresholdBandDrawer : PropertyDrawer
{
    private const float PreviewSize = 18f;
    private const float Spacing = 6f;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        SerializedProperty maxValueProperty = property.FindPropertyRelative("MaxValue");
        SerializedProperty tileIdProperty = property.FindPropertyRelative("TileId");

        EditorGUI.BeginProperty(position, label, property);

        Rect contentRect = EditorGUI.PrefixLabel(position, label);
        float maxWidth = Mathf.Max(90f, contentRect.width * 0.34f);
        Rect maxRect = new Rect(contentRect.x, contentRect.y, maxWidth, EditorGUIUtility.singleLineHeight);
        Rect pickerRect = new Rect(maxRect.xMax + Spacing, contentRect.y, contentRect.width - maxWidth - Spacing, EditorGUIUtility.singleLineHeight);

        EditorGUI.PropertyField(maxRect, maxValueProperty, GUIContent.none);

        GenGraph graph = ResolveGraph(property);
        TileRulesetAsset ruleset = graph != null ? graph.TileRuleset : null;

        if (ruleset == null || ruleset.Entries.Count == 0)
        {
            EditorGUI.PropertyField(pickerRect, tileIdProperty, GUIContent.none);
            EditorGUI.EndProperty();
            return;
        }

        DrawTilePickerButton(pickerRect, ruleset, tileIdProperty);
        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return EditorGUIUtility.singleLineHeight;
    }

    private static void DrawTilePickerButton(Rect rect, TileRulesetAsset ruleset, SerializedProperty tileIdProperty)
    {
        int tileId = tileIdProperty.intValue;
        TileRuleEntry currentEntry = ruleset.TryGetEntry(tileId, out TileRuleEntry foundEntry)
            ? foundEntry
            : null;

        string buttonLabel = currentEntry != null
            ? ruleset.GetDisplayName(tileId)
            : $"Missing Tile {tileId}";

        Rect previewRect = new Rect(rect.x + 4f, rect.y + 1f, PreviewSize, PreviewSize);
        Rect labelRect = new Rect(previewRect.xMax + 6f, rect.y, rect.width - PreviewSize - 24f, rect.height);
        Rect arrowRect = new Rect(rect.xMax - 16f, rect.y, 16f, rect.height);

        if (GUI.Button(rect, GUIContent.none, EditorStyles.popup))
            ShowTileMenu(rect, ruleset, tileIdProperty);

        Texture preview = GetTilePreviewTexture(currentEntry);
        if (preview != null)
            GUI.DrawTexture(previewRect, preview, ScaleMode.ScaleToFit, true);
        else
            EditorGUI.DrawRect(previewRect, currentEntry != null ? currentEntry.PreviewColor : Color.magenta);

        GUI.Label(labelRect, buttonLabel, EditorStyles.label);
        GUI.Label(arrowRect, "\u25BE", EditorStyles.centeredGreyMiniLabel);
    }

    private static void ShowTileMenu(Rect rect, TileRulesetAsset ruleset, SerializedProperty tileIdProperty)
    {
        GenericMenu menu = new GenericMenu();
        IReadOnlyList<TileRuleEntry> entries = ruleset.Entries;

        for (int index = 0; index < entries.Count; index++)
        {
            TileRuleEntry entry = entries[index];
            if (entry == null)
                continue;

            int candidateId = entry.TileId;
            string label = ruleset.GetDisplayName(candidateId);

            menu.AddItem(
                new GUIContent(label, GetTilePreviewTexture(entry)),
                tileIdProperty.intValue == candidateId,
                () =>
                {
                    tileIdProperty.serializedObject.Update();
                    tileIdProperty.intValue = candidateId;
                    tileIdProperty.serializedObject.ApplyModifiedProperties();
                });
        }

        menu.DropDown(rect);
    }

    private static Texture GetTilePreviewTexture(TileRuleEntry entry)
    {
        if (entry == null || entry.Tile == null)
            return null;

        if (TryGetTileSprite(entry.Tile, out Sprite sprite) && sprite != null)
            return sprite.texture;

        Texture texture = AssetPreview.GetAssetPreview(entry.Tile);
        if (texture != null)
            return texture;

        return AssetPreview.GetMiniThumbnail(entry.Tile);
    }

    private static bool TryGetTileSprite(TileBase tileBase, out Sprite sprite)
    {
        sprite = null;
        if (tileBase == null)
            return false;

        if (tileBase is Tile tile && tile.sprite != null)
        {
            sprite = tile.sprite;
            return true;
        }

        SerializedObject serializedTile = new SerializedObject(tileBase);
        string[] spritePropertyNames = { "m_DefaultSprite", "m_Sprite", "m_Sprites.Array.data[0]" };

        for (int index = 0; index < spritePropertyNames.Length; index++)
        {
            SerializedProperty property = serializedTile.FindProperty(spritePropertyNames[index]);
            if (property != null && property.propertyType == SerializedPropertyType.ObjectReference)
            {
                sprite = property.objectReferenceValue as Sprite;
                if (sprite != null)
                    return true;
            }
        }

        return false;
    }

    private static GenGraph ResolveGraph(SerializedProperty property)
    {
        Object target = property.serializedObject.targetObject;
        if (target is GenGraph directGraph)
            return directGraph;

        string path = AssetDatabase.GetAssetPath(target);
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return AssetDatabase.LoadMainAssetAtPath(path) as GenGraph;
    }
}
