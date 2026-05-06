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
    [NodeDisplayName("Point Grid")]
    [Description("Generates one point per grid cell at the cell centre, with optional deterministic jitter.")]
    public sealed class PointGridNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        private const int DefaultBatchSize = 64;
        private const string DefaultNodeName = "Point Grid";
        private const string InputPortName = "Jitter";
        private const string FallbackOutputPortName = "Points";
        private const string PreferredOutputDisplayName = "Points";

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;

        private NodePortDefinition[] _ports;
        private ChannelDeclaration[] _channelDeclarations;
        private string _inputChannelName;
        private string _outputChannelName;

        [MinValue(1.0f)]
        [DescriptionAttribute("Grid cell size, in tiles.")]
        private int _cellSize;

        [RangeAttribute(0.0f, 1.0f)]
        [DescriptionAttribute("Scales the optional jitter input before converting it into a per-cell offset.")]
        private float _jitterScale;

        [MinValue(0.0f)]
        [DescriptionAttribute("Maximum number of grid points to emit. Zero keeps every generated cell point.")]
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

        public PointGridNode(
            string nodeId,
            string nodeName,
            string inputChannelName = "",
            string outputChannelName = "",
            int cellSize = 1,
            float jitterScale = 0.0f,
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
            _cellSize = math.max(1, cellSize);
            _jitterScale = math.saturate(jitterScale);
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

            if (string.Equals(name, "cellSize", StringComparison.OrdinalIgnoreCase))
            {
                int parsedValue;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedValue))
                {
                    _cellSize = math.max(1, parsedValue);
                }

                return;
            }

            if (string.Equals(name, "jitterScale", StringComparison.OrdinalIgnoreCase))
            {
                float parsedValue;
                if (float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsedValue))
                {
                    _jitterScale = math.saturate(parsedValue);
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
            int cellsX = (context.Width + _cellSize - 1) / _cellSize;
            int cellsY = (context.Height + _cellSize - 1) / _cellSize;
            int cellCount = cellsX * cellsY;

            NativeList<int2> output = context.GetPointListChannel(_outputChannelName);
            output.Clear();

            if (output.Capacity < cellCount)
            {
                output.Capacity = cellCount;
            }

            NativeArray<int2> generatedPoints = new NativeArray<int2>(cellCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            JobHandle generateHandle;
            bool useJitter = !string.IsNullOrWhiteSpace(_inputChannelName) && _jitterScale > 0.0f;
            if (useJitter)
            {
                NativeArray<float> jitter = context.GetFloatChannel(_inputChannelName);
                PointGridJitterJob jitterJob = new PointGridJitterJob
                {
                    Jitter = jitter,
                    OutputPoints = generatedPoints,
                    Width = context.Width,
                    Height = context.Height,
                    CellsX = cellsX,
                    CellSize = _cellSize,
                    JitterScale = _jitterScale,
                    LocalSeed = context.LocalSeed
                };

                generateHandle = jitterJob.Schedule(cellCount, DefaultBatchSize, context.InputDependency);
            }
            else
            {
                PointGridJob gridJob = new PointGridJob
                {
                    OutputPoints = generatedPoints,
                    Width = context.Width,
                    Height = context.Height,
                    CellsX = cellsX,
                    CellSize = _cellSize
                };

                generateHandle = gridJob.Schedule(cellCount, DefaultBatchSize, context.InputDependency);
            }

            CollectGridPointsJob collectJob = new CollectGridPointsJob
            {
                InputPoints = generatedPoints,
                Output = output
            };

            JobHandle collectHandle = collectJob.Schedule(generateHandle);
            JobHandle sampleHandle = PointListSamplingUtility.ScheduleShuffleAndLimit(output, _pointCount, context.LocalSeed, 0x9D6C63F5u, collectHandle);
            JobHandle disposeHandle = generatedPoints.Dispose(sampleHandle);
            return disposeHandle;
        }

        private void RefreshPorts()
        {
            string outputPortDisplayName = GraphPortNameUtility.ResolveOutputDisplayName(_nodeId, _outputChannelName, PreferredOutputDisplayName);
            _ports = new[]
            {
                new NodePortDefinition(InputPortName, PortDirection.Input, ChannelType.Float, PortCapacity.Single, false),
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
        private struct PointGridJob : IJobParallelFor
        {
            public NativeArray<int2> OutputPoints;
            public int Width;
            public int Height;
            public int CellsX;
            public int CellSize;

            public void Execute(int index)
            {
                int cellX = index % CellsX;
                int cellY = index / CellsX;
                OutputPoints[index] = CalculateBasePoint(cellX, cellY, Width, Height, CellSize);
            }
        }

        [BurstCompile]
        private struct PointGridJitterJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<float> Jitter;

            public NativeArray<int2> OutputPoints;
            public int Width;
            public int Height;
            public int CellsX;
            public int CellSize;
            public float JitterScale;
            public long LocalSeed;

            public void Execute(int index)
            {
                int cellX = index % CellsX;
                int cellY = index / CellsX;
                int2 basePoint = CalculateBasePoint(cellX, cellY, Width, Height, CellSize);

                int sampleIndex = (basePoint.y * Width) + basePoint.x;
                float jitterValue = math.saturate(Jitter[sampleIndex]);
                float magnitude = jitterValue * JitterScale * CellSize;
                float angle = HashToUnitFloat(cellX, cellY) * (math.PI * 2.0f);
                float2 displacedPoint = new float2(basePoint.x, basePoint.y) + (new float2(math.cos(angle), math.sin(angle)) * magnitude);

                OutputPoints[index] = new int2(
                    math.clamp((int)math.round(displacedPoint.x), 0, Width - 1),
                    math.clamp((int)math.round(displacedPoint.y), 0, Height - 1));
            }

            private float HashToUnitFloat(int cellX, int cellY)
            {
                uint seedLow = unchecked((uint)LocalSeed);
                uint seedHigh = unchecked((uint)(LocalSeed >> 32));
                uint hash = math.hash(new uint4(unchecked((uint)cellX), unchecked((uint)cellY), seedLow, seedHigh));
                return (float)(hash / 4294967296.0d);
            }
        }

        [BurstCompile]
        private struct CollectGridPointsJob : IJob
        {
            [ReadOnly]
            public NativeArray<int2> InputPoints;

            public NativeList<int2> Output;

            public void Execute()
            {
                int index;
                for (index = 0; index < InputPoints.Length; index++)
                {
                    Output.Add(InputPoints[index]);
                }
            }
        }

        private static int2 CalculateBasePoint(int cellX, int cellY, int width, int height, int cellSize)
        {
            int startX = cellX * cellSize;
            int startY = cellY * cellSize;
            int cellWidth = math.min(cellSize, width - startX);
            int cellHeight = math.min(cellSize, height - startY);

            return new int2(
                math.clamp(startX + (cellWidth / 2), 0, width - 1),
                math.clamp(startY + (cellHeight / 2), 0, height - 1));
        }
    }
}
