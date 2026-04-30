using System;

namespace DynamicDungeon.Runtime.Placement
{
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

        public PrefabPlacementRecord(int templateIndex, int originX, int originY, byte rotationQuarterTurns, bool mirrorX, bool mirrorY)
        {
            TemplateIndex = templateIndex;
            OriginX = originX;
            OriginY = originY;
            RotationQuarterTurns = (byte)(rotationQuarterTurns & 3);
            Flags = 0;

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
}
