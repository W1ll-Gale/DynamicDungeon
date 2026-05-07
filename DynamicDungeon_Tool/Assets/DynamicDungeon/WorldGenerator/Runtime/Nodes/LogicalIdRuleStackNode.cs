using System;
using System.Collections.Generic;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace DynamicDungeon.Runtime.Nodes
{
    [NodeCategory("Output")]
    [NodeDisplayName("Logical ID Rule Stack")]
    [Description("Applies ordered logical ID rewrite rules using a multi-input mask stack.")]
    public sealed class LogicalIdRuleStackNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        private struct ResolvedRule
        {
            public int MaskSlot;
            public int SourceLogicalId;
            public int TargetLogicalId;
        }

        private const int DefaultBatchSize = 64;
        private const string DefaultNodeName = "Logical ID Rule Stack";
        private const string BasePortName = "Base";
        private const string MasksPortName = "Masks";
        private const string DefaultOutputChannelName = GraphOutputUtility.OutputInputPortName;
        private const string PreferredOutputDisplayName = "Logical IDs";

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private string _inputBaseChannelName;
        private string[] _inputMaskChannelNames;
        private string _outputChannelName;
        private string _rules;
        private ResolvedRule[] _resolvedRules;
        private NodePortDefinition[] _ports;
        private ChannelDeclaration[] _channelDeclarations;

        public IReadOnlyList<NodePortDefinition> Ports => _ports;
        public IReadOnlyList<ChannelDeclaration> ChannelDeclarations => _channelDeclarations;
        public IReadOnlyList<BlackboardKey> BlackboardDeclarations => _blackboardDeclarations;
        public string NodeId => _nodeId;
        public string NodeName => _nodeName;

        public LogicalIdRuleStackNode(
            string nodeId,
            string nodeName,
            string inputBaseChannelName = "",
            string outputChannelName = "",
            string rules = "")
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _inputBaseChannelName = inputBaseChannelName ?? string.Empty;
            _inputMaskChannelNames = Array.Empty<string>();
            _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(nodeId, outputChannelName, PreferredOutputDisplayName);
            _rules = rules ?? string.Empty;
            _resolvedRules = ResolveRules(ParseRules(_rules));

            RefreshPorts();
            RefreshChannelDeclarations();
        }

        public void ReceiveInputConnections(InputConnectionMap inputConnections)
        {
            _inputBaseChannelName = inputConnections != null ? inputConnections.FirstOrDefault(BasePortName) : string.Empty;
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
                _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(_nodeId, value, DefaultOutputChannelName);
                RefreshPorts();
                RefreshChannelDeclarations();
            }
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            NativeArray<int> baseIds = context.GetIntChannel(_inputBaseChannelName);
            NativeArray<int> output = context.GetIntChannel(_outputChannelName);
            CopyIdsJob copyJob = new CopyIdsJob { Input = baseIds, Output = output };
            JobHandle dependency = copyJob.Schedule(output.Length, DefaultBatchSize, context.InputDependency);

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

                ApplyRuleJob applyRuleJob = new ApplyRuleJob
                {
                    Output = output,
                    Mask = mask,
                    HasMask = hasMask,
                    SourceLogicalId = rule.SourceLogicalId,
                    TargetLogicalId = rule.TargetLogicalId
                };

                dependency = applyRuleJob.Schedule(output.Length, DefaultBatchSize, dependency);
                if (!hasMask)
                {
                    dependency = mask.Dispose(dependency);
                }
            }

            return dependency;
        }

        private void RefreshPorts()
        {
            string outputPortDisplayName = GraphPortNameUtility.ResolveOutputDisplayName(_nodeId, _outputChannelName, PreferredOutputDisplayName);
            _ports = new[]
            {
                new NodePortDefinition(BasePortName, PortDirection.Input, ChannelType.Int, PortCapacity.Single, true),
                new NodePortDefinition(MasksPortName, PortDirection.Input, ChannelType.BoolMask, PortCapacity.Multi, false, "Masks are addressed by one-based Mask Slot in the rule table."),
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.Int, displayName: outputPortDisplayName)
            };
        }

        private void RefreshChannelDeclarations()
        {
            List<ChannelDeclaration> declarations = new List<ChannelDeclaration>();
            if (!string.IsNullOrWhiteSpace(_inputBaseChannelName))
            {
                declarations.Add(new ChannelDeclaration(_inputBaseChannelName, ChannelType.Int, false));
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

            declarations.Add(new ChannelDeclaration(_outputChannelName, ChannelType.Int, true));
            _channelDeclarations = declarations.ToArray();
        }

        private static NativeArray<byte> CreatePassMask()
        {
            NativeArray<byte> mask = new NativeArray<byte>(1, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            mask[0] = 1;
            return mask;
        }

        private static LogicalIdRuleSet ParseRules(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return new LogicalIdRuleSet();
            }

            try
            {
                return JsonUtility.FromJson<LogicalIdRuleSet>(rawJson) ?? new LogicalIdRuleSet();
            }
            catch
            {
                return new LogicalIdRuleSet();
            }
        }

        private static ResolvedRule[] ResolveRules(LogicalIdRuleSet ruleSet)
        {
            LogicalIdRule[] rawRules = ruleSet != null && ruleSet.Rules != null ? ruleSet.Rules : Array.Empty<LogicalIdRule>();
            List<ResolvedRule> resolvedRules = new List<ResolvedRule>(rawRules.Length);

            for (int ruleIndex = 0; ruleIndex < rawRules.Length; ruleIndex++)
            {
                LogicalIdRule rule = rawRules[ruleIndex];
                if (rule == null || !rule.Enabled || rule.TargetLogicalId < 0)
                {
                    continue;
                }

                resolvedRules.Add(new ResolvedRule
                {
                    MaskSlot = math.max(0, rule.MaskSlot),
                    SourceLogicalId = rule.SourceLogicalId,
                    TargetLogicalId = rule.TargetLogicalId
                });
            }

            return resolvedRules.ToArray();
        }

        [BurstCompile]
        private struct CopyIdsJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<int> Input;

            public NativeArray<int> Output;

            public void Execute(int index)
            {
                Output[index] = Input[index];
            }
        }

        [BurstCompile]
        private struct ApplyRuleJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<byte> Mask;

            public NativeArray<int> Output;
            public bool HasMask;
            public int SourceLogicalId;
            public int TargetLogicalId;

            public void Execute(int index)
            {
                int current = Output[index];
                if (SourceLogicalId >= 0 && current != SourceLogicalId)
                {
                    return;
                }

                if (HasMask && Mask[index] == 0)
                {
                    return;
                }

                Output[index] = TargetLogicalId;
            }
        }
    }
}
