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

namespace DynamicDungeon.Runtime.Nodes
{
    [NodeCategory("Point Generation")]
    [NodeDisplayName("Edge Finder")]
    [Description("Finds tile positions along the boundary where type A and type B meet.")]
    public sealed class EdgeFinderNode : IGenNode, IInputConnectionReceiver, IParameterReceiver, IParameterVisibilityProvider
    {
        private const int DefaultBatchSize = 64;
        private const string DefaultNodeName = "Edge Finder";
        private const string InputPortName = "LogicalIds";
        private const string FallbackOutputPortName = "Points";
        private const string PreferredOutputDisplayName = "Points";

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;

        private NodePortDefinition[] _ports;
        private ChannelDeclaration[] _channelDeclarations;
        private string _inputChannelName;
        private string _outputChannelName;

        [DescriptionAttribute("When enabled, side A resolves semantic tags instead of using IdA directly.")]
        private bool _useTagsA;

        [DescriptionAttribute("Semantic tag used for side A when Use Tags A is enabled.")]
        private string _tagA;

        [MinValue(0.0f)]
        [DescriptionAttribute("Logical ID used for side A when Use Tags A is disabled.")]
        private ushort _idA;

        [DescriptionAttribute("When enabled, side B resolves semantic tags instead of using IdB directly.")]
        private bool _useTagsB;

        [DescriptionAttribute("Semantic tag used for side B when Use Tags B is enabled.")]
        private string _tagB;

        [MinValue(0.0f)]
        [DescriptionAttribute("Logical ID used for side B when Use Tags B is disabled.")]
        private ushort _idB;

        [DescriptionAttribute("Which side of the boundary should be emitted.")]
        private EdgeSide _side;

        [MinValue(1.0f)]
        [DescriptionAttribute("Minimum straight horizontal or vertical run length for boundary tiles to qualify.")]
        private int _minRunLength;

        [MinValue(0.0f)]
        [DescriptionAttribute("Maximum number of boundary points to emit. Zero keeps every qualifying boundary tile.")]
        private int _pointCount;

        private int[] _resolvedTypeAIds;
        private int[] _resolvedTypeBIds;
        private bool _tagRegistryMissing;

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

        public EdgeFinderNode(
            string nodeId,
            string nodeName,
            string inputChannelName = "",
            string outputChannelName = "",
            bool useTagsA = false,
            string tagA = "",
            ushort idA = 0,
            bool useTagsB = false,
            string tagB = "",
            ushort idB = 0,
            EdgeSide side = EdgeSide.Both,
            int minRunLength = 1,
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
            _useTagsA = useTagsA;
            _tagA = tagA ?? string.Empty;
            _idA = idA;
            _useTagsB = useTagsB;
            _tagB = tagB ?? string.Empty;
            _idB = idB;
            _side = side;
            _minRunLength = math.max(1, minRunLength);
            _pointCount = math.max(0, pointCount);
            _resolvedTypeAIds = Array.Empty<int>();
            _resolvedTypeBIds = Array.Empty<int>();

            RefreshResolvedTypes();
            RefreshPorts();
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

            if (string.Equals(name, "useTagsA", StringComparison.OrdinalIgnoreCase))
            {
                bool parsedValue;
                if (bool.TryParse(value, out parsedValue))
                {
                    _useTagsA = parsedValue;
                    RefreshResolvedTypes();
                }

                return;
            }

            if (string.Equals(name, "tagA", StringComparison.OrdinalIgnoreCase))
            {
                _tagA = value ?? string.Empty;
                RefreshResolvedTypes();
                return;
            }

            if (string.Equals(name, "idA", StringComparison.OrdinalIgnoreCase))
            {
                ushort parsedValue;
                if (ushort.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedValue))
                {
                    _idA = parsedValue;
                    RefreshResolvedTypes();
                }

                return;
            }

            if (string.Equals(name, "useTagsB", StringComparison.OrdinalIgnoreCase))
            {
                bool parsedValue;
                if (bool.TryParse(value, out parsedValue))
                {
                    _useTagsB = parsedValue;
                    RefreshResolvedTypes();
                }

                return;
            }

            if (string.Equals(name, "tagB", StringComparison.OrdinalIgnoreCase))
            {
                _tagB = value ?? string.Empty;
                RefreshResolvedTypes();
                return;
            }

