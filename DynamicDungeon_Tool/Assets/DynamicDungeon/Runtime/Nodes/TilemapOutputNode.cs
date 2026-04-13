using System;
using System.Collections.Generic;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using Unity.Jobs;

namespace DynamicDungeon.Runtime.Nodes
{
    [NodeCategory("Output")]
    [NodeDisplayName("Output")]
    public sealed class TilemapOutputNode : IGenNode, IInputConnectionReceiver
    {
        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private readonly NodePortDefinition[] _ports;

        private ChannelDeclaration[] _channelDeclarations;
        private string _inputChannelName;

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
                new NodePortDefinition(GraphOutputUtility.OutputInputPortName, PortDirection.Input, ChannelType.Int, PortCapacity.Single, true)
            };

            RefreshChannelDeclarations();
        }

        public void ReceiveInputConnections(IReadOnlyDictionary<string, string> inputConnections)
        {
            string inputChannelName;
            if (inputConnections != null &&
                inputConnections.TryGetValue(GraphOutputUtility.OutputInputPortName, out inputChannelName))
            {
                _inputChannelName = inputChannelName ?? string.Empty;
            }
            else
            {
                _inputChannelName = string.Empty;
            }

            RefreshChannelDeclarations();
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            return context.InputDependency;
        }

        private void RefreshChannelDeclarations()
        {
            if (string.IsNullOrWhiteSpace(_inputChannelName))
            {
                _channelDeclarations = Array.Empty<ChannelDeclaration>();
                return;
            }

            _channelDeclarations = new[]
            {
                new ChannelDeclaration(_inputChannelName, ChannelType.Int, false)
            };
        }
    }
}
