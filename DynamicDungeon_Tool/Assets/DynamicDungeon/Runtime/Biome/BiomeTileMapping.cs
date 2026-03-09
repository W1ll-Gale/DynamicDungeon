using System;
using UnityEngine.Tilemaps;

namespace DynamicDungeon.Runtime.Biome
{
    [Serializable]
    public sealed class BiomeTileMapping
    {
        public ushort LogicalId;
        public TileBase Tile;
    }
}
