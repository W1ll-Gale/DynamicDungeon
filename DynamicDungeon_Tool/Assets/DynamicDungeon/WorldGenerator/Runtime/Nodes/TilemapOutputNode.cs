using System;
using System.Collections.Generic;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using Unity.Jobs;
using UnityEngine;


namespace DynamicDungeon.Runtime.Nodes
{
    [NodeCategory("Output")]
    [NodeDisplayName("Output")]
    [HelpURL("https://dynamicdungeon.mrbytesized.com/docs/nodes/utility/tilemap-output")]
    public sealed class TilemapOutputNode : IGenNode, IInputConnectionReceiver
    {
        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private readonly NodePortDefinition[] _ports;

        private ChannelDeclaration[] _channelDeclarations;
        private string _logicalIdsChannelName;
        private string _biomeChannelName;
        private string _prefabPlacementsChannelName;

        public IReadOnlyList<NodePortDefinition> Ports
        {
            get
            {
                return _ports;
            }
        }

        public IReadOnlyList<ChannelDeclaration> ChannelDeclarations
        {
            get
            {
                return _channelDeclarations;
            }
        }

        public IReadOnlyList<BlackboardKey> BlackboardDeclarations
        {
            get
            {
                return _blackboardDeclarations;
            }
        }

        public string NodeId
        {
            get
            {
                return _nodeId;
            }
        }

        public string NodeName
        {
            get
            {
                return _nodeName;
            }
        }

        public TilemapOutputNode(string nodeId, string nodeName)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? GraphOutputUtility.OutputNodeDisplayName : nodeName;
            _ports = new[]
            {
                new NodePortDefinition(GraphOutputUtility.OutputInputPortName, PortDirection.Input, ChannelType.Int, PortCapacity.Single, true, "Primary logical ID channel used to render tiles.", "Logical IDs"),
                new NodePortDefinition(GraphOutputUtility.BiomeInputPortName, PortDirection.Input, ChannelType.Int, PortCapacity.Single, false, "Optional biome channel used by biome-aware output passes.", "Biomes"),
                new NodePortDefinition(GraphOutputUtility.PrefabPlacementInputPortName, PortDirection.Input, ChannelType.PrefabPlacementList, PortCapacity.Multi, false, "Optional prefab placement writers used by placement output passes.", "Placements")
            };

            RefreshChannelDeclarations();
        }

        public void ReceiveInputConnections(InputConnectionMap inputConnections)
        {
            string inputChannelName;
            if (inputConnections != null &&
                inputConnections.TryGetValue(GraphOutputUtility.OutputInputPortName, out inputChannelName))
            {
                _logicalIdsChannelName = inputChannelName ?? string.Empty;
            }
            else
            {
                _logicalIdsChannelName = string.Empty;
            }

            _biomeChannelName = ResolveOptionalInput(inputConnections, GraphOutputUtility.BiomeInputPortName);
            _prefabPlacementsChannelName = ResolveOptionalInput(inputConnections, GraphOutputUtility.PrefabPlacementInputPortName);

            RefreshChannelDeclarations();
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            return context.InputDependency;
        }

        private void RefreshChannelDeclarations()
        {
            List<ChannelDeclaration> declarations = new List<ChannelDeclaration>();
            if (!string.IsNullOrWhiteSpace(_logicalIdsChannelName))
            {
                declarations.Add(new ChannelDeclaration(_logicalIdsChannelName, ChannelType.Int, false));
            }

            if (!string.IsNullOrWhiteSpace(_biomeChannelName))
            {
                declarations.Add(new ChannelDeclaration(_biomeChannelName, ChannelType.Int, false));
            }

            if (!string.IsNullOrWhiteSpace(_prefabPlacementsChannelName))
            {
                declarations.Add(new ChannelDeclaration(_prefabPlacementsChannelName, ChannelType.PrefabPlacementList, false));
            }

            _channelDeclarations = declarations.Count == 0
                ? Array.Empty<ChannelDeclaration>()
                : declarations.ToArray();
        }

        private static string ResolveOptionalInput(InputConnectionMap inputConnections, string portName)
        {
            string channelName;
            if (inputConnections != null &&
                inputConnections.TryGetValue(portName, out channelName))
            {
                return channelName ?? string.Empty;
            }

            return string.Empty;
        }
    }
}
