using System;
using System.Collections.Generic;
using UnityEngine;

namespace DynamicDungeon.Runtime.Graph
{
    [Serializable]
    public sealed class GenGroupData
    {
        public string GroupId;
        public string Title;
        public Rect Position;
        public List<string> ContainedNodeIds = new List<string>();
        public Color BackgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.3f);
    }
}
