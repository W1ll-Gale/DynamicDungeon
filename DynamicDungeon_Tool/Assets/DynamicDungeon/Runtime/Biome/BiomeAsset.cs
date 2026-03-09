using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DynamicDungeon.Runtime.Biome
{
    [CreateAssetMenu(fileName = "Biome", menuName = "DynamicDungeon/Biome")]
    public sealed class BiomeAsset : ScriptableObject
    {
        public List<BiomeTileMapping> TileMappings = new List<BiomeTileMapping>();

        public bool TryGetTile(ushort logicalId, out TileBase tile)
        {
            int index;
            for (index = 0; index < TileMappings.Count; index++)
            {
                BiomeTileMapping mapping = TileMappings[index];
                if (mapping != null && mapping.LogicalId == logicalId)
                {
                    tile = mapping.Tile;
                    return tile != null;
                }
            }

            tile = null;
            return false;
        }
    }
}
