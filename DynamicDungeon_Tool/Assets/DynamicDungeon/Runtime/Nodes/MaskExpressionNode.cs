using System;
using System.Collections.Generic;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace DynamicDungeon.Runtime.Nodes
{
    [NodeCategory("Blend")]
    [NodeDisplayName("Mask Expression")]
    [Description("Combines ordered mask slots with per-row operations, including inverted inputs and subtract steps.")]
    public sealed class MaskExpressionNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        private struct ResolvedRule
        {
            public int MaskSlot;
            public MaskExpressionOperation Operation;
            public bool Invert;
        }

        private const int DefaultBatchSize = 64;
        private const string DefaultNodeName = "Mask Expression";
        private const string MasksPortName = "Masks";
        private const string FallbackOutputPortName = "Mask";
        private const string PreferredOutputDisplayName = FallbackOutputPortName;

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private string _outputChannelName;
        private string[] _inputChannelNames;

        [DescriptionAttribute("Ordered mask expression rows encoded as JSON. Each row references a one-based Mask Slot.")]
        private string _rules;

        private ResolvedRule[] _resolvedRules;
        private NodePortDefinition[] _ports;
        private ChannelDeclaration[] _channelDeclarations;

        public IReadOnlyList<NodePortDefinition> Ports => _ports;
        public IReadOnlyList<ChannelDeclaration> ChannelDeclarations => _channelDeclarations;
        public IReadOnlyList<BlackboardKey> BlackboardDeclarations => _blackboardDeclarations;
        public string NodeId => _nodeId;
        public string NodeName => _nodeName;

        public MaskExpressionNode(
            string nodeId,
            string nodeName,
            string outputChannelName = "",
            string rules = "")
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(nodeId, outputChannelName, FallbackOutputPortName);
            _inputChannelNames = Array.Empty<string>();
            _rules = rules ?? string.Empty;
            _resolvedRules = ResolveRules(ParseRules(_rules));

            RefreshPorts();
            RefreshChannelDeclarations();
        }

        public void ReceiveInputConnections(InputConnectionMap inputConnections)
        {
            IReadOnlyList<string> connections = inputConnections != null
                ? inputConnections.GetAll(MasksPortName)
                : Array.Empty<string>();

            _inputChannelNames = new string[connections.Count];
            for (int index = 0; index < connections.Count; index++)
            {
                _inputChannelNames[index] = connections[index] ?? string.Empty;
            }

            RefreshChannelDeclarations();
        }

        public void ReceiveParameter(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (string.Equals(name, "rules", StringComparison.OrdinalIgnoreCase))
            {
                _rules = value ?? string.Empty;
                _resolvedRules = ResolveRules(ParseRules(_rules));
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
            NativeArray<byte> output = context.GetBoolMaskChannel(_outputChannelName);
            JobHandle dependency = context.InputDependency;
            bool initialized = false;

            for (int ruleIndex = 0; ruleIndex < _resolvedRules.Length; ruleIndex++)
            {
                ResolvedRule rule = _resolvedRules[ruleIndex];
                if (rule.MaskSlot <= 0 ||
                    _inputChannelNames == null ||
                    rule.MaskSlot > _inputChannelNames.Length ||
                    string.IsNullOrWhiteSpace(_inputChannelNames[rule.MaskSlot - 1]))
                {
                    continue;
                }

                NativeArray<byte> input = context.GetBoolMaskChannel(_inputChannelNames[rule.MaskSlot - 1]);
                if (!initialized || rule.Operation == MaskExpressionOperation.Replace)
                {
                    CopyMaskJob copyJob = new CopyMaskJob
                    {
                        Input = input,
                        Output = output,
                        Invert = rule.Invert
                    };
                    dependency = copyJob.Schedule(output.Length, DefaultBatchSize, dependency);
                    initialized = true;
                    continue;
                }

                CombineMaskJob combineJob = new CombineMaskJob
                {
                    Input = input,
                    Output = output,
                    Operation = (int)rule.Operation,
                    InvertInput = rule.Invert
                };
                dependency = combineJob.Schedule(output.Length, DefaultBatchSize, dependency);
            }

            if (!initialized)
            {
                ClearMaskJob clearJob = new ClearMaskJob { Output = output };
                dependency = clearJob.Schedule(output.Length, DefaultBatchSize, dependency);
            }

            return dependency;
        }

        private void RefreshPorts()
        {
            string outputPortDisplayName = GraphPortNameUtility.ResolveOutputDisplayName(_nodeId, _outputChannelName, PreferredOutputDisplayName);
            _ports = new[]
            {
                new NodePortDefinition(MasksPortName, PortDirection.Input, ChannelType.BoolMask, PortCapacity.Multi, true, "Masks are addressed by one-based Mask Slot in the expression table."),
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.BoolMask, displayName: outputPortDisplayName)
            };
        }

        private void RefreshChannelDeclarations()
        {
            List<ChannelDeclaration> declarations = new List<ChannelDeclaration>();
            if (_inputChannelNames != null)
            {
                for (int index = 0; index < _inputChannelNames.Length; index++)
                {
                    if (!string.IsNullOrWhiteSpace(_inputChannelNames[index]))
                    {
                        declarations.Add(new ChannelDeclaration(_inputChannelNames[index], ChannelType.BoolMask, false));
                    }
                }
            }

            declarations.Add(new ChannelDeclaration(_outputChannelName, ChannelType.BoolMask, true));
            _channelDeclarations = declarations.ToArray();
        }

        private static MaskExpressionRuleSet ParseRules(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return new MaskExpressionRuleSet();
            }

            try
            {
                return JsonUtility.FromJson<MaskExpressionRuleSet>(rawJson) ?? new MaskExpressionRuleSet();
            }
            catch
            {
                return new MaskExpressionRuleSet();
            }
        }

        private static ResolvedRule[] ResolveRules(MaskExpressionRuleSet ruleSet)
        {
            MaskExpressionRule[] rules = ruleSet != null && ruleSet.Rules != null
                ? ruleSet.Rules
                : Array.Empty<MaskExpressionRule>();
            List<ResolvedRule> resolvedRules = new List<ResolvedRule>();

            for (int ruleIndex = 0; ruleIndex < rules.Length; ruleIndex++)
            {
                MaskExpressionRule rule = rules[ruleIndex];
                if (rule == null || !rule.Enabled)
                {
                    continue;
                }

                resolvedRules.Add(new ResolvedRule
                {
                    MaskSlot = Math.Max(0, rule.MaskSlot),
                    Operation = rule.Operation,
                    Invert = rule.Invert
                });
            }

            return resolvedRules.ToArray();
        }

        [BurstCompile]
        private struct ClearMaskJob : IJobParallelFor
        {
            public NativeArray<byte> Output;

            public void Execute(int index)
            {
                Output[index] = 0;
            }
        }

        [BurstCompile]
        private struct CopyMaskJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<byte> Input;

            public NativeArray<byte> Output;
            public bool Invert;

            public void Execute(int index)
            {
                byte value = Input[index] != 0 ? (byte)1 : (byte)0;
                Output[index] = Invert ? (byte)(value == 0 ? 1 : 0) : value;
            }
        }

        [BurstCompile]
        private struct CombineMaskJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<byte> Input;

            public NativeArray<byte> Output;
            public int Operation;
            public bool InvertInput;

            public void Execute(int index)
            {
                bool output = Output[index] != 0;
                bool input = Input[index] != 0;
                if (InvertInput)
                {
                    input = !input;
                }

                switch ((MaskExpressionOperation)Operation)
                {
                    case MaskExpressionOperation.AND:
                        output = output && input;
                        break;
                    case MaskExpressionOperation.XOR:
                        output = output ^ input;
                        break;
                    case MaskExpressionOperation.Subtract:
                        output = output && !input;
                        break;
                    default:
                        output = output || input;
                        break;
                }

                Output[index] = output ? (byte)1 : (byte)0;
            }
        }
    }
}
