using System;
using System.Collections.Generic;
using DynamicDungeon.Editor.Utilities;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace DynamicDungeon.Editor.Windows
{
    public sealed class NodeSearchWindow : ScriptableObject, ISearchWindowProvider
    {
        private DynamicDungeonGraphView _graphView;
        private Vector2 _graphLocalSearchPosition;

        public void Initialise(DynamicDungeonGraphView graphView)
        {
            _graphView = graphView;
            hideFlags = HideFlags.HideAndDontSave;
        }

        public void SetGraphLocalSearchPosition(Vector2 graphLocalSearchPosition)
        {
            _graphLocalSearchPosition = graphLocalSearchPosition;
        }

        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            List<SearchTreeEntry> entries = new List<SearchTreeEntry>();
            entries.Add(new SearchTreeGroupEntry(new GUIContent("Create Node"), 0));

            IReadOnlyList<Type> nodeTypes = NodeDiscovery.DiscoverNodeTypes();
            string currentCategory = null;

            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < nodeTypes.Count; nodeIndex++)
            {
                Type nodeType = nodeTypes[nodeIndex];
                string category = NodeDiscovery.GetNodeCategory(nodeType);
                if (!string.Equals(currentCategory, category, StringComparison.Ordinal))
                {
                    entries.Add(new SearchTreeGroupEntry(new GUIContent(category), 1));
                    currentCategory = category;
                }

                SearchTreeEntry entry = new SearchTreeEntry(new GUIContent(NodeDiscovery.GetNodeDisplayName(nodeType)));
                entry.level = 2;
                entry.userData = nodeType;
                entries.Add(entry);
            }

            return entries;
        }

        public bool OnSelectEntry(SearchTreeEntry searchTreeEntry, SearchWindowContext context)
        {
            Type nodeType = searchTreeEntry.userData as Type;
            if (nodeType == null || _graphView == null)
            {
                return false;
            }

            _graphView.CreateNodeFromSearch(nodeType, _graphLocalSearchPosition);
            return true;
        }
    }
}
