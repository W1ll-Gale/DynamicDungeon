using System;
using System.Collections.Generic;
using DynamicDungeon.Runtime.Biome;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Semantic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DynamicDungeon.Runtime.Output
{
    public sealed class TilemapOutputPass
    {
        public void Execute(
            WorldSnapshot snapshot,
            string intChannelName,
            BiomeAsset biome,
            TileSemanticRegistry registry,
            TilemapLayerWriter writer,
            IReadOnlyList<TilemapLayerDefinition> layerDefinitions,
            Vector3Int tilemapOffset)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (string.IsNullOrWhiteSpace(intChannelName))
            {
                throw new ArgumentException("An int channel name is required.", nameof(intChannelName));
            }

            if (biome == null)
            {
                throw new ArgumentNullException(nameof(biome));
            }

            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            if (layerDefinitions == null)
            {
                throw new ArgumentNullException(nameof(layerDefinitions));
            }

            WorldSnapshot.IntChannelSnapshot channelSnapshot = GetIntChannel(snapshot, intChannelName);
            WorldSnapshot.IntChannelSnapshot biomeChannelSnapshot = TryGetIntChannel(snapshot, BiomeChannelUtility.ChannelName);
            IReadOnlyList<BiomeAsset> biomeChannelBiomes = snapshot.BiomeChannelBiomes ?? Array.Empty<BiomeAsset>();
            TilemapLayerDefinition catchAllLayer = FindCatchAllLayer(layerDefinitions);
            int[] emptyTagIds = Array.Empty<int>();

            int index;
            for (index = 0; index < channelSnapshot.Data.Length; index++)
            {
                int logicalIdValue = channelSnapshot.Data[index];
                if (logicalIdValue == LogicalTileId.Void)
                {
                    continue;
                }

                ushort logicalId = unchecked((ushort)logicalIdValue);
                int x = index % snapshot.Width;
                int y = index / snapshot.Width;
                Vector2Int cellPosition = new Vector2Int(x, y);
                BiomeAsset resolvedBiome = ResolveBiome(biome, biomeChannelSnapshot, biomeChannelBiomes, index);
                TileBase tile;
                if (resolvedBiome == null || !resolvedBiome.TryGetTile(logicalId, cellPosition, out tile))
                {
                    continue;
                }

                int[] tagIds = registry != null ? registry.GetTagIds(logicalId) : emptyTagIds;
                TilemapLayerDefinition layer = ResolveLayer(layerDefinitions, catchAllLayer, tagIds, registry);
                if (layer == null)
                {
                    continue;
                }

                Vector3Int position = new Vector3Int(x + tilemapOffset.x, y + tilemapOffset.y, tilemapOffset.z);
                writer.WriteTile(position, tile, layer);
            }
        }

        public void ExecuteBackgroundFill(
            WorldSnapshot snapshot,
            string intChannelName,
            BiomeAsset biome,
            TilemapLayerWriter writer,
            TilemapLayerDefinition backgroundLayer,
            Vector3Int tilemapOffset,
            ushort backgroundLogicalId,
            string biomeChannelName = null)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (string.IsNullOrWhiteSpace(intChannelName))
            {
                throw new ArgumentException("An int channel name is required.", nameof(intChannelName));
            }

            if (biome == null)
            {
                throw new ArgumentNullException(nameof(biome));
            }

            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            if (backgroundLayer == null)
            {
                return;
            }

            WorldSnapshot.IntChannelSnapshot channelSnapshot = GetIntChannel(snapshot, intChannelName);
            string resolvedBiomeChannelName = string.IsNullOrWhiteSpace(biomeChannelName) ? BiomeChannelUtility.ChannelName : biomeChannelName;
            WorldSnapshot.IntChannelSnapshot biomeChannelSnapshot = TryGetIntChannel(snapshot, resolvedBiomeChannelName);
            IReadOnlyList<BiomeAsset> biomeChannelBiomes = snapshot.BiomeChannelBiomes ?? Array.Empty<BiomeAsset>();
            int[] highestSolidByColumn = BuildHighestSolidByColumn(snapshot, channelSnapshot);

            int x;
            for (x = 0; x < snapshot.Width; x++)
            {
                int highestSolidY = highestSolidByColumn[x];
                if (highestSolidY <= 0)
                {
                    continue;
                }

                int y;
                for (y = 0; y < highestSolidY; y++)
                {
                    int index = (y * snapshot.Width) + x;
                    if (channelSnapshot.Data[index] != LogicalTileId.Void)
                    {
                        continue;
                    }

                    Vector2Int cellPosition = new Vector2Int(x, y);
                    BiomeAsset resolvedBiome = ResolveBiome(biome, biomeChannelSnapshot, biomeChannelBiomes, index);
                    TileBase tile;
                    if (resolvedBiome == null || !resolvedBiome.TryGetTile(backgroundLogicalId, cellPosition, out tile))
                    {
                        continue;
                    }

                    Vector3Int position = new Vector3Int(x + tilemapOffset.x, y + tilemapOffset.y, tilemapOffset.z);
                    writer.WriteTile(position, tile, backgroundLayer);
                }
            }
        }

        private static WorldSnapshot.IntChannelSnapshot GetIntChannel(WorldSnapshot snapshot, string intChannelName)
        {
            WorldSnapshot.IntChannelSnapshot channelSnapshot = TryGetIntChannel(snapshot, intChannelName);
            if (channelSnapshot != null)
            {
                return channelSnapshot;
            }

            throw new InvalidOperationException("WorldSnapshot does not contain int channel '" + intChannelName + "'.");
        }

        private static WorldSnapshot.IntChannelSnapshot TryGetIntChannel(WorldSnapshot snapshot, string intChannelName)
        {
            int index;
            for (index = 0; index < snapshot.IntChannels.Length; index++)
            {
                WorldSnapshot.IntChannelSnapshot channelSnapshot = snapshot.IntChannels[index];
                if (channelSnapshot != null && string.Equals(channelSnapshot.Name, intChannelName, StringComparison.Ordinal))
                {
                    return channelSnapshot;
                }
            }

            return null;
        }

        private static TilemapLayerDefinition FindCatchAllLayer(IReadOnlyList<TilemapLayerDefinition> layerDefinitions)
        {
            int index;
            for (index = 0; index < layerDefinitions.Count; index++)
            {
                TilemapLayerDefinition layerDefinition = layerDefinitions[index];
                if (layerDefinition != null && layerDefinition.IsCatchAll)
                {
                    return layerDefinition;
                }
            }

            return null;
        }

        private static int[] BuildHighestSolidByColumn(WorldSnapshot snapshot, WorldSnapshot.IntChannelSnapshot channelSnapshot)
        {
            int[] highestSolidByColumn = new int[snapshot.Width];

            int x;
            for (x = 0; x < highestSolidByColumn.Length; x++)
            {
                highestSolidByColumn[x] = -1;
            }

            int index;
            for (index = 0; index < channelSnapshot.Data.Length; index++)
            {
                if (channelSnapshot.Data[index] == LogicalTileId.Void)
                {
                    continue;
                }

                int xCoordinate = index % snapshot.Width;
                int yCoordinate = index / snapshot.Width;
                if (yCoordinate > highestSolidByColumn[xCoordinate])
                {
                    highestSolidByColumn[xCoordinate] = yCoordinate;
                }
            }

            return highestSolidByColumn;
        }

        private static TilemapLayerDefinition ResolveLayer(
            IReadOnlyList<TilemapLayerDefinition> layerDefinitions,
            TilemapLayerDefinition catchAllLayer,
            int[] tagIds,
            TileSemanticRegistry registry)
        {
            if (registry == null)
            {
                if (catchAllLayer == null)
                {
                    throw new InvalidOperationException("A catch-all TilemapLayerDefinition is required when no TileSemanticRegistry is available.");
                }

                return catchAllLayer;
            }

            int index;
            for (index = 0; index < layerDefinitions.Count; index++)
            {
                TilemapLayerDefinition layerDefinition = layerDefinitions[index];
                if (layerDefinition == null || layerDefinition.IsCatchAll)
                {
                    continue;
                }

                if (layerDefinition.MatchesTags(tagIds, registry.AllTags))
                {
                    return layerDefinition;
                }
            }

            return catchAllLayer;
        }

        private static BiomeAsset ResolveBiome(
            BiomeAsset fallbackBiome,
            WorldSnapshot.IntChannelSnapshot biomeChannelSnapshot,
            IReadOnlyList<BiomeAsset> biomeChannelBiomes,
            int index)
        {
            if (biomeChannelSnapshot == null ||
                biomeChannelSnapshot.Data == null ||
                index < 0 ||
                index >= biomeChannelSnapshot.Data.Length)
            {
                return fallbackBiome;
            }

            int biomeIndex = biomeChannelSnapshot.Data[index];
            if (biomeIndex < 0 || biomeIndex >= biomeChannelBiomes.Count)
            {
                return fallbackBiome;
            }

            BiomeAsset resolvedBiome = biomeChannelBiomes[biomeIndex];
            return resolvedBiome != null ? resolvedBiome : fallbackBiome;
        }
    }
}
