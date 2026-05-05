using System;
using System.Collections.Generic;
using UnityEngine;

namespace DynamicDungeon.Runtime.Graph
{
    [Serializable]
    public sealed class GenNodeData
    {
        public string NodeId = string.Empty;
        public string NodeTypeName = string.Empty;
        public string NodeName = string.Empty;
        public Vector2 Position;
        public List<GenPortData> Ports = new List<GenPortData>();
        public List<SerializedParameter> Parameters = new List<SerializedParameter>();

        public GenNodeData()
        {
        }

        public GenNodeData(string nodeId, string nodeTypeName, string nodeName, Vector2 position)
        {
            NodeId = nodeId ?? string.Empty;
            NodeTypeName = nodeTypeName ?? string.Empty;
            NodeName = nodeName ?? string.Empty;
            Position = position;
        }
    }
}
