using System;
using DynamicDungeon.Runtime;
using DynamicDungeon.Runtime.Core;
using UnityEngine;

namespace DynamicDungeon.Runtime.Component
{
    [CreateAssetMenu(fileName = "BakedWorldSnapshot", menuName = DynamicDungeonMenuPaths.BakedWorldSnapshotAssetMenu)]
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
