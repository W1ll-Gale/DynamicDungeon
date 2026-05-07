using System;
using System.Collections.Generic;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using System.Globalization;
using DynamicDungeon.Runtime.Biome;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace DynamicDungeon.Runtime.Nodes
{
    [NodeCategory("Biome")]
    [NodeDisplayName("Biome Merge")]
    [Description("Merges two complete biome channels using a float mask. Outputs the layer biome where mask > 0.5, else base biome.")]
    public sealed class BiomeMergeNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        private const int DefaultBatchSize = 64;
        private const string DefaultNodeName = "Biome Merge";
        private const string BasePortName = "Base Biome";
        private const string LayerPortName = "Layer Biome";
        private const string MaskPortName = "Mask";
        private const string FallbackOutputPortName = BiomeChannelUtility.ChannelName;

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private readonly NodePortDefinition[] _ports;

        private string _inputBaseChannelName;
        private string _inputLayerChannelName;
        private string _inputMaskChannelName;
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

        public BiomeMergeNode(
            string nodeId,
            string nodeName,
            string inputBaseChannelName = "",
            string inputLayerChannelName = "",
            string inputMaskChannelName = "")
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _inputBaseChannelName = inputBaseChannelName ?? string.Empty;
            _inputLayerChannelName = inputLayerChannelName ?? string.Empty;
            _inputMaskChannelName = inputMaskChannelName ?? string.Empty;

            _ports = new[]
            {
                new NodePortDefinition(BasePortName, PortDirection.Input, ChannelType.Int, PortCapacity.Single, true),
                new NodePortDefinition(LayerPortName, PortDirection.Input, ChannelType.Int, PortCapacity.Single, true),
                new NodePortDefinition(MaskPortName, PortDirection.Input, ChannelType.Float, PortCapacity.Single, true),
                new NodePortDefinition(FallbackOutputPortName, PortDirection.Output, ChannelType.Int, displayName: "Biomes")
            };

            RefreshChannelDeclarations();
        }

        public void ReceiveInputConnections(InputConnectionMap inputConnections)
        {
            _inputBaseChannelName = ResolveInput(inputConnections, BasePortName);
            _inputLayerChannelName = ResolveInput(inputConnections, LayerPortName);
            _inputMaskChannelName = ResolveInput(inputConnections, MaskPortName);

            RefreshChannelDeclarations();
        }

        private string ResolveInput(IReadOnlyDictionary<string, string> connections, string portName)
        {
            if (connections != null && connections.TryGetValue(portName, out string channelName))
            {
                return channelName ?? string.Empty;
            }
            return string.Empty;
        }

        public void ReceiveParameter(string name, string value)
        {
            // No custom parameters for this node
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            NativeArray<int> baseInput = context.GetIntChannel(_inputBaseChannelName);
            NativeArray<int> layerInput = context.GetIntChannel(_inputLayerChannelName);
            NativeArray<float> maskInput = context.GetFloatChannel(_inputMaskChannelName);
            NativeArray<int> output = context.GetIntChannel(FallbackOutputPortName);

            BiomeMergeJob job = new BiomeMergeJob
            {
                BaseInput = baseInput,
                LayerInput = layerInput,
                MaskInput = maskInput,
                Output = output
            };

            return job.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
        }

        private void RefreshChannelDeclarations()
        {
            List<ChannelDeclaration> declarations = new List<ChannelDeclaration>();

            if (!string.IsNullOrWhiteSpace(_inputBaseChannelName))
                declarations.Add(new ChannelDeclaration(_inputBaseChannelName, ChannelType.Int, false));
            if (!string.IsNullOrWhiteSpace(_inputLayerChannelName))
                declarations.Add(new ChannelDeclaration(_inputLayerChannelName, ChannelType.Int, false));
            if (!string.IsNullOrWhiteSpace(_inputMaskChannelName))
                declarations.Add(new ChannelDeclaration(_inputMaskChannelName, ChannelType.Float, false));

            declarations.Add(new ChannelDeclaration(FallbackOutputPortName, ChannelType.Int, true));

            _channelDeclarations = declarations.ToArray();
        }

        [BurstCompile]
        private struct BiomeMergeJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<int> BaseInput;
            [ReadOnly] public NativeArray<int> LayerInput;
            [ReadOnly] public NativeArray<float> MaskInput;

            public NativeArray<int> Output;

            public void Execute(int index)
            {
                Output[index] = MaskInput[index] > 0.5f ? LayerInput[index] : BaseInput[index];
            }
        }
    }
}
