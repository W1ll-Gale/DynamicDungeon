using System;
using System.Collections.Generic;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace DynamicDungeon.Runtime.Nodes
{
    [NodeCategory("Utility")]
    [NodeDisplayName("Copy Int Channel")]
    [Description("Copies an int channel to a named output channel so later graph stages can preserve an intermediate result.")]
    public sealed class IntChannelCopyNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        private const int DefaultBatchSize = 64;
        private const string DefaultNodeName = "Copy Int Channel";
        private const string InputPortName = "Input";
        private const string FallbackOutputPortName = "CopiedInt";

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private string _inputChannelName;
        private string _outputChannelName;
        private NodePortDefinition[] _ports;
        private ChannelDeclaration[] _channelDeclarations;

        public IReadOnlyList<NodePortDefinition> Ports
        {
            get { return _ports; }
        }

        public IReadOnlyList<ChannelDeclaration> ChannelDeclarations
        {
            get { return _channelDeclarations; }
        }

        public IReadOnlyList<BlackboardKey> BlackboardDeclarations
        {
            get { return _blackboardDeclarations; }
        }

        public string NodeId
        {
            get { return _nodeId; }
        }

        public string NodeName
        {
            get { return _nodeName; }
        }

        public IntChannelCopyNode(string nodeId, string nodeName, string inputChannelName = "", string outputChannelName = "")
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _inputChannelName = inputChannelName ?? string.Empty;
            _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(nodeId, outputChannelName, FallbackOutputPortName);

            RefreshPorts();
            RefreshChannelDeclarations();
        }

        public void ReceiveInputConnections(IReadOnlyDictionary<string, string> inputConnections)
        {
            string inputChannelName;
            if (inputConnections != null && inputConnections.TryGetValue(InputPortName, out inputChannelName))
            {
                _inputChannelName = inputChannelName ?? string.Empty;
            }
            else
            {
                _inputChannelName = string.Empty;
            }

            RefreshChannelDeclarations();
        }

        public void ReceiveParameter(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (string.Equals(name, "outputChannelName", StringComparison.OrdinalIgnoreCase))
            {
                _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(_nodeId, value, FallbackOutputPortName);
                RefreshPorts();
                RefreshChannelDeclarations();
            }
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            NativeArray<int> input = context.GetIntChannel(_inputChannelName);
            NativeArray<int> output = context.GetIntChannel(_outputChannelName);

            IntChannelCopyJob job = new IntChannelCopyJob
            {
                Input = input,
                Output = output
            };

            return job.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
        }

        private void RefreshPorts()
        {
            string outputPortDisplayName = GraphPortNameUtility.ResolveOutputDisplayName(_nodeId, _outputChannelName, FallbackOutputPortName);
            _ports = new[]
            {
                new NodePortDefinition(InputPortName, PortDirection.Input, ChannelType.Int, PortCapacity.Single, true),
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.Int, displayName: outputPortDisplayName)
            };
        }

        private void RefreshChannelDeclarations()
        {
            List<ChannelDeclaration> declarations = new List<ChannelDeclaration>(2);
            if (!string.IsNullOrWhiteSpace(_inputChannelName))
            {
                declarations.Add(new ChannelDeclaration(_inputChannelName, ChannelType.Int, false));
            }

            declarations.Add(new ChannelDeclaration(_outputChannelName, ChannelType.Int, true));
            _channelDeclarations = declarations.ToArray();
        }

        [BurstCompile]
        private struct IntChannelCopyJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<int> Input;

            public NativeArray<int> Output;

            public void Execute(int index)
            {
                Output[index] = Input[index];
            }
        }
    }
}
