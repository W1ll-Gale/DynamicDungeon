using System;
using System.Collections.Generic;
using DynamicDungeon.Runtime.Core;

namespace DynamicDungeon.Runtime.Placement
{
    [Serializable]
    public sealed class PrefabPlacementMetadata
    {
        public int Id;
        public string Type = string.Empty;
        public string Payload = string.Empty;

        public PrefabPlacementMetadata()
        {
        }

        public PrefabPlacementMetadata(int id, string type, string payload)
        {
            Id = id;
            Type = type ?? string.Empty;
            Payload = payload ?? string.Empty;
        }
    }

    [Serializable]
    public struct PrefabPlacementRecord
    {
        private const byte MirrorXBit = 1 << 0;
        private const byte MirrorYBit = 1 << 1;

        public int TemplateIndex;
        public int OriginX;
        public int OriginY;
        public byte RotationQuarterTurns;
        public byte Flags;
        public int MetadataId;

        public PrefabPlacementRecord(int templateIndex, int originX, int originY, byte rotationQuarterTurns, bool mirrorX, bool mirrorY)
            : this(templateIndex, originX, originY, rotationQuarterTurns, mirrorX, mirrorY, 0)
        {
        }

        public PrefabPlacementRecord(int templateIndex, int originX, int originY, byte rotationQuarterTurns, bool mirrorX, bool mirrorY, int metadataId)
        {
            TemplateIndex = templateIndex;
            OriginX = originX;
            OriginY = originY;
            RotationQuarterTurns = (byte)(rotationQuarterTurns & 3);
            Flags = 0;
            MetadataId = metadataId;

            if (mirrorX)
            {
                Flags |= MirrorXBit;
            }

            if (mirrorY)
            {
                Flags |= MirrorYBit;
            }
        }

        public bool MirrorX
        {
            get
            {
                return (Flags & MirrorXBit) != 0;
            }
        }

        public bool MirrorY
        {
            get
            {
                return (Flags & MirrorYBit) != 0;
            }
        }
    }

    public interface IPrefabPlacementInstanceProcessor
    {
        void ProcessPrefabPlacement(PrefabPlacementRecord placement, PrefabPlacementMetadata metadata);
    }

    public static class PrefabPlacementMetadataUtility
    {
        private const string MetadataKey = "__PrefabPlacementMetadata";

        public static int AddMetadata(ManagedBlackboard managedBlackboard, string type, string payload)
        {
            if (managedBlackboard == null)
            {
                return 0;
            }

            List<PrefabPlacementMetadata> metadata;
            if (!managedBlackboard.Read(MetadataKey, out metadata) || metadata == null)
            {
                metadata = new List<PrefabPlacementMetadata>();
                managedBlackboard.Write(MetadataKey, metadata);
            }

            int id = metadata.Count + 1;
            metadata.Add(new PrefabPlacementMetadata(id, type, payload));
            return id;
        }

        public static PrefabPlacementMetadata[] ReadMetadataSnapshot(ManagedBlackboard managedBlackboard)
        {
            if (managedBlackboard == null)
            {
                return Array.Empty<PrefabPlacementMetadata>();
            }

            List<PrefabPlacementMetadata> metadata;
            if (!managedBlackboard.Read(MetadataKey, out metadata) || metadata == null || metadata.Count == 0)
            {
                return Array.Empty<PrefabPlacementMetadata>();
            }

            PrefabPlacementMetadata[] snapshot = new PrefabPlacementMetadata[metadata.Count];
            int index;
            for (index = 0; index < metadata.Count; index++)
            {
                PrefabPlacementMetadata item = metadata[index];
                snapshot[index] = item != null
                    ? new PrefabPlacementMetadata(item.Id, item.Type, item.Payload)
                    : new PrefabPlacementMetadata();
            }

            return snapshot;
        }
    }
}
