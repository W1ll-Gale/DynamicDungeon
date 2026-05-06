using System;
using System.Collections.Generic;
using DynamicDungeon.Editor.Nodes;
using DynamicDungeon.Editor.Utilities;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace DynamicDungeon.Editor.Windows
{
    public sealed class NodeSearchWindow : ScriptableObject, ISearchWindowProvider
    {
        private DynamicDungeonGraphView _graphView;
        private Vector2 _graphLocalSearchPosition;
        private bool _hasChannelTypeFilter;
        private ChannelType _channelTypeFilter;
        private PortDirection _requiredCandidateDirection;

        public void Initialise(DynamicDungeonGraphView graphView)
        {
            _graphView = graphView;
            hideFlags = HideFlags.HideAndDontSave;
        }

        public void SetGraphLocalSearchPosition(Vector2 graphLocalSearchPosition)
        {
            _graphLocalSearchPosition = graphLocalSearchPosition;
        }

        public void ClearChannelTypeFilter()
        {
            _hasChannelTypeFilter = false;
            _channelTypeFilter = default;
            _requiredCandidateDirection = default;
        }

        public void SetChannelTypeFilter(ChannelType channelType, PortDirection requiredCandidateDirection)
        {
            _hasChannelTypeFilter = true;
            _channelTypeFilter = channelType;
            _requiredCandidateDirection = requiredCandidateDirection;
        }

        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            List<SearchTreeEntry> entries = new List<SearchTreeEntry>();
            string rootTitle = _hasChannelTypeFilter
                ? "Create Compatible Node (" + FormatChannelType(_channelTypeFilter) + ")"
                : "Create Node";
            entries.Add(new SearchTreeGroupEntry(new GUIContent(rootTitle), 0));

            IReadOnlyList<Type> nodeTypes = NodeDiscovery.DiscoverNodeTypes();
            string currentCategory = null;

            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < nodeTypes.Count; nodeIndex++)
            {
                Type nodeType = nodeTypes[nodeIndex];
                if (!MatchesChannelTypeFilter(nodeType))
                {
                    continue;
                }

                if (GraphOutputUtility.IsOutputNodeType(nodeType) &&
                    _graphView != null &&
                    _graphView.Graph != null &&
                    GraphOutputUtility.CountOutputNodes(_graphView.Graph) > 0)
                {
                    continue;
                }

                string category = NodeDiscovery.GetNodeCategory(nodeType);
                if (!string.Equals(currentCategory, category, StringComparison.Ordinal))
                {
                    entries.Add(new SearchTreeGroupEntry(new GUIContent(category), 1));
                    currentCategory = category;
                }

                SearchTreeEntry entry = new SearchTreeEntry(new GUIContent(
                    NodeDiscovery.GetNodeDisplayName(nodeType),
                    NodeDiscovery.GetNodeDescription(nodeType)));
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
            ClearChannelTypeFilter();
            return true;
        }

        private bool MatchesChannelTypeFilter(Type nodeType)
        {
            if (!_hasChannelTypeFilter)
            {
                return true;
            }

            IGenNode prototype;
            string errorMessage;
            if (!GenNodeInstantiationUtility.TryCreatePrototypeNodeInstance(
                    nodeType,
                    "node-search-filter",
                    NodeDiscovery.GetNodeDisplayName(nodeType),
                    out prototype,
                    out errorMessage) ||
                prototype == null ||
                prototype.Ports == null)
            {
                return false;
            }

            IReadOnlyList<NodePortDefinition> ports = prototype.Ports;
            int portIndex;
            for (portIndex = 0; portIndex < ports.Count; portIndex++)
            {
                NodePortDefinition port = ports[portIndex];
                if (port.Direction != _requiredCandidateDirection)
                {
                    continue;
                }

                if (CanConnectFilterToPort(port.Type))
                {
                    return true;
                }
            }

            return false;
        }

        private bool CanConnectFilterToPort(ChannelType candidateType)
        {
            if (_requiredCandidateDirection == PortDirection.Input)
            {
                return candidateType == _channelTypeFilter ||
                    CastRegistry.CanCast(_channelTypeFilter, candidateType);
            }

            return candidateType == _channelTypeFilter ||
                CastRegistry.CanCast(candidateType, _channelTypeFilter);
        }

        private static string FormatChannelType(ChannelType channelType)
        {
            switch (channelType)
            {
                case ChannelType.Float:
                    return "Float";
                case ChannelType.Int:
                    return "Int";
                case ChannelType.BoolMask:
                    return "Bool Mask";
                case ChannelType.PointList:
                    return "Point List";
                case ChannelType.PrefabPlacementList:
                    return "Prefab Placement";
                default:
                    return channelType.ToString();
            }
        }
    }
}
