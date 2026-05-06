using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DynamicDungeon.Runtime.Biome
{
    public enum TileMappingType
    {
        Direct,
        WeightedRandom,
        RuleTile,
        AnimatedTile,
        Sprite
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
        public List<ushort> LogicalIds = new List<ushort>();
        public TileMappingType TileType;
        public TileBase Tile;
        public Sprite SpriteAsset;
        public List<WeightedTileEntry> WeightedTiles = new List<WeightedTileEntry>();

        [NonSerialized]
        public Tile GeneratedSpriteTile;

        [NonSerialized]
        public Sprite GeneratedSpriteSource;
    }
}
