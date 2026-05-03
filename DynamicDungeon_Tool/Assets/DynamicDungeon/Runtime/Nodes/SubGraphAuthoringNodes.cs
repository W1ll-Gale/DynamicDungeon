using System;
using System.Collections.Generic;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using Unity.Jobs;

namespace DynamicDungeon.Runtime.Nodes
{
    [HideInNodeSearch]
    [SubGraphNode(SubGraphNode.NestedGraphParameterName)]
    public sealed class SubGraphNode : IGenNode
    {
        public const string NestedGraphParameterName = "NestedGraph";
        public const string NestedGraphPathParameterName = "NestedGraphPath";

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly GenGraph _nestedGraph;
        private readonly string _nodeId;
        private readonly string _nodeName;
        private readonly NodePortDefinition[] _ports;
        private readonly ChannelDeclaration[] _channelDeclarations;

        public IReadOnlyList<NodePortDefinition> Ports => _ports;
        public IReadOnlyList<ChannelDeclaration> ChannelDeclarations => _channelDeclarations;
        public IReadOnlyList<BlackboardKey> BlackboardDeclarations => _blackboardDeclarations;
        public string NodeId => _nodeId;
        public string NodeName => _nodeName;
        public GenGraph NestedGraph => _nestedGraph;

        public SubGraphNode(GenNodeData nodeData)
        {
            if (nodeData == null)
            {
                throw new ArgumentNullException(nameof(nodeData));
            }

            if (string.IsNullOrWhiteSpace(nodeData.NodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeData));
            }

