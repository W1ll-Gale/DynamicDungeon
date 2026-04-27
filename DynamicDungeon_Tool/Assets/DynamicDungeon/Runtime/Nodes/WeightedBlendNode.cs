using System;
using System.Collections.Generic;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace DynamicDungeon.Runtime.Nodes
{
    [NodeCategory("Blend")]
    [NodeDisplayName("Weighted Blend")]
    [Description("Blends float inputs A and B using a per-tile weight clamped to the 0 to 1 range.")]
    public sealed class WeightedBlendNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        private const int DefaultBatchSize = 64;
        private const string DefaultNodeName = "Weighted Blend";
        private const string InputAPortName = "A";
        private const string InputBPortName = "B";
        private const string WeightPortName = "Weight";
        private const string FallbackOutputPortName = "Output";
        private const string PreferredOutputDisplayName = FallbackOutputPortName;

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private string _outputChannelName;
        private NodePortDefinition[] _ports;

        private string _inputAChannelName;
        private string _inputBChannelName;
        private string _weightChannelName;
        private ChannelDeclaration[] _channelDeclarations;

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

        public WeightedBlendNode(string nodeId, string nodeName, string inputAChannelName, string inputBChannelName, string weightChannelName, string outputChannelName)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _inputAChannelName = inputAChannelName ?? string.Empty;
            _inputBChannelName = inputBChannelName ?? string.Empty;
            _weightChannelName = weightChannelName ?? string.Empty;
            _outputChannelName = string.IsNullOrWhiteSpace(outputChannelName) || string.Equals(outputChannelName, GraphPortNameUtility.LegacyGenericOutputDisplayName, StringComparison.Ordinal) ? GraphPortNameUtility.CreateGeneratedOutputPortName(nodeId, FallbackOutputPortName) : outputChannelName;

            RefreshPorts();
            RefreshChannelDeclarations();
        }

        private void RefreshPorts()
        {
            string outputPortDisplayName = GraphPortNameUtility.ResolveOutputDisplayName(_nodeId, _outputChannelName, PreferredOutputDisplayName);
            _ports = new[]
            {
                new NodePortDefinition(InputAPortName, PortDirection.Input, ChannelType.Float, PortCapacity.Single, true),
                new NodePortDefinition(InputBPortName, PortDirection.Input, ChannelType.Float, PortCapacity.Single, true),
                new NodePortDefinition(WeightPortName, PortDirection.Input, ChannelType.Float, PortCapacity.Single, true),
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.Float, displayName: outputPortDisplayName)
            };
        }

        public void ReceiveInputConnections(IReadOnlyDictionary<string, string> inputConnections)
        {
            _inputAChannelName = ResolveInputConnection(inputConnections, InputAPortName);
            _inputBChannelName = ResolveInputConnection(inputConnections, InputBPortName);
            _weightChannelName = ResolveInputConnection(inputConnections, WeightPortName);
            RefreshChannelDeclarations();
        }

        public void ReceiveParameter(string name, string value)
        {
            if (string.Equals(name, "outputChannelName", StringComparison.OrdinalIgnoreCase))
            {
                _outputChannelName = string.IsNullOrWhiteSpace(value) || string.Equals(value, GraphPortNameUtility.LegacyGenericOutputDisplayName, StringComparison.Ordinal) ? GraphPortNameUtility.CreateGeneratedOutputPortName(_nodeId, FallbackOutputPortName) : value;
                RefreshPorts();
                RefreshChannelDeclarations();
            }
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            NativeArray<float> inputA = context.GetFloatChannel(_inputAChannelName);
            NativeArray<float> inputB = context.GetFloatChannel(_inputBChannelName);
            NativeArray<float> weight = context.GetFloatChannel(_weightChannelName);
            NativeArray<float> output = context.GetFloatChannel(_outputChannelName);
            WeightedBlendJob job = new WeightedBlendJob
            {
                InputA = inputA,
                InputB = inputB,
                Weight = weight,
                Output = output
            };

            return job.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
        }

        private static string ResolveInputConnection(IReadOnlyDictionary<string, string> inputConnections, string portName)
        {
            string inputChannelName;
            if (inputConnections != null && inputConnections.TryGetValue(portName, out inputChannelName))
            {
                return inputChannelName ?? string.Empty;
            }

            return string.Empty;
        }

        private void RefreshChannelDeclarations()
        {
            List<ChannelDeclaration> channelDeclarations = new List<ChannelDeclaration>(4);

            if (!string.IsNullOrWhiteSpace(_inputAChannelName))
            {
                channelDeclarations.Add(new ChannelDeclaration(_inputAChannelName, ChannelType.Float, false));
            }

            if (!string.IsNullOrWhiteSpace(_inputBChannelName))
            {
                channelDeclarations.Add(new ChannelDeclaration(_inputBChannelName, ChannelType.Float, false));
            }

            if (!string.IsNullOrWhiteSpace(_weightChannelName))
            {
                channelDeclarations.Add(new ChannelDeclaration(_weightChannelName, ChannelType.Float, false));
            }

            channelDeclarations.Add(new ChannelDeclaration(_outputChannelName, ChannelType.Float, true));
            _channelDeclarations = channelDeclarations.ToArray();
        }

        [BurstCompile]
        private struct WeightedBlendJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<float> InputA;

            [ReadOnly]
            public NativeArray<float> InputB;

            [ReadOnly]
            public NativeArray<float> Weight;

            public NativeArray<float> Output;

            public void Execute(int index)
            {
                float weightValue = math.clamp(Weight[index], 0.0f, 1.0f);
                Output[index] = math.lerp(InputA[index], InputB[index], weightValue);
            }
        }
    }
}
