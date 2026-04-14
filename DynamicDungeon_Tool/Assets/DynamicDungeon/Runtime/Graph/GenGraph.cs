using System;
using System.Collections.Generic;
using DynamicDungeon.Runtime.Biome;
using DynamicDungeon.Runtime.Component;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Semantic;
using UnityEngine;

namespace DynamicDungeon.Runtime.Graph
{
    public sealed class GenGraph : ScriptableObject
    {
        // Graph identity
        public int SchemaVersion;

        // World settings
        public int WorldWidth = 128;
        public int WorldHeight = 128;
        public long DefaultSeed;
        public SeedMode DefaultSeedMode = SeedMode.Stable;
        public int MaxValidationRetries = 1;

        // Asset references
        public BiomeAsset Biome;
        public TileSemanticRegistry TileSemanticRegistry;

        // Sub-graph scope
        public bool PromoteBlackboardToParentScope;

        // Graph elements
        public List<GenNodeData> Nodes = new List<GenNodeData>();
        public List<GenConnectionData> Connections = new List<GenConnectionData>();
        public List<GenStickyNoteData> StickyNotes = new List<GenStickyNoteData>();
        public List<GenGroupData> Groups = new List<GenGroupData>();
        public List<ExposedProperty> ExposedProperties = new List<ExposedProperty>();

        public GenNodeData AddNode(string nodeTypeName, string displayName, Vector2 position)
        {
            EnsureCollectionsInitialised();

            GenNodeData nodeData = new GenNodeData(Guid.NewGuid().ToString(), nodeTypeName, displayName, position);
            Nodes.Add(nodeData);
            return nodeData;
        }

        public bool RemoveNode(string nodeId)
        {
            EnsureCollectionsInitialised();

            if (string.IsNullOrEmpty(nodeId))
            {
                return false;
            }

            int nodeIndex = FindNodeIndex(nodeId);
            if (nodeIndex < 0)
            {
                return false;
            }

            GenNodeData nodeData = Nodes[nodeIndex];
            if (GraphOutputUtility.IsOutputNode(nodeData))
            {
                return false;
            }

            Nodes.RemoveAt(nodeIndex);

            int connectionIndex;
            for (connectionIndex = Connections.Count - 1; connectionIndex >= 0; connectionIndex--)
            {
                GenConnectionData connection = Connections[connectionIndex];
                if (connection == null)
                {
                    continue;
                }

                if (connection.FromNodeId == nodeId || connection.ToNodeId == nodeId)
                {
                    Connections.RemoveAt(connectionIndex);
                }
            }

            int groupIndex;
            for (groupIndex = 0; groupIndex < Groups.Count; groupIndex++)
            {
                GenGroupData group = Groups[groupIndex];
                if (group != null && group.ContainedNodeIds != null)
                {
                    group.ContainedNodeIds.Remove(nodeId);
                }
            }

            return true;
        }

        public bool AddConnection(string fromNodeId, string fromPortName, string toNodeId, string toPortName)
        {
            EnsureCollectionsInitialised();

            if (string.IsNullOrEmpty(fromNodeId) || string.IsNullOrEmpty(fromPortName) ||
                string.IsNullOrEmpty(toNodeId) || string.IsNullOrEmpty(toPortName))
            {
                return false;
            }

            if (GetNode(fromNodeId) == null || GetNode(toNodeId) == null)
            {
                return false;
            }

            int connectionIndex;
            for (connectionIndex = 0; connectionIndex < Connections.Count; connectionIndex++)
            {
                GenConnectionData existingConnection = Connections[connectionIndex];
                if (existingConnection == null)
                {
                    continue;
                }

                if (existingConnection.FromNodeId == fromNodeId &&
                    existingConnection.FromPortName == fromPortName &&
                    existingConnection.ToNodeId == toNodeId &&
                    existingConnection.ToPortName == toPortName)
                {
                    return false;
                }
            }

            Connections.Add(new GenConnectionData(fromNodeId, fromPortName, toNodeId, toPortName));
            return true;
        }

        public bool RemoveConnection(string fromNodeId, string fromPortName, string toNodeId, string toPortName)
        {
            EnsureCollectionsInitialised();

            int connectionIndex;
            for (connectionIndex = 0; connectionIndex < Connections.Count; connectionIndex++)
            {
                GenConnectionData connection = Connections[connectionIndex];
                if (connection == null)
                {
                    continue;
                }

                if (connection.FromNodeId == fromNodeId &&
                    connection.FromPortName == fromPortName &&
                    connection.ToNodeId == toNodeId &&
                    connection.ToPortName == toPortName)
                {
                    Connections.RemoveAt(connectionIndex);
                    return true;
                }
            }

            return false;
        }

        public GenNodeData GetNode(string nodeId)
        {
            EnsureCollectionsInitialised();

            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < Nodes.Count; nodeIndex++)
            {
                GenNodeData node = Nodes[nodeIndex];
                if (node != null && node.NodeId == nodeId)
                {
                    return node;
                }
            }

            return null;
        }

        public GenStickyNoteData AddStickyNote(string text, Rect position)
        {
            EnsureCollectionsInitialised();

            GenStickyNoteData noteData = new GenStickyNoteData();
            noteData.NoteId = Guid.NewGuid().ToString();
            noteData.Text = text ?? string.Empty;
            noteData.Position = position;
            StickyNotes.Add(noteData);
            return noteData;
        }