            if (string.Equals(name, "idB", StringComparison.OrdinalIgnoreCase))
            {
                ushort parsedValue;
                if (ushort.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedValue))
                {
                    _idB = parsedValue;
                    RefreshResolvedTypes();
                }

                return;
            }

            if (string.Equals(name, "side", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    _side = (EdgeSide)Enum.Parse(typeof(EdgeSide), value ?? string.Empty, true);
                }
                catch
                {
                }

                return;
            }

            if (string.Equals(name, "minRunLength", StringComparison.OrdinalIgnoreCase))
            {
                int parsedValue;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedValue))
                {
                    _minRunLength = math.max(1, parsedValue);
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

        public bool IsParameterVisible(string parameterName)
        {
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                return true;
            }

            if (string.Equals(parameterName, "tagA", StringComparison.OrdinalIgnoreCase))
            {
                return _useTagsA;
            }

            if (string.Equals(parameterName, "idA", StringComparison.OrdinalIgnoreCase))
            {
                return !_useTagsA;
            }

            if (string.Equals(parameterName, "tagB", StringComparison.OrdinalIgnoreCase))
            {
                return _useTagsB;
            }

            if (string.Equals(parameterName, "idB", StringComparison.OrdinalIgnoreCase))
            {
                return !_useTagsB;
            }

            return true;
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
                    "Edge Finder could not resolve one or more semantic tags because TileSemanticRegistry is unavailable. Tag-based edge matching returns no points.",
                    _nodeId,
                    InputPortName);

                return context.InputDependency;
            }

            if (_resolvedTypeAIds.Length == 0 || _resolvedTypeBIds.Length == 0)
            {
                return context.InputDependency;
            }

            NativeParallelHashSet<int> typeAIds = new NativeParallelHashSet<int>(_resolvedTypeAIds.Length, Allocator.TempJob);
            NativeParallelHashSet<int> typeBIds = new NativeParallelHashSet<int>(_resolvedTypeBIds.Length, Allocator.TempJob);

            PopulateSet(typeAIds, _resolvedTypeAIds);
            PopulateSet(typeBIds, _resolvedTypeBIds);

            NativeStream stream = new NativeStream(input.Length, Allocator.TempJob);

            EdgeFinderJob edgeJob = new EdgeFinderJob
            {
                Input = input,
                TypeAIds = typeAIds,
                TypeBIds = typeBIds,
                Writer = stream.AsWriter(),
                Width = context.Width,
                Height = context.Height,
                Side = (int)_side,
                MinRunLength = _minRunLength
            };

            CollectEdgePointsJob collectJob = new CollectEdgePointsJob
            {
                Reader = stream.AsReader(),
                Output = output,
                ForEachCount = input.Length
            };

            JobHandle edgeHandle = edgeJob.Schedule(input.Length, DefaultBatchSize, context.InputDependency);
            JobHandle collectHandle = collectJob.Schedule(edgeHandle);
            JobHandle sampleHandle = PointListSamplingUtility.ScheduleShuffleAndLimit(output, _pointCount, context.LocalSeed, 0x7E4AD143u, collectHandle);
            JobHandle disposeStreamHandle = stream.Dispose(sampleHandle);
            JobHandle disposeTypeAHandle = typeAIds.Dispose(disposeStreamHandle);
            return typeBIds.Dispose(disposeTypeAHandle);
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

        private void RefreshResolvedTypes()
        {
            _tagRegistryMissing = false;
            _resolvedTypeAIds = ResolveTypeIds(_useTagsA, _tagA, _idA, ref _tagRegistryMissing);
            _resolvedTypeBIds = ResolveTypeIds(_useTagsB, _tagB, _idB, ref _tagRegistryMissing);
        }

        private static void PopulateSet(NativeParallelHashSet<int> targetSet, int[] values)
        {
            int index;
            for (index = 0; index < values.Length; index++)
            {
                targetSet.Add(values[index]);
            }
        }

        private static int[] ResolveTypeIds(bool useTags, string tagName, ushort id, ref bool tagRegistryMissing)
        {
            if (!useTags)
            {
                return new[] { (int)id };
            }

            int resolvedTagId;
            int[] matchingLogicalIds;
            bool registryMissing;
            bool resolved = SpatialQueryTagResolutionUtility.TryResolveMatchingLogicalIds(tagName, out resolvedTagId, out matchingLogicalIds, out registryMissing);
            if (registryMissing)
            {
                tagRegistryMissing = true;
            }

            if (!resolved || matchingLogicalIds == null || matchingLogicalIds.Length == 0)
            {
                return Array.Empty<int>();
            }

            return matchingLogicalIds;
        }

        [BurstCompile]
        private struct EdgeFinderJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<int> Input;

            [ReadOnly]
            public NativeParallelHashSet<int> TypeAIds;

            [ReadOnly]
            public NativeParallelHashSet<int> TypeBIds;

            public NativeStream.Writer Writer;
            public int Width;
            public int Height;
            public int Side;
            public int MinRunLength;

            public void Execute(int index)
            {
                Writer.BeginForEachIndex(index);

                int x = index % Width;
                int y = index / Width;

                bool boundaryOnA;
                bool boundaryOnB;
                EvaluateBoundaryFlags(x, y, out boundaryOnA, out boundaryOnB);

                if (ShouldEmitPoint(x, y, boundaryOnA, boundaryOnB))
                {
                    Writer.Write(new int2(x, y));
                }

                Writer.EndForEachIndex();
            }

            private bool ShouldEmitPoint(int x, int y, bool boundaryOnA, bool boundaryOnB)
            {
                if (Side == (int)EdgeSide.SideA)
                {
                    return boundaryOnA && HasRequiredRunLength(x, y, true, false);
                }

                if (Side == (int)EdgeSide.SideB)
                {
                    return boundaryOnB && HasRequiredRunLength(x, y, false, true);
                }

                if (boundaryOnA && HasRequiredRunLength(x, y, true, false))
                {
                    return true;
                }

                if (boundaryOnB && HasRequiredRunLength(x, y, false, true))
                {
                    return true;
                }

                return false;
            }

            private bool HasRequiredRunLength(int x, int y, bool includeSideA, bool includeSideB)
            {
                if (MinRunLength <= 1)
                {
                    return true;
                }

                int horizontalRunLength = 1 +
                    CountRunInDirection(x, y, -1, 0, includeSideA, includeSideB) +
                    CountRunInDirection(x, y, 1, 0, includeSideA, includeSideB);

                if (horizontalRunLength >= MinRunLength)
                {
                    return true;
                }

                int verticalRunLength = 1 +
                    CountRunInDirection(x, y, 0, -1, includeSideA, includeSideB) +
                    CountRunInDirection(x, y, 0, 1, includeSideA, includeSideB);

                return verticalRunLength >= MinRunLength;
            }

            private int CountRunInDirection(int startX, int startY, int stepX, int stepY, bool includeSideA, bool includeSideB)
            {
                int runLength = 0;
                int x = startX + stepX;
                int y = startY + stepY;

                while (x >= 0 && x < Width && y >= 0 && y < Height)
                {
                    bool boundaryOnA;
                    bool boundaryOnB;
                    EvaluateBoundaryFlags(x, y, out boundaryOnA, out boundaryOnB);

                    bool matchesRun = (includeSideA && boundaryOnA) || (includeSideB && boundaryOnB);
                    if (!matchesRun)
                    {
                        break;
                    }

                    runLength++;
                    x += stepX;
                    y += stepY;
                }

                return runLength;
            }

            private void EvaluateBoundaryFlags(int x, int y, out bool boundaryOnA, out bool boundaryOnB)
            {
                int tileValue = Input[(y * Width) + x];
                bool matchesA = TypeAIds.Contains(tileValue);
                bool matchesB = TypeBIds.Contains(tileValue);

                boundaryOnA = matchesA && HasCardinalNeighbourMatching(x, y, TypeBIds);
                boundaryOnB = matchesB && HasCardinalNeighbourMatching(x, y, TypeAIds);
            }

            private bool HasCardinalNeighbourMatching(int x, int y, NativeParallelHashSet<int> targetSet)
            {
                if (x > 0 && targetSet.Contains(Input[(y * Width) + (x - 1)]))
                {
                    return true;
                }

                if (x < Width - 1 && targetSet.Contains(Input[(y * Width) + (x + 1)]))
                {
                    return true;
                }

                if (y > 0 && targetSet.Contains(Input[((y - 1) * Width) + x]))
                {
                    return true;
                }

                if (y < Height - 1 && targetSet.Contains(Input[((y + 1) * Width) + x]))
                {
                    return true;
                }

                return false;
            }
        }

        [BurstCompile]
        private struct CollectEdgePointsJob : IJob
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
