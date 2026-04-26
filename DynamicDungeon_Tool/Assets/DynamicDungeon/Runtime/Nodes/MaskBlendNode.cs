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
    [NodeCategory("Blend")]
    [NodeDisplayName("Mask Blend")]
    [Description("Selects per tile between float inputs A and B using a bool mask.")]
    public sealed class MaskBlendNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        private const int DefaultBatchSize = 64;
        private const string DefaultNodeName = "Mask Blend";
        private const string InputAPortName = "A";
        private const string InputBPortName = "B";
        private const string MaskPortName = "Mask";
        private const string FallbackOutputPortName = "Output";
        private const string PreferredOutputDisplayName = FallbackOutputPortName;

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private readonly string _outputChannelName;
        private readonly NodePortDefinition[] _ports;

        private string _inputAChannelName;
        private string _inputBChannelName;
        private string _maskChannelName;
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

        public MaskBlendNode(string nodeId, string nodeName, string inputAChannelName, string inputBChannelName, string maskChannelName, string outputChannelName)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _inputAChannelName = inputAChannelName ?? string.Empty;
            _inputBChannelName = inputBChannelName ?? string.Empty;
            _maskChannelName = maskChannelName ?? string.Empty;
            _outputChannelName = string.IsNullOrWhiteSpace(outputChannelName) ? FallbackOutputPortName : outputChannelName;
            string outputPortDisplayName = GraphPortNameUtility.ResolveOutputDisplayName(nodeId, _outputChannelName, PreferredOutputDisplayName);
            _ports = new[]
            {
                new NodePortDefinition(InputAPortName, PortDirection.Input, ChannelType.Float, PortCapacity.Single, true),
                new NodePortDefinition(InputBPortName, PortDirection.Input, ChannelType.Float, PortCapacity.Single, true),
                new NodePortDefinition(MaskPortName, PortDirection.Input, ChannelType.BoolMask, PortCapacity.Single, true),
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.Float, displayName: outputPortDisplayName)
            };

            RefreshChannelDeclarations();
        }

        public void ReceiveInputConnections(IReadOnlyDictionary<string, string> inputConnections)
        {
            _inputAChannelName = ResolveInputConnection(inputConnections, InputAPortName);
            _inputBChannelName = ResolveInputConnection(inputConnections, InputBPortName);
            _maskChannelName = ResolveInputConnection(inputConnections, MaskPortName);
            RefreshChannelDeclarations();
        }

        public void ReceiveParameter(string name, string value)
        {
            return;
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            NativeArray<float> inputA = context.GetFloatChannel(_inputAChannelName);
            NativeArray<float> inputB = context.GetFloatChannel(_inputBChannelName);
            NativeArray<byte> mask = context.GetBoolMaskChannel(_maskChannelName);
            NativeArray<float> output = context.GetFloatChannel(_outputChannelName);
            MaskBlendJob job = new MaskBlendJob
            {
                InputA = inputA,
                InputB = inputB,
                Mask = mask,
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

            if (!string.IsNullOrWhiteSpace(_maskChannelName))
            {
                channelDeclarations.Add(new ChannelDeclaration(_maskChannelName, ChannelType.BoolMask, false));
            }

            channelDeclarations.Add(new ChannelDeclaration(_outputChannelName, ChannelType.Float, true));
            _channelDeclarations = channelDeclarations.ToArray();
        }

        [BurstCompile]
        private struct MaskBlendJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<float> InputA;

            [ReadOnly]
            public NativeArray<float> InputB;

            [ReadOnly]
            public NativeArray<byte> Mask;

            public NativeArray<float> Output;

            public void Execute(int index)
            {
                Output[index] = Mask[index] != 0 ? InputA[index] : InputB[index];
            }
        }
    }
}
