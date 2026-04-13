using System;
using DynamicDungeon.Runtime.Core;
using UnityEngine;

namespace DynamicDungeon.Runtime.Component
{
    [CreateAssetMenu(fileName = "BakedWorldSnapshot", menuName = "DynamicDungeon/Baked World Snapshot")]
    public sealed class BakedWorldSnapshot : ScriptableObject
    {
        public WorldSnapshot Snapshot;
        public string OutputChannelName = string.Empty;
        public long Seed;
        public string Timestamp = string.Empty;
        public int Width;
        public int Height;
    }
}
