using System;
using System.Collections.Generic;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using System.Globalization;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace DynamicDungeon.Runtime.Nodes
{
    [NodeCategory("Point Generation")]
    [NodeDisplayName("Stochastic Scatter")]
    [HelpURL("https://dynamicdungeon.mrbytesized.com/docs/nodes/sampling/stochastic-scatter")]
    [Description("Generates scattered points by rolling a deterministic per-tile random value against an input weight map.")]
    public sealed class StochasticScatterNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        private const int DefaultBatchSize = 64;
        private const string DefaultNodeName = "Stochastic Scatter";
        private const string InputPortName = "Weights";
        private const string FallbackOutputPortName = "Points";
        private const string PreferredOutputDisplayName = "Points";

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;

        private NodePortDefinition[] _ports;
        private ChannelDeclaration[] _channelDeclarations;
        private string _inputChannelName;
        private string _outputChannelName;

        [RangeAttribute(0.0f, 1.0f)]
        [DescriptionAttribute("Minimum input weight required before a tile is rolled against its deterministic random value.")]
        private float _threshold;

        [MinValue(0.0f)]
        [DescriptionAttribute("Maximum number of scattered points to emit. Zero keeps every successful scatter.")]
        private int _pointCount;

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

        public StochasticScatterNode(
            string nodeId,
            string nodeName,
            string inputChannelName = "",
            string outputChannelName = "",
            float threshold = 0.0f,
            int pointCount = 0)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _inputChannelName = inputChannelName ?? string.Empty;
            _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(nodeId, outputChannelName, FallbackOutputPortName);
            _threshold = math.saturate(threshold);
            _pointCount = math.max(0, pointCount);

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

            if (string.Equals(name, "threshold", StringComparison.OrdinalIgnoreCase))
            {
                float parsedValue;
                if (float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsedValue))
                {
                    _threshold = math.saturate(parsedValue);
                }

                return;
            }

            if (string.Equals(name, "pointCount", StringComparison.OrdinalIgnoreCase))
            {
                int parsedValue;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedValue))
                {
                    _pointCount = math.max(0, parsedValue);
                }

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
            NativeArray<float> input = context.GetFloatChannel(_inputChannelName);
            NativeList<int2> output = context.GetPointListChannel(_outputChannelName);
            output.Clear();

            if (output.Capacity < input.Length)
            {
                output.Capacity = input.Length;
            }

            NativeStream stream = new NativeStream(input.Length, Allocator.TempJob);

            StochasticScatterJob scatterJob = new StochasticScatterJob
            {
                Input = input,
                Writer = stream.AsWriter(),
                Width = context.Width,
                Threshold = _threshold,
                LocalSeed = context.LocalSeed
            };

            CollectScatterPointsJob collectJob = new CollectScatterPointsJob
            {
                Reader = stream.AsReader(),
                Output = output,
                ForEachCount = input.Length
            };

            JobHandle scatterHandle = scatterJob.Schedule(input.Length, DefaultBatchSize, context.InputDependency);
            JobHandle collectHandle = collectJob.Schedule(scatterHandle);
            JobHandle sampleHandle = PointListSamplingUtility.ScheduleShuffleAndLimit(output, _pointCount, context.LocalSeed, 0x51A77E31u, collectHandle);
            return stream.Dispose(sampleHandle);
        }

        private void RefreshPorts()
        {
            string outputPortDisplayName = GraphPortNameUtility.ResolveOutputDisplayName(_nodeId, _outputChannelName, PreferredOutputDisplayName);
            _ports = new[]
            {
                new NodePortDefinition(InputPortName, PortDirection.Input, ChannelType.Float, PortCapacity.Single, true),
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.PointList, displayName: outputPortDisplayName)
            };
        }

        private void RefreshChannelDeclarations()
        {
            if (!string.IsNullOrWhiteSpace(_inputChannelName))
            {
                _channelDeclarations = new[]
                {
                    new ChannelDeclaration(_inputChannelName, ChannelType.Float, false),
                    new ChannelDeclaration(_outputChannelName, ChannelType.PointList, true)
                };
                return;
            }

            _channelDeclarations = new[]
            {
                new ChannelDeclaration(_outputChannelName, ChannelType.PointList, true)
            };
        }

        [BurstCompile]
        private struct StochasticScatterJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<float> Input;

            public NativeStream.Writer Writer;
            public int Width;
            public float Threshold;
            public long LocalSeed;

            public void Execute(int index)
            {
                Writer.BeginForEachIndex(index);

                float weight = math.saturate(Input[index]);
                if (weight >= Threshold)
                {
                    int x = index % Width;
                    int y = index / Width;
                    if (weight > HashToUnitFloat(x, y))
                    {
                        Writer.Write(new int2(x, y));
                    }
                }

                Writer.EndForEachIndex();
            }

            private float HashToUnitFloat(int x, int y)
            {
                uint seedLow = unchecked((uint)LocalSeed);
                uint seedHigh = unchecked((uint)(LocalSeed >> 32));
                uint hash = math.hash(new uint4(unchecked((uint)x), unchecked((uint)y), seedLow, seedHigh));
                return (float)(hash / 4294967296.0d);
            }
        }

        [BurstCompile]
        private struct CollectScatterPointsJob : IJob
        {
            public NativeStream.Reader Reader;
            public NativeList<int2> Output;
            public int ForEachCount;

            public void Execute()
            {
                int forEachIndex;
                for (forEachIndex = 0; forEachIndex < ForEachCount; forEachIndex++)
                {
                    int pointCount = Reader.BeginForEachIndex(forEachIndex);

                    int pointIndex;
                    for (pointIndex = 0; pointIndex < pointCount; pointIndex++)
                    {
                        Output.Add(Reader.Read<int2>());
                    }

                    Reader.EndForEachIndex();
                }
            }
        }
    }
}