        public bool RemoveStickyNote(string noteId)
        {
            EnsureCollectionsInitialised();

            if (string.IsNullOrEmpty(noteId))
            {
                return false;
            }

            int noteIndex = FindStickyNoteIndex(noteId);
            if (noteIndex < 0)
            {
                return false;
            }

            StickyNotes.RemoveAt(noteIndex);
            return true;
        }

        public GenGroupData AddGroup(string title, Rect position)
        {
            EnsureCollectionsInitialised();

            GenGroupData groupData = new GenGroupData();
            groupData.GroupId = Guid.NewGuid().ToString();
            groupData.Title = title ?? string.Empty;
            groupData.Position = position;
            groupData.ContainedNodeIds = new List<string>();
            Groups.Add(groupData);
            return groupData;
        }

        public bool RemoveGroup(string groupId)
        {
            EnsureCollectionsInitialised();

            if (string.IsNullOrEmpty(groupId))
            {
                return false;
            }

            int groupIndex = FindGroupIndex(groupId);
            if (groupIndex < 0)
            {
                return false;
            }

            Groups.RemoveAt(groupIndex);
            return true;
        }

        public bool UpdateGroupMembers(string groupId, List<string> nodeIds)
        {
            EnsureCollectionsInitialised();

            GenGroupData groupData = GetGroup(groupId);
            if (groupData == null)
            {
                return false;
            }

            groupData.ContainedNodeIds = nodeIds != null ? new List<string>(nodeIds) : new List<string>();
            return true;
        }

        public GenGroupData GetGroup(string groupId)
        {
            EnsureCollectionsInitialised();

            int groupIndex;
            for (groupIndex = 0; groupIndex < Groups.Count; groupIndex++)
            {
                GenGroupData group = Groups[groupIndex];
                if (group != null && group.GroupId == groupId)
                {
                    return group;
                }
            }

            return null;
        }

        public ExposedProperty AddExposedProperty(string name, ChannelType type, string defaultValue)
        {
            EnsureCollectionsInitialised();

            if (type != ChannelType.Float && type != ChannelType.Int)
            {
                return null;
            }

            ExposedProperty property = new ExposedProperty();
            property.PropertyId = Guid.NewGuid().ToString();
            property.PropertyName = name ?? string.Empty;
            property.Type = type;
            property.DefaultValue = defaultValue ?? "0";
            ExposedProperties.Add(property);
            return property;
        }

        public bool RemoveExposedProperty(string propertyId)
        {
            EnsureCollectionsInitialised();

            if (string.IsNullOrEmpty(propertyId))
            {
                return false;
            }

            int propertyIndex = FindExposedPropertyIndex(propertyId);
            if (propertyIndex < 0)
            {
                return false;
            }

            ExposedProperties.RemoveAt(propertyIndex);
            return true;
        }

        public ExposedProperty GetExposedProperty(string propertyId)
        {
            EnsureCollectionsInitialised();

            int propertyIndex;
            for (propertyIndex = 0; propertyIndex < ExposedProperties.Count; propertyIndex++)
            {
                ExposedProperty property = ExposedProperties[propertyIndex];
                if (property != null && property.PropertyId == propertyId)
                {
                    return property;
                }
            }

            return null;
        }

        public ExposedProperty GetExposedPropertyByName(string propertyName)
        {
            EnsureCollectionsInitialised();

            int propertyIndex;
            for (propertyIndex = 0; propertyIndex < ExposedProperties.Count; propertyIndex++)
            {
                ExposedProperty property = ExposedProperties[propertyIndex];
                if (property != null && string.Equals(property.PropertyName, propertyName, StringComparison.Ordinal))
                {
                    return property;
                }
            }

            return null;
        }

        private void OnEnable()
        {
            EnsureCollectionsInitialised();
        }

        private void EnsureCollectionsInitialised()
        {
            if (Nodes == null)
            {
                Nodes = new List<GenNodeData>();
            }

            if (Connections == null)
            {
                Connections = new List<GenConnectionData>();
            }

            if (StickyNotes == null)
            {
                StickyNotes = new List<GenStickyNoteData>();
            }

            if (Groups == null)
            {
                Groups = new List<GenGroupData>();
            }

            if (ExposedProperties == null)
            {
                ExposedProperties = new List<ExposedProperty>();
            }
        }

        private int FindNodeIndex(string nodeId)
        {
            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < Nodes.Count; nodeIndex++)
            {
                GenNodeData node = Nodes[nodeIndex];
                if (node != null && node.NodeId == nodeId)
                {
                    return nodeIndex;
                }
            }

            return -1;
        }

        private int FindStickyNoteIndex(string noteId)
        {
            int noteIndex;
            for (noteIndex = 0; noteIndex < StickyNotes.Count; noteIndex++)
            {
                GenStickyNoteData note = StickyNotes[noteIndex];
                if (note != null && note.NoteId == noteId)
                {
                    return noteIndex;
                }
            }

            return -1;
        }

        private int FindGroupIndex(string groupId)
        {
            int groupIndex;
            for (groupIndex = 0; groupIndex < Groups.Count; groupIndex++)
            {
                GenGroupData group = Groups[groupIndex];
                if (group != null && group.GroupId == groupId)
                {
                    return groupIndex;
                }
            }

            return -1;
        }

        private int FindExposedPropertyIndex(string propertyId)
        {
            int propertyIndex;
            for (propertyIndex = 0; propertyIndex < ExposedProperties.Count; propertyIndex++)
            {
                ExposedProperty property = ExposedProperties[propertyIndex];
                if (property != null && property.PropertyId == propertyId)
                {
                    return propertyIndex;
                }
            }

            return -1;
        }
    }
}
