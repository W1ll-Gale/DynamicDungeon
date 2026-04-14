using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DynamicDungeon.Runtime.Biome
{
    public sealed class BiomeAsset : ScriptableObject
    {
        public List<BiomeTileMapping> TileMappings = new List<BiomeTileMapping>();

        public bool TryGetTile(ushort logicalId, out TileBase tile)
        {
            return TryGetTile(logicalId, Vector2Int.zero, out tile);
        }

        public bool TryGetTile(ushort logicalId, Vector2Int cellPosition, out TileBase tile)
        {
            int index;
            for (index = 0; index < TileMappings.Count; index++)
            {
                BiomeTileMapping mapping = TileMappings[index];
                if (mapping != null && mapping.LogicalId == logicalId)
                {
                    return TryResolveTile(mapping, cellPosition, out tile);
                }
            }

            tile = null;
            return false;
        }

        private bool TryResolveTile(BiomeTileMapping mapping, Vector2Int cellPosition, out TileBase tile)
        {
            if (mapping.TileType == TileMappingType.WeightedRandom)
            {
                return TryResolveWeightedTile(mapping, cellPosition, out tile);
            }

            tile = mapping.Tile;
            return tile != null;
        }

        private bool TryResolveWeightedTile(BiomeTileMapping mapping, Vector2Int cellPosition, out TileBase tile)
        {
            if (mapping.WeightedTiles == null || mapping.WeightedTiles.Count == 0)
            {
                tile = null;
                return false;
            }

            float totalWeight = 0.0f;
            TileBase lastValidTile = null;
            int index;
            for (index = 0; index < mapping.WeightedTiles.Count; index++)
            {
                WeightedTileEntry entry = mapping.WeightedTiles[index];
                if (entry == null || entry.Tile == null || entry.Weight <= 0.0f)
                {
                    continue;
                }

                totalWeight += entry.Weight;
                lastValidTile = entry.Tile;
            }

            if (lastValidTile == null || totalWeight <= 0.0f)
            {
                tile = null;
                return false;
            }

            float roll = GetDeterministicWeightRoll(cellPosition, totalWeight);
            float cumulativeWeight = 0.0f;

            for (index = 0; index < mapping.WeightedTiles.Count; index++)
            {
                WeightedTileEntry entry = mapping.WeightedTiles[index];
                if (entry == null || entry.Tile == null || entry.Weight <= 0.0f)
                {
                    continue;
                }

                cumulativeWeight += entry.Weight;
                if (roll < cumulativeWeight)
                {
                    tile = entry.Tile;
                    return true;
                }
            }

            tile = lastValidTile;
            return true;
        }

        private float GetDeterministicWeightRoll(Vector2Int cellPosition, float totalWeight)
        {
            unchecked
            {
                int hash = GetInstanceID();
                hash = (hash * 486187739) + cellPosition.x;
                hash = (hash * 16777619) ^ cellPosition.y;

                uint unsignedHash = (uint)hash;
                return (float)(unsignedHash / 4294967296.0d) * totalWeight;
            }
        }
    }
}
