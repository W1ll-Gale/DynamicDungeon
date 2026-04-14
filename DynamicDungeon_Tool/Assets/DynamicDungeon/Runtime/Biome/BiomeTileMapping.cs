using System;
using System.Collections.Generic;
using UnityEngine.Tilemaps;

namespace DynamicDungeon.Runtime.Biome
{
    public enum TileMappingType
    {
        Direct,
        WeightedRandom,
        RuleTile,
        AnimatedTile
    }

    [Serializable]
    public sealed class WeightedTileEntry
    {
        public TileBase Tile;
        public float Weight = 1.0f;
    }

    [Serializable]
    public sealed class BiomeTileMapping
    {
        public ushort LogicalId;
        public TileMappingType TileType;
        public TileBase Tile;
        public List<WeightedTileEntry> WeightedTiles = new List<WeightedTileEntry>();
    }
}
