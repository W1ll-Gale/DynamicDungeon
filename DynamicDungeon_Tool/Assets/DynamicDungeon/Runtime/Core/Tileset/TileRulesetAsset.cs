using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[Serializable]
public sealed class TileRuleEntry
{
    [Tooltip("Integer tile ID written to tile layers.")]
    public int TileId = 0;

    [Tooltip("The Unity Tile asset placed on the Tilemap for this ID.")]
    public TileBase Tile = null;

    [Tooltip("Colour used in the node graph preview thumbnails.")]
    public Color PreviewColor = Color.white;

    [Tooltip("Optional label for readability in the inspector.")]
    public string Description = "";

    [Tooltip("Whether gameplay systems and validators should treat this tile as walkable.")]
    public bool IsWalkable = false;

    [Tooltip("Optional semantic tags used by validators and placement rules.")]
    public List<string> Tags = new List<string>();

    public bool HasTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || Tags == null) return false;
        for (int i = 0; i < Tags.Count; i++)
        {
            if (string.Equals(Tags[i], tag, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}

[CreateAssetMenu(fileName = "TileRuleset", menuName = "DynamicDungeon/Tile Ruleset")]
public sealed class TileRulesetAsset : ScriptableObject
{
    [SerializeField]
    private List<TileRuleEntry> _entries = new List<TileRuleEntry>
    {
        new TileRuleEntry
        {
            TileId = 0,
            PreviewColor = new Color(0.13f, 0.13f, 0.13f),
            Description = "Wall"
        },
        new TileRuleEntry
        {
            TileId = 1,
            PreviewColor = new Color(0.76f, 0.72f, 0.64f),
            Description = "Floor",
            IsWalkable = true,
            Tags = new List<string> { "Walkable" }
        },
    };

    public IReadOnlyList<TileRuleEntry> Entries => _entries;

    public bool TryGetEntry(int tileId, out TileRuleEntry entry)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].TileId == tileId)
            {
                entry = _entries[i];
                return true;
            }
        }

        entry = null;
        return false;
    }

    public TileBase GetTile(int tileId)
        => TryGetEntry(tileId, out TileRuleEntry entry) ? entry.Tile : null;

    public string GetDisplayName(int tileId)
    {
        if (!TryGetEntry(tileId, out TileRuleEntry entry))
            return $"Tile {tileId}";

        if (!string.IsNullOrWhiteSpace(entry.Description))
            return entry.Description;

        if (entry.Tile != null)
            return entry.Tile.name;

        return $"Tile {tileId}";
    }

    public Color GetPreviewColor(int tileId)
        => TryGetEntry(tileId, out TileRuleEntry entry) ? entry.PreviewColor : Color.magenta;

    public bool IsWalkable(int tileId, int fallbackOpenTileValue = 0)
        => TryGetEntry(tileId, out TileRuleEntry entry) ? entry.IsWalkable : tileId == fallbackOpenTileValue;

    public bool HasTag(int tileId, string tag)
        => TryGetEntry(tileId, out TileRuleEntry entry) && entry.HasTag(tag);
}
