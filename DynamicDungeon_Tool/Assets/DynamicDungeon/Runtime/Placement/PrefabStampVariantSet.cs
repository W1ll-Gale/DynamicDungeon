using System;

namespace DynamicDungeon.Runtime.Placement
{
    [Serializable]
    public sealed class PrefabStampVariant
    {
        public string Prefab = string.Empty;
        public float Weight = 1.0f;
    }

    [Serializable]
    public sealed class PrefabStampVariantSet
    {
        public PrefabStampVariant[] Variants = Array.Empty<PrefabStampVariant>();
    }
}