            _nodeId = nodeData.NodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeData.NodeName) ? "Sub Graph" : nodeData.NodeName;
            _nestedGraph = ResolveNestedGraph(nodeData.Parameters);
            _ports = BuildPorts(nodeData.Ports);
            _channelDeclarations = BuildWrapperDeclarations(_ports);
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            return context.InputDependency;
        }

        private static GenGraph ResolveNestedGraph(IReadOnlyList<SerializedParameter> parameters)
        {
            if (parameters == null)
            {
                return null;
            }

            for (int parameterIndex = 0; parameterIndex < parameters.Count; parameterIndex++)
            {
                SerializedParameter parameter = parameters[parameterIndex];
                if (parameter == null)
                {
                    continue;
                }

                if (string.Equals(parameter.Name, NestedGraphParameterName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(parameter.Name, NestedGraphPathParameterName, StringComparison.OrdinalIgnoreCase))
                {
                    GenGraph graph = parameter.ObjectReference as GenGraph;
                    if (graph != null)
                    {
                        return graph;
                    }
                }
            }

            return null;
        }

        private static NodePortDefinition[] BuildPorts(IReadOnlyList<GenPortData> ports)
        {
            if (ports == null || ports.Count == 0)
            {
                return Array.Empty<NodePortDefinition>();
            }

            List<NodePortDefinition> definitions = new List<NodePortDefinition>(ports.Count);
            for (int portIndex = 0; portIndex < ports.Count; portIndex++)
            {
                GenPortData port = ports[portIndex];
                if (port == null || string.IsNullOrWhiteSpace(port.PortName))
                {
                    continue;
                }

                PortCapacity capacity = port.Direction == PortDirection.Input
                    ? PortCapacity.Single
                    : PortCapacity.Multi;
                definitions.Add(new NodePortDefinition(
                    port.PortName,
                    port.Direction,
                    port.Type,
                    capacity,
                    false,
                    null,
                    port.DisplayName));
            }

            return definitions.ToArray();
        }

        private static ChannelDeclaration[] BuildWrapperDeclarations(IReadOnlyList<NodePortDefinition> ports)
        {
            if (ports == null || ports.Count == 0)
            {
                return Array.Empty<ChannelDeclaration>();
            }

            List<ChannelDeclaration> declarations = new List<ChannelDeclaration>(ports.Count);
            for (int portIndex = 0; portIndex < ports.Count; portIndex++)
            {
                NodePortDefinition port = ports[portIndex];
                declarations.Add(new ChannelDeclaration(
                    port.Name,
                    port.Type,
                    port.Direction == PortDirection.Output));
            }

            return declarations.ToArray();
        }
    }

    [HideInNodeSearch]
    public sealed class SubGraphInputNode : IGenNode
    {
        public const string DefaultNodeName = "Sub Graph Input";

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private readonly NodePortDefinition[] _ports;
        private readonly ChannelDeclaration[] _channelDeclarations;

        public IReadOnlyList<NodePortDefinition> Ports => _ports;
        public IReadOnlyList<ChannelDeclaration> ChannelDeclarations => _channelDeclarations;
        public IReadOnlyList<BlackboardKey> BlackboardDeclarations => _blackboardDeclarations;
        public string NodeId => _nodeId;
        public string NodeName => _nodeName;

        public SubGraphInputNode(GenNodeData nodeData)
        {
            if (nodeData == null)
            {
                throw new ArgumentNullException(nameof(nodeData));
            }

            _nodeId = nodeData.NodeId ?? string.Empty;
            _nodeName = string.IsNullOrWhiteSpace(nodeData.NodeName) ? DefaultNodeName : nodeData.NodeName;
            _ports = BuildPorts(nodeData.Ports);
            _channelDeclarations = BuildOutputDeclarations(_ports);
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            return context.InputDependency;
        }

        private static NodePortDefinition[] BuildPorts(IReadOnlyList<GenPortData> ports)
        {
            if (ports == null || ports.Count == 0)
            {
                return Array.Empty<NodePortDefinition>();
            }

            List<NodePortDefinition> definitions = new List<NodePortDefinition>(ports.Count);
            for (int portIndex = 0; portIndex < ports.Count; portIndex++)
            {
                GenPortData port = ports[portIndex];
                if (port == null || string.IsNullOrWhiteSpace(port.PortName))
                {
                    continue;
                }

                definitions.Add(new NodePortDefinition(
                    port.PortName,
                    PortDirection.Output,
                    port.Type,
                    PortCapacity.Multi,
                    false,
                    null,
                    port.DisplayName));
            }

            return definitions.ToArray();
        }

        private static ChannelDeclaration[] BuildOutputDeclarations(IReadOnlyList<NodePortDefinition> ports)
        {
            List<ChannelDeclaration> declarations = new List<ChannelDeclaration>(ports.Count);
            for (int portIndex = 0; portIndex < ports.Count; portIndex++)
            {
                NodePortDefinition port = ports[portIndex];
                declarations.Add(new ChannelDeclaration(port.Name, port.Type, true));
            }

            return declarations.ToArray();
        }
    }

    [HideInNodeSearch]
    public sealed class SubGraphOutputNode : IGenNode, IInputConnectionReceiver
    {
        public const string DefaultNodeName = "Sub Graph Output";

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private readonly NodePortDefinition[] _ports;
        private ChannelDeclaration[] _channelDeclarations;

        public IReadOnlyList<NodePortDefinition> Ports => _ports;
        public IReadOnlyList<ChannelDeclaration> ChannelDeclarations => _channelDeclarations;
        public IReadOnlyList<BlackboardKey> BlackboardDeclarations => _blackboardDeclarations;
        public string NodeId => _nodeId;
        public string NodeName => _nodeName;

        public SubGraphOutputNode(GenNodeData nodeData)
        {
            if (nodeData == null)
            {
                throw new ArgumentNullException(nameof(nodeData));
            }

            _nodeId = nodeData.NodeId ?? string.Empty;
            _nodeName = string.IsNullOrWhiteSpace(nodeData.NodeName) ? DefaultNodeName : nodeData.NodeName;
            _ports = BuildPorts(nodeData.Ports);
            _channelDeclarations = Array.Empty<ChannelDeclaration>();
        }

        public void ReceiveInputConnections(InputConnectionMap inputConnections)
        {
            List<ChannelDeclaration> declarations = new List<ChannelDeclaration>();
            for (int portIndex = 0; portIndex < _ports.Length; portIndex++)
            {
                NodePortDefinition port = _ports[portIndex];
                IReadOnlyList<string> channelNames = inputConnections != null
                    ? inputConnections.GetAll(port.Name)
                    : Array.Empty<string>();

                for (int channelIndex = 0; channelIndex < channelNames.Count; channelIndex++)
                {
                    string channelName = channelNames[channelIndex];
                    if (!string.IsNullOrWhiteSpace(channelName))
                    {
                        declarations.Add(new ChannelDeclaration(channelName, port.Type, false));
                    }
                }
            }

            _channelDeclarations = declarations.ToArray();
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            return context.InputDependency;
        }

        private static NodePortDefinition[] BuildPorts(IReadOnlyList<GenPortData> ports)
        {
            if (ports == null || ports.Count == 0)
            {
                return Array.Empty<NodePortDefinition>();
            }

            List<NodePortDefinition> definitions = new List<NodePortDefinition>(ports.Count);
            for (int portIndex = 0; portIndex < ports.Count; portIndex++)
            {
                GenPortData port = ports[portIndex];
                if (port == null || string.IsNullOrWhiteSpace(port.PortName))
                {
                    continue;
                }

                definitions.Add(new NodePortDefinition(
                    port.PortName,
                    PortDirection.Input,
                    port.Type,
                    PortCapacity.Multi,
                    false,
                    null,
                    port.DisplayName));
            }

            return definitions.ToArray();
        }
    }
}
