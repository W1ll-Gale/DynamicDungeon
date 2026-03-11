using System;
using System.Collections.Generic;
using UnityEngine;

namespace DynamicDungeon.Runtime.Graph
{
    [CreateAssetMenu(fileName = "GenGraph", menuName = "DynamicDungeon/Generation Graph")]
    public sealed class GenGraph : ScriptableObject
    {
        public int SchemaVersion;
        public int WorldWidth = 128;
        public int WorldHeight = 128;
        public long DefaultSeed;
        public List<GenNodeData> Nodes = new List<GenNodeData>();
        public List<GenConnectionData> Connections = new List<GenConnectionData>();

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
    }
}
