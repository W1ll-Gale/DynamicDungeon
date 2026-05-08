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
    [NodeCategory("Query")]
    [NodeDisplayName("Contextual Query")]
    [Description("Returns all tile positions whose surrounding neighbourhood matches every configured relative-offset condition.")]
    [HelpURL("https://dynamicdungeon.mrbytesized.com/docs/nodes/spatial-query/contextual-query")]
    public sealed class ContextualQueryNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        [Serializable]
        private sealed class NeighbourConditionRecord
        {
            public Vector2Int Offset = Vector2Int.zero;
            public bool MatchById = true;
            public int LogicalId;
            public string TagName = string.Empty;
        }

        [Serializable]
        private sealed class NeighbourConditionCollection
        {
            public NeighbourConditionRecord[] Entries = Array.Empty<NeighbourConditionRecord>();
        }

        private struct ResolvedNeighbourCondition
        {
            public int2 Offset;
            public bool MatchById;
            public int LogicalId;
            public int[] MatchingLogicalIds;
        }

        private struct JobCondition
        {
            public int2 Offset;
            public int LogicalId;
            public int MatchMode;
        }

        private const int DefaultBatchSize = 64;
        private const string DefaultNodeName = "Contextual Query";
        private const string InputPortName = "Input";
        private const string FallbackOutputPortName = "Points";
        private const string PreferredOutputDisplayName = "Points";

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;

        private NodePortDefinition[] _ports;
        private ChannelDeclaration[] _channelDeclarations;
        private string _inputChannelName;
        private string _outputChannelName;

        [DescriptionAttribute("Neighbour conditions encoded as JSON: {\"Entries\":[{\"Offset\":{\"x\":0,\"y\":1},\"MatchById\":true,\"LogicalId\":1,\"TagName\":\"\"}]}")]
        private string _conditions;

        private ResolvedNeighbourCondition[] _resolvedConditions;
        private bool _tagRegistryMissing;
        private bool _hasImpossibleCondition;

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

        public ContextualQueryNode(string nodeId, string nodeName, string inputChannelName = "", string outputChannelName = "", string conditions = "")
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _inputChannelName = inputChannelName ?? string.Empty;
            _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(nodeId, outputChannelName, FallbackOutputPortName);
            _conditions = conditions ?? string.Empty;
            _resolvedConditions = Array.Empty<ResolvedNeighbourCondition>();

            RefreshResolvedConditions();
            RefreshPorts();
            RefreshChannelDeclarations();
        }

        public void ReceiveInputConnections(InputConnectionMap inputConnections)
        {
            string inputChannelName;
            if (inputConnections != null && inputConnections.TryGetValue(InputPortName, out inputChannelName))
            {
                _inputChannelName = inputChannelName ?? string.Empty;
            }
            else
            {
                _inputChannelName = string.Empty;
            }

            RefreshChannelDeclarations();
        }

        public void ReceiveParameter(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (string.Equals(name, "conditions", StringComparison.OrdinalIgnoreCase))
            {
                _conditions = value ?? string.Empty;
                RefreshResolvedConditions();
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
            NativeArray<int> input = context.GetIntChannel(_inputChannelName);
            NativeList<int2> output = context.GetPointListChannel(_outputChannelName);
            output.Clear();

            if (output.Capacity < input.Length)
            {
                output.Capacity = input.Length;
            }

            if (_tagRegistryMissing)
            {
                ManagedBlackboardDiagnosticUtility.AppendWarning(
                    context.ManagedBlackboard,
                    "Contextual Query could not resolve semantic tags because TileSemanticRegistry is unavailable. Tag-based conditions always return false.",
                    _nodeId,
                    InputPortName);
            }

            if (_tagRegistryMissing || _hasImpossibleCondition)
            {
                return context.InputDependency;
            }

            NativeArray<JobCondition> conditions = new NativeArray<JobCondition>(_resolvedConditions.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            NativeParallelMultiHashMap<int, int> tagMatchLookup = new NativeParallelMultiHashMap<int, int>(1, Allocator.TempJob);
            bool hasTagConditions = false;
            int tagLookupEntryCount = 0;

            int conditionIndex;
            for (conditionIndex = 0; conditionIndex < _resolvedConditions.Length; conditionIndex++)
            {
                ResolvedNeighbourCondition resolvedCondition = _resolvedConditions[conditionIndex];
                conditions[conditionIndex] = new JobCondition
                {
                    Offset = resolvedCondition.Offset,
                    LogicalId = resolvedCondition.LogicalId,
                    MatchMode = resolvedCondition.MatchById ? 0 : 1
                };

                if (!resolvedCondition.MatchById)
                {
                    hasTagConditions = true;
                    tagLookupEntryCount += resolvedCondition.MatchingLogicalIds != null ? resolvedCondition.MatchingLogicalIds.Length : 0;
                }
            }

            if (hasTagConditions)
            {
                tagMatchLookup.Capacity = math.max(1, tagLookupEntryCount);

                for (conditionIndex = 0; conditionIndex < _resolvedConditions.Length; conditionIndex++)
                {
                    ResolvedNeighbourCondition resolvedCondition = _resolvedConditions[conditionIndex];
                    if (resolvedCondition.MatchById || resolvedCondition.MatchingLogicalIds == null)
                    {
                        continue;
                    }

                    int logicalIdIndex;
                    for (logicalIdIndex = 0; logicalIdIndex < resolvedCondition.MatchingLogicalIds.Length; logicalIdIndex++)
                    {
                        tagMatchLookup.Add(conditionIndex, resolvedCondition.MatchingLogicalIds[logicalIdIndex]);
                    }
                }
            }

            ContextualQueryJob job = new ContextualQueryJob
            {
                Input = input,
                Output = output.AsParallelWriter(),
                Conditions = conditions,
                TagMatchLookup = tagMatchLookup,
                Width = context.Width,
                Height = context.Height
            };

            JobHandle jobHandle = job.Schedule(input.Length, DefaultBatchSize, context.InputDependency);
            JobHandle disposeConditionsHandle = conditions.Dispose(jobHandle);
            return tagMatchLookup.Dispose(disposeConditionsHandle);
        }

        private void RefreshPorts()
        {
            string outputPortDisplayName = GraphPortNameUtility.ResolveOutputDisplayName(_nodeId, _outputChannelName, PreferredOutputDisplayName);
            _ports = new[]
            {
                new NodePortDefinition(InputPortName, PortDirection.Input, ChannelType.Int, PortCapacity.Single, true),
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.PointList, displayName: outputPortDisplayName)
            };
        }

        private void RefreshChannelDeclarations()
        {
            if (!string.IsNullOrWhiteSpace(_inputChannelName))
            {
                _channelDeclarations = new[]
                {
                    new ChannelDeclaration(_inputChannelName, ChannelType.Int, false),
                    new ChannelDeclaration(_outputChannelName, ChannelType.PointList, true)
                };
                return;
            }

            _channelDeclarations = new[]
            {
                new ChannelDeclaration(_outputChannelName, ChannelType.PointList, true)
            };
        }

        private void RefreshResolvedConditions()
        {
            NeighbourConditionRecord[] parsedConditions = ParseConditions(_conditions);
            ResolvedNeighbourCondition[] resolvedConditions = new ResolvedNeighbourCondition[parsedConditions.Length];

            _tagRegistryMissing = false;
            _hasImpossibleCondition = false;

            int conditionIndex;
            for (conditionIndex = 0; conditionIndex < parsedConditions.Length; conditionIndex++)
            {
                NeighbourConditionRecord parsedCondition = parsedConditions[conditionIndex] ?? new NeighbourConditionRecord();
                ResolvedNeighbourCondition resolvedCondition = new ResolvedNeighbourCondition();
                resolvedCondition.Offset = new int2(parsedCondition.Offset.x, parsedCondition.Offset.y);
                resolvedCondition.MatchById = parsedCondition.MatchById;
                resolvedCondition.LogicalId = math.max(0, parsedCondition.LogicalId);
                resolvedCondition.MatchingLogicalIds = Array.Empty<int>();

                if (!parsedCondition.MatchById)
                {
                    bool registryMissing;
                    int resolvedTagId;
                    int[] matchingLogicalIds;
                    bool resolved = SpatialQueryTagResolutionUtility.TryResolveMatchingLogicalIds(
                        parsedCondition.TagName,
                        out resolvedTagId,
                        out matchingLogicalIds,
                        out registryMissing);

                    if (registryMissing)
                    {
                        _tagRegistryMissing = true;
                    }
                    else
                    {
                        resolvedCondition.LogicalId = resolvedTagId;
                        resolvedCondition.MatchingLogicalIds = matchingLogicalIds ?? Array.Empty<int>();
                        if (!resolved || resolvedCondition.MatchingLogicalIds.Length == 0)
                        {
                            _hasImpossibleCondition = true;
                        }
                    }
                }

                resolvedConditions[conditionIndex] = resolvedCondition;
            }

            _resolvedConditions = resolvedConditions;
        }

        private static NeighbourConditionRecord[] ParseConditions(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return Array.Empty<NeighbourConditionRecord>();
            }

            try
            {
                NeighbourConditionCollection collection = JsonUtility.FromJson<NeighbourConditionCollection>(rawJson);
                if (collection == null || collection.Entries == null)
                {
                    return Array.Empty<NeighbourConditionRecord>();
                }

                return collection.Entries;
            }
            catch
            {
                return Array.Empty<NeighbourConditionRecord>();
            }
        }

        [BurstCompile]
        private struct ContextualQueryJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<int> Input;

            [ReadOnly]
            public NativeArray<JobCondition> Conditions;

            [ReadOnly]
            public NativeParallelMultiHashMap<int, int> TagMatchLookup;

            public NativeList<int2>.ParallelWriter Output;
            public int Width;
            public int Height;

            public void Execute(int index)
            {
                int x = index % Width;
                int y = index / Width;

                int conditionIndex;
                for (conditionIndex = 0; conditionIndex < Conditions.Length; conditionIndex++)
                {
                    JobCondition condition = Conditions[conditionIndex];
                    int neighbourX = x + condition.Offset.x;
                    int neighbourY = y + condition.Offset.y;
                    if (neighbourX < 0 || neighbourX >= Width || neighbourY < 0 || neighbourY >= Height)
                    {
                        return;
                    }

                    int neighbourIndex = (neighbourY * Width) + neighbourX;
                    int neighbourLogicalId = Input[neighbourIndex];

                    if (condition.MatchMode == 0)
                    {
                        if (neighbourLogicalId != condition.LogicalId)
                        {
                            return;
                        }

                        continue;
                    }

                    if (!ConditionMatchesTag(conditionIndex, neighbourLogicalId))
                    {
                        return;
                    }
                }

                Output.AddNoResize(new int2(x, y));
            }

            private bool ConditionMatchesTag(int conditionIndex, int logicalId)
            {
                int matchedLogicalId;
                NativeParallelMultiHashMapIterator<int> iterator;
                if (!TagMatchLookup.TryGetFirstValue(conditionIndex, out matchedLogicalId, out iterator))
                {
                    return false;
                }

                do
                {
                    if (matchedLogicalId == logicalId)
                    {
                        return true;
                    }
                }
                while (TagMatchLookup.TryGetNextValue(out matchedLogicalId, ref iterator));

                return false;
            }
        }
    }
}
