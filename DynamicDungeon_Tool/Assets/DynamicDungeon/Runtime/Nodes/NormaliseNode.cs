using System;
using System.Collections.Generic;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace DynamicDungeon.Runtime.Nodes
{
    [NodeCategory("Filter")]
    [NodeDisplayName("Normalise")]
    [Description("Normalises a float channel to 0-1 using the actual minimum and maximum values in the input.")]
    public sealed class NormaliseNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        private const int DefaultBatchSize = 64;
        private const int ReductionBatchSize = 64;
        private const string DefaultNodeName = "Normalise";
        private const string InputPortName = "Input";
        private const string FallbackOutputPortName = "Output";
        private const string PreferredOutputDisplayName = FallbackOutputPortName;

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private readonly string _outputChannelName;
        private readonly NodePortDefinition[] _ports;

        private string _inputChannelName;
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

        public NormaliseNode(string nodeId, string nodeName, string inputChannelName, string outputChannelName)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _inputChannelName = inputChannelName ?? string.Empty;
            _outputChannelName = string.IsNullOrWhiteSpace(outputChannelName) ? FallbackOutputPortName : outputChannelName;
            string outputPortDisplayName = GraphPortNameUtility.ResolveOutputDisplayName(nodeId, _outputChannelName, PreferredOutputDisplayName);
            _ports = new[]
            {
                new NodePortDefinition(InputPortName, PortDirection.Input, ChannelType.Float, PortCapacity.Single, true),
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.Float, displayName: outputPortDisplayName)
            };

            RefreshChannelDeclarations();
        }

        public void ReceiveInputConnections(IReadOnlyDictionary<string, string> inputConnections)
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
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            NativeArray<float> input = context.GetFloatChannel(_inputChannelName);
            NativeArray<float> output = context.GetFloatChannel(_outputChannelName);
            int reductionCount = math.max(1, (output.Length + ReductionBatchSize - 1) / ReductionBatchSize);
            NativeArray<float2> partialMinMax = new NativeArray<float2>(reductionCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            NativeArray<float2> finalMinMax = new NativeArray<float2>(1, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            NormaliseMinMaxReductionJob reductionJob = new NormaliseMinMaxReductionJob
            {
                Input = input,
                PartialMinMax = partialMinMax,
                Length = output.Length,
                BatchSize = ReductionBatchSize
            };

            JobHandle reductionHandle = reductionJob.Schedule(reductionCount, 1, context.InputDependency);

            NormaliseFinalMinMaxJob finalMinMaxJob = new NormaliseFinalMinMaxJob
            {
                PartialMinMax = partialMinMax,
                FinalMinMax = finalMinMax
            };

            JobHandle finalMinMaxHandle = finalMinMaxJob.Schedule(reductionHandle);

            NormaliseApplyJob applyJob = new NormaliseApplyJob
            {
                Input = input,
                Output = output,
                FinalMinMax = finalMinMax
            };

            JobHandle applyHandle = applyJob.Schedule(output.Length, DefaultBatchSize, finalMinMaxHandle);
            JobHandle partialDisposeHandle = partialMinMax.Dispose(applyHandle);
            JobHandle finalDisposeHandle = finalMinMax.Dispose(applyHandle);
            return JobHandle.CombineDependencies(partialDisposeHandle, finalDisposeHandle);
        }

        private void RefreshChannelDeclarations()
        {
            if (!string.IsNullOrWhiteSpace(_inputChannelName))
            {
                _channelDeclarations = new[]
                {
                    new ChannelDeclaration(_inputChannelName, ChannelType.Float, false),
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
        private struct NormaliseMinMaxReductionJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<float> Input;

            public NativeArray<float2> PartialMinMax;
            public int Length;
            public int BatchSize;

            public void Execute(int index)
            {
                int start = index * BatchSize;
                int end = math.min(start + BatchSize, Length);
                float minValue = float.MaxValue;
                float maxValue = float.MinValue;

                int valueIndex;
                for (valueIndex = start; valueIndex < end; valueIndex++)
                {
                    float value = Input[valueIndex];
                    minValue = math.min(minValue, value);
                    maxValue = math.max(maxValue, value);
                }

                PartialMinMax[index] = new float2(minValue, maxValue);
            }
        }

        [BurstCompile]
        private struct NormaliseFinalMinMaxJob : IJob
        {
            [ReadOnly]
            public NativeArray<float2> PartialMinMax;

            public NativeArray<float2> FinalMinMax;

            public void Execute()
            {
                float minValue = float.MaxValue;
                float maxValue = float.MinValue;

                int index;
                for (index = 0; index < PartialMinMax.Length; index++)
                {
                    float2 minMax = PartialMinMax[index];
                    minValue = math.min(minValue, minMax.x);
                    maxValue = math.max(maxValue, minMax.y);
                }

                FinalMinMax[0] = new float2(minValue, maxValue);
            }
        }

        [BurstCompile]
        private struct NormaliseApplyJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<float> Input;

            [ReadOnly]
            public NativeArray<float2> FinalMinMax;

            public NativeArray<float> Output;

            public void Execute(int index)
            {
                float2 minMax = FinalMinMax[0];
                float range = minMax.y - minMax.x;
                if (range == 0.0f)
                {
                    Output[index] = 0.0f;
                    return;
                }

                Output[index] = (Input[index] - minMax.x) / range;
            }
        }
    }
}
