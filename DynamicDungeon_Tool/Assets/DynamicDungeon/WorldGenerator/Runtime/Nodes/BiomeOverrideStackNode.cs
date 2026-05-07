using System;
using System.Collections.Generic;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
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
    [NodeDisplayName("Biome Override Stack")]
    [Description("Applies ordered mask-to-biome override rules from one compact table.")]
    public sealed class BiomeOverrideStackNode : IGenNode, IInputConnectionReceiver, IParameterReceiver, IBiomeChannelNode
    {
        private struct ResolvedRule
        {
            public int MaskSlot;
            public int BiomeIndex;
            public float Probability;
        }

        private const int DefaultBatchSize = 64;
        private const string DefaultNodeName = "Biome Override Stack";
        private const string BiomeInputPortName = "Biome Input";
        private const string MasksPortName = "Masks";

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private string _inputBiomeChannelName;
        private string[] _inputMaskChannelNames;

        [DescriptionAttribute("Ordered biome override rows encoded as JSON. Each row references a one-based Mask Slot.")]
        private string _rules;

        private BiomeOverrideStackRuleSet _parsedRules;
        private ResolvedRule[] _resolvedRules;
        private NodePortDefinition[] _ports;
        private ChannelDeclaration[] _channelDeclarations;

        public IReadOnlyList<NodePortDefinition> Ports => _ports;
        public IReadOnlyList<ChannelDeclaration> ChannelDeclarations => _channelDeclarations;
        public IReadOnlyList<BlackboardKey> BlackboardDeclarations => _blackboardDeclarations;
        public string NodeId => _nodeId;
        public string NodeName => _nodeName;

        public BiomeOverrideStackNode(string nodeId, string nodeName, string rules = "")
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _inputBiomeChannelName = string.Empty;
            _inputMaskChannelNames = Array.Empty<string>();
            _rules = rules ?? string.Empty;
            _parsedRules = ParseRules(_rules);
            _resolvedRules = Array.Empty<ResolvedRule>();

            _ports = new[]
            {
                new NodePortDefinition(BiomeInputPortName, PortDirection.Input, ChannelType.Int, PortCapacity.Single, false, "Optional ordering dependency from an existing biome channel."),
                new NodePortDefinition(MasksPortName, PortDirection.Input, ChannelType.BoolMask, PortCapacity.Multi, true, "Masks are addressed by one-based Mask Slot in the override table."),
                new NodePortDefinition(BiomeChannelUtility.ChannelName, PortDirection.Output, ChannelType.Int, displayName: "Biomes")
            };

            RefreshChannelDeclarations();
        }

        public void ReceiveInputConnections(InputConnectionMap inputConnections)
        {
            _inputBiomeChannelName = inputConnections != null
                ? inputConnections.FirstOrDefault(BiomeInputPortName)
                : string.Empty;

            IReadOnlyList<string> maskConnections = inputConnections != null
                ? inputConnections.GetAll(MasksPortName)
                : Array.Empty<string>();

            _inputMaskChannelNames = new string[maskConnections.Count];
            for (int index = 0; index < maskConnections.Count; index++)
            {
                _inputMaskChannelNames[index] = maskConnections[index] ?? string.Empty;
            }

            RefreshChannelDeclarations();
        }

        public void ReceiveParameter(string name, string value)
        {
            if (string.Equals(name, "rules", StringComparison.OrdinalIgnoreCase))
            {
                _rules = value ?? string.Empty;
                _parsedRules = ParseRules(_rules);
                _resolvedRules = Array.Empty<ResolvedRule>();
            }
        }

        public bool ResolveBiomePalette(BiomeChannelPalette palette, out string errorMessage)
        {
            if (palette == null)
            {
                throw new ArgumentNullException(nameof(palette));
            }

            List<ResolvedRule> resolvedRules = new List<ResolvedRule>();
            BiomeOverrideStackRule[] rules = _parsedRules != null && _parsedRules.Rules != null
                ? _parsedRules.Rules
                : Array.Empty<BiomeOverrideStackRule>();

            for (int ruleIndex = 0; ruleIndex < rules.Length; ruleIndex++)
            {
                BiomeOverrideStackRule rule = rules[ruleIndex];
                if (rule == null || !rule.Enabled || string.IsNullOrWhiteSpace(rule.OverrideBiome))
                {
                    continue;
                }

                int biomeIndex;
                if (!palette.TryResolveIndex(rule.OverrideBiome, out biomeIndex, out errorMessage))
                {
                    errorMessage = "Biome Override Stack node '" + _nodeName + "' rule " + (ruleIndex + 1).ToString() + " could not resolve its override biome: " + errorMessage;
                    return false;
                }

                resolvedRules.Add(new ResolvedRule
                {
                    MaskSlot = math.max(0, rule.MaskSlot),
                    BiomeIndex = biomeIndex,
                    Probability = math.saturate(rule.Probability)
                });
            }

            _resolvedRules = resolvedRules.ToArray();
            errorMessage = null;
            return true;
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            NativeArray<int> biomeChannel = context.GetIntChannel(BiomeChannelUtility.ChannelName);
            JobHandle dependency = context.InputDependency;

            for (int ruleIndex = 0; ruleIndex < _resolvedRules.Length; ruleIndex++)
            {
                ResolvedRule rule = _resolvedRules[ruleIndex];
                bool hasMask = rule.MaskSlot > 0 &&
                    _inputMaskChannelNames != null &&
                    rule.MaskSlot <= _inputMaskChannelNames.Length &&
                    !string.IsNullOrWhiteSpace(_inputMaskChannelNames[rule.MaskSlot - 1]);

                NativeArray<byte> mask = hasMask
                    ? context.GetBoolMaskChannel(_inputMaskChannelNames[rule.MaskSlot - 1])
                    : CreatePassMask();

                ApplyBiomeOverrideJob job = new ApplyBiomeOverrideJob
                {
                    BiomeChannel = biomeChannel,
                    Mask = mask,
                    HasMask = hasMask,
                    Width = context.Width,
                    LocalSeed = context.LocalSeed + ruleIndex * 104729L,
                    BiomeIndex = rule.BiomeIndex,
                    Probability = rule.Probability
                };

                dependency = job.Schedule(biomeChannel.Length, DefaultBatchSize, dependency);
                if (!hasMask)
                {
                    dependency = mask.Dispose(dependency);
                }
            }

            return dependency;
        }

        private void RefreshChannelDeclarations()
        {
            List<ChannelDeclaration> declarations = new List<ChannelDeclaration>();
            if (!string.IsNullOrWhiteSpace(_inputBiomeChannelName))
            {
                declarations.Add(new ChannelDeclaration(_inputBiomeChannelName, ChannelType.Int, false));
            }

            if (_inputMaskChannelNames != null)
            {
                for (int index = 0; index < _inputMaskChannelNames.Length; index++)
                {
                    if (!string.IsNullOrWhiteSpace(_inputMaskChannelNames[index]))
                    {
                        declarations.Add(new ChannelDeclaration(_inputMaskChannelNames[index], ChannelType.BoolMask, false));
                    }
                }
            }

            declarations.Add(new ChannelDeclaration(BiomeChannelUtility.ChannelName, ChannelType.Int, true));
            _channelDeclarations = declarations.ToArray();
        }

        private static NativeArray<byte> CreatePassMask()
        {
            NativeArray<byte> mask = new NativeArray<byte>(1, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            mask[0] = 1;
            return mask;
        }

        private static BiomeOverrideStackRuleSet ParseRules(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return new BiomeOverrideStackRuleSet();
            }

            try
            {
                return JsonUtility.FromJson<BiomeOverrideStackRuleSet>(rawJson) ?? new BiomeOverrideStackRuleSet();
            }
            catch
            {
                return new BiomeOverrideStackRuleSet();
            }
        }

        [BurstCompile]
        private struct ApplyBiomeOverrideJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<byte> Mask;

            public NativeArray<int> BiomeChannel;
            public bool HasMask;
            public int Width;
            public long LocalSeed;
            public int BiomeIndex;
            public float Probability;

            public void Execute(int index)
            {
                if (HasMask && Mask[index] == 0)
                {
                    return;
                }

                if (Probability < 1.0f)
                {
                    int x = index % Width;
                    int y = index / Width;
                    if (HashToUnitFloat(x, y) > Probability)
                    {
                        return;
                    }
                }

                BiomeChannel[index] = BiomeIndex;
            }

            private float HashToUnitFloat(int x, int y)
            {
                uint seedLow = unchecked((uint)LocalSeed);
                uint seedHigh = unchecked((uint)(LocalSeed >> 32));
                uint hash = math.hash(new uint4(unchecked((uint)x), unchecked((uint)y), seedLow, seedHigh));
                return (float)(hash / 4294967296.0d);
            }
        }
    }
}
