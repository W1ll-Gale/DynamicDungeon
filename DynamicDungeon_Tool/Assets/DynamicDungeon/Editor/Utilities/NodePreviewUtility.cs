using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class NodePreviewUtility
{
    private static readonly Color EmptyColor = new Color(0.22f, 0.22f, 0.22f);
    private static readonly Color MarkerColor = new Color(0.30f, 0.85f, 0.95f);

    public static Texture2D GeneratePreview(
        NodeValue value,
        int texWidth,
        int texHeight,
        TileRulesetAsset ruleset = null)
    {
        if (value == null) return SolidTexture(texWidth, texHeight, EmptyColor);

        switch (value.Kind)
        {
            case PortDataKind.World:
                return value.TryGetWorld(out GenMap world)
                    ? GeneratePreview(world, texWidth, texHeight, ruleset)
                    : SolidTexture(texWidth, texHeight, EmptyColor);
            case PortDataKind.FloatLayer:
                return value.TryGetFloatLayer(out FloatLayer floatLayer)
                    ? FromFloatLayer(floatLayer, texWidth, texHeight)
                    : SolidTexture(texWidth, texHeight, EmptyColor);
            case PortDataKind.IntLayer:
                return value.TryGetIntLayer(out IntLayer intLayer)
                    ? FromIntLayer(intLayer, texWidth, texHeight, ruleset)
                    : SolidTexture(texWidth, texHeight, EmptyColor);
            case PortDataKind.BoolMask:
                return value.TryGetBoolMask(out BoolMaskLayer mask)
                    ? FromBoolMask(mask, texWidth, texHeight)
                    : SolidTexture(texWidth, texHeight, EmptyColor);
            case PortDataKind.MarkerSet:
                return value.TryGetMarkerSet(out MarkerSet markers)
                    ? FromMarkerSet(markers, texWidth, texHeight)
                    : SolidTexture(texWidth, texHeight, EmptyColor);
            case PortDataKind.ValidationReport:
                return value.TryGetValidationReport(out ValidationReport report)
                    ? FromValidationReport(report, texWidth, texHeight)
                    : SolidTexture(texWidth, texHeight, EmptyColor);
            default:
                return SolidTexture(texWidth, texHeight, EmptyColor);
        }
    }

    public static Texture2D GeneratePreview(
        GenMap map,
        int texWidth,
        int texHeight,
        TileRulesetAsset ruleset = null)
    {
        if (map == null) return SolidTexture(texWidth, texHeight, EmptyColor);

        if (map.TryGetIntLayer("Tiles", out IntLayer tiles))
            return FromIntLayer(tiles, texWidth, texHeight, ruleset);

        foreach (KeyValuePair<string, IntLayer> pair in map.IntLayers)
            return FromIntLayer(pair.Value, texWidth, texHeight, ruleset);

        foreach (KeyValuePair<string, BoolMaskLayer> pair in map.MaskLayers)
            return FromBoolMask(pair.Value, texWidth, texHeight);

        foreach (KeyValuePair<string, FloatLayer> pair in map.FloatLayers)
            return FromFloatLayer(pair.Value, texWidth, texHeight);

        foreach (KeyValuePair<string, MarkerSet> pair in map.MarkerSets)
            return FromMarkerSet(pair.Value, texWidth, texHeight, map.Width, map.Height);

        return SolidTexture(texWidth, texHeight, EmptyColor);
    }

    private static Texture2D FromFloatLayer(FloatLayer layer, int texWidth, int texHeight)
    {
        Texture2D texture = NewTexture(texWidth, texHeight);
        float[,] normalised = layer.ToNormalizedFloats();

        for (int px = 0; px < texWidth; px++)
        {
            for (int py = 0; py < texHeight; py++)
            {
                int dataX = SampleCoord(px, texWidth, layer.Width);
                int dataY = SampleCoord(py, texHeight, layer.Height);
                float value = normalised[dataX, dataY];
                texture.SetPixel(px, py, new Color(value, value, value));
            }
        }

        texture.Apply();
        return texture;
    }

    private static Texture2D FromIntLayer(
        IntLayer layer,
        int texWidth,
        int texHeight,
        TileRulesetAsset ruleset = null)
    {
        Texture2D texture = NewTexture(texWidth, texHeight);

        int max = int.MinValue;
        int min = int.MaxValue;
        for (int x = 0; x < layer.Width; x++)
        {
            for (int y = 0; y < layer.Height; y++)
            {
                int value = layer.GetValue(x, y);
                if (value > max) max = value;
                if (value < min) min = value;
            }
        }

        int range = Mathf.Max(1, max - min);

        for (int px = 0; px < texWidth; px++)
        {
            for (int py = 0; py < texHeight; py++)
            {
                float sampleX = (float)px / Mathf.Max(1, texWidth) * layer.Width;
                float sampleY = (float)py / Mathf.Max(1, texHeight) * layer.Height;
                int dataX = Mathf.Clamp(Mathf.FloorToInt(sampleX), 0, layer.Width - 1);
                int dataY = Mathf.Clamp(Mathf.FloorToInt(sampleY), 0, layer.Height - 1);
                int tileId = layer.GetValue(dataX, dataY);
                float localU = Mathf.Repeat(sampleX, 1f);
                float localV = Mathf.Repeat(sampleY, 1f);
                Color color = ResolveTilePreviewColor(tileId, localU, localV, ruleset, min, range);
                texture.SetPixel(px, py, color);
            }
        }

        texture.Apply();
        return texture;
    }

    private static Texture2D FromBoolMask(BoolMaskLayer layer, int texWidth, int texHeight)
    {
        Texture2D texture = NewTexture(texWidth, texHeight);

        for (int px = 0; px < texWidth; px++)
        {
            for (int py = 0; py < texHeight; py++)
            {
                int dataX = SampleCoord(px, texWidth, layer.Width);
                int dataY = SampleCoord(py, texHeight, layer.Height);
                texture.SetPixel(px, py, layer.GetValue(dataX, dataY) ? Color.white : EmptyColor);
            }
        }

        texture.Apply();
        return texture;
    }

    private static Texture2D FromMarkerSet(
        MarkerSet markerSet,
        int texWidth,
        int texHeight,
        int widthHint = 32,
        int heightHint = 32)
    {
        Texture2D texture = SolidTexture(texWidth, texHeight, EmptyColor);

        int width = Mathf.Max(1, widthHint);
        int height = Mathf.Max(1, heightHint);

        foreach (Marker marker in markerSet.Markers)
        {
            if (marker == null) continue;

            int px = Mathf.Clamp(Mathf.RoundToInt((float)marker.Position.x / width * (texWidth - 1)), 0, texWidth - 1);
            int py = Mathf.Clamp(Mathf.RoundToInt((float)marker.Position.y / height * (texHeight - 1)), 0, texHeight - 1);
            texture.SetPixel(px, py, MarkerColor);
        }

        texture.Apply();
        return texture;
    }

    private static Texture2D FromValidationReport(ValidationReport report, int texWidth, int texHeight)
    {
        Color color = report != null && report.IsValid
            ? new Color(0.20f, 0.55f, 0.25f)
            : new Color(0.65f, 0.20f, 0.20f);

        return SolidTexture(texWidth, texHeight, color);
    }

    private static Texture2D SolidTexture(int width, int height, Color color)
    {
        Texture2D texture = NewTexture(width, height);
        Color[] pixels = new Color[width * height];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    private static Texture2D NewTexture(int width, int height)
    {
        return new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
    }

    private static int SampleCoord(int pixel, int texSize, int dataSize)
    {
        int coord = Mathf.FloorToInt((float)pixel / texSize * dataSize);
        return Mathf.Clamp(coord, 0, dataSize - 1);
    }

    private static Color ResolveTilePreviewColor(
        int tileId,
        float localU,
        float localV,
        TileRulesetAsset ruleset,
        int min,
        int range)
    {
        if (ruleset != null && ruleset.TryGetEntry(tileId, out TileRuleEntry entry))
        {
            if (TrySampleTileSprite(entry.Tile, localU, localV, out Color sampled))
                return sampled;

            return entry.PreviewColor;
        }

        return Color.HSVToRGB(((float)(tileId - min) / Mathf.Max(1, range)) * 0.72f, 0.55f, 0.80f);
    }

    private static bool TrySampleTileSprite(TileBase tileBase, float localU, float localV, out Color color)
    {
        color = default;

        if (!TryGetTileSprite(tileBase, out Sprite sprite) || sprite == null)
            return false;

        Texture2D texture = sprite.texture;
        if (texture == null || !texture.isReadable)
            return TrySampleSpritePreview(sprite, localU, localV, out color) ||
                   TrySampleAssetPreview(tileBase, localU, localV, out color);

        Rect rect = sprite.textureRect;
        int x = Mathf.Clamp(Mathf.FloorToInt(rect.x + localU * rect.width), Mathf.FloorToInt(rect.x), Mathf.FloorToInt(rect.xMax) - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt(rect.y + localV * rect.height), Mathf.FloorToInt(rect.y), Mathf.FloorToInt(rect.yMax) - 1);
        color = texture.GetPixel(x, y);
        return true;
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

    private static bool TrySampleSpritePreview(Sprite sprite, float localU, float localV, out Color color)
    {
        color = default;
        if (sprite == null)
            return false;

        Texture2D preview = AssetPreview.GetAssetPreview(sprite) as Texture2D ??
                            AssetPreview.GetMiniThumbnail(sprite) as Texture2D;
        if (preview == null || !preview.isReadable)
            return false;

        int x = Mathf.Clamp(Mathf.FloorToInt(localU * preview.width), 0, preview.width - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt(localV * preview.height), 0, preview.height - 1);
        color = preview.GetPixel(x, y);
        return true;
    }

    private static bool TrySampleAssetPreview(Object asset, float localU, float localV, out Color color)
    {
        color = default;
        Texture2D preview = AssetPreview.GetAssetPreview(asset) as Texture2D ?? AssetPreview.GetMiniThumbnail(asset) as Texture2D;
        if (preview == null || !preview.isReadable)
            return false;

        int x = Mathf.Clamp(Mathf.FloorToInt(localU * preview.width), 0, preview.width - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt(localV * preview.height), 0, preview.height - 1);
        color = preview.GetPixel(x, y);
        return true;
    }
}
