using System;

namespace DynamicDungeon.Runtime.Nodes
{
    [Serializable]
    public sealed class PlacementSetRule
    {
        public bool Enabled = true;
        public int WeightSlot = 1;
        public int MaskSlot;
        public string Prefab = string.Empty;
        public float Threshold = 0.0f;
        public float Density = 1.0f;
        public int PointCount;
        public int OffsetX;
        public int OffsetY;
        public bool MirrorX;
        public bool MirrorY;
        public bool AllowRotation;
    }

    [Serializable]
    public sealed class PlacementSetRuleSet
    {
        public PlacementSetRule[] Rules = Array.Empty<PlacementSetRule>();
    }
}
