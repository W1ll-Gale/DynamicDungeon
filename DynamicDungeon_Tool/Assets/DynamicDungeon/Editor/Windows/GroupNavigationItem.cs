using UnityEngine;

namespace DynamicDungeon.Editor.Windows
{
    internal readonly struct GroupNavigationItem
    {
        public readonly string GroupId;
        public readonly string Title;
        public readonly int NodeCount;
        public readonly Rect Position;

        public GroupNavigationItem(string groupId, string title, int nodeCount, Rect position)
        {
            GroupId = groupId ?? string.Empty;
            Title = string.IsNullOrWhiteSpace(title) ? "Group" : title;
            NodeCount = nodeCount;
            Position = position;
        }
    }
}
