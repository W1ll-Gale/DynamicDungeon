using System;
using System.Collections.Generic;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using DynamicDungeon.Runtime.Placement;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace DynamicDungeon.Runtime.Nodes
{
    [NodeCategory("Placement")]
    [NodeDisplayName("Placement Set")]
    [Description("Generates and stamps multiple prefab placement rows from one multi-input weight stack.")]
    public sealed class PlacementSetNode : IGenNode, IInputConnectionReceiver, IParameterReceiver, IPrefabPlacementNode
    {
        private struct ResolvedRule
        {
            public int WeightSlot;
            public int MaskSlot;
            public int TemplateIndex;
            public float Threshold;
            public float Density;
            public int PointCount;
            public int OffsetX;
            public int OffsetY;
            public bool MirrorX;
            public bool MirrorY;
            public bool AllowRotation;
        }

        private const int DefaultBatchSize = 64;
        private const string DefaultNodeName = "Placement Set";
        private const string WeightsPortName = "Weights";
        private const string MasksPortName = "Masks";

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private string[] _inputWeightChannelNames;
        private string[] _inputMaskChannelNames;

        [DescriptionAttribute("Ordered prefab placement rows encoded as JSON. Each row references a one-based Weight Slot.")]
        private string _rules;

        private PlacementSetRuleSet _parsedRules;
        private ResolvedRule[] _resolvedRules;
        private NodePortDefinition[] _ports;
        private ChannelDeclaration[] _channelDeclarations;

        public IReadOnlyList<NodePortDefinition> Ports => _ports;
        public IReadOnlyList<ChannelDeclaration> ChannelDeclarations => _channelDeclarations;
        public IReadOnlyList<BlackboardKey> BlackboardDeclarations => _blackboardDeclarations;
        public string NodeId => _nodeId;
        public string NodeName => _nodeName;

        public PlacementSetNode(string nodeId, string nodeName, string rules = "")
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _inputWeightChannelNames = Array.Empty<string>();
            _inputMaskChannelNames = Array.Empty<string>();
            _rules = rules ?? string.Empty;
            _parsedRules = ParseRules(_rules);
            _resolvedRules = Array.Empty<ResolvedRule>();

            _ports = new[]
            {
                new NodePortDefinition(WeightsPortName, PortDirection.Input, ChannelType.Float, PortCapacity.Multi, true, "Weight maps are addressed by one-based Weight Slot in the placement table."),
                new NodePortDefinition(MasksPortName, PortDirection.Input, ChannelType.BoolMask, PortCapacity.Multi, false, "Optional candidate masks are addressed by one-based Mask Slot in the placement table."),
                new NodePortDefinition(PrefabPlacementChannelUtility.ChannelName, PortDirection.Output, ChannelType.PrefabPlacementList)
            };

            RefreshChannelDeclarations();
        }

        public void ReceiveInputConnections(InputConnectionMap inputConnections)
        {
            IReadOnlyList<string> weightConnections = inputConnections != null
                ? inputConnections.GetAll(WeightsPortName)
                : Array.Empty<string>();

            _inputWeightChannelNames = new string[weightConnections.Count];
            for (int index = 0; index < weightConnections.Count; index++)
            {
                _inputWeightChannelNames[index] = weightConnections[index] ?? string.Empty;
            }

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

        public bool ResolvePrefabPalette(PrefabStampPalette palette, out string errorMessage)
        {
            if (palette == null)
            {
                throw new ArgumentNullException(nameof(palette));
            }

            List<ResolvedRule> resolvedRules = new List<ResolvedRule>();
            PlacementSetRule[] rules = _parsedRules != null && _parsedRules.Rules != null
                ? _parsedRules.Rules
                : Array.Empty<PlacementSetRule>();

            for (int ruleIndex = 0; ruleIndex < rules.Length; ruleIndex++)
            {
                PlacementSetRule rule = rules[ruleIndex];
                if (rule == null || !rule.Enabled || string.IsNullOrWhiteSpace(rule.Prefab))
                {
                    continue;
                }

                int templateIndex;
                PrefabStampTemplate template;
                if (!palette.TryResolve(rule.Prefab, out templateIndex, out template, out errorMessage))
                {
                    errorMessage = "Placement Set node '" + _nodeName + "' rule " + (ruleIndex + 1).ToString() + " could not resolve its prefab: " + errorMessage;
                    return false;
                }

                resolvedRules.Add(new ResolvedRule
                {
                    WeightSlot = math.max(0, rule.WeightSlot),
                    MaskSlot = math.max(0, rule.MaskSlot),
                    TemplateIndex = templateIndex,
                    Threshold = math.saturate(rule.Threshold),
                    Density = math.max(0.0f, rule.Density),
                    PointCount = math.max(0, rule.PointCount),
                    OffsetX = rule.OffsetX,
                    OffsetY = rule.OffsetY,
                    MirrorX = rule.MirrorX,
                    MirrorY = rule.MirrorY,
                    AllowRotation = rule.AllowRotation
                });
            }

            _resolvedRules = resolvedRules.ToArray();
            errorMessage = null;
            return true;
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            NativeList<PrefabPlacementRecord> placements = context.GetPrefabPlacementListChannel(PrefabPlacementChannelUtility.ChannelName);
            JobHandle dependency = context.InputDependency;

            for (int ruleIndex = 0; ruleIndex < _resolvedRules.Length; ruleIndex++)
            {
                ResolvedRule rule = _resolvedRules[ruleIndex];
                bool hasWeights = _inputWeightChannelNames != null &&
                    rule.WeightSlot > 0 &&
                    rule.WeightSlot <= _inputWeightChannelNames.Length &&
                    !string.IsNullOrWhiteSpace(_inputWeightChannelNames[rule.WeightSlot - 1]);
                bool hasMask = _inputMaskChannelNames != null &&
                    rule.MaskSlot > 0 &&
                    rule.MaskSlot <= _inputMaskChannelNames.Length &&
                    !string.IsNullOrWhiteSpace(_inputMaskChannelNames[rule.MaskSlot - 1]);

                if (!hasWeights && !hasMask)
                {
                    continue;
                }

                NativeArray<float> weights = hasWeights ? context.GetFloatChannel(_inputWeightChannelNames[rule.WeightSlot - 1]) : default;
                NativeArray<byte> mask = hasMask ? context.GetBoolMaskChannel(_inputMaskChannelNames[rule.MaskSlot - 1]) : default;
                PlacementSetRuleJob job = new PlacementSetRuleJob
                {
                    Weights = weights,
                    Mask = mask,
                    Placements = placements,
                    Width = context.Width,
                    Height = context.Height,
                    LocalSeed = context.LocalSeed + ruleIndex * 130363L,
                    TemplateIndex = rule.TemplateIndex,
                    Threshold = rule.Threshold,
                    Density = rule.Density,
                    PointCount = rule.PointCount,
                    HasWeights = hasWeights,
                    HasMask = hasMask,
                    OffsetX = rule.OffsetX,
                    OffsetY = rule.OffsetY,
                    MirrorX = rule.MirrorX,
                    MirrorY = rule.MirrorY,
                    AllowRotation = rule.AllowRotation
                };

                dependency = job.Schedule(dependency);
            }

            return dependency;
        }

        private void RefreshChannelDeclarations()
        {
            List<ChannelDeclaration> declarations = new List<ChannelDeclaration>();
            if (_inputWeightChannelNames != null)
            {
                for (int index = 0; index < _inputWeightChannelNames.Length; index++)
                {
                    if (!string.IsNullOrWhiteSpace(_inputWeightChannelNames[index]))
                    {
                        declarations.Add(new ChannelDeclaration(_inputWeightChannelNames[index], ChannelType.Float, false));
                    }
                }
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

            declarations.Add(new ChannelDeclaration(PrefabPlacementChannelUtility.ChannelName, ChannelType.PrefabPlacementList, true));
            _channelDeclarations = declarations.ToArray();
        }

        private static PlacementSetRuleSet ParseRules(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return new PlacementSetRuleSet();
            }

            try
            {
                return JsonUtility.FromJson<PlacementSetRuleSet>(rawJson) ?? new PlacementSetRuleSet();
            }
            catch
            {
                return new PlacementSetRuleSet();
            }
        }

        [BurstCompile]
        private struct PlacementSetRuleJob : IJob
        {
            [ReadOnly]
            public NativeArray<float> Weights;

            [ReadOnly]
            public NativeArray<byte> Mask;

            public NativeList<PrefabPlacementRecord> Placements;
            public int Width;
            public int Height;
            public long LocalSeed;
            public int TemplateIndex;
            public float Threshold;
            public float Density;
            public int PointCount;
            public bool HasWeights;
            public bool HasMask;
            public int OffsetX;
            public int OffsetY;
            public bool MirrorX;
            public bool MirrorY;
            public bool AllowRotation;

            public void Execute()
            {
                int emitted = 0;
                int length = HasWeights ? Weights.Length : Mask.Length;
                for (int index = 0; index < length; index++)
                {
                    if (HasMask && Mask[index] == 0)
                    {
                        continue;
                    }

                    float baseWeight = HasWeights ? Weights[index] : 1.0f;
                    float weight = math.saturate(baseWeight * Density);
                    if (weight < Threshold)
                    {
                        continue;
                    }

                    int x = index % Width;
                    int y = index / Width;
                    if (HashToUnitFloat(x, y, 0xA53D2E4Fu) > weight)
                    {
                        continue;
                    }

                    int placementX = x + OffsetX;
                    int placementY = y + OffsetY;
                    if (placementX < 0 || placementX >= Width || placementY < 0 || placementY >= Height)
                    {
                        continue;
                    }

                    bool mirrorX = MirrorX && HashToUnitFloat(x, y, 0xB5297A4Du) >= 0.5f;
                    bool mirrorY = MirrorY && HashToUnitFloat(x, y, 0x68E31DA4u) >= 0.5f;
                    byte rotation = AllowRotation ? (byte)math.min(3, (int)(HashToUnitFloat(x, y, 0x1B56C4E9u) * 4.0f)) : (byte)0;
                    Placements.Add(new PrefabPlacementRecord(TemplateIndex, placementX, placementY, rotation, mirrorX, mirrorY));
                    emitted++;

                    if (PointCount > 0 && emitted >= PointCount)
                    {
                        return;
                    }
                }
            }

            private float HashToUnitFloat(int x, int y, uint salt)
            {
                uint seedLow = unchecked((uint)LocalSeed);
                uint seedHigh = unchecked((uint)(LocalSeed >> 32));
                uint hash = math.hash(new uint4(unchecked((uint)x), unchecked((uint)y), seedLow ^ salt, seedHigh));
                return (float)(hash / 4294967296.0d);
            }
        }
    }
}
