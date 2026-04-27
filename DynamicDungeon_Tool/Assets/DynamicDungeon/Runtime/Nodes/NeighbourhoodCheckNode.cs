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
    [NodeCategory("Query")]
    [NodeDisplayName("Neighbourhood Check")]
    [Description("Checks whether a logical ID or semantic tag exists within a configurable neighbourhood radius around each tile.")]
    public sealed class NeighbourhoodCheckNode : IGenNode, IInputConnectionReceiver, IParameterReceiver, IParameterVisibilityProvider
    {
        private const int DefaultBatchSize = 64;
        private const string DefaultNodeName = "Neighbourhood Check";
        private const string InputPortName = "Input";
        private const string FallbackOutputPortName = "Mask";
        private const string PreferredOutputDisplayName = "Mask";

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;

        private NodePortDefinition[] _ports;
        private ChannelDeclaration[] _channelDeclarations;
        private string _inputChannelName;
        private string _outputChannelName;

        [DescriptionAttribute("When enabled, this node matches by logical ID instead of semantic tag.")]
        private bool _matchById;

        [MinValue(0.0f)]
        [DescriptionAttribute("Logical ID to search for when Match By ID is enabled.")]
        private int _logicalId;

        [DescriptionAttribute("Semantic tag to search for when Match By ID is disabled.")]
        private string _tagName;

        [MinValue(0.0f)]
        [DescriptionAttribute("Neighbourhood radius, in tiles.")]
        private int _radius;

        [DescriptionAttribute("How the search radius is interpreted.")]
        private DistanceMode _distanceMode;

        private int _resolvedTagId;
        private int[] _matchingLogicalIds;
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

        public NeighbourhoodCheckNode(
            string nodeId,
            string nodeName,
            string inputChannelName = "",
            string outputChannelName = "",
            bool matchById = true,
            int logicalId = 0,
            string tagName = "",
            int radius = 1,
            DistanceMode distanceMode = DistanceMode.Chebyshev)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _inputChannelName = inputChannelName ?? string.Empty;
            _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(nodeId, outputChannelName, FallbackOutputPortName);
            _matchById = matchById;
            _logicalId = math.max(0, logicalId);
            _tagName = tagName ?? string.Empty;
            _radius = math.max(0, radius);
            _distanceMode = distanceMode;
            _resolvedTagId = -1;
            _matchingLogicalIds = Array.Empty<int>();

            RefreshTagResolution();
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

            if (string.Equals(name, "matchById", StringComparison.OrdinalIgnoreCase))
            {
                bool parsedValue;
                if (bool.TryParse(value, out parsedValue))
                {
                    _matchById = parsedValue;
                    RefreshTagResolution();
                }

                return;
            }

            if (string.Equals(name, "logicalId", StringComparison.OrdinalIgnoreCase))
            {
                int parsedValue;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedValue))
                {
                    _logicalId = math.max(0, parsedValue);
                }

                return;
            }

            if (string.Equals(name, "tagName", StringComparison.OrdinalIgnoreCase))
            {
                _tagName = value ?? string.Empty;
                RefreshTagResolution();
                return;
            }

            if (string.Equals(name, "radius", StringComparison.OrdinalIgnoreCase))
            {
                int parsedValue;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedValue))
                {
                    _radius = math.max(0, parsedValue);
                }

                return;
            }

            if (string.Equals(name, "distanceMode", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    _distanceMode = (DistanceMode)Enum.Parse(typeof(DistanceMode), value ?? string.Empty, true);
                }
                catch
                {
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

            if (string.Equals(parameterName, "logicalId", StringComparison.OrdinalIgnoreCase))
            {
                return _matchById;
            }

            if (string.Equals(parameterName, "tagName", StringComparison.OrdinalIgnoreCase))
            {
                return !_matchById;
            }

            return true;
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            NativeArray<int> input = context.GetIntChannel(_inputChannelName);
            NativeArray<byte> output = context.GetBoolMaskChannel(_outputChannelName);

            if (_matchById)
            {
                return ScheduleIdMatch(context.InputDependency, input, output, context.Width, context.Height);
            }

            if (_tagRegistryMissing)
            {
                ManagedBlackboardDiagnosticUtility.AppendWarning(
                    context.ManagedBlackboard,
                    "Neighbourhood Check could not resolve semantic tags because TileSemanticRegistry is unavailable. Tag-based matches always return false.",
                    _nodeId,
                    InputPortName);

                ZeroBoolMask(output);
                return context.InputDependency;
            }

            if (_matchingLogicalIds == null || _matchingLogicalIds.Length == 0)
            {
                ZeroBoolMask(output);
                return context.InputDependency;
            }

            NativeParallelHashSet<int> matchingLogicalIds = new NativeParallelHashSet<int>(_matchingLogicalIds.Length, Allocator.TempJob);

            int index;
            for (index = 0; index < _matchingLogicalIds.Length; index++)
            {
                matchingLogicalIds.Add(_matchingLogicalIds[index]);
            }

            NeighbourhoodTagMatchJob job = new NeighbourhoodTagMatchJob
            {
                Input = input,
                Output = output,
                MatchingLogicalIds = matchingLogicalIds,
                Width = context.Width,
                Height = context.Height,
                Radius = _radius,
                RadiusSquared = _radius * _radius,
                DistanceMode = (int)_distanceMode
            };

            JobHandle jobHandle = job.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
            return matchingLogicalIds.Dispose(jobHandle);
        }

        private void RefreshPorts()
        {
            string outputPortDisplayName = GraphPortNameUtility.ResolveOutputDisplayName(_nodeId, _outputChannelName, PreferredOutputDisplayName);
            _ports = new[]
            {
                new NodePortDefinition(InputPortName, PortDirection.Input, ChannelType.Int, PortCapacity.Single, true),
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.BoolMask, displayName: outputPortDisplayName)
            };
        }

        private void RefreshChannelDeclarations()
        {
            if (!string.IsNullOrWhiteSpace(_inputChannelName))
            {
                _channelDeclarations = new[]
                {
                    new ChannelDeclaration(_inputChannelName, ChannelType.Int, false),
                    new ChannelDeclaration(_outputChannelName, ChannelType.BoolMask, true)
                };
                return;
            }

            _channelDeclarations = new[]
            {
                new ChannelDeclaration(_outputChannelName, ChannelType.BoolMask, true)
            };
        }

        private void RefreshTagResolution()
        {
            _resolvedTagId = -1;
            _matchingLogicalIds = Array.Empty<int>();
            _tagRegistryMissing = false;

            if (_matchById)
            {
                return;
            }

            SpatialQueryTagResolutionUtility.TryResolveMatchingLogicalIds(_tagName, out _resolvedTagId, out _matchingLogicalIds, out _tagRegistryMissing);
        }

        private JobHandle ScheduleIdMatch(JobHandle inputDependency, NativeArray<int> input, NativeArray<byte> output, int width, int height)
        {
            NeighbourhoodIdMatchJob job = new NeighbourhoodIdMatchJob
            {
                Input = input,
                Output = output,
                LogicalId = _logicalId,
                Width = width,
                Height = height,
                Radius = _radius,
                RadiusSquared = _radius * _radius,
                DistanceMode = (int)_distanceMode
            };

            return job.Schedule(output.Length, DefaultBatchSize, inputDependency);
        }

        private static void ZeroBoolMask(NativeArray<byte> output)
        {
            int index;
            for (index = 0; index < output.Length; index++)
            {
                output[index] = 0;
            }
        }

        [BurstCompile]
        private struct NeighbourhoodIdMatchJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<int> Input;

            public NativeArray<byte> Output;
            public int LogicalId;
            public int Width;
            public int Height;
            public int Radius;
            public int RadiusSquared;
            public int DistanceMode;

            public void Execute(int index)
            {
                Output[index] = Evaluate(index) ? (byte)1 : (byte)0;
            }

            private bool Evaluate(int index)
            {
                int x = index % Width;
                int y = index / Width;

                int offsetY;
                for (offsetY = -Radius; offsetY <= Radius; offsetY++)
                {
                    int neighbourY = y + offsetY;
                    if (neighbourY < 0 || neighbourY >= Height)
                    {
                        continue;
                    }

                    int offsetX;
                    for (offsetX = -Radius; offsetX <= Radius; offsetX++)
                    {
                        if (DistanceMode == (int)Runtime.Nodes.DistanceMode.Euclidean &&
                            ((offsetX * offsetX) + (offsetY * offsetY)) > RadiusSquared)
                        {
                            continue;
                        }

                        int neighbourX = x + offsetX;
                        if (neighbourX < 0 || neighbourX >= Width)
                        {
                            continue;
                        }

                        int neighbourIndex = (neighbourY * Width) + neighbourX;
                        if (Input[neighbourIndex] == LogicalId)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
        }

        [BurstCompile]
        private struct NeighbourhoodTagMatchJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<int> Input;

            [ReadOnly]
            public NativeParallelHashSet<int> MatchingLogicalIds;

            public NativeArray<byte> Output;
            public int Width;
            public int Height;
            public int Radius;
            public int RadiusSquared;
            public int DistanceMode;

            public void Execute(int index)
            {
                Output[index] = Evaluate(index) ? (byte)1 : (byte)0;
            }

            private bool Evaluate(int index)
            {
                int x = index % Width;
                int y = index / Width;

                int offsetY;
                for (offsetY = -Radius; offsetY <= Radius; offsetY++)
                {
                    int neighbourY = y + offsetY;
                    if (neighbourY < 0 || neighbourY >= Height)
                    {
                        continue;
                    }

                    int offsetX;
                    for (offsetX = -Radius; offsetX <= Radius; offsetX++)
                    {
                        if (DistanceMode == (int)Runtime.Nodes.DistanceMode.Euclidean &&
                            ((offsetX * offsetX) + (offsetY * offsetY)) > RadiusSquared)
                        {
                            continue;
                        }

                        int neighbourX = x + offsetX;
                        if (neighbourX < 0 || neighbourX >= Width)
                        {
                            continue;
                        }

                        int neighbourIndex = (neighbourY * Width) + neighbourX;
                        if (MatchingLogicalIds.Contains(Input[neighbourIndex]))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
        }
    }
}
