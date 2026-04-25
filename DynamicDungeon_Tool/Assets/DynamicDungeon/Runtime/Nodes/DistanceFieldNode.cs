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
    [NodeDisplayName("Distance Field")]
    [Description("Computes a normalised distance field from a bool mask using forward and backward sweeps.")]
    public sealed class DistanceFieldNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        private const int DefaultBatchSize = 64;
        private const float DiagonalCost = 1.41421356237f;
        private const string DefaultNodeName = "Distance Field";
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

        public DistanceFieldNode(string nodeId, string nodeName, string inputChannelName, string outputChannelName)
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
                new NodePortDefinition(InputPortName, PortDirection.Input, ChannelType.BoolMask, PortCapacity.Single, true),
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
            NativeArray<byte> input = context.GetBoolMaskChannel(_inputChannelName);
            NativeArray<float> output = context.GetFloatChannel(_outputChannelName);
            NativeArray<float> distances = new NativeArray<float>(output.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            float maxDistance = math.sqrt((float)(context.Width * context.Width + context.Height * context.Height));
            float unreachableDistance = maxDistance + (float)(context.Width + context.Height + 1);

            DistanceFieldInitialiseJob initialiseJob = new DistanceFieldInitialiseJob
            {
                Input = input,
                Distances = distances,
                UnreachableDistance = unreachableDistance
            };

            JobHandle initialiseHandle = initialiseJob.Schedule(distances.Length, DefaultBatchSize, context.InputDependency);

            DistanceFieldForwardSweepJob forwardJob = new DistanceFieldForwardSweepJob
            {
                Distances = distances,
                Width = context.Width,
                Height = context.Height
            };

            JobHandle forwardHandle = forwardJob.Schedule(initialiseHandle);

            DistanceFieldBackwardSweepJob backwardJob = new DistanceFieldBackwardSweepJob
            {
                Distances = distances,
                Width = context.Width,
                Height = context.Height
            };

            JobHandle backwardHandle = backwardJob.Schedule(forwardHandle);

            DistanceFieldNormaliseJob normaliseJob = new DistanceFieldNormaliseJob
            {
                Distances = distances,
                Output = output,
                MaxDistance = math.max(1.0f, maxDistance)
            };

            JobHandle normaliseHandle = normaliseJob.Schedule(output.Length, DefaultBatchSize, backwardHandle);
            JobHandle disposeHandle = distances.Dispose(normaliseHandle);
            return disposeHandle;
        }

        private void RefreshChannelDeclarations()
        {
            if (!string.IsNullOrWhiteSpace(_inputChannelName))
            {
                _channelDeclarations = new[]
                {
                    new ChannelDeclaration(_inputChannelName, ChannelType.BoolMask, false),
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
        private struct DistanceFieldInitialiseJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<byte> Input;

            public NativeArray<float> Distances;
            public float UnreachableDistance;

            public void Execute(int index)
            {
                Distances[index] = Input[index] != 0 ? 0.0f : UnreachableDistance;
            }
        }

        [BurstCompile]
        private struct DistanceFieldForwardSweepJob : IJob
        {
            public NativeArray<float> Distances;
            public int Width;
            public int Height;

            public void Execute()
            {
                int y;
                for (y = 0; y < Height; y++)
                {
                    int x;
                    for (x = 0; x < Width; x++)
                    {
                        int index = y * Width + x;
                        float best = Distances[index];
                        if (x > 0)
                        {
                            best = math.min(best, Distances[index - 1] + 1.0f);
                        }

                        if (y > 0)
                        {
                            best = math.min(best, Distances[index - Width] + 1.0f);
                        }

                        if (x > 0 && y > 0)
                        {
                            best = math.min(best, Distances[index - Width - 1] + DiagonalCost);
                        }

                        if (x < Width - 1 && y > 0)
                        {
                            best = math.min(best, Distances[index - Width + 1] + DiagonalCost);
                        }

                        Distances[index] = best;
                    }
                }
            }
        }

        [BurstCompile]
        private struct DistanceFieldBackwardSweepJob : IJob
        {
            public NativeArray<float> Distances;
            public int Width;
            public int Height;

            public void Execute()
            {
                int y;
                for (y = Height - 1; y >= 0; y--)
                {
                    int x;
                    for (x = Width - 1; x >= 0; x--)
                    {
                        int index = y * Width + x;
                        float best = Distances[index];
                        if (x < Width - 1)
                        {
                            best = math.min(best, Distances[index + 1] + 1.0f);
                        }

                        if (y < Height - 1)
                        {
                            best = math.min(best, Distances[index + Width] + 1.0f);
                        }

                        if (x < Width - 1 && y < Height - 1)
                        {
                            best = math.min(best, Distances[index + Width + 1] + DiagonalCost);
                        }

                        if (x > 0 && y < Height - 1)
                        {
                            best = math.min(best, Distances[index + Width - 1] + DiagonalCost);
                        }

                        Distances[index] = best;
                    }
                }
            }
        }

        [BurstCompile]
        private struct DistanceFieldNormaliseJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<float> Distances;

            public NativeArray<float> Output;
            public float MaxDistance;

            public void Execute(int index)
            {
                Output[index] = math.saturate(Distances[index] / MaxDistance);
            }
        }
    }
}
