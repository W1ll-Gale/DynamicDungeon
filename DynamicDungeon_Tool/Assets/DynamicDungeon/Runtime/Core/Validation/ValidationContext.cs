using System;

public sealed class ValidationContext
{
    public GenMap Map { get; }
    public string WalkabilityLayerId { get; }
    public string WalkabilityLayerName { get; }
    public int OpenTileValue { get; }
    public TileRulesetAsset Ruleset { get; }
    public string WalkableTag { get; }
    public long Seed => Map.Seed;

    public ValidationContext(
        GenMap map,
        string walkabilityLayerId,
        string walkabilityLayerName,
        int openTileValue = 0,
        TileRulesetAsset ruleset = null,
        string walkableTag = "Walkable")
    {
        Map = map ?? throw new ArgumentNullException(nameof(map));
        WalkabilityLayerId = walkabilityLayerId ?? string.Empty;
        WalkabilityLayerName = string.IsNullOrWhiteSpace(walkabilityLayerName) ? "Unassigned Layer" : walkabilityLayerName;
        OpenTileValue = openTileValue;
        Ruleset = ruleset;
        WalkableTag = walkableTag;
    }

    public bool IsOpen(int x, int y)
    {
        if (string.IsNullOrWhiteSpace(WalkabilityLayerId)) return false;
        if (!Map.TryGetIntLayer(WalkabilityLayerId, out IntLayer layer)) return false;
        if (!IsInBounds(x, y)) return false;

        int tileId = layer.GetValue(x, y);
        if (Ruleset != null)
        {
            if (!string.IsNullOrWhiteSpace(WalkableTag) && Ruleset.HasTag(tileId, WalkableTag))
                return true;

            return Ruleset.IsWalkable(tileId, OpenTileValue);
        }

        return tileId == OpenTileValue;
    }

    public bool IsInBounds(int x, int y) => x >= 0 && x < Map.Width && y >= 0 && y < Map.Height;
}
