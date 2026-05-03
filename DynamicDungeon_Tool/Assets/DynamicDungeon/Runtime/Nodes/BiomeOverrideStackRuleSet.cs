using System;

namespace DynamicDungeon.Runtime.Nodes
{
    [Serializable]
    public sealed class BiomeOverrideStackRule
    {
        public bool Enabled = true;
        public int MaskSlot = 1;
        public string OverrideBiome = string.Empty;
        public float Probability = 1.0f;
    }

    [Serializable]
    public sealed class BiomeOverrideStackRuleSet
    {
        public BiomeOverrideStackRule[] Rules = Array.Empty<BiomeOverrideStackRule>();
    }
}
