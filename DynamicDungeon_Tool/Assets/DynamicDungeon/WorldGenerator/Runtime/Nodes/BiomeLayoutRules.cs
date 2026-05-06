using System;

namespace DynamicDungeon.Runtime.Nodes
{
    [Serializable]
    public sealed class BiomeLayoutEntry
    {
        public string Biome = string.Empty;
        public float Weight = 1.0f;
        public int MinSize;
        public int MaxSize;
        public bool Enabled = true;
    }

    [Serializable]
    public sealed class BiomeLayoutConstraint
    {
        public BiomeLayoutConstraintType Type = BiomeLayoutConstraintType.Required;
        public string Biome = string.Empty;
        public int Size;
        public bool Enabled = true;
    }

    [Serializable]
    public sealed class BiomeLayoutRules
    {
        public BiomeLayoutEntry[] Entries = Array.Empty<BiomeLayoutEntry>();
        public BiomeLayoutConstraint[] Constraints = Array.Empty<BiomeLayoutConstraint>();
    }
}
