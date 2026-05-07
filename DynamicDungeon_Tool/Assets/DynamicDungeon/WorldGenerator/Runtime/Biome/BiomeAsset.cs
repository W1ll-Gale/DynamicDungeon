using System.Collections.Generic;
using DynamicDungeon.Runtime.Semantic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DynamicDungeon.Runtime.Biome
{
    public sealed class BiomeAsset : ScriptableObject
    {
        public List<string> ExcludedRegistryGuids = new List<string>();
        public List<BiomeTileMapping> TileMappings = new List<BiomeTileMapping>();

        private void OnDisable()
        {
            ReleaseGeneratedSpriteTiles();
        }

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
                if (mapping != null && mapping.LogicalIds != null && mapping.LogicalIds.Contains(logicalId))
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

            if (mapping.TileType == TileMappingType.Sprite)
            {
                return TryResolveSpriteTile(mapping, out tile);
            }

            tile = mapping.Tile;
            return tile != null;
        }

        private bool TryResolveSpriteTile(BiomeTileMapping mapping, out TileBase tile)
        {
            if (mapping.SpriteAsset == null)
            {
                tile = null;
                return false;
            }

            tile = GetOrCreateSpriteTile(mapping);
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
                uint hash = (uint)GetInstanceID();
                hash ^= (uint)cellPosition.x * 374761393u;
                hash = (hash << 17) | (hash >> 15);
                hash ^= (uint)cellPosition.y * 668265263u;
                hash *= 2246822519u;
                hash ^= hash >> 15;
                hash *= 3266489917u;
                hash ^= hash >> 16;

                uint unsignedHash = hash;
                return (float)(unsignedHash / 4294967296.0d) * totalWeight;
            }
        }

        private TileBase GetOrCreateSpriteTile(BiomeTileMapping mapping)
        {
            if (mapping.GeneratedSpriteTile != null && mapping.GeneratedSpriteSource == mapping.SpriteAsset)
            {
                return mapping.GeneratedSpriteTile;
            }

            ReleaseGeneratedSpriteTile(mapping);

            Tile spriteTile = CreateInstance<Tile>();
            spriteTile.sprite = mapping.SpriteAsset;
            spriteTile.name = mapping.SpriteAsset.name + "_GeneratedTile";
            spriteTile.hideFlags = HideFlags.HideAndDontSave;

            mapping.GeneratedSpriteTile = spriteTile;
            mapping.GeneratedSpriteSource = mapping.SpriteAsset;
            return spriteTile;
        }

        private void ReleaseGeneratedSpriteTiles()
        {
            if (TileMappings == null)
            {
                return;
            }

            int index;
            for (index = 0; index < TileMappings.Count; index++)
            {
                ReleaseGeneratedSpriteTile(TileMappings[index]);
            }
        }

        private static void ReleaseGeneratedSpriteTile(BiomeTileMapping mapping)
        {
            if (mapping == null || mapping.GeneratedSpriteTile == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(mapping.GeneratedSpriteTile);
            }
            else
            {
                DestroyImmediate(mapping.GeneratedSpriteTile);
            }

            mapping.GeneratedSpriteTile = null;
            mapping.GeneratedSpriteSource = null;
        }
    }
}
