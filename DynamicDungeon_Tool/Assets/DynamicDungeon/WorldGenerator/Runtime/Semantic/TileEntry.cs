using System;
using System.Collections.Generic;

namespace DynamicDungeon.Runtime.Semantic
{
    [Serializable]
    public sealed class TileEntry
    {
        public ushort LogicalId;
        public string DisplayName = string.Empty;
        public List<string> Tags = new List<string>();
    }
}
