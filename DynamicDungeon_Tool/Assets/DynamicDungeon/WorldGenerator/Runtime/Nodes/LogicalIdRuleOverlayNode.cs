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
    [NodeDisplayName("Logical ID Rule Overlay")]
    [HelpURL("https://dynamicdungeon.mrbytesized.com/docs/nodes/logical-id/logicalidruleoverlay")]
    [Description("Applies ordered logical ID rewrite rules using optional mask slots.")]
    public sealed class LogicalIdRuleOverlayNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        private struct ResolvedRule
        {
            public int MaskSlot;
            public int SourceLogicalId;
            public int TargetLogicalId;
        }

        private const int DefaultBatchSize = 64;
        private const string DefaultNodeName = "Logical ID Rule Overlay";
        private const string BasePortName = "Base";
        private const string Mask1PortName = "Mask 1";
        private const string Mask2PortName = "Mask 2";
        private const string Mask3PortName = "Mask 3";
        private const string Mask4PortName = "Mask 4";
        private const string DefaultOutputChannelName = GraphOutputUtility.OutputInputPortName;
        private const string PreferredOutputDisplayName = "Logical IDs";

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private string _outputChannelName;
        private NodePortDefinition[] _ports;

        private string _inputBaseChannelName;
        private string _inputMask1ChannelName;
        private string _inputMask2ChannelName;
        private string _inputMask3ChannelName;
        private string _inputMask4ChannelName;

        [DescriptionAttribute("Ordered logical ID rewrite rules encoded as JSON. Use the custom editor table to author this value.")]
        private string _rules;

        private LogicalIdRuleSet _parsedRules;
        private ResolvedRule[] _resolvedRules;
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

        public LogicalIdRuleOverlayNode(
            string nodeId,
            string nodeName,
            string inputBaseChannelName = "",
            string inputMask1ChannelName = "",
            string inputMask2ChannelName = "",
            string inputMask3ChannelName = "",
            string inputMask4ChannelName = "",
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
            _inputMask1ChannelName = inputMask1ChannelName ?? string.Empty;
            _inputMask2ChannelName = inputMask2ChannelName ?? string.Empty;
            _inputMask3ChannelName = inputMask3ChannelName ?? string.Empty;
            _inputMask4ChannelName = inputMask4ChannelName ?? string.Empty;
            _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(nodeId, outputChannelName, PreferredOutputDisplayName);
            _rules = rules ?? string.Empty;
            _parsedRules = ParseRules(_rules);
            _resolvedRules = ResolveRules(_parsedRules);

            RefreshPorts();
            RefreshChannelDeclarations();
        }

        public void ReceiveInputConnections(InputConnectionMap inputConnections)
        {
            _inputBaseChannelName = ResolveInputConnection(inputConnections, BasePortName);
            _inputMask1ChannelName = ResolveInputConnection(inputConnections, Mask1PortName);
            _inputMask2ChannelName = ResolveInputConnection(inputConnections, Mask2PortName);
            _inputMask3ChannelName = ResolveInputConnection(inputConnections, Mask3PortName);
            _inputMask4ChannelName = ResolveInputConnection(inputConnections, Mask4PortName);
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
                _parsedRules = ParseRules(_rules);
                _resolvedRules = ResolveRules(_parsedRules);
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
            bool hasMask1 = !string.IsNullOrWhiteSpace(_inputMask1ChannelName);
            bool hasMask2 = !string.IsNullOrWhiteSpace(_inputMask2ChannelName);
            bool hasMask3 = !string.IsNullOrWhiteSpace(_inputMask3ChannelName);
            bool hasMask4 = !string.IsNullOrWhiteSpace(_inputMask4ChannelName);
            NativeArray<byte> mask1 = hasMask1 ? context.GetBoolMaskChannel(_inputMask1ChannelName) : CreateUnusedMask();
            NativeArray<byte> mask2 = hasMask2 ? context.GetBoolMaskChannel(_inputMask2ChannelName) : CreateUnusedMask();
            NativeArray<byte> mask3 = hasMask3 ? context.GetBoolMaskChannel(_inputMask3ChannelName) : CreateUnusedMask();
            NativeArray<byte> mask4 = hasMask4 ? context.GetBoolMaskChannel(_inputMask4ChannelName) : CreateUnusedMask();
            NativeArray<ResolvedRule> rules = new NativeArray<ResolvedRule>(_resolvedRules.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            int ruleIndex;
            for (ruleIndex = 0; ruleIndex < _resolvedRules.Length; ruleIndex++)
            {
                rules[ruleIndex] = _resolvedRules[ruleIndex];
            }

            LogicalIdRuleOverlayJob job = new LogicalIdRuleOverlayJob
            {
                BaseIds = baseIds,
                Output = output,
                Mask1 = mask1,
                Mask2 = mask2,
                Mask3 = mask3,
                Mask4 = mask4,
                HasMask1 = hasMask1,
                HasMask2 = hasMask2,
                HasMask3 = hasMask3,
                HasMask4 = hasMask4,
                Rules = rules
            };

            try
            {
                JobHandle jobHandle = job.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
                JobHandle disposeHandle = rules.Dispose(jobHandle);
                disposeHandle = DisposeUnusedMask(mask1, hasMask1, disposeHandle);
                disposeHandle = DisposeUnusedMask(mask2, hasMask2, disposeHandle);
                disposeHandle = DisposeUnusedMask(mask3, hasMask3, disposeHandle);
                disposeHandle = DisposeUnusedMask(mask4, hasMask4, disposeHandle);
                return disposeHandle;
            }
            catch
            {
                rules.Dispose();
                DisposeUnusedMaskImmediately(mask1, hasMask1);
                DisposeUnusedMaskImmediately(mask2, hasMask2);
                DisposeUnusedMaskImmediately(mask3, hasMask3);
                DisposeUnusedMaskImmediately(mask4, hasMask4);
                throw;
            }
        }

        private static NativeArray<byte> CreateUnusedMask()
        {
            return new NativeArray<byte>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);
        }

        private static JobHandle DisposeUnusedMask(NativeArray<byte> mask, bool isExternal, JobHandle dependency)
        {
            return isExternal ? dependency : mask.Dispose(dependency);
        }

        private static void DisposeUnusedMaskImmediately(NativeArray<byte> mask, bool isExternal)
        {
            if (!isExternal)
            {
                mask.Dispose();
            }
        }

        private void RefreshPorts()
        {
            string outputPortDisplayName = GraphPortNameUtility.ResolveOutputDisplayName(_nodeId, _outputChannelName, PreferredOutputDisplayName);
            _ports = new[]
            {
                new NodePortDefinition(BasePortName, PortDirection.Input, ChannelType.Int, PortCapacity.Single, true),
                new NodePortDefinition(Mask1PortName, PortDirection.Input, ChannelType.BoolMask, PortCapacity.Single, false),
                new NodePortDefinition(Mask2PortName, PortDirection.Input, ChannelType.BoolMask, PortCapacity.Single, false),
                new NodePortDefinition(Mask3PortName, PortDirection.Input, ChannelType.BoolMask, PortCapacity.Single, false),
                new NodePortDefinition(Mask4PortName, PortDirection.Input, ChannelType.BoolMask, PortCapacity.Single, false),
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.Int, displayName: outputPortDisplayName)
            };
        }

        private void RefreshChannelDeclarations()
        {
            List<ChannelDeclaration> declarations = new List<ChannelDeclaration>(6);

            if (!string.IsNullOrWhiteSpace(_inputBaseChannelName))
            {
                declarations.Add(new ChannelDeclaration(_inputBaseChannelName, ChannelType.Int, false));
            }

            AddMaskDeclaration(declarations, _inputMask1ChannelName);
            AddMaskDeclaration(declarations, _inputMask2ChannelName);
            AddMaskDeclaration(declarations, _inputMask3ChannelName);
            AddMaskDeclaration(declarations, _inputMask4ChannelName);

            declarations.Add(new ChannelDeclaration(_outputChannelName, ChannelType.Int, true));
            _channelDeclarations = declarations.ToArray();
        }

        private static void AddMaskDeclaration(List<ChannelDeclaration> declarations, string channelName)
        {
            if (!string.IsNullOrWhiteSpace(channelName))
            {
                declarations.Add(new ChannelDeclaration(channelName, ChannelType.BoolMask, false));
            }
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

        private static LogicalIdRuleSet ParseRules(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return new LogicalIdRuleSet();
            }

            try
            {
                LogicalIdRuleSet rules = JsonUtility.FromJson<LogicalIdRuleSet>(rawJson);
                return rules ?? new LogicalIdRuleSet();
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

            int ruleIndex;
            for (ruleIndex = 0; ruleIndex < rawRules.Length; ruleIndex++)
            {
                LogicalIdRule rule = rawRules[ruleIndex];
                if (rule == null || !rule.Enabled || rule.TargetLogicalId < 0)
                {
                    continue;
                }

                resolvedRules.Add(new ResolvedRule
                {
                    MaskSlot = math.clamp(rule.MaskSlot, 0, 4),
                    SourceLogicalId = rule.SourceLogicalId,
                    TargetLogicalId = rule.TargetLogicalId
                });
            }

            return resolvedRules.ToArray();
        }

        [BurstCompile]
        private struct LogicalIdRuleOverlayJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<int> BaseIds;

            [ReadOnly]
            public NativeArray<byte> Mask1;

            [ReadOnly]
            public NativeArray<byte> Mask2;

            [ReadOnly]
            public NativeArray<byte> Mask3;

            [ReadOnly]
            public NativeArray<byte> Mask4;

            [ReadOnly]
            public NativeArray<ResolvedRule> Rules;

            public NativeArray<int> Output;
            public bool HasMask1;
            public bool HasMask2;
            public bool HasMask3;
            public bool HasMask4;

            public void Execute(int index)
            {
                int value = BaseIds[index];

                int ruleIndex;
                for (ruleIndex = 0; ruleIndex < Rules.Length; ruleIndex++)
                {
                    ResolvedRule rule = Rules[ruleIndex];
                    if (rule.SourceLogicalId >= 0 && value != rule.SourceLogicalId)
                    {
                        continue;
                    }

                    if (!MaskPasses(rule.MaskSlot, index))
                    {
                        continue;
                    }

                    value = rule.TargetLogicalId;
                }

                Output[index] = value;
            }

            private bool MaskPasses(int maskSlot, int index)
            {
                if (maskSlot <= 0)
                {
                    return true;
                }

                if (maskSlot == 1)
                {
                    return HasMask1 && Mask1[index] != 0;
                }

                if (maskSlot == 2)
                {
                    return HasMask2 && Mask2[index] != 0;
                }

                if (maskSlot == 3)
                {
                    return HasMask3 && Mask3[index] != 0;
                }

                return HasMask4 && Mask4[index] != 0;
            }
        }
    }
}
