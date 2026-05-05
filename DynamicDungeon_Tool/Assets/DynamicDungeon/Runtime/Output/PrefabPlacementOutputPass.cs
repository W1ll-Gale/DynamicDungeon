using System;
using System.Collections.Generic;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Placement;
using UnityEngine;
using Unity.Mathematics;

namespace DynamicDungeon.Runtime.Output
{
    public sealed class PrefabPlacementOutputPass
    {
        public void Execute(WorldSnapshot snapshot, Grid grid, GeneratedPrefabWriter writer, Vector3Int tilemapOffset)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (grid == null)
            {
                throw new ArgumentNullException(nameof(grid));
            }

            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            WorldSnapshot.PrefabPlacementListChannelSnapshot channelSnapshot = GetPlacementChannel(snapshot, PrefabPlacementChannelUtility.ChannelName);
            if (channelSnapshot == null ||
                channelSnapshot.Data == null ||
                channelSnapshot.Data.Length == 0 ||
                snapshot.PrefabPlacementPrefabs == null ||
                snapshot.PrefabPlacementTemplates == null)
            {
                return;
            }

            Dictionary<int, PrefabPlacementMetadata> metadataById = BuildMetadataLookup(snapshot.PrefabPlacementMetadata);

            int index;
            for (index = 0; index < channelSnapshot.Data.Length; index++)
            {
                PrefabPlacementRecord placement = channelSnapshot.Data[index];
                if (placement.TemplateIndex < 0 ||
                    placement.TemplateIndex >= snapshot.PrefabPlacementPrefabs.Length ||
                    placement.TemplateIndex >= snapshot.PrefabPlacementTemplates.Length)
                {
                    continue;
                }

                GameObject prefab = snapshot.PrefabPlacementPrefabs[placement.TemplateIndex];
                if (prefab == null)
                {
                    continue;
                }

                PrefabStampTemplate template = snapshot.PrefabPlacementTemplates[placement.TemplateIndex];
                int2 normalizationOffset = GetNormalizationOffset(template, placement);
                Vector3Int cell = new Vector3Int(
                    placement.OriginX + normalizationOffset.x + tilemapOffset.x,
                    placement.OriginY + normalizationOffset.y + tilemapOffset.y,
                    tilemapOffset.z);
                Quaternion rotation = Quaternion.Euler(0.0f, 0.0f, placement.RotationQuarterTurns * 90.0f);
                Vector3 mirrorScale = new Vector3(placement.MirrorX ? -1.0f : 1.0f, placement.MirrorY ? -1.0f : 1.0f, 1.0f);
                Vector3 anchorOffset = template.AnchorOffset;
                Vector3 worldPosition = grid.CellToWorld(cell) + anchorOffset;

                GameObject instance = writer.WritePrefab(prefab, worldPosition, rotation, mirrorScale);
                ApplyPlacementProcessors(instance, placement, TryGetMetadata(metadataById, placement.MetadataId));
            }
        }

        private static Dictionary<int, PrefabPlacementMetadata> BuildMetadataLookup(PrefabPlacementMetadata[] metadata)
        {
            Dictionary<int, PrefabPlacementMetadata> lookup = new Dictionary<int, PrefabPlacementMetadata>();
            if (metadata == null)
            {
                return lookup;
            }

            int index;
            for (index = 0; index < metadata.Length; index++)
            {
                PrefabPlacementMetadata item = metadata[index];
                if (item != null && item.Id > 0)
                {
                    lookup[item.Id] = item;
                }
            }

            return lookup;
        }

        private static PrefabPlacementMetadata TryGetMetadata(Dictionary<int, PrefabPlacementMetadata> metadataById, int metadataId)
        {
            if (metadataById == null || metadataId <= 0)
            {
                return null;
            }

            metadataById.TryGetValue(metadataId, out PrefabPlacementMetadata metadata);
            return metadata;
        }

        private static void ApplyPlacementProcessors(GameObject instance, PrefabPlacementRecord placement, PrefabPlacementMetadata metadata)
        {
            if (instance == null)
            {
                return;
            }

            MonoBehaviour[] behaviours = instance.GetComponentsInChildren<MonoBehaviour>(true);
            int index;
            for (index = 0; index < behaviours.Length; index++)
            {
                if (behaviours[index] is IPrefabPlacementInstanceProcessor processor)
                {
                    processor.ProcessPrefabPlacement(placement, metadata);
                }
            }
        }

        private static WorldSnapshot.PrefabPlacementListChannelSnapshot GetPlacementChannel(WorldSnapshot snapshot, string channelName)
        {
            if (snapshot.PrefabPlacementChannels == null)
            {
                return null;
            }

            int index;
            for (index = 0; index < snapshot.PrefabPlacementChannels.Length; index++)
            {
                WorldSnapshot.PrefabPlacementListChannelSnapshot channel = snapshot.PrefabPlacementChannels[index];
                if (channel != null && string.Equals(channel.Name, channelName, StringComparison.Ordinal))
                {
                    return channel;
                }
            }

            return null;
        }

        private static int2 GetNormalizationOffset(PrefabStampTemplate template, PrefabPlacementRecord placement)
        {
            Vector2Int[] occupiedCells = template.OccupiedCells;
            if (occupiedCells == null || occupiedCells.Length == 0)
            {
                return int2.zero;
            }

            int2 min = template.UsesTilemapFootprint
                ? GetTransformedBoundsMin(occupiedCells[0], placement)
                : TransformOffsetRaw(occupiedCells[0], placement);

            int index;
            for (index = 1; index < occupiedCells.Length; index++)
            {
                int2 transformed = template.UsesTilemapFootprint
                    ? GetTransformedBoundsMin(occupiedCells[index], placement)
                    : TransformOffsetRaw(occupiedCells[index], placement);
                min = math.min(min, transformed);
            }

            return new int2(-min.x, -min.y);
        }

        private static int2 GetTransformedBoundsMin(Vector2Int source, PrefabPlacementRecord placement)
        {
            int2 min = TransformCornerRaw(source.x, source.y, placement);
            min = math.min(min, TransformCornerRaw(source.x + 1, source.y, placement));
            min = math.min(min, TransformCornerRaw(source.x, source.y + 1, placement));
            min = math.min(min, TransformCornerRaw(source.x + 1, source.y + 1, placement));
            return min;
        }

        private static int2 TransformCornerRaw(int x, int y, PrefabPlacementRecord placement)
        {
            int transformedX = placement.MirrorX ? -x : x;
            int transformedY = placement.MirrorY ? -y : y;

            if (placement.RotationQuarterTurns == 1)
            {
                return new int2(-transformedY, transformedX);
            }

            if (placement.RotationQuarterTurns == 2)
            {
                return new int2(-transformedX, -transformedY);
            }

            if (placement.RotationQuarterTurns == 3)
            {
                return new int2(transformedY, -transformedX);
            }

            return new int2(transformedX, transformedY);
        }

        private static int2 TransformOffsetRaw(Vector2Int source, PrefabPlacementRecord placement)
        {
            return TransformCornerRaw(source.x, source.y, placement);
        }
    }
}
