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
    [NodeDisplayName("Biome Mask")]
    [Description("Extracts a single biome's presence from a biome channel into a float mask (1.0 if present, 0.0 if not).")]
    public sealed class BiomeMaskNode : IGenNode, IInputConnectionReceiver, IParameterReceiver, IBiomeChannelNode
    {
        private const int DefaultBatchSize = 64;
        private const string DefaultNodeName = "Biome Mask";
        private const string InputPortName = "Biome Channel";
        private const string FallbackOutputPortName = "Output";
        private const string PreferredOutputDisplayName = FallbackOutputPortName;

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private string _outputChannelName;
        private NodePortDefinition[] _ports;

        private string _inputBiomeChannelName;

        [AssetGuidReference(typeof(BiomeAsset))]
        [Description("Target biome asset to extract into the mask. Stored as an asset GUID in the graph.")]
        private string _biome;

        private int _resolvedBiomeIndex;
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

        public BiomeMaskNode(
            string nodeId,
            string nodeName,
            string inputBiomeChannelName = "",
            string outputChannelName = "",
            string targetBiome = "",
            string biome = "")
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _inputBiomeChannelName = inputBiomeChannelName ?? string.Empty;
            _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(nodeId, outputChannelName, FallbackOutputPortName);
            _biome = string.IsNullOrWhiteSpace(biome) ? targetBiome : biome;
            _biome = _biome ?? string.Empty;
            _resolvedBiomeIndex = BiomeChannelUtility.UnassignedBiomeIndex;

            RefreshPorts();
            RefreshChannelDeclarations();
        }

        private void RefreshPorts()
        {
            string outputPortDisplayName = GraphPortNameUtility.ResolveOutputDisplayName(_nodeId, _outputChannelName, PreferredOutputDisplayName);
            _ports = new[]
            {
                new NodePortDefinition(InputPortName, PortDirection.Input, ChannelType.Int, PortCapacity.Single, false),
                new NodePortDefinition(FallbackOutputPortName, PortDirection.Output, ChannelType.Float, displayName: outputPortDisplayName)
            };
        }

        public void ReceiveInputConnections(IReadOnlyDictionary<string, string> inputConnections)
        {
            string inputChannelName;
            if (inputConnections != null && inputConnections.TryGetValue(InputPortName, out inputChannelName))
            {
                _inputBiomeChannelName = inputChannelName ?? string.Empty;
            }
            else
            {
                _inputBiomeChannelName = string.Empty;
            }

            RefreshChannelDeclarations();
        }

        public void ReceiveParameter(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (string.Equals(name, "targetBiome", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "biome", StringComparison.OrdinalIgnoreCase))
            {
                _biome = value ?? string.Empty;
                return;
            }

            if (string.Equals(name, "outputChannelName", StringComparison.OrdinalIgnoreCase))
            {
                _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(_nodeId, value, FallbackOutputPortName);
                RefreshPorts();
                RefreshChannelDeclarations();
            }
        }

        public bool ResolveBiomePalette(BiomeChannelPalette palette, out string errorMessage)
        {
            if (palette == null)
            {
                throw new ArgumentNullException(nameof(palette));
            }

            if (!palette.TryResolveIndex(_biome, out _resolvedBiomeIndex, out errorMessage))
            {
                errorMessage = "Biome Mask node '" + _nodeName + "' could not resolve its target biome: " + errorMessage;
                return false;
            }

            return true;
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            NativeArray<int> input = context.GetIntChannel(_inputBiomeChannelName);
            NativeArray<float> output = context.GetFloatChannel(_outputChannelName);

            BiomeMaskJob job = new BiomeMaskJob
            {
                Input = input,
                Output = output,
                TargetBiomeIndex = _resolvedBiomeIndex
            };

            return job.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
        }

        private void RefreshChannelDeclarations()
        {
            if (!string.IsNullOrWhiteSpace(_inputBiomeChannelName))
            {
                _channelDeclarations = new[]
                {
                    new ChannelDeclaration(_inputBiomeChannelName, ChannelType.Int, false),
                    new ChannelDeclaration(_outputChannelName, ChannelType.Float, true)
                };
                return;
            }

            _channelDeclarations = new[]
            {
                new ChannelDeclaration(_outputChannelName, ChannelType.Float, true)
            };
        }

        [BurstCompile]
        private struct BiomeMaskJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<int> Input;

            public NativeArray<float> Output;
            public int TargetBiomeIndex;

            public void Execute(int index)
            {
                Output[index] = Input[index] == TargetBiomeIndex ? 1.0f : 0.0f;
            }
        }
    }
}
