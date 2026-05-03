using System;
using System.Collections.Generic;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using DynamicDungeon.Runtime.Biome;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace DynamicDungeon.Runtime.Nodes
{
    [NodeCategory("Biome")]
    [NodeDisplayName("Biome Weight Blend")]
    [Description("Blends up to 4 biomes based on float weight channels. The biome with the highest weight wins per cell.")]
    public sealed class BiomeWeightBlendNode : IGenNode, IInputConnectionReceiver, IParameterReceiver, IBiomeChannelNode
    {
        private const int DefaultBatchSize = 64;
        private const string DefaultNodeName = "Biome Weight Blend";

        // Up to 4 inputs for this node
        private const string Weight1PortName = "Weight 1";
        private const string Weight2PortName = "Weight 2";
        private const string Weight3PortName = "Weight 3";
        private const string Weight4PortName = "Weight 4";
        private const string FallbackOutputPortName = BiomeChannelUtility.ChannelName;

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private readonly NodePortDefinition[] _ports;

        [AssetGuidReference(typeof(BiomeAsset))]
        [Description("Biome asset for Weight 1.")]
        private string _biome1;

        [AssetGuidReference(typeof(BiomeAsset))]
        [Description("Biome asset for Weight 2.")]
        private string _biome2;

        [AssetGuidReference(typeof(BiomeAsset))]
        [Description("Biome asset for Weight 3.")]
        private string _biome3;

        [AssetGuidReference(typeof(BiomeAsset))]
        [Description("Biome asset for Weight 4.")]
        private string _biome4;

        private string _input1ChannelName;
        private string _input2ChannelName;
        private string _input3ChannelName;
        private string _input4ChannelName;

        private int _resolvedBiome1Index = BiomeChannelUtility.UnassignedBiomeIndex;
        private int _resolvedBiome2Index = BiomeChannelUtility.UnassignedBiomeIndex;
        private int _resolvedBiome3Index = BiomeChannelUtility.UnassignedBiomeIndex;
        private int _resolvedBiome4Index = BiomeChannelUtility.UnassignedBiomeIndex;

        private ChannelDeclaration[] _channelDeclarations;

        public IReadOnlyList<NodePortDefinition> Ports => _ports;
        public IReadOnlyList<ChannelDeclaration> ChannelDeclarations => _channelDeclarations;
        public IReadOnlyList<BlackboardKey> BlackboardDeclarations => _blackboardDeclarations;
        public string NodeId => _nodeId;
        public string NodeName => _nodeName;

        public BiomeWeightBlendNode(
            string nodeId, 
            string nodeName,
            string biome1 = "", string biome2 = "", string biome3 = "", string biome4 = "",
            string input1ChannelName = "", string input2ChannelName = "", string input3ChannelName = "", string input4ChannelName = "")
        {
            if (string.IsNullOrWhiteSpace(nodeId))
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            
            _biome1 = biome1 ?? string.Empty;
            _biome2 = biome2 ?? string.Empty;
            _biome3 = biome3 ?? string.Empty;
            _biome4 = biome4 ?? string.Empty;

            _input1ChannelName = input1ChannelName ?? string.Empty;
            _input2ChannelName = input2ChannelName ?? string.Empty;
            _input3ChannelName = input3ChannelName ?? string.Empty;
            _input4ChannelName = input4ChannelName ?? string.Empty;

            _ports = new[]
            {
                new NodePortDefinition(Weight1PortName, PortDirection.Input, ChannelType.Float, PortCapacity.Single, false),
                new NodePortDefinition(Weight2PortName, PortDirection.Input, ChannelType.Float, PortCapacity.Single, false),
                new NodePortDefinition(Weight3PortName, PortDirection.Input, ChannelType.Float, PortCapacity.Single, false),
                new NodePortDefinition(Weight4PortName, PortDirection.Input, ChannelType.Float, PortCapacity.Single, false),
                new NodePortDefinition(FallbackOutputPortName, PortDirection.Output, ChannelType.Int, displayName: FallbackOutputPortName)
            };

            RefreshChannelDeclarations();
        }

        public void ReceiveInputConnections(InputConnectionMap inputConnections)
        {
            _input1ChannelName = ResolveInput(inputConnections, Weight1PortName);
            _input2ChannelName = ResolveInput(inputConnections, Weight2PortName);
            _input3ChannelName = ResolveInput(inputConnections, Weight3PortName);
            _input4ChannelName = ResolveInput(inputConnections, Weight4PortName);

            RefreshChannelDeclarations();
        }

        private string ResolveInput(IReadOnlyDictionary<string, string> connections, string portName)
        {
            if (connections != null && connections.TryGetValue(portName, out string channelName))
                return channelName ?? string.Empty;
            return string.Empty;
        }

        public void ReceiveParameter(string name, string value)
        {
            if (string.Equals(name, "biome1", StringComparison.OrdinalIgnoreCase)) _biome1 = value;
            else if (string.Equals(name, "biome2", StringComparison.OrdinalIgnoreCase)) _biome2 = value;
            else if (string.Equals(name, "biome3", StringComparison.OrdinalIgnoreCase)) _biome3 = value;
            else if (string.Equals(name, "biome4", StringComparison.OrdinalIgnoreCase)) _biome4 = value;
        }

        public bool ResolveBiomePalette(BiomeChannelPalette palette, out string errorMessage)
        {
            if (palette == null) throw new ArgumentNullException(nameof(palette));

            _resolvedBiome1Index = BiomeChannelUtility.UnassignedBiomeIndex;
            _resolvedBiome2Index = BiomeChannelUtility.UnassignedBiomeIndex;
            _resolvedBiome3Index = BiomeChannelUtility.UnassignedBiomeIndex;
            _resolvedBiome4Index = BiomeChannelUtility.UnassignedBiomeIndex;

            if (!string.IsNullOrEmpty(_biome1) && !palette.TryResolveIndex(_biome1, out _resolvedBiome1Index, out errorMessage))
            {
                errorMessage = $"Biome Weight Blend node '{_nodeName}' could not resolve biome 1: {errorMessage}";
                return false;
            }
            if (!string.IsNullOrEmpty(_biome2) && !palette.TryResolveIndex(_biome2, out _resolvedBiome2Index, out errorMessage))
            {
                errorMessage = $"Biome Weight Blend node '{_nodeName}' could not resolve biome 2: {errorMessage}";
                return false;
            }
            if (!string.IsNullOrEmpty(_biome3) && !palette.TryResolveIndex(_biome3, out _resolvedBiome3Index, out errorMessage))
            {
                errorMessage = $"Biome Weight Blend node '{_nodeName}' could not resolve biome 3: {errorMessage}";
                return false;
            }
            if (!string.IsNullOrEmpty(_biome4) && !palette.TryResolveIndex(_biome4, out _resolvedBiome4Index, out errorMessage))
            {
                errorMessage = $"Biome Weight Blend node '{_nodeName}' could not resolve biome 4: {errorMessage}";
                return false;
            }

            errorMessage = null;
            return true;
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            NativeArray<int> output = context.GetIntChannel(FallbackOutputPortName);

            // We handle missing inputs by providing dummy uninitialized NativeArrays, 
            // but our job struct handles IsCreated checks to avoid accessing them.
            NativeArray<float> w1 = !string.IsNullOrEmpty(_input1ChannelName) ? context.GetFloatChannel(_input1ChannelName) : default;
            NativeArray<float> w2 = !string.IsNullOrEmpty(_input2ChannelName) ? context.GetFloatChannel(_input2ChannelName) : default;
            NativeArray<float> w3 = !string.IsNullOrEmpty(_input3ChannelName) ? context.GetFloatChannel(_input3ChannelName) : default;
            NativeArray<float> w4 = !string.IsNullOrEmpty(_input4ChannelName) ? context.GetFloatChannel(_input4ChannelName) : default;

            BiomeWeightBlendJob job = new BiomeWeightBlendJob
            {
                Output = output,
                
                Weight1 = w1, HasW1 = w1.IsCreated,
                Weight2 = w2, HasW2 = w2.IsCreated,
                Weight3 = w3, HasW3 = w3.IsCreated,
                Weight4 = w4, HasW4 = w4.IsCreated,

                Biome1Index = _resolvedBiome1Index,
                Biome2Index = _resolvedBiome2Index,
                Biome3Index = _resolvedBiome3Index,
                Biome4Index = _resolvedBiome4Index
            };

            return job.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
        }

        private void RefreshChannelDeclarations()
        {
            var declarations = new List<ChannelDeclaration>();

            if (!string.IsNullOrWhiteSpace(_input1ChannelName)) declarations.Add(new ChannelDeclaration(_input1ChannelName, ChannelType.Float, false));
            if (!string.IsNullOrWhiteSpace(_input2ChannelName)) declarations.Add(new ChannelDeclaration(_input2ChannelName, ChannelType.Float, false));
            if (!string.IsNullOrWhiteSpace(_input3ChannelName)) declarations.Add(new ChannelDeclaration(_input3ChannelName, ChannelType.Float, false));
            if (!string.IsNullOrWhiteSpace(_input4ChannelName)) declarations.Add(new ChannelDeclaration(_input4ChannelName, ChannelType.Float, false));

            declarations.Add(new ChannelDeclaration(FallbackOutputPortName, ChannelType.Int, true));

            _channelDeclarations = declarations.ToArray();
        }

        [BurstCompile]
        private struct BiomeWeightBlendJob : IJobParallelFor
        {
            public NativeArray<int> Output;

            [ReadOnly] public NativeArray<float> Weight1;
            public bool HasW1;
            [ReadOnly] public NativeArray<float> Weight2;
            public bool HasW2;
            [ReadOnly] public NativeArray<float> Weight3;
            public bool HasW3;
            [ReadOnly] public NativeArray<float> Weight4;
            public bool HasW4;

            public int Biome1Index;
            public int Biome2Index;
            public int Biome3Index;
            public int Biome4Index;

            public void Execute(int index)
            {
                float maxWeight = float.MinValue;
                int bestBiome = -1; // Unassigned

                if (HasW1)
                {
                    float w = Weight1[index];
                    if (w > maxWeight) { maxWeight = w; bestBiome = Biome1Index; }
                }
                if (HasW2)
                {
                    float w = Weight2[index];
                    if (w > maxWeight) { maxWeight = w; bestBiome = Biome2Index; }
                }
                if (HasW3)
                {
                    float w = Weight3[index];
                    if (w > maxWeight) { maxWeight = w; bestBiome = Biome3Index; }
                }
                if (HasW4)
                {
                    float w = Weight4[index];
                    if (w > maxWeight) { maxWeight = w; bestBiome = Biome4Index; }
                }

                Output[index] = bestBiome;
            }
        }
    }
}
